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

    private bool _panning;
    private Point _panStartScreen;
    private Vec3 _panStartCenter;

    private int _dragIndex = -1;
    private bool _dragging;
    private Point _dragStartScreen;
    private Vec3 _dragOrigin;
    private List<int> _moveIndices;
    private List<Vec3> _moveOrigins;

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

    public void SetPlane(PlaneKind plane)
    {
        _plane = plane;
        Invalidate();
    }

    public void FrameAll()
    {
        // Defer until the control has a real size (applied in OnPaint).
        _pendingFrame = true;
        Invalidate();
    }

    private void ApplyFrame()
    {
        if (_doc == null || _doc.Points.Count == 0)
        {
            return;
        }

        Vec3 min = _doc.Points[0].Position;
        Vec3 max = _doc.Points[0].Position;
        foreach (RoadPoint p in _doc.Points)
        {
            min = Vec3.Min(min, p.Position);
            max = Vec3.Max(max, p.Position);
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
        g.Clear(BackColor);

        if (_pendingFrame && Width > 0 && Height > 0)
        {
            ApplyFrame();
            _pendingFrame = false;
        }

        DrawGrid(g);
        DrawAxis(g);

        if (_doc == null)
        {
            DrawBorder(g);
            DrawTitle(g);
            return;
        }

        RoadPreviewMesh mesh = RoadPreviewMesh.Build(_doc.Points, 24, _doc.Settings.Thickness);
        DrawRoad(g, mesh);
        DrawPoints(g);
        DrawHint(g);
        DrawBox(g);
        DrawTitle(g);
        DrawBorder(g);
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

    private void DrawRoad(Graphics g, RoadPreviewMesh mesh)
    {
        using Pen edge = new Pen(Color.FromArgb(210, 210, 220), 1.6f);
        using Pen center = new Pen(Color.FromArgb(120, 210, 120), 1.4f);
        using Pen rib = new Pen(Color.FromArgb(70, 70, 78), 1f);
        using Pen wall = new Pen(Color.FromArgb(110, 116, 126), 1.2f);

        bool hasThickness = _doc.Settings.Thickness > 0;

        DrawPolyline(g, mesh.Left, edge);
        DrawPolyline(g, mesh.Right, edge);
        DrawPolyline(g, mesh.Center, center);

        // Ribs across the road every 4 preview samples.
        for (int i = 0; i < mesh.Center.Count; i += 4)
        {
            g.DrawLine(rib, WorldToScreenF(mesh.Left[i]), WorldToScreenF(mesh.Right[i]));
        }

        // Each side's bottom edge is drawn independently so the walls terminate on a
        // visible line without connecting underneath (no fake bottom) unless the bottom
        // face is enabled.
        if (hasThickness && (_doc.Settings.SolidBottom || _doc.Settings.SolidLeft))
        {
            DrawPolyline(g, mesh.BottomLeft, wall);
        }

        if (hasThickness && (_doc.Settings.SolidBottom || _doc.Settings.SolidRight))
        {
            DrawPolyline(g, mesh.BottomRight, wall);
        }

        if (hasThickness && _doc.Settings.SolidLeft)
        {
            for (int i = 0; i < mesh.Center.Count; i += 4)
            {
                g.DrawLine(wall, WorldToScreenF(mesh.Left[i]), WorldToScreenF(mesh.BottomLeft[i]));
            }
        }

        if (hasThickness && _doc.Settings.SolidRight)
        {
            for (int i = 0; i < mesh.Center.Count; i += 4)
            {
                g.DrawLine(wall, WorldToScreenF(mesh.Right[i]), WorldToScreenF(mesh.BottomRight[i]));
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

        using Pen pen = new Pen(Color.FromArgb(220, 220, 230))
        {
            DashStyle = DashStyle.Dash
        };
        g.DrawRectangle(pen, RectangleFromPoints(_boxStart, _boxCurrent));
    }

    private void DrawPolyline(Graphics g, IReadOnlyList<Vec3> pts, Pen pen)
    {
        if (pts.Count < 2)
        {
            return;
        }

        PointF prev = WorldToScreenF(pts[0]);
        for (int i = 1; i < pts.Count; i++)
        {
            PointF cur = WorldToScreenF(pts[i]);
            g.DrawLine(pen, prev, cur);
            prev = cur;
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

                for (int k = 0; k < _moveIndices.Count; k++)
                {
                    _doc.Points[_moveIndices[k]].Position = _moveOrigins[k] + delta;
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
            else
            {
                PointSelected?.Invoke(_dragIndex, (ModifierKeys & Keys.Control) != 0);
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
                _doc.Points.Add(new RoadPoint(world, GetDefaultWidth(), GetDefaultBank()));
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
}
