using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RoadGen.Core;
using RoadGen.Core.Vtf;
using RoadGen.Rendering;

namespace RoadGen.UI;

/// <summary>A software-rendered perspective 3D viewport with Hammer-style freelook camera
/// (fly with WASD/Q/E, right-drag to look, wheel to dolly) and click-to-select.</summary>
public sealed class Viewport3D : Control
{
    private RoadDocument _doc;
    private VmfWorld _referenceWorld;
    private double _yaw = -0.85;
    private double _pitch = 0.55;
    private Vec3 _eye = new Vec3(2000, 2600, 1800);
    private bool _autoTarget = true;

    // Freelook fly speed (world units/second). The camera flies with WASD/Q/E and the wheel
    // dollies along the view direction; nothing orbits a target anymore.
    private double _flySpeed = 1500;

    // Keyboard fly speed multiplier (WASD/Q/E). The wheel dolly and Shift+wheel speed tuning
    // are intentionally NOT scaled by this, so they stay fine-grained.
    private const double MovementSpeedScale = 2.0;

    // Right-button "look": the cursor is captured/hidden at the view centre and turns the camera.
    private bool _looking;

    // Movement key state. While any fly key is held the camera advances once per rendered
    // frame (ApplyFlyMovement) by the real elapsed time; the timer below only keeps repaints
    // coming while a key is held but nothing else (mouse look, etc.) is invalidating.
    private bool _keyForward, _keyBack, _keyLeft, _keyRight, _keyUp, _keyDown;
    private readonly System.Windows.Forms.Timer _flyTimer = new System.Windows.Forms.Timer { Interval = 16 };
    private long _lastMoveMs;

    private bool _clickCandidate;
    private Point _downPos;

    private Vec3 _right;
    private Vec3 _up;
    private Vec3 _forward;
    private double _focal;

    public Action<int, bool> PointSelected;
    public Func<int> GetSelectedIndex = () => -1;
    public Func<IReadOnlyCollection<int>> GetSelectedIndices = () => Array.Empty<int>();

    public Viewport3D()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = Color.FromArgb(32, 32, 36);
        TabStop = false;
        _flyTimer.Tick += OnFlyTimerTick;
    }

    public void SetDocument(RoadDocument doc) => _doc = doc;

    public bool ShowSegments;
    public bool ShowFeatureSegments;
    public bool ShowReferenceWorld = true;

    /// <summary>When true, the imported reference world is rendered with its actual game
    /// materials (textured) instead of only as a wireframe. The wireframe still draws on top.</summary>
    public bool ShowTexturedReference;

    /// <summary>When true (default), faces of the imported layout whose material is a Source
    /// tool texture (tools/*, e.g. clip/skip/areaportal) are not drawn at all — only real
    /// geometry shows, like Hammer hiding tool brushes from the view.</summary>
    public bool HideToolTextures = true;

    /// <summary>Decoded texture cache. MainWindow points it at the auto-discovered Source
    /// content folders (SetContentRoots, each mounted loose + *_dir.vpk) so faces render
    /// with their real game materials, mirroring Hammer's mounted search paths.</summary>
    public readonly VtfMaterialCache MaterialCache = new VtfMaterialCache();

    private readonly FrameBuffer _frameBuffer = new FrameBuffer();
    private readonly Dictionary<Bitmap, int[]> _texturePixels = new Dictionary<Bitmap, int[]>();

    /// <summary>Sets the imported VMF layout to render as a reference behind the road.</summary>
    public void SetReferenceWorld(VmfWorld world)
    {
        _referenceWorld = world;
        _texturePixels.Clear();
        Invalidate();
    }

    /// <summary>Cancels an in-progress look/click. The 3D view has no point drag, but this
    /// keeps the API consistent when points are deleted.</summary>
    public void CancelDrag()
    {
        StopLooking();
        _clickCandidate = false;
    }

    public void FrameAll()
    {
        _autoTarget = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (_autoTarget)
        {
            FrameContent();
        }

        SetupCamera();
        ApplyFlyMovement();
        DrawGroundGrid(g);
        DrawAxes(g);
        DrawTexturedReference(g);
        DrawReferenceWorld(g);

        if (_doc != null)
        {
            DrawAllTracks(g);
            DrawEdgeFeatures(g);
            DrawSegments(g);
            DrawFeatureSegments(g);
            DrawInactivePoints(g);
            DrawPoints(g);
        }

        DrawTitle(g);
        DrawBorder(g);
    }

    private void SetupCamera()
    {
        // Freelook basis: the camera looks along -dir (dir points from the world out to the
        // eye, matching the old orbit framing so the default view is unchanged). The eye is a
        // free position moved by WASD/Q/E and the wheel; nothing orbits a target.
        _forward = -ViewDir(_yaw, _pitch);
        _right = Vec3.Cross(_forward, Vec3.UnitZ).Normalized();
        if (_right.LengthSq < 1e-6)
        {
            _right = Vec3.UnitX;
        }

        _up = Vec3.Cross(_right, _forward).Normalized();
        _focal = Math.Max(Height, 1) * 1.4;
    }

    /// <summary>Direction from the world out toward the eye for the given orientation
    /// (the eye sits on this side of whatever it is looking at).</summary>
    private static Vec3 ViewDir(double yaw, double pitch) => new Vec3(
        Math.Cos(pitch) * Math.Sin(yaw),
        Math.Cos(pitch) * Math.Cos(yaw),
        Math.Sin(pitch));

    /// <summary>Places the free-fly eye so the current track content is framed (used on the
    /// first paint and by Frame All). The look direction is untouched, so framing preserves
    /// whatever orientation the user has set.</summary>
    private void FrameContent()
    {
        bool foundAny = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;
        if (_doc != null)
        {
            foreach (Track track in _doc.Tracks)
            {
                foreach (RoadPoint p in track.Points)
                {
                    if (!foundAny)
                    {
                        min = p.Position;
                        max = p.Position;
                        foundAny = true;
                    }
                    else
                    {
                        min = Vec3.Min(min, p.Position);
                        max = Vec3.Max(max, p.Position);
                    }
                }
            }
        }

        if (foundAny)
        {
            Vec3 centre = (min + max) / 2.0;
            double span = Math.Max(256, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
            double dist = span * 2.6;
            _eye = centre + ViewDir(_yaw, _pitch) * dist;
            _flySpeed = Math.Max(400, dist * 0.8);
        }
    }

    private PointF? Project(Vec3 p)
    {
        Vec3 d = p - _eye;
        double cz = Vec3.Dot(d, _forward);
        if (cz < 1.0)
        {
            return null;
        }

        double cx = Vec3.Dot(d, _right);
        double cy = Vec3.Dot(d, _up);
        double sx = Width / 2.0 + cx * _focal / cz;
        double sy = Height / 2.0 - cy * _focal / cz;
        return new PointF((float)sx, (float)sy);
    }

    private void DrawGroundGrid(Graphics g)
    {
        const double spacing = 512;
        const double extent = 4096;
        double z = 0;

        using Pen minor = new Pen(Color.FromArgb(70, 76, 86));
        using Pen major = new Pen(Color.FromArgb(140, 150, 162));

        for (double w = -extent; w <= extent + 0.5; w += spacing)
        {
            Draw3DLine(g, new Vec3(w, -extent, z), new Vec3(w, extent, z), Math.Abs(w) < 0.5 ? major : minor);
            Draw3DLine(g, new Vec3(-extent, w, z), new Vec3(extent, w, z), Math.Abs(w) < 0.5 ? major : minor);
        }
    }

    private void DrawAxes(Graphics g)
    {
        using Pen xp = new Pen(Color.FromArgb(230, 80, 80), 2);
        using Pen yp = new Pen(Color.FromArgb(80, 190, 80), 2);
        using Pen zp = new Pen(Color.FromArgb(80, 140, 230), 2);

        Draw3DLine(g, Vec3.Zero, new Vec3(512, 0, 0), xp);
        Draw3DLine(g, Vec3.Zero, new Vec3(0, 512, 0), yp);
        Draw3DLine(g, Vec3.Zero, new Vec3(0, 0, 512), zp);
    }

    private void DrawAllTracks(Graphics g)
    {
        Track activeTrack = _doc.ActiveTrack;
        const int stepsPerSegment = 20;

        foreach (RoadChain chain in _doc.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            RoadPreviewMesh mesh = RoadPreviewMesh.Build(chain.Points, stepsPerSegment, chain.Closed);

            foreach (ChainSpan span in chain.Spans)
            {
                if (span.EndPoint - span.StartPoint < 2)
                {
                    continue;
                }

                bool isActive = ReferenceEquals(span.Track, activeTrack);
                int startIndex = span.StartPoint * stepsPerSegment;
                int endIndex = (span.EndPoint - 1) * stepsPerSegment;
                DrawMeshRange(g, mesh, span.Track.Settings, isActive, startIndex, endIndex);
            }
        }
    }

    private void DrawMeshRange(Graphics g, RoadPreviewMesh mesh, RoadSettings settings, bool isActive, int startIndex, int endIndex)
    {
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        if (endIndex > mesh.Center.Count - 1)
        {
            endIndex = mesh.Center.Count - 1;
        }

        if (startIndex > endIndex)
        {
            return;
        }

        using Pen edge = new Pen(isActive ? Color.FromArgb(80, 195, 255) : Color.FromArgb(105, 115, 130), isActive ? 1.8f : 1.1f);
        using Pen center = new Pen(isActive ? Color.FromArgb(130, 240, 130) : Color.FromArgb(95, 105, 120), isActive ? 1.6f : 1.0f);
        using Pen rib = new Pen(isActive ? Color.FromArgb(105, 105, 118) : Color.FromArgb(58, 60, 68), 1f);
        using Pen wall = new Pen(isActive ? Color.FromArgb(255, 160, 70) : Color.FromArgb(80, 85, 98), 1.2f);

        DrawPolylineRange(g, mesh.Left, edge, startIndex, endIndex);
        DrawPolylineRange(g, mesh.Right, edge, startIndex, endIndex);
        DrawPolylineRange(g, mesh.Center, center, startIndex, endIndex);

        if (!ShowSegments)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 5 == 0)
                {
                    Draw3DLine(g, mesh.Left[i], mesh.Right[i], rib);
                }
            }
        }

        if (settings.SolidBottom || settings.SolidLeft)
        {
            DrawPolylineRange(g, mesh.BottomLeft, wall, startIndex, endIndex);
        }

        if (settings.SolidBottom || settings.SolidRight)
        {
            DrawPolylineRange(g, mesh.BottomRight, wall, startIndex, endIndex);
        }

        if (!ShowSegments && settings.SolidBottom)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 5 == 0)
                {
                    Draw3DLine(g, mesh.BottomLeft[i], mesh.BottomRight[i], rib);
                }
            }
        }

        if (settings.SolidLeft)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 5 == 0)
                {
                    Draw3DLine(g, mesh.Left[i], mesh.BottomLeft[i], wall);
                }
            }
        }

        if (settings.SolidRight)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 5 == 0)
                {
                    Draw3DLine(g, mesh.Right[i], mesh.BottomRight[i], wall);
                }
            }
        }
    }

    private void DrawPolylineRange(Graphics g, IReadOnlyList<Vec3> points, Pen pen, int startIndex, int endIndex)
    {
        for (int index = startIndex; index < endIndex; index++)
        {
            Draw3DLine(g, points[index], points[index + 1], pen);
        }
    }

    private void DrawEdgeFeatures(Graphics g)
    {
        if (_doc == null)
        {
            return;
        }

        Track activeTrack = _doc.ActiveTrack;
        const int stepsPerSegment = 20;

        foreach (RoadChain chain in _doc.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            foreach (ChainFeature chainFeature in chain.CollectFeatures())
            {
                EdgePreviewMesh mesh = EdgePreviewMesh.Build(chain.Points, stepsPerSegment, chainFeature, chain.Closed);
                if (mesh.InnerTop.Count < 2)
                {
                    continue;
                }

                EdgeFeature feature = chainFeature.Feature;
                int last = mesh.InnerTop.Count - 1;
                bool strip = feature.Kind != EdgeFeatureKind.Guardrail;

                // Draw the feature piecewise by span so only the active track's own
                // portion is highlighted; the tracks it is welded to stay muted. This
                // mirrors the road body, which colors each span by its own track.
                foreach (ChainSpan span in chain.Spans)
                {
                    int lo = Math.Max(span.StartPoint, chainFeature.StartPoint);
                    int hi = Math.Min(span.EndPoint - 1, chainFeature.EndPoint - 1);
                    if (lo > hi)
                    {
                        continue;
                    }

                    int startIndex = (lo - chainFeature.StartPoint) * stepsPerSegment;
                    int endIndex = (hi - chainFeature.StartPoint) * stepsPerSegment;
                    if (startIndex < 0)
                    {
                        startIndex = 0;
                    }

                    if (endIndex > last)
                    {
                        endIndex = last;
                    }

                    if (startIndex > endIndex)
                    {
                        continue;
                    }

                    bool isActive = ReferenceEquals(span.Track, activeTrack);
                    using Pen pen = new Pen(isActive ? Color.FromArgb(155, 175, 255) : Color.FromArgb(92, 98, 114), isActive ? 1.6f : 1.0f);

                    DrawPolylineRange(g, mesh.InnerTop, pen, startIndex, endIndex);

                    if (feature.SolidBottom || feature.SolidInner)
                    {
                        DrawPolylineRange(g, mesh.InnerBase, pen, startIndex, endIndex);
                    }

                    if (strip)
                    {
                        DrawPolylineRange(g, mesh.OuterTop, pen, startIndex, endIndex);

                        if (feature.SolidBottom || feature.SolidOuter)
                        {
                            DrawPolylineRange(g, mesh.OuterBase, pen, startIndex, endIndex);
                        }
                    }

                    for (int i = startIndex; i <= endIndex; i += 5)
                    {
                        if (strip)
                        {
                            Draw3DLine(g, mesh.InnerTop[i], mesh.OuterTop[i], pen);
                        }

                        if (feature.SolidBottom && strip)
                        {
                            Draw3DLine(g, mesh.InnerBase[i], mesh.OuterBase[i], pen);
                        }

                        if (feature.SolidInner)
                        {
                            Draw3DLine(g, mesh.InnerTop[i], mesh.InnerBase[i], pen);
                        }

                        if (feature.SolidOuter && strip)
                        {
                            Draw3DLine(g, mesh.OuterTop[i], mesh.OuterBase[i], pen);
                        }
                    }
                }
            }
        }
    }

    private void DrawInactivePoints(Graphics g)
    {
        if (_doc == null)
        {
            return;
        }

        Track activeTrack = _doc.ActiveTrack;
        using Brush fill = new SolidBrush(Color.FromArgb(110, 115, 125));

        foreach (Track track in _doc.Tracks)
        {
            if (ReferenceEquals(track, activeTrack))
            {
                continue;
            }

            foreach (RoadPoint point in track.Points)
            {
                PointF? s = Project(point.Position);
                if (s != null)
                {
                    g.FillEllipse(fill, s.Value.X - 3, s.Value.Y - 3, 6, 6);
                }
            }
        }
    }

    private void DrawSegments(Graphics g)
    {
        if (!ShowSegments || _doc == null || _doc.Points.Count < 2)
        {
            return;
        }

        bool activeClosed = _doc.Points.Count >= 3 && RoadDocument.PositionsMatch(_doc.Points[0].Position, _doc.Points[_doc.Points.Count - 1].Position);
        var segments = SegmentLayout.Compute(_doc.Points, _doc.Settings, activeClosed);
        using Pen pen = new Pen(Color.FromArgb(255, 100, 220), 1.2f);
        foreach (SegmentLayout.Segment seg in segments)
        {
            Vec3 a = seg.A, b = seg.B, c = seg.C, d = seg.D;
            Vec3 downStart = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T0, activeClosed);
            Vec3 downEnd = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T1, activeClosed);
            Vec3 a2 = a + downStart, b2 = b + downEnd, c2 = c + downEnd, d2 = a2 + c2 - b2;

            // Top face: the base parallelogram Hammer reconstructs.
            Draw3DLine(g, a, b, pen);
            Draw3DLine(g, b, c, pen);
            Draw3DLine(g, c, d, pen);
            Draw3DLine(g, d, a, pen);

            Draw3DLine(g, a2, b2, pen);
            Draw3DLine(g, b2, c2, pen);
            Draw3DLine(g, c2, d2, pen);
            Draw3DLine(g, d2, a2, pen);

            Draw3DLine(g, a, a2, pen);
            Draw3DLine(g, b, b2, pen);
            Draw3DLine(g, c, c2, pen);
            Draw3DLine(g, d, d2, pen);
        }
    }

    private void DrawFeatureSegments(Graphics g)
    {
        if (!ShowFeatureSegments || _doc == null)
        {
            return;
        }

        using Pen pen = new Pen(Color.FromArgb(80, 220, 255), 1.2f);

        foreach (RoadChain chain in _doc.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            foreach (ChainFeature chainFeature in chain.CollectFeatures())
            {
                List<SegmentLayout.Segment> segments = SegmentLayout.ComputeFeatureSegments(chain, chainFeature);
                foreach (SegmentLayout.Segment seg in segments)
                {
                    Vec3 a = seg.A, b = seg.B, c = seg.C, d = seg.D;
                    Draw3DLine(g, a, b, pen);
                    Draw3DLine(g, b, c, pen);
                    Draw3DLine(g, c, d, pen);
                    Draw3DLine(g, d, a, pen);
                }
            }
        }
    }

    private void DrawPoints(Graphics g)
    {
        if (_doc == null)
        {
            return;
        }

        HashSet<int> selected = new HashSet<int>(GetSelectedIndices());
        if (selected.Count == 0 && GetSelectedIndex() >= 0)
        {
            selected.Add(GetSelectedIndex());
        }

        using Font f = new Font("Segoe UI", 9, FontStyle.Bold);
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            PointF? s = Project(_doc.Points[i].Position);
            if (s == null)
            {
                continue;
            }

            bool isSel = selected.Contains(i);
            using Brush fill = new SolidBrush(isSel ? Color.FromArgb(255, 200, 90) : Color.FromArgb(240, 100, 100));
            g.FillEllipse(fill, s.Value.X - 5, s.Value.Y - 5, 10, 10);
            g.DrawEllipse(Pens.Black, s.Value.X - 5, s.Value.Y - 5, 10, 10);
            g.DrawString(i.ToString(), f, Brushes.White, s.Value.X + 7, s.Value.Y - 7);
        }
    }

    private void DrawTitle(Graphics g)
    {
        using Font f = new Font("Segoe UI", 9, FontStyle.Bold);
        using Brush b = new SolidBrush(Color.FromArgb(225, 230, 240));
        g.DrawString("3D (freelook: WASD/QE move, hold right-drag to look, wheel fly, click select)", f, b, 8, 7);
    }

    private void DrawBorder(Graphics g)
    {
        using Pen p = new Pen(Color.FromArgb(100, 108, 120));
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    private void Draw3DLine(Graphics g, Vec3 a, Vec3 b, Pen pen)
    {
        PointF? pa = Project(a);
        PointF? pb = Project(b);
        if (pa == null || pb == null)
        {
            return;
        }

        g.DrawLine(pen, pa.Value, pb.Value);
    }

    private void DrawTexturedReference(Graphics g)
    {
        // No search-path requirement here: when no games were discovered every face falls back
        // to the world-aligned checkerboard, so toggling textures always produces visible output.
        if (!ShowTexturedReference || _referenceWorld == null)
        {
            return;
        }

        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        _frameBuffer.Resize(Width, Height);
        _frameBuffer.Clear();

        // Brushes: fan-triangulate each face using Hammer-exact UVs (uaxis/vaxis). Faces with
        // no readable material get a world-aligned checkerboard derived from the face's own
        // Hammer axes, so missing textures stay glued to the world and zoom consistently.
        foreach (VmfBrush brush in _referenceWorld.Brushes)
        {
            foreach (VmfFace face in brush.Faces)
            {
                if (IsNoDraw(face.Material) || (HideToolTextures && IsToolTexture(face.Material)))
                {
                    continue;
                }

                // Hammer culls back faces in the textured view: only the side of a face that
                // faces the camera is drawn, so stepping inside a closed solid shows nothing
                // of it (no interior texturing). The Layout grid/wireframe still draws every
                // face. Faces are single-sided here — this is a cull, not a second draw.
                // A face faces the camera when its outward normal points back at it:
                // dot(N, V0 - eye) < 0 (camera in front). Back faces give > 0 and are culled.
                if (Vec3.Dot(face.Normal, face.Vertices[0] - _eye) > 0)
                {
                    continue;
                }

                Bitmap texture = MaterialCache.GetMaterialBitmap(face.Material);
                bool fallback = MaterialCache.IsFallback(texture);
                int[] bits = GetTexturePixels(texture);
                int count = face.Vertices.Count;
                for (int i = 1; i + 1 < count; i++)
                {
                    Vec3 v0 = face.Vertices[0];
                    Vec3 v1 = face.Vertices[i];
                    Vec3 v2 = face.Vertices[i + 1];
                    GetUv(v0, face, fallback, texture.Width, texture.Height, out double u0, out double t0);
                    GetUv(v1, face, fallback, texture.Width, texture.Height, out double u1, out double t1);
                    GetUv(v2, face, fallback, texture.Width, texture.Height, out double u2, out double t2);
                    TextureRasterizer.FillTriangle(_frameBuffer,
                        new TextureRasterizer.Vertex(v0, (float)u0, (float)t0),
                        new TextureRasterizer.Vertex(v1, (float)u1, (float)t1),
                        new TextureRasterizer.Vertex(v2, (float)u2, (float)t2),
                        _eye, _forward, _right, _up, (float)_focal, Width, Height,
                        bits, texture.Width, texture.Height, fallback);
                }
            }
        }

        // Displacements: each quad becomes two triangles, UV from the side's Hammer axes.
        foreach (VmfDisplacement displacement in _referenceWorld.Displacements)
        {
            if (HideToolTextures && IsToolTexture(displacement.Material))
            {
                continue;
            }

            Bitmap texture = MaterialCache.GetMaterialBitmap(displacement.Material);
            bool fallback = MaterialCache.IsFallback(texture);
            int[] bits = GetTexturePixels(texture);
            int n = displacement.Grid.GetLength(0);
            for (int r = 0; r + 1 < n; r++)
            {
                for (int c = 0; c + 1 < n; c++)
                {
                    Vec3 p00 = displacement.Grid[r, c];
                    Vec3 p10 = displacement.Grid[r, c + 1];
                    Vec3 p11 = displacement.Grid[r + 1, c + 1];
                    Vec3 p01 = displacement.Grid[r + 1, c];
                    GetUv(p00, displacement, fallback, texture.Width, texture.Height, out double u00, out double v00);
                    GetUv(p10, displacement, fallback, texture.Width, texture.Height, out double u10, out double v10);
                    GetUv(p11, displacement, fallback, texture.Width, texture.Height, out double u11, out double v11);
                    GetUv(p01, displacement, fallback, texture.Width, texture.Height, out double u01, out double v01);

                    TextureRasterizer.FillTriangle(_frameBuffer,
                        new TextureRasterizer.Vertex(p00, (float)u00, (float)v00),
                        new TextureRasterizer.Vertex(p10, (float)u10, (float)v10),
                        new TextureRasterizer.Vertex(p11, (float)u11, (float)v11),
                        _eye, _forward, _right, _up, (float)_focal, Width, Height,
                        bits, texture.Width, texture.Height, fallback);
                    TextureRasterizer.FillTriangle(_frameBuffer,
                        new TextureRasterizer.Vertex(p00, (float)u00, (float)v00),
                        new TextureRasterizer.Vertex(p11, (float)u11, (float)v11),
                        new TextureRasterizer.Vertex(p01, (float)u01, (float)v01),
                        _eye, _forward, _right, _up, (float)_focal, Width, Height,
                        bits, texture.Width, texture.Height, fallback);
                }
            }
        }

        _frameBuffer.Blit(g, 0, 0);
    }

    // For missing-texture (fallback) faces the vertex UVs are set to the point's distance along
    // the face's own Hammer axes in checker-cell units; the rasterizer tiles them into the
    // checkerboard texture, producing a static, face-aligned grid that zooms with the camera.
    private static void GetUv(Vec3 p, VmfFace face, bool fallback, double texW, double texH, out double u, out double v)
    {
        if (fallback)
        {
            u = Vec3.Dot(face.UAxis, p) / TextureRasterizer.FallbackCellSize;
            v = Vec3.Dot(face.VAxis, p) / TextureRasterizer.FallbackCellSize;
            return;
        }

        face.GetUV(p, texW, texH, out u, out v);
    }

    private static void GetUv(Vec3 p, VmfDisplacement displacement, bool fallback, double texW, double texH, out double u, out double v)
    {
        if (fallback)
        {
            u = Vec3.Dot(displacement.UAxis, p) / TextureRasterizer.FallbackCellSize;
            v = Vec3.Dot(displacement.VAxis, p) / TextureRasterizer.FallbackCellSize;
            return;
        }

        displacement.GetUV(p, texW, texH, out u, out v);
    }

    private int[] GetTexturePixels(Bitmap bitmap)
    {
        if (_texturePixels.TryGetValue(bitmap, out int[] cached))
        {
            return cached;
        }

        int[] bits = ReadBitmapPixels(bitmap);
        _texturePixels[bitmap] = bits;
        return bits;
    }

    private static int[] ReadBitmapPixels(Bitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        int[] bits = new int[width * height];
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, bits, 0, bits.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bits;
    }

    private void DrawReferenceWorld(Graphics g)
    {
        if (!ShowReferenceWorld || _referenceWorld == null)
        {
            return;
        }

        using Pen brushPen = new Pen(Color.FromArgb(150, 162, 182), 1f);
        foreach (VmfBrush brush in _referenceWorld.Brushes)
        {
            foreach (VmfFace face in brush.Faces)
            {
                if (IsNoDraw(face.Material) || (HideToolTextures && IsToolTexture(face.Material)))
                {
                    continue;
                }

                DrawClosedLoop3D(g, face.Vertices, brushPen);
            }
        }

        using Pen displacementPen = new Pen(Color.FromArgb(120, 220, 150), 1f);
        foreach (VmfDisplacement displacement in _referenceWorld.Displacements)
        {
            if (HideToolTextures && IsToolTexture(displacement.Material))
            {
                continue;
            }

            DrawDisplacementWire3D(g, displacement, displacementPen);
        }
    }

    private void DrawClosedLoop3D(Graphics g, IReadOnlyList<Vec3> pts, Pen pen)
    {
        if (pts.Count < 3)
        {
            return;
        }

        for (int i = 0; i < pts.Count; i++)
        {
            Draw3DLine(g, pts[i], pts[(i + 1) % pts.Count], pen);
        }
    }

    private void DrawDisplacementWire3D(Graphics g, VmfDisplacement displacement, Pen pen)
    {
        int n = displacement.Grid.GetLength(0);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (c + 1 < n)
                {
                    Draw3DLine(g, displacement.Grid[r, c], displacement.Grid[r, c + 1], pen);
                }

                if (r + 1 < n)
                {
                    Draw3DLine(g, displacement.Grid[r, c], displacement.Grid[r + 1, c], pen);
                }
            }
        }
    }

    private static bool IsNoDraw(string material) =>
        material != null && material.IndexOf("NODRAW", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsToolTexture(string material) =>
        material != null && material.TrimStart('/').StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            _clickCandidate = true;
            _downPos = e.Location;
            TryPick(e.Location, out int idx);
            PointSelected?.Invoke(idx, (ModifierKeys & Keys.Control) != 0);
        }
        else if (e.Button == MouseButtons.Right)
        {
            // Freelook: hold the right button to look around from the current position.
            StartLooking();
            _autoTarget = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_looking)
        {
            // The cursor is held at the view centre; the offset from centre turns the camera.
            Point centre = new Point(ClientRectangle.Width / 2, ClientRectangle.Height / 2);
            int dx = e.X - centre.X;
            int dy = e.Y - centre.Y;
            if (dx != 0 || dy != 0)
            {
                _yaw += dx * 0.005;
                _pitch += dy * 0.005;
                _pitch = Math.Max(-1.55, Math.Min(1.55, _pitch));
                Invalidate();
            }

            CenterLookCursor();
        }
        else if (_clickCandidate)
        {
            int moved = Math.Abs(e.X - _downPos.X) + Math.Abs(e.Y - _downPos.Y);
            if (moved > 4)
            {
                _clickCandidate = false;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_looking && e.Button == MouseButtons.Right)
        {
            StopLooking();
        }
        else if (e.Button == MouseButtons.Left)
        {
            _clickCandidate = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _autoTarget = false;

        int notches = e.Delta / 120;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            // Shift+wheel tunes the fly speed instead of moving.
            _flySpeed *= notches > 0 ? 1.25 : 1.0 / 1.25;
            _flySpeed = Math.Max(100, Math.Min(200000, _flySpeed));
        }
        else
        {
            // Wheel dollies along the view direction (Hammer-style fly).
            _eye = _eye + _forward * (_flySpeed * 0.20 * notches);
        }

        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        SetKey(e.KeyCode, true);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        SetKey(e.KeyCode, false);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        StopKeys();
        if (_looking)
        {
            StopLooking();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flyTimer.Stop();
            _flyTimer.Dispose();
            Cursor.Show(); // in case a look session left it hidden
        }

        base.Dispose(disposing);
    }

    private void SetKey(Keys key, bool down)
    {
        switch (key)
        {
            case Keys.W: _keyForward = down; break;
            case Keys.S: _keyBack = down; break;
            case Keys.A: _keyLeft = down; break;
            case Keys.D: _keyRight = down; break;
            case Keys.Q: _keyUp = down; break;
            case Keys.E: _keyDown = down; break;
            default: return;
        }

        if (down)
        {
            _autoTarget = false;
            if (!_flyTimer.Enabled)
            {
                _lastMoveMs = Environment.TickCount64; // reset baseline so the first frame doesn't jump
                _flyTimer.Start();
            }
        }
        else if (!_keyForward && !_keyBack && !_keyLeft && !_keyRight && !_keyUp && !_keyDown)
        {
            _flyTimer.Stop();
        }
    }

    private void StopKeys()
    {
        _keyForward = _keyBack = _keyLeft = _keyRight = _keyUp = _keyDown = false;
        _flyTimer.Stop();
    }

    private void OnFlyTimerTick(object sender, EventArgs e)
    {
        // The timer no longer moves the camera — that happens once per rendered frame in
        // ApplyFlyMovement. Its only job here is to keep repaints coming while a fly key is
        // held but nothing else (mouse look, etc.) is invalidating the viewport.
        if (!_keyForward && !_keyBack && !_keyLeft && !_keyRight && !_keyUp && !_keyDown)
        {
            _flyTimer.Stop();
            return;
        }

        Invalidate();
    }

    /// <summary>Advances the camera while a fly key is held. Called from OnPaint every rendered
    /// frame, so motion is driven by the frames that are actually being drawn rather than by the
    /// 16 ms timer. During mouse look every mouse move floods the UI thread with repaints (each
    /// a full software-textured frame), which starves the low-priority WM_TIMER messages — that
    /// is what made WASD/Q/E appear dead while looking. Tying movement to OnPaint fixes it: as
    /// long as the viewport is drawing frames (which it must be for the look rotation to be
    /// visible), the camera advances by the real elapsed time between frames.</summary>
    private void ApplyFlyMovement()
    {
        // Refresh the baseline on every frame so a later key press can't inherit a huge dt.
        long nowMs = Environment.TickCount64;
        double dt = (nowMs - _lastMoveMs) / 1000.0;
        _lastMoveMs = nowMs;
        if (dt <= 0.0)
        {
            dt = _flyTimer.Interval / 1000.0;
        }
        else if (dt > 0.25)
        {
            dt = 0.25; // clamp after a long stall so the camera doesn't teleport
        }

        if (!_keyForward && !_keyBack && !_keyLeft && !_keyRight && !_keyUp && !_keyDown)
        {
            return;
        }

        double speed = _flySpeed;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            speed *= 3.5;
        }

        Vec3 move = Vec3.Zero;
        if (_keyForward) move += _forward;
        if (_keyBack) move -= _forward;
        if (_keyRight) move += _right;
        if (_keyLeft) move -= _right;
        if (_keyUp) move += _up;
        if (_keyDown) move -= _up;

        if (move.LengthSq > 0)
        {
            Vec3 dir = move.Normalized();
            _eye += dir * (speed * MovementSpeedScale * dt);
        }
    }

    private void StartLooking()
    {
        _looking = true;
        Capture = true;
        Cursor.Hide();
        CenterLookCursor();
    }

    private void StopLooking()
    {
        _looking = false;
        Capture = false;
        Cursor.Show();
    }

    private void CenterLookCursor()
    {
        Point centre = new Point(ClientRectangle.Width / 2, ClientRectangle.Height / 2);
        Cursor.Position = PointToScreen(centre);
    }

    private bool TryPick(Point s, out int index)
    {
        index = -1;
        if (_doc == null)
        {
            return false;
        }

        double best = 14;
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            PointF? p = Project(_doc.Points[i].Position);
            if (p == null)
            {
                continue;
            }

            double d = Math.Sqrt((p.Value.X - s.X) * (p.Value.X - s.X) + (p.Value.Y - s.Y) * (p.Value.Y - s.Y));
            if (d < best)
            {
                best = d;
                index = i;
            }
        }

        return index >= 0;
    }
}
