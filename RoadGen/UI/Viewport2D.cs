using System;
using System.Collections.Generic;
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
    }

    public void SetDocument(RoadDocument doc) => _doc = doc;

    public bool ShowSegments;

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

        if (_doc == null)
        {
            DrawBorder(g);
            DrawTitle(g);
            return;
        }

        DrawAllTracks(g);
        DrawSegments(g);
        DrawInactivePoints(g);
        DrawPoints(g);
        DrawHint(g);
        DrawBox(g);
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

        // Avoid drawing absurd numbers of lines when zoomed way out.
        while ((maxX - minX) / snap > 250)
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

            RoadPreviewMesh mesh = RoadPreviewMesh.Build(chain.Points, stepsPerSegment);

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

        var segments = SegmentLayout.Compute(_doc.Points, _doc.Settings);
        using Pen pen = new Pen(Color.FromArgb(255, 100, 220), 1.1f);
        foreach (SegmentLayout.Segment seg in segments)
        {
            Vec3 a = seg.A, b = seg.B, c = seg.C, d = seg.D;
            Vec3 downStart = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T0);
            Vec3 downEnd = new Vec3(0, 0, -1) * RoadCurve.Thickness(_doc.Points, seg.T1);
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

        using Pen pen = new Pen(Color.FromArgb(220, 220, 230))
        {
            DashStyle = DashStyle.Dash
        };
        g.DrawRectangle(pen, RectangleFromPoints(_boxStart, _boxCurrent));
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

        if (_panning)
        {
            double dx = (e.X - _panStartScreen.X) / _zoom;
            double dy = (e.Y - _panStartScreen.Y) / _zoom;
            _center = new Vec3(_panStartCenter.X - dx, _panStartCenter.Y + dy, 0);
            Invalidate();
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
                var indices = PointsInBox(RectangleFromPoints(_boxStart, _boxCurrent));
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

    private List<int> PointsInBox(Rectangle r)
    {
        var result = new List<int>();
        if (_doc == null)
        {
            return result;
        }

        for (int i = 0; i < _doc.Points.Count; i++)
        {
            PointF s = WorldToScreenF(_doc.Points[i].Position);
            if (r.Contains((int)s.X, (int)s.Y))
            {
                result.Add(i);
            }
        }

        return result;
    }

    private static Rectangle RectangleFromPoints(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
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
