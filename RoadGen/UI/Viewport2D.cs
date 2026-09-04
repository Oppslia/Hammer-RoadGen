using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RoadGen.Core;

namespace RoadGen.UI;

/// <summary>An orthographic 2D viewport (Top, Front or Side) with pan, zoom and
/// point editing (click to select, drag to move, ctrl+click to add).</summary>
public sealed class Viewport2D : Control
{
    public enum PlaneKind
    {
        Top,
        Front,
        Side
    }

    private RoadDocument _doc;
    private VmfWorld _referenceWorld;
    private PlaneKind _plane = PlaneKind.Top;
    private Vec3 _center = new Vec3(512, 512, 0);
    private double _zoom = 0.45;
    private bool _pendingFrame = true;

    // Cached bitmap of the grid + axes. These depend only on the view transform
    // and snap, not on the road, so they are re-rendered only when the view
    // changes and blitted on every frame (point dragging gets much faster).
    private Bitmap _gridCache;
    private double _gridCacheCenterX;
    private double _gridCacheCenterY;
    private double _gridCacheZoom;
    private double _gridCacheSnap;

    private bool _panning;
    private Point _panStartScreen;
    private Vec3 _panStartCenter;

    private int _dragIndex = -1;
    private bool _dragging;
    private Point _dragStartScreen;
    private Vec3 _dragOrigin;
    private List<int> _moveIndices;
    private List<Vec3> _moveOrigins;

    // Captured on mouse-down so a drag keeps the existing selection intact while
    // a plain click can still collapse to a single point on release.
    private bool _wasSelectedOnDown;
    private bool _ctrlOnDown;

    private bool _boxPending;
    private bool _boxSelecting;
    private Point _boxStart;
    private Point _boxCurrent;

    // Tooltip shown when hovering a welded (joined) node, hinting how to break the
    // weld. _hoverWeldIndex tracks the point currently showing the tooltip so it
    // stays put while hovering and only hides when the target changes. The tooltip
    // is debounced: the pointer must hold on the node for _hoverDelay before it
    // appears.
    private readonly ToolTip _hoverToolTip = new ToolTip();
    private readonly System.Windows.Forms.Timer _hoverTimer = new System.Windows.Forms.Timer { Interval = 500 };
    private Point _hoverLocation;
    private int _hoverWeldIndex = -2;

    public Action<int, bool> PointSelected;
    public Action<IReadOnlyList<int>, bool> BoxSelected;
    public Action<IReadOnlyList<int>> PointsEdited;
    public Action<int> PointAdded;
    public Action EditBegin;
    public Action EditEnd;
    public Action PointAddBegin;
    public Func<int> GetSelectedIndex = () => -1;
    public Func<IReadOnlyCollection<int>> GetSelectedIndices = () => Array.Empty<int>();

    public Viewport2D()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(42, 42, 46);
        TabStop = false;
        _hoverToolTip.AutoPopDelay = 6000;
        _hoverToolTip.InitialDelay = 250;
        _hoverToolTip.ReshowDelay = 100;
        _hoverToolTip.ShowAlways = true;
        _hoverTimer.Tick += OnHoverTimerTick;
    }

    public void SetDocument(RoadDocument doc) => _doc = doc;

    public bool ShowSegments;
    public bool ShowFeatureSegments;
    public bool ShowReferenceWorld = true;

    /// <summary>When true (default), faces of the imported layout whose material is a Source
    /// tool texture (tools/*, e.g. clip/skip/areaportal) are not drawn, matching Hammer hiding
    /// tool brushes from the view.</summary>
    public bool HideToolTextures = true;

    /// <summary>Shared cordon (owned by MainWindow). While it is active, imported layout
    /// brushes/displacements whose bounds don't intersect the box are not drawn and the box is
    /// outlined in red. The road editing preview is never culled by the cordon.</summary>
    private Cordon _cordon;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Cordon Cordon
    {
        get => _cordon;
        set
        {
            if (ReferenceEquals(_cordon, value))
            {
                return;
            }

            if (_cordon != null)
            {
                _cordon.Changed -= OnCordonChanged;
            }

            _cordon = value;
            if (_cordon != null)
            {
                _cordon.Changed += OnCordonChanged;
            }

            Invalidate();
        }
    }

    /// <summary>When true the cordon tool is armed: left-drag in this view re-draws the
    /// cordon box on the two axes of the current plane (the third axis keeps its existing
    /// range), and the box is drawn even while culling is off.</summary>
    public bool CordonEditing;

    private void OnCordonChanged()
    {
        Invalidate();
    }

    // Cordon edit-drag state (tool armed). The out-of-plane axis keeps the box's range from
    // the moment the drag began, so a Top-view drag selects an X/Y region at the full existing
    // height rather than collapsing the box to a flat slab.
    private bool _cordonDragging;
    private Point _cordonDragStart;
    private Vec3 _cordonStartMins;
    private Vec3 _cordonStartMaxs;

    // Which part of the box a cordon drag grabbed: 0..3 = a corner handle (resize) or
    // CordonGripMove = inside the box (translate). Pressing empty space (CordonGripNone)
    // does nothing — the box is NEVER redrawn from a click-drag; it always exists and is
    // moved/resized instead. For a corner drag the two per-axis halves (min vs max) are
    // tracked separately and flip when the pointer crosses the opposite side.
    private int _cordonGrip = CordonGripMove;
    private bool _cordonGripHMax;
    private bool _cordonGripVMax;
    private const int CordonGripMove = -1;   // drag inside the box translates it
    private const int CordonGripNone = -2;   // press on empty space: no action

    // Lazily computed AABB per imported brush/displacement, used only while the cordon is
    // active so culling never has to re-walk vertices every frame. Cleared with the world.
    private readonly Dictionary<VmfBrush, (Vec3 Min, Vec3 Max)> _brushBounds = new Dictionary<VmfBrush, (Vec3 Min, Vec3 Max)>();
    private readonly Dictionary<VmfDisplacement, (Vec3 Min, Vec3 Max)> _dispBounds = new Dictionary<VmfDisplacement, (Vec3 Min, Vec3 Max)>();

    /// <summary>Sets the imported VMF layout to render as a reference behind the road.</summary>
    public void SetReferenceWorld(VmfWorld world)
    {
        _referenceWorld = world;
        _brushBounds.Clear();
        _dispBounds.Clear();
        Invalidate();
    }

    public void SetPlane(PlaneKind plane)
    {
        _plane = plane;
        Invalidate();
    }

    /// <summary>Cancel any in-progress point drag/pan/box selection. Called when a
    /// point is deleted mid-drag so the drag's stale indices don't keep moving the
    /// wrong points after the list shifts.</summary>
    public void CancelDrag()
    {
        if (_dragging)
        {
            EditEnd?.Invoke();
        }

        _dragIndex = -1;
        _dragging = false;
        _boxPending = false;
        _boxSelecting = false;
        _panning = false;
        _cordonDragging = false;
        Capture = false;
        Cursor = Cursors.Default;
    }

    public void FrameAll()
    {
        // Defer until the control has a real size (applied in OnPaint).
        _pendingFrame = true;
        Invalidate();
    }

    private void ApplyFrame()
    {
        if (_doc == null)
        {
            return;
        }

        bool foundAny = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;
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

        if (!foundAny)
        {
            return;
        }

        Vec3 mid = (min + max) / 2.0;
        double span = Math.Max(128, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)) + 256);

        if (_plane == PlaneKind.Top)
        {
            _center = new Vec3(mid.X, mid.Y, 0);
            _zoom = Math.Min(Width, Height) / span;
        }
        else if (_plane == PlaneKind.Front)
        {
            _center = new Vec3(mid.X, mid.Z, 0);
            _zoom = Math.Min(Width, Height) / span;
        }
        else
        {
            _center = new Vec3(mid.Y, mid.Z, 0);
            _zoom = Math.Min(Width, Height) / span;
        }

        _zoom = Math.Max(0.001, Math.Min(10, _zoom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_pendingFrame && Width > 0 && Height > 0)
        {
            ApplyFrame();
            _pendingFrame = false;
        }

        DrawGridAndAxes(g);
        DrawReferenceWorld(g);

        if (_doc == null)
        {
            DrawBorder(g);
            DrawTitle(g);
            return;
        }

        DrawAllTracks(g);
        DrawEdgeFeatures(g);
        DrawSegments(g);
        DrawFeatureSegments(g);
        DrawInactivePoints(g);
        DrawPoints(g);
        DrawHint(g);
        DrawBox(g);
        DrawCordon(g);
        DrawTitle(g);
        DrawBorder(g);
    }

    private void DrawGridAndAxes(Graphics g)
    {
        double snap = _doc != null && _doc.Settings.Snap > 0 ? _doc.Settings.Snap : 64;
        if (snap <= 0)
        {
            snap = 64;
        }

        // Regenerate the cached grid only when the view transform, size or snap
        // changed. During point dragging none of these change, so each frame just
        // blits the cached bitmap instead of drawing hundreds of AA lines.
        if (_gridCache == null
            || _gridCache.Width != Width
            || _gridCache.Height != Height
            || _gridCacheCenterX != _center.X
            || _gridCacheCenterY != _center.Y
            || _gridCacheZoom != _zoom
            || _gridCacheSnap != snap)
        {
            _gridCache?.Dispose();
            _gridCache = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            using (Graphics bg = Graphics.FromImage(_gridCache))
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.Clear(BackColor);
                DrawGrid(bg);
                DrawAxis(bg);
            }

            _gridCacheCenterX = _center.X;
            _gridCacheCenterY = _center.Y;
            _gridCacheZoom = _zoom;
            _gridCacheSnap = snap;
        }

        g.DrawImage(_gridCache, 0, 0, Width, Height);
    }

    private void DrawGrid(Graphics g)
    {
        double snap = _doc != null && _doc.Settings.Snap > 0 ? _doc.Settings.Snap : 64;
        if (snap <= 0)
        {
            snap = 64;
        }

        double left = ScreenToWorldX(0);
        double right = ScreenToWorldX(Width);
        double top = ScreenToWorldY(0);
        double bottom = ScreenToWorldY(Height);

        double minX = Math.Min(left, right);
        double maxX = Math.Max(left, right);
        double minY = Math.Min(top, bottom);
        double maxY = Math.Max(top, bottom);

        // Keep the grid glued to the configured size (snap) so it behaves like a
        // stable world-space lattice (Hammer-style) instead of re-scaling to the
        // current perspective as you zoom. Only coarsen the spacing when the line
        // count would be absurd (very far zoom-out, where the lines are sub-pixel
        // anyway), rather than doubling at a modest cell count.
        double span = Math.Max(maxX - minX, maxY - minY);
        const int maxLines = 2000;
        while (span / snap > maxLines)
        {
            snap *= 2;
        }

        using Pen minor = new Pen(Color.FromArgb(72, 78, 88));
        using Pen major = new Pen(Color.FromArgb(140, 150, 164));
        using Pen zero = new Pen(Color.FromArgb(95, 175, 110), 1.5f);

        int startI = (int)Math.Floor(minX / snap);
        int endI = (int)Math.Ceiling(maxX / snap);
        for (int i = startI; i <= endI; i++)
        {
            double wx = i * snap;
            PointF a = WorldToScreenF(PlaneVec(wx, minY, 0));
            PointF b = WorldToScreenF(PlaneVec(wx, maxY, 0));
            Pen pen = i == 0 ? zero : (i % 8 == 0 ? major : minor);
            g.DrawLine(pen, a, b);
        }

        int startJ = (int)Math.Floor(minY / snap);
        int endJ = (int)Math.Ceiling(maxY / snap);
        for (int j = startJ; j <= endJ; j++)
        {
            double wy = j * snap;
            PointF a = WorldToScreenF(PlaneVec(minX, wy, 0));
            PointF b = WorldToScreenF(PlaneVec(maxX, wy, 0));
            Pen pen = j == 0 ? zero : (j % 8 == 0 ? major : minor);
            g.DrawLine(pen, a, b);
        }
    }

    private void DrawAxis(Graphics g)
    {
        using Font f = new Font("Segoe UI", 8, FontStyle.Bold);
        using Brush b = new SolidBrush(Color.FromArgb(235, 235, 240));
        using Pen hp = new Pen(Color.FromArgb(235, 90, 90));
        using Pen vp = new Pen(Color.FromArgb(90, 150, 235));

        string hLabel, vLabel;
        switch (_plane)
        {
            case PlaneKind.Top:
                hLabel = "X";
                vLabel = "Y";
                break;
            case PlaneKind.Front:
                hLabel = "X";
                vLabel = "Z";
                break;
            default:
                hLabel = "Y";
                vLabel = "Z";
                break;
        }

        double minH = ScreenToWorldX(0);
        double maxH = ScreenToWorldX(Width);
        double maxV = ScreenToWorldY(0);
        double minV = ScreenToWorldY(Height);

        // Full-length axes through the origin (only when the origin is in view).
        if (minV <= 0 && maxV >= 0)
        {
            PointF a = WorldToScreenF(PlaneVec(minH, 0, 0));
            PointF b2 = WorldToScreenF(PlaneVec(maxH, 0, 0));
            g.DrawLine(hp, a, b2);
        }

        if (minH <= 0 && maxH >= 0)
        {
            PointF a = WorldToScreenF(PlaneVec(0, minV, 0));
            PointF b2 = WorldToScreenF(PlaneVec(0, maxV, 0));
            g.DrawLine(vp, a, b2);
        }

        // Always show a small axis indicator in the bottom-left corner.
        PointF corner = new PointF(16, Height - 30);
        g.DrawLine(hp, corner, new PointF(corner.X + 18, corner.Y));
        g.DrawLine(vp, corner, new PointF(corner.X, corner.Y - 18));
        g.DrawString(hLabel, f, b, corner.X + 21, corner.Y - 7);
        g.DrawString(vLabel, f, b, corner.X - 8, corner.Y - 30);
    }

    private Vec3 PlaneVec(double h, double v, double o) => _plane switch
    {
        PlaneKind.Top => new Vec3(h, v, o),
        PlaneKind.Front => new Vec3(h, o, v),
        _ => new Vec3(o, h, v)
    };

    private void DrawAllTracks(Graphics g)
    {
        Track activeTrack = _doc.ActiveTrack;
        const int stepsPerSegment = 24;

        foreach (RoadChain chain in _doc.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            RoadPreviewMesh mesh = RoadPreviewMesh.Build(chain.Points, stepsPerSegment, chain.Closed);

            // Draw each track's own portion with that track's settings (solid
            // sides, thickness) and active/muted color. The chain shares one
            // spline so the road still flows smoothly through the junction.
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

        using Pen edge = new Pen(isActive ? Color.FromArgb(80, 190, 255) : Color.FromArgb(105, 115, 130), isActive ? 2.6f : 1.6f);
        using Pen center = new Pen(isActive ? Color.FromArgb(120, 235, 120) : Color.FromArgb(95, 105, 120), isActive ? 2.4f : 1.4f);
        using Pen rib = new Pen(isActive ? Color.FromArgb(85, 85, 95) : Color.FromArgb(58, 60, 68), 2f);
        using Pen wall = new Pen(isActive ? Color.FromArgb(255, 160, 70) : Color.FromArgb(80, 85, 98), 2.2f);

        bool showThickness = _plane != PlaneKind.Top;

        DrawPolylineRange(g, mesh.Left, edge, startIndex, endIndex);
        DrawPolylineRange(g, mesh.Right, edge, startIndex, endIndex);
        DrawPolylineRange(g, mesh.Center, center, startIndex, endIndex);

        if (!ShowSegments)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 4 == 0)
                {
                    g.DrawLine(rib, WorldToScreenF(mesh.Left[i]), WorldToScreenF(mesh.Right[i]));
                }
            }
        }

        if (showThickness && (settings.SolidBottom || settings.SolidLeft))
        {
            DrawPolylineRange(g, mesh.BottomLeft, wall, startIndex, endIndex);
        }

        if (showThickness && (settings.SolidBottom || settings.SolidRight))
        {
            DrawPolylineRange(g, mesh.BottomRight, wall, startIndex, endIndex);
        }

        if (showThickness && settings.SolidLeft)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 4 == 0)
                {
                    g.DrawLine(wall, WorldToScreenF(mesh.Left[i]), WorldToScreenF(mesh.BottomLeft[i]));
                }
            }
        }

        if (showThickness && settings.SolidRight)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 4 == 0)
                {
                    g.DrawLine(wall, WorldToScreenF(mesh.Right[i]), WorldToScreenF(mesh.BottomRight[i]));
                }
            }
        }

        if (showThickness && settings.SolidBottom && !ShowSegments)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (i % 4 == 0)
                {
                    g.DrawLine(rib, WorldToScreenF(mesh.BottomLeft[i]), WorldToScreenF(mesh.BottomRight[i]));
                }
            }
        }
    }

    private void DrawPolylineRange(Graphics g, IReadOnlyList<Vec3> points, Pen pen, int startIndex, int endIndex)
    {
        if (startIndex >= endIndex)
        {
            return;
        }

        PointF previous = WorldToScreenF(points[startIndex]);
        for (int index = startIndex + 1; index <= endIndex; index++)
        {
            PointF current = WorldToScreenF(points[index]);
            g.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    private void DrawEdgeFeatures(Graphics g)
    {
        if (_doc == null)
        {
            return;
        }

        Track activeTrack = _doc.ActiveTrack;
        const int stepsPerSegment = 24;

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
                // mirrors DrawAllTracks, which colors each span by its own track.
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
                    using Pen pen = new Pen(isActive ? Color.FromArgb(155, 175, 255) : Color.FromArgb(92, 98, 114), isActive ? 2.0f : 1.4f);

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

                    for (int i = startIndex; i <= endIndex; i += 4)
                    {
                        if (strip)
                        {
                            g.DrawLine(pen, WorldToScreenF(mesh.InnerTop[i]), WorldToScreenF(mesh.OuterTop[i]));
                        }

                        if (feature.SolidBottom && strip)
                        {
                            g.DrawLine(pen, WorldToScreenF(mesh.InnerBase[i]), WorldToScreenF(mesh.OuterBase[i]));
                        }

                        if (feature.SolidInner)
                        {
                            g.DrawLine(pen, WorldToScreenF(mesh.InnerTop[i]), WorldToScreenF(mesh.InnerBase[i]));
                        }

                        if (feature.SolidOuter && strip)
                        {
                            g.DrawLine(pen, WorldToScreenF(mesh.OuterTop[i]), WorldToScreenF(mesh.OuterBase[i]));
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
                PointF s = WorldToScreenF(point.Position);
                g.FillEllipse(fill, s.X - 3, s.Y - 3, 6, 6);
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
        using Pen pen = new Pen(Color.FromArgb(255, 100, 220), 1.1f);
        foreach (SegmentLayout.Segment seg in segments)
        {
            Vec3 a = seg.A, b = seg.B, c = seg.C, d = seg.D;
            Vec3 downStart = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T0, activeClosed);
            Vec3 downEnd = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T1, activeClosed);
            Vec3 a2 = a + downStart, b2 = b + downEnd, c2 = c + downEnd, d2 = a2 + c2 - b2;

            // Top face: the base parallelogram Hammer reconstructs.
            g.DrawLine(pen, WorldToScreenF(a), WorldToScreenF(b));
            g.DrawLine(pen, WorldToScreenF(b), WorldToScreenF(c));
            g.DrawLine(pen, WorldToScreenF(c), WorldToScreenF(d));
            g.DrawLine(pen, WorldToScreenF(d), WorldToScreenF(a));

            g.DrawLine(pen, WorldToScreenF(a2), WorldToScreenF(b2));
            g.DrawLine(pen, WorldToScreenF(b2), WorldToScreenF(c2));
            g.DrawLine(pen, WorldToScreenF(c2), WorldToScreenF(d2));
            g.DrawLine(pen, WorldToScreenF(d2), WorldToScreenF(a2));

            g.DrawLine(pen, WorldToScreenF(a), WorldToScreenF(a2));
            g.DrawLine(pen, WorldToScreenF(b), WorldToScreenF(b2));
            g.DrawLine(pen, WorldToScreenF(c), WorldToScreenF(c2));
            g.DrawLine(pen, WorldToScreenF(d), WorldToScreenF(d2));
        }
    }

    private void DrawFeatureSegments(Graphics g)
    {
        if (!ShowFeatureSegments || _doc == null)
        {
            return;
        }

        using Pen pen = new Pen(Color.FromArgb(80, 220, 255), 1.1f);

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
                    g.DrawLine(pen, WorldToScreenF(a), WorldToScreenF(b));
                    g.DrawLine(pen, WorldToScreenF(b), WorldToScreenF(c));
                    g.DrawLine(pen, WorldToScreenF(c), WorldToScreenF(d));
                    g.DrawLine(pen, WorldToScreenF(d), WorldToScreenF(a));
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

        var selected = new HashSet<int>(GetSelectedIndices());
        if (selected.Count == 0 && GetSelectedIndex() >= 0)
        {
            selected.Add(GetSelectedIndex());
        }

        using Font f = new Font("Segoe UI", 8, FontStyle.Bold);
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            Vec3 p = _doc.Points[i].Position;
            PointF s = WorldToScreenF(p);
            bool isSel = selected.Contains(i);
            using Brush fill = new SolidBrush(isSel ? Color.FromArgb(255, 200, 90) : Color.FromArgb(235, 90, 90));
            g.FillEllipse(fill, s.X - 5, s.Y - 5, 10, 10);
            g.DrawEllipse(Pens.Black, s.X - 5, s.Y - 5, 10, 10);
            g.DrawString(i.ToString(), f, Brushes.White, s.X + 7, s.Y - 6);
        }
    }

    private void DrawTitle(Graphics g)
    {
        string name = _plane switch
        {
            PlaneKind.Top => "Top (X/Y)",
            PlaneKind.Front => "Front (X/Z)",
            _ => "Side (Y/Z)"
        };

        using Font f = new Font("Segoe UI", 9, FontStyle.Bold);
        using Brush b = new SolidBrush(Color.FromArgb(225, 230, 240));
        g.DrawString(name, f, b, 8, 7);
    }

    private void DrawBorder(Graphics g)
    {
        using Pen p = new Pen(Color.FromArgb(100, 108, 120));
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    private void DrawHint(Graphics g)
    {
        if (_doc == null || _doc.Points.Count >= 2)
        {
            return;
        }

        using Font f = new Font("Segoe UI", 9);
        using Brush b = new SolidBrush(Color.FromArgb(185, 190, 200));
        string msg = "Ctrl+click to add points";
        SizeF size = g.MeasureString(msg, f);
        g.DrawString(msg, f, b, (Width - size.Width) / 2f, Height / 2f - 8);
    }

    private void DrawBox(Graphics g)
    {
        if (!_boxSelecting)
        {
            return;
        }

        // Snap the box corners to the grid (when snapping is enabled), so the
        // selection window follows the snap setting like the point tool does.
        GetBoxWorldBounds(out double hMin, out double hMax, out double vMin, out double vMax);

        using Pen pen = new Pen(Color.FromArgb(220, 220, 230))
        {
            DashStyle = DashStyle.Dash
        };

        PointF tl = WorldToScreenF(PlaneVec(hMin, vMax, 0));
        PointF br = WorldToScreenF(PlaneVec(hMax, vMin, 0));
        g.DrawRectangle(pen, RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y));

        // Show the selection's world size next to the box.
        double width = Math.Abs(hMax - hMin);
        double height = Math.Abs(vMax - vMin);
        (string hLabel, string vLabel) = PlaneLabels();
        string size = $"{hLabel}: {width:0.#}  {vLabel}: {height:0.#}";

        using Font f = new Font("Segoe UI", 8);
        SizeF textSize = g.MeasureString(size, f);
        PointF labelAt = new PointF(tl.X, tl.Y - textSize.Height - 3);
        using Brush bg = new SolidBrush(Color.FromArgb(180, 24, 24, 28));
        g.FillRectangle(bg, labelAt.X, labelAt.Y, textSize.Width + 6, textSize.Height + 4);
        using Brush text = new SolidBrush(Color.FromArgb(235, 235, 240));
        g.DrawString(size, f, text, labelAt.X + 3, labelAt.Y + 2);
    }

    /// <summary>World-space bounds of the current box selection, with the corners
    /// snapped to the grid when snapping is enabled.</summary>
    private void GetBoxWorldBounds(out double hMin, out double hMax, out double vMin, out double vMax)
    {
        double outOfPlane = GetDefaultOutOfPlane();
        Vec3 a = Snap(ScreenToWorld(_boxStart, outOfPlane));
        Vec3 b = Snap(ScreenToWorld(_boxCurrent, outOfPlane));
        double ah = PlaneHorizontal(a);
        double av = PlaneVertical(a);
        double bh = PlaneHorizontal(b);
        double bv = PlaneVertical(b);
        hMin = Math.Min(ah, bh);
        hMax = Math.Max(ah, bh);
        vMin = Math.Min(av, bv);
        vMax = Math.Max(av, bv);
    }

    private (string h, string v) PlaneLabels() => _plane switch
    {
        PlaneKind.Top => ("X", "Y"),
        PlaneKind.Front => ("X", "Z"),
        _ => ("Y", "Z")
    };

    private void DrawReferenceWorld(Graphics g)
    {
        if (!ShowReferenceWorld || _referenceWorld == null)
        {
            return;
        }

        using Pen brushPen = new Pen(Color.FromArgb(150, 162, 182), 1f);
        foreach (VmfBrush brush in _referenceWorld.Brushes)
        {
            if (IsCordonCulled(brush))
            {
                continue;
            }

            foreach (VmfFace face in brush.Faces)
            {
                if (IsNoDraw(face.Material) || (HideToolTextures && IsToolTexture(face.Material)))
                {
                    continue;
                }

                DrawClosedLoop(g, face.Vertices, brushPen);
            }
        }

        using Pen displacementPen = new Pen(Color.FromArgb(120, 220, 150), 1f);
        foreach (VmfDisplacement displacement in _referenceWorld.Displacements)
        {
            if (IsCordonCulled(displacement))
            {
                continue;
            }

            if (HideToolTextures && IsToolTexture(displacement.Material))
            {
                continue;
            }

            DrawDisplacementWire(g, displacement, displacementPen);
        }
    }

    private void DrawClosedLoop(Graphics g, IReadOnlyList<Vec3> pts, Pen pen)
    {
        if (pts.Count < 3)
        {
            return;
        }

        for (int i = 0; i < pts.Count; i++)
        {
            g.DrawLine(pen, WorldToScreenF(pts[i]), WorldToScreenF(pts[(i + 1) % pts.Count]));
        }
    }

    private void DrawDisplacementWire(Graphics g, VmfDisplacement displacement, Pen pen)
    {
        int n = displacement.Grid.GetLength(0);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (c + 1 < n)
                {
                    g.DrawLine(pen, WorldToScreenF(displacement.Grid[r, c]), WorldToScreenF(displacement.Grid[r, c + 1]));
                }

                if (r + 1 < n)
                {
                    g.DrawLine(pen, WorldToScreenF(displacement.Grid[r, c]), WorldToScreenF(displacement.Grid[r + 1, c]));
                }
            }
        }
    }

    private static bool IsNoDraw(string material) =>
        material != null && material.IndexOf("NODRAW", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsToolTexture(string material) =>
        material != null && material.TrimStart('/').StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a brush lies entirely outside the active cordon and should not be
    /// drawn. When the cordon is off this is always false and costs nothing (bounds are only
    /// computed on demand and cached per brush).</summary>
    private bool IsCordonCulled(VmfBrush brush)
    {
        Cordon c = Cordon;
        if (c == null || !c.Active)
        {
            return false;
        }

        if (!_brushBounds.TryGetValue(brush, out (Vec3 Min, Vec3 Max) b))
        {
            Vec3 min = Vec3.Zero, max = Vec3.Zero;
            bool any = false;
            foreach (VmfFace face in brush.Faces)
            {
                foreach (Vec3 v in face.Vertices)
                {
                    if (!any)
                    {
                        min = v;
                        max = v;
                        any = true;
                    }
                    else
                    {
                        min = Vec3.Min(min, v);
                        max = Vec3.Max(max, v);
                    }
                }
            }

            b = (min, max);
            _brushBounds[brush] = b;
        }

        return c.Culls(b.Min, b.Max);
    }

    /// <summary>Cordon cull test for a displacement, with the same on-demand cached AABB.</summary>
    private bool IsCordonCulled(VmfDisplacement displacement)
    {
        Cordon c = Cordon;
        if (c == null || !c.Active)
        {
            return false;
        }

        if (!_dispBounds.TryGetValue(displacement, out (Vec3 Min, Vec3 Max) b))
        {
            List<Vec3> points = new List<Vec3>();
            int n = displacement.Grid.GetLength(0);
            for (int r = 0; r < n; r++)
            {
                for (int col = 0; col < n; col++)
                {
                    points.Add(displacement.Grid[r, col]);
                }
            }

            Cordon.ComputeBounds(points, out Vec3 min, out Vec3 max);
            b = (min, max);
            _dispBounds[displacement] = b;
        }

        return c.Culls(b.Min, b.Max);
    }

    /// <summary>Outlines the cordon box on this view's plane so the user can see what is
    /// inside it. Red while cordoning is active (culling on); pale cyan while the cordon tool
    /// is armed but not yet active. Because each 2D plane is axis-aligned, projecting the
    /// box's two corner points is enough to draw its rectangle. While the tool is armed the
    /// four corner handles are drawn too — grab one to resize, drag inside the box to move it.
    /// </summary>
    private void DrawCordon(Graphics g)
    {
        Cordon c = Cordon;
        if (c == null || (!CordonEditing && !c.Enabled))
        {
            return;
        }

        PointF a = WorldToScreenF(c.Mins);
        PointF b = WorldToScreenF(c.Maxs);
        float x = Math.Min(a.X, b.X);
        float y = Math.Min(a.Y, b.Y);
        float w = Math.Abs(a.X - b.X);
        float h = Math.Abs(a.Y - b.Y);

        using Pen pen = new Pen(c.Enabled ? Color.FromArgb(255, 70, 60) : Color.FromArgb(80, 200, 230), c.Enabled ? 2f : 1.2f);
        g.DrawRectangle(pen, x, y, w, h);

        if (CordonEditing && c.HasBounds)
        {
            const int size = 7;
            using SolidBrush fill = new SolidBrush(Color.FromArgb(240, 245, 250));
            using Pen border = new Pen(Color.FromArgb(30, 30, 34), 1f);
            g.FillRectangle(fill, x - size / 2f, y - size / 2f, size, size);
            g.DrawRectangle(border, x - size / 2f, y - size / 2f, size, size);
            g.FillRectangle(fill, x + w - size / 2f, y - size / 2f, size, size);
            g.DrawRectangle(border, x + w - size / 2f, y - size / 2f, size, size);
            g.FillRectangle(fill, x + w - size / 2f, y + h - size / 2f, size, size);
            g.DrawRectangle(border, x + w - size / 2f, y + h - size / 2f, size, size);
            g.FillRectangle(fill, x - size / 2f, y + h - size / 2f, size, size);
            g.DrawRectangle(border, x - size / 2f, y + h - size / 2f, size, size);
        }
    }

    /// <summary>Begins a cordon edit drag. Only two interactions are possible in the 2D
    /// views: drag a corner handle to resize the box, or drag inside it to move it. Pressing
    /// empty space does nothing — the cordon always exists (10k x 10k by default) and is
    /// never redrawn from a click-drag. The out-of-plane axis range is captured so a drag
    /// only affects the two in-plane axes.</summary>
    private void BeginCordonEdit(Point location)
    {
        Cordon c = Cordon;
        if (c == null)
        {
            return;
        }

        int grip = HitTestCordonGrip(location, c);
        if (grip == CordonGripNone)
        {
            return; // empty space: no redraw, no action
        }

        _cordonDragging = true;
        _cordonDragStart = location;
        _cordonStartMins = c.Mins;
        _cordonStartMaxs = c.Maxs;

        Capture = true;
        Cursor = Cursors.Cross;

        _cordonGrip = grip;
        if (_cordonGrip is >= 0 and <= 3)
        {
            // Corner numbers are WORLD combos (not screen positions):
            // 0=(minH,minV) 1=(maxH,minV) 2=(maxH,maxV) 3=(minH,maxV)
            _cordonGripHMax = _cordonGrip == 1 || _cordonGrip == 2;
            _cordonGripVMax = _cordonGrip == 2 || _cordonGrip == 3;
        }

        // A plain press must NOT change the box (no snap, no reshape); only an actual drag
        // (mouse move) applies the edit, so clicking a handle can never collapse the box.
    }

    /// <summary>Returns a corner-handle index 0..3 when the press hit one, CordonGripMove
    /// when it hit inside the box (translate), or CordonGripNone for empty space. The corner
    /// indices are WORLD min/max combos — the screen corners are mapped onto them accounting
    /// for the view's Y flip (larger world V appears at the TOP of the screen).</summary>
    private int HitTestCordonGrip(Point location, Cordon c)
    {
        const int tol = 6;
        PointF a = WorldToScreenF(c.Mins);
        PointF b = WorldToScreenF(c.Maxs);
        float x0 = Math.Min(a.X, b.X);
        float x1 = Math.Max(a.X, b.X);
        float y0 = Math.Min(a.Y, b.Y);
        float y1 = Math.Max(a.Y, b.Y);

        // Screen layout (world -> screen): minH is left, maxH is right; maxV is TOP (y0),
        // minV is BOTTOM (y1). So:
        //   top-left    (x0,y0) = (minH, maxV) -> combo 3
        //   top-right   (x1,y0) = (maxH, maxV) -> combo 2
        //   bottom-right(x1,y1) = (maxH, minV) -> combo 1
        //   bottom-left (x0,y1) = (minH, minV) -> combo 0
        if (Math.Abs(location.X - x0) <= tol && Math.Abs(location.Y - y0) <= tol) return 3;
        if (Math.Abs(location.X - x1) <= tol && Math.Abs(location.Y - y0) <= tol) return 2;
        if (Math.Abs(location.X - x1) <= tol && Math.Abs(location.Y - y1) <= tol) return 1;
        if (Math.Abs(location.X - x0) <= tol && Math.Abs(location.Y - y1) <= tol) return 0;

        if (location.X >= x0 - tol && location.X <= x1 + tol
            && location.Y >= y0 - tol && location.Y <= y1 + tol)
        {
            return CordonGripMove;
        }

        return CordonGripNone;
    }

    /// <summary>Applies the current drag to the cordon box (called on press and on every
    /// mouse move). Everything is computed from the box captured at drag start so repeated
    /// moves never accumulate error. Drags snap to the grid like point/selection movement:
    /// a moved box shifts by whole grid steps and a dragged corner lands on a grid line.</summary>
    private void ApplyCordonEdit(Point location)
    {
        Cordon c = Cordon;
        if (c == null)
        {
            return;
        }

        Vec3 mins = _cordonStartMins;
        Vec3 maxs = _cordonStartMaxs;

        if (_cordonGrip == CordonGripMove)
        {
            // Translate the whole box on this plane; the out-of-plane axis keeps its range.
            // Both the grab point and the pointer are snapped, so the box moves in whole grid
            // steps (a plain press produces no jump because the delta is zero).
            double hStart = SnapCoord(ScreenToWorldX(_cordonDragStart.X));
            double hCur = SnapCoord(ScreenToWorldX(location.X));
            double vStart = SnapCoord(ScreenToWorldY(_cordonDragStart.Y));
            double vCur = SnapCoord(ScreenToWorldY(location.Y));
            double dh = hCur - hStart;
            double dv = vCur - vStart;
            PlaneHVRange(mins, maxs, out double hMin, out double hMax, out double vMin, out double vMax);
            SetPlaneBox(mins, maxs, hMin + dh, hMax + dh, vMin + dv, vMax + dv, out mins, out maxs);
        }
        else
        {
            // Resize a corner: the two in-plane axes follow the (grid-snapped) pointer,
            // flipping sides smoothly as the pointer crosses the opposite edge.
            double h = SnapCoord(ScreenToWorldX(location.X));
            double v = SnapCoord(ScreenToWorldY(location.Y));
            PlaneHVRange(mins, maxs, out double hMin, out double hMax, out double vMin, out double vMax);
            ResizeHalf(ref _cordonGripHMax, ref hMin, ref hMax, hMin, hMax, h);
            ResizeHalf(ref _cordonGripVMax, ref vMin, ref vMax, vMin, vMax, v);
            SetPlaneBox(mins, maxs, hMin, hMax, vMin, vMax, out mins, out maxs);
        }

        c.Set(c.Enabled, mins, maxs);
        Invalidate();
    }

    /// <summary>Snaps a single world coordinate to the road grid (honours the grid-snap
    /// setting; with snapping off or no document it returns the value unchanged), matching
    /// how point/selection movement snaps.</summary>
    private double SnapCoord(double value)
    {
        RoadSettings settings = _doc != null ? _doc.Settings : null;
        return settings != null ? settings.Snapped(value) : value;
    }

    /// <summary>Moves a box endpoint toward the pointer, flipping which side (min vs max) is
    /// being dragged when the pointer crosses the opposite endpoint so a corner resize never
    /// makes the box invert-and-jump.</summary>
    private static void ResizeHalf(ref bool editingMax, ref double mn, ref double mx, double startMn, double startMx, double value)
    {
        if (editingMax)
        {
            if (value < startMn)
            {
                editingMax = false;
                mn = value;
            }
            else
            {
                mx = value;
            }
        }
        else if (value > startMx)
        {
            editingMax = true;
            mx = value;
        }
        else
        {
            mn = value;
        }
    }

    /// <summary>Extracts the box's horizontal (h) and vertical (v) extent on this plane's two
    /// axes. The out-of-plane axis is left untouched by callers.</summary>
    private void PlaneHVRange(Vec3 mins, Vec3 maxs, out double hMin, out double hMax, out double vMin, out double vMax)
    {
        switch (_plane)
        {
            case PlaneKind.Top:
                hMin = mins.X; hMax = maxs.X; vMin = mins.Y; vMax = maxs.Y;
                break;
            case PlaneKind.Front:
                hMin = mins.X; hMax = maxs.X; vMin = mins.Z; vMax = maxs.Z;
                break;
            default:
                hMin = mins.Y; hMax = maxs.Y; vMin = mins.Z; vMax = maxs.Z;
                break;
        }
    }

    /// <summary>Rebuilds a box from (hMin, hMax, vMin, vMax) on this plane, preserving the
    /// out-of-plane axis from <paramref name="baseMins"/> / <paramref name="baseMaxs"/>.</summary>
    private void SetPlaneBox(Vec3 baseMins, Vec3 baseMaxs, double hMin, double hMax, double vMin, double vMax, out Vec3 mins, out Vec3 maxs)
    {
        switch (_plane)
        {
            case PlaneKind.Top:
                mins = new Vec3(hMin, vMin, baseMins.Z);
                maxs = new Vec3(hMax, vMax, baseMaxs.Z);
                break;
            case PlaneKind.Front:
                mins = new Vec3(hMin, baseMins.Y, vMin);
                maxs = new Vec3(hMax, baseMaxs.Y, vMax);
                break;
            default:
                mins = new Vec3(baseMins.X, hMin, vMin);
                maxs = new Vec3(baseMaxs.X, hMax, vMax);
                break;
        }
    }

    // ----- world/screen mapping -----

    private double ScreenToWorldX(double sx) => (sx - Width / 2.0) / _zoom + _center.X;

    private double ScreenToWorldY(double sy) => _center.Y - (sy - Height / 2.0) / _zoom;

    private PointF WorldToScreenF(Vec3 w)
    {
        double h = PlaneHorizontal(w);
        double v = PlaneVertical(w);
        double sx = (h - _center.X) * _zoom + Width / 2.0;
        double sy = (_center.Y - v) * _zoom + Height / 2.0;
        return new PointF((float)sx, (float)sy);
    }

    private double PlaneHorizontal(Vec3 w) => _plane switch
    {
        PlaneKind.Side => w.Y,
        _ => w.X
    };

    private double PlaneVertical(Vec3 w) => _plane switch
    {
        PlaneKind.Top => w.Y,
        _ => w.Z
    };

    private double OutOfPlane(Vec3 w) => _plane switch
    {
        PlaneKind.Top => w.Z,
        PlaneKind.Front => w.Y,
        _ => w.X
    };

    private Vec3 ScreenToWorld(Point s, double outOfPlane)
    {
        double h = ScreenToWorldX(s.X);
        double v = ScreenToWorldY(s.Y);
        return _plane switch
        {
            PlaneKind.Top => new Vec3(h, v, outOfPlane),
            PlaneKind.Front => new Vec3(h, outOfPlane, v),
            _ => new Vec3(outOfPlane, h, v)
        };
    }

    private Vec3 Snap(Vec3 v)
    {
        if (_doc == null)
        {
            return v;
        }

        return new Vec3(
            _doc.Settings.Snapped(v.X),
            _doc.Settings.Snapped(v.Y),
            _doc.Settings.Snapped(v.Z));
    }

    private void BeginMoveDrag()
    {
        _moveIndices = new List<int>();
        _moveOrigins = new List<Vec3>();

        var selected = GetSelectedIndices();
        bool moveGroup = selected.Contains(_dragIndex) && selected.Count > 1;

        if (moveGroup)
        {
            foreach (int i in selected)
            {
                _moveIndices.Add(i);
                _moveOrigins.Add(_doc.Points[i].Position);
            }
        }
        else
        {
            _moveIndices.Add(_dragIndex);
            _moveOrigins.Add(_doc.Points[_dragIndex].Position);
        }

        _dragOrigin = _doc.Points[_dragIndex].Position;
    }

    private bool IsPointSelected(int index)
    {
        var selected = GetSelectedIndices();
        foreach (int i in selected)
        {
            if (i == index)
            {
                return true;
            }
        }

        // Mirror DrawPoints: fall back to the single tracked selection.
        return selected.Count == 0 && GetSelectedIndex() == index;
    }

    // ----- mouse interaction -----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _hoverToolTip.Hide(this);
        _hoverTimer.Stop();
        _hoverWeldIndex = -2;

        if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
        {
            _panning = true;
            _panStartScreen = e.Location;
            _panStartCenter = _center;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // Cordon tool: a left press starts or reshapes the cordon box on this plane's
        // two axes. It does not need a road document (the box can be drawn over the layout).
        if (CordonEditing && Cordon != null)
        {
            BeginCordonEdit(e.Location);
            return;
        }

        if (_doc == null)
        {
            return;
        }

        if (TryHitPoint(e.Location, out int idx))
        {
            _dragIndex = idx;
            _dragStartScreen = e.Location;
            _dragging = false;

            _wasSelectedOnDown = IsPointSelected(idx);
            _ctrlOnDown = (ModifierKeys & Keys.Control) != 0;

            // Give immediate feedback: select (or ctrl-toggle) right on press. If
            // the point was already selected we leave the selection alone so a
            // drag moves the whole group; click-to-collapse happens on release.
            if (_ctrlOnDown || !_wasSelectedOnDown)
            {
                PointSelected?.Invoke(idx, _ctrlOnDown);
            }
        }
        else
        {
            // Potential box selection (or a plain click to add / clear).
            _boxPending = true;
            _boxSelecting = false;
            _boxStart = e.Location;
            _boxCurrent = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHoverTooltip(e.Location);

        if (_panning)
        {
            double dx = (e.X - _panStartScreen.X) / _zoom;
            double dy = (e.Y - _panStartScreen.Y) / _zoom;
            _center = new Vec3(_panStartCenter.X - dx, _panStartCenter.Y + dy, 0);
            Invalidate();
            return;
        }

        if (_cordonDragging)
        {
            ApplyCordonEdit(e.Location);
            return;
        }

        if (_dragIndex >= 0 && _doc != null)
        {
            if (!_dragging)
            {
                int d = Math.Abs(e.X - _dragStartScreen.X) + Math.Abs(e.Y - _dragStartScreen.Y);
                if (d > 4)
                {
                    _dragging = true;
                    BeginMoveDrag();
                    EditBegin?.Invoke();
                    Cursor = Cursors.Hand;
                }
            }

            if (_dragging)
            {
                double outOfPlane = OutOfPlane(_dragOrigin);
                Vec3 world = Snap(ScreenToWorld(e.Location, outOfPlane));
                Vec3 delta = world - _dragOrigin;
                bool breakWeld = (ModifierKeys & Keys.Shift) != 0;

                for (int k = 0; k < _moveIndices.Count; k++)
                {
                    int pointIndex = _moveIndices[k];
                    Vec3 newPosition = _moveOrigins[k] + delta;

                    if (breakWeld)
                    {
                        // Shift+drag moves only this point, leaving any welded
                        // points in other tracks behind (breaks the weld).
                        _doc.Points[pointIndex].Position = newPosition;
                    }
                    else
                    {
                        Vec3 oldPosition = _doc.Points[pointIndex].Position;
                        _doc.MovePointWelded(_doc.ActiveTrack, pointIndex, newPosition, oldPosition);
                    }
                }

                _doc.NotifyChanged();
                PointsEdited?.Invoke(_moveIndices);
            }

            return;
        }

        if (_boxPending)
        {
            if (!_boxSelecting)
            {
                int d = Math.Abs(e.X - _boxStart.X) + Math.Abs(e.Y - _boxStart.Y);
                if (d > 4)
                {
                    _boxSelecting = true;
                }
            }

            if (_boxSelecting)
            {
                _boxCurrent = e.Location;
                Invalidate();
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left && _cordonDragging)
        {
            _cordonDragging = false;
            Capture = false;
            Cursor = Cursors.Default;
            Invalidate();
            return;
        }

        if (_panning && (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right))
        {
            _panning = false;
            Cursor = Cursors.Default;
            return;
        }

        if (e.Button == MouseButtons.Left && _dragIndex >= 0)
        {
            if (_dragging)
            {
                EditEnd?.Invoke();
            }
            else if (!_ctrlOnDown && _wasSelectedOnDown)
            {
                // Plain click on an already-selected point: collapse to a single
                // selection (it wasn't changed on mouse-down).
                PointSelected?.Invoke(_dragIndex, false);
            }

            _dragIndex = -1;
            _dragging = false;
            Cursor = Cursors.Default;
            return;
        }

        if (e.Button == MouseButtons.Left && _boxPending)
        {
            _boxPending = false;

            if (_boxSelecting)
            {
                var indices = PointsInBox();
                BoxSelected?.Invoke(indices, (ModifierKeys & Keys.Control) != 0);
            }
            else if ((ModifierKeys & Keys.Control) != 0)
            {
                PointAddBegin?.Invoke();
                double outOfPlane = GetDefaultOutOfPlane();
                Vec3 world = Snap(ScreenToWorld(e.Location, outOfPlane));
                _doc.Points.Add(new RoadPoint(world, GetDefaultWidth(), GetDefaultBank(), GetDefaultThickness()));
                _doc.NotifyChanged();
                PointAdded?.Invoke(_doc.Points.Count - 1);
            }
            else
            {
                BoxSelected?.Invoke(Array.Empty<int>(), false);
            }

            _boxSelecting = false;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        // Zoom around the cursor.
        Point cursor = e.Location;
        double worldHBefore = ScreenToWorldX(cursor.X);
        double worldVBefore = ScreenToWorldY(cursor.Y);

        double factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        _zoom = Math.Max(0.001, Math.Min(20, _zoom * factor));

        double worldHAfter = ScreenToWorldX(cursor.X);
        double worldVAfter = ScreenToWorldY(cursor.Y);
        _center = new Vec3(_center.X + (worldHBefore - worldHAfter), _center.Y + (worldVBefore - worldVAfter), 0);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverToolTip.Hide(this);
        _hoverTimer.Stop();
        _hoverWeldIndex = -2;
    }

    private bool TryHitPoint(Point s, out int index)
    {
        index = -1;
        if (_doc == null)
        {
            return false;
        }

        double best = 12;
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            PointF p = WorldToScreenF(_doc.Points[i].Position);
            double d = Math.Sqrt((p.X - s.X) * (p.X - s.X) + (p.Y - s.Y) * (p.Y - s.Y));
            if (d < best)
            {
                best = d;
                index = i;
            }
        }

        return index >= 0;
    }

    /// <summary>Debounced hint when hovering a welded (joined) track node, i.e. a
    /// point whose position is shared with another track. The pointer must hold on
    /// the node for the timer interval before the tooltip appears; it hides when the
    /// pointer leaves the node, starts dragging/panning/box-selecting, or leaves the
    /// control.</summary>
    private void UpdateHoverTooltip(Point location)
    {
        _hoverLocation = location;

        int target = -1;
        if (!_panning && !CordonEditing && _dragIndex < 0 && !_dragging && !_boxSelecting && _doc != null
            && _doc.Points != null && TryHitPoint(location, out int idx)
            && idx >= 0 && idx < _doc.Points.Count
            && IsWeldPoint(_doc.Points[idx].Position))
        {
            target = idx;
        }

        if (target == _hoverWeldIndex)
        {
            return;
        }

        _hoverWeldIndex = target;
        _hoverTimer.Stop();
        _hoverToolTip.Hide(this);

        if (target >= 0)
        {
            _hoverTimer.Start();
        }
    }

    private void OnHoverTimerTick(object sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hoverWeldIndex < 0)
        {
            return;
        }

        _hoverToolTip.Show("Hold Shift + M1 and drag to disconnect", this, _hoverLocation.X + 14, _hoverLocation.Y + 14, 6000);
    }

    /// <summary>True when a position is shared by more than one track, i.e. it is a
    /// welded/joined junction node.</summary>
    private bool IsWeldPoint(Vec3 position)
    {
        if (_doc == null)
        {
            return false;
        }

        int matches = 0;
        foreach (Track track in _doc.Tracks)
        {
            foreach (RoadPoint point in track.Points)
            {
                if (RoadDocument.PositionsMatch(point.Position, position))
                {
                    matches++;
                    if (matches >= 2)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private List<int> PointsInBox()
    {
        var result = new List<int>();
        if (_doc == null)
        {
            return result;
        }

        GetBoxWorldBounds(out double hMin, out double hMax, out double vMin, out double vMax);
        for (int i = 0; i < _doc.Points.Count; i++)
        {
            double h = PlaneHorizontal(_doc.Points[i].Position);
            double v = PlaneVertical(_doc.Points[i].Position);
            if (h >= hMin && h <= hMax && v >= vMin && v <= vMax)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private double GetDefaultOutOfPlane()
    {
        int sel = GetSelectedIndex();
        if (sel >= 0 && _doc != null && sel < _doc.Points.Count)
        {
            return OutOfPlane(_doc.Points[sel].Position);
        }

        return 0;
    }

    private double GetDefaultWidth()
    {
        int sel = GetSelectedIndex();
        if (sel >= 0 && _doc != null && sel < _doc.Points.Count)
        {
            return _doc.Points[sel].Width;
        }

        return 256;
    }

    private double GetDefaultBank()
    {
        int sel = GetSelectedIndex();
        if (sel >= 0 && _doc != null && sel < _doc.Points.Count)
        {
            return _doc.Points[sel].BankDegrees;
        }

        return 0;
    }

    private double GetDefaultThickness()
    {
        int sel = GetSelectedIndex();
        if (sel >= 0 && _doc != null && sel < _doc.Points.Count)
        {
            return _doc.Points[sel].Thickness;
        }

        return 64;
    }
}
