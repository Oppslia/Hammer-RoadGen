using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RoadGen.Core;

namespace RoadGen.UI;

/// <summary>A software-rendered perspective 3D viewport with orbit, pan and zoom.</summary>
public sealed class Viewport3D : Control
{
    private RoadDocument _doc;
    private double _yaw = -0.85;
    private double _pitch = 0.55;
    private double _dist = 2800;
    private Vec3 _target = Vec3.Zero;
    private bool _autoTarget = true;

    private bool _orbiting;
    private bool _panning;
    private Point _lastMouse;
    private bool _clickCandidate;
    private Point _downPos;

    private Vec3 _eye;
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
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(32, 32, 36);
        TabStop = false;
    }

    public void SetDocument(RoadDocument doc) => _doc = doc;

    public bool ShowSegments;

    /// <summary>Cancel any in-progress orbit/pan/click. The 3D view has no point
    /// drag, but this keeps the API consistent when points are deleted.</summary>
    public void CancelDrag()
    {
        _orbiting = false;
        _panning = false;
        _clickCandidate = false;
        Cursor = Cursors.Default;
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

        if (_autoTarget && _doc != null && _doc.Points.Count > 0)
        {
            Vec3 min = _doc.Points[0].Position;
            Vec3 max = _doc.Points[0].Position;
            foreach (RoadPoint p in _doc.Points)
            {
                min = Vec3.Min(min, p.Position);
                max = Vec3.Max(max, p.Position);
            }

            _target = (min + max) / 2.0;
            double span = Math.Max(256, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
            _dist = span * 3.2;
        }

        SetupCamera();
        DrawGroundGrid(g);
        DrawAxes(g);

        if (_doc != null)
        {
            RoadPreviewMesh mesh = RoadPreviewMesh.Build(_doc.Points, 20, _doc.Settings.Thickness);
            DrawRoad(g, mesh);
            DrawSegments(g);
            DrawPoints(g);
        }

        DrawTitle(g);
        DrawBorder(g);
    }

    private void SetupCamera()
    {
        Vec3 dir = new Vec3(
            Math.Cos(_pitch) * Math.Sin(_yaw),
            Math.Cos(_pitch) * Math.Cos(_yaw),
            Math.Sin(_pitch));
        _eye = _target + dir * _dist;
        _forward = (_target - _eye).Normalized();
        _right = Vec3.Cross(_forward, Vec3.UnitZ).Normalized();
        if (_right.LengthSq < 1e-6)
        {
            _right = Vec3.UnitX;
        }

        _up = Vec3.Cross(_right, _forward).Normalized();
        _focal = Math.Max(Height, 1) * 1.4;
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

    private void DrawRoad(Graphics g, RoadPreviewMesh mesh)
    {
        using Pen edge = new Pen(Color.FromArgb(80, 195, 255), 1.8f);   // cyan: road edges
        using Pen center = new Pen(Color.FromArgb(130, 240, 130), 1.6f); // green: centerline
        using Pen rib = new Pen(Color.FromArgb(105, 105, 118), 1f);
        using Pen wall = new Pen(Color.FromArgb(255, 160, 70), 1.2f);    // orange: walls/bottom

        bool hasThickness = _doc.Settings.Thickness > 0;

        DrawPolyline(g, mesh.Left, edge);
        DrawPolyline(g, mesh.Right, edge);
        DrawPolyline(g, mesh.Center, center);

        if (!ShowSegments)
        {
            for (int i = 0; i < mesh.Center.Count; i += 5)
            {
                Draw3DLine(g, mesh.Left[i], mesh.Right[i], rib);
            }
        }

        // Each side's bottom edge is drawn independently so the walls terminate on a
        // visible line. The connecting ribs underneath are only drawn when the bottom
        // face is enabled, so an unchecked bottom does not look closed.
        if (hasThickness && (_doc.Settings.SolidBottom || _doc.Settings.SolidLeft))
        {
            DrawPolyline(g, mesh.BottomLeft, wall);
        }

        if (hasThickness && (_doc.Settings.SolidBottom || _doc.Settings.SolidRight))
        {
            DrawPolyline(g, mesh.BottomRight, wall);
        }

        if (!ShowSegments && hasThickness && _doc.Settings.SolidBottom)
        {
            for (int i = 0; i < mesh.Center.Count; i += 5)
            {
                Draw3DLine(g, mesh.BottomLeft[i], mesh.BottomRight[i], rib);
            }
        }

        if (hasThickness && _doc.Settings.SolidLeft)
        {
            for (int i = 0; i < mesh.Center.Count; i += 5)
            {
                Draw3DLine(g, mesh.Left[i], mesh.BottomLeft[i], wall);
            }
        }

        if (hasThickness && _doc.Settings.SolidRight)
        {
            for (int i = 0; i < mesh.Center.Count; i += 5)
            {
                Draw3DLine(g, mesh.Right[i], mesh.BottomRight[i], wall);
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
        double thickness = _doc.Settings.Thickness;
        Vec3 down = new Vec3(0, 0, -1) * thickness;
        using Pen pen = new Pen(Color.FromArgb(255, 100, 220), 1.2f);
        foreach (SegmentLayout.Segment seg in segments)
        {
            Vec3 a = seg.A, b = seg.B, c = seg.C, d = seg.D;
            Vec3 a2 = a + down, b2 = b + down, c2 = c + down, d2 = d + down;

            // Top face: the base parallelogram Hammer reconstructs.
            Draw3DLine(g, a, b, pen);
            Draw3DLine(g, b, c, pen);
            Draw3DLine(g, c, d, pen);
            Draw3DLine(g, d, a, pen);

            if (thickness > 0)
            {
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
        g.DrawString("3D (right-drag orbit, click select)", f, b, 8, 7);
    }

    private void DrawBorder(Graphics g)
    {
        using Pen p = new Pen(Color.FromArgb(100, 108, 120));
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    private void DrawPolyline(Graphics g, IReadOnlyList<Vec3> pts, Pen pen)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Draw3DLine(g, pts[i], pts[i + 1], pen);
        }
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _lastMouse = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            _clickCandidate = true;
            _downPos = e.Location;
            TryPick(e.Location, out int idx);
            PointSelected?.Invoke(idx, (ModifierKeys & Keys.Control) != 0);
        }
        else if (e.Button == MouseButtons.Right)
        {
            _orbiting = true;
            Cursor = Cursors.SizeAll;
        }
        else if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;

        if (_orbiting)
        {
            _yaw += dx * 0.01;
            _pitch += dy * 0.01;
            _pitch = Math.Max(-1.55, Math.Min(1.55, _pitch));
            _autoTarget = false;
            Invalidate();
        }
        else if (_panning)
        {
            SetupCamera();
            double scale = _dist * 0.0012;
            _target = _target - _right * (dx * scale) + _up * (dy * scale);
            _autoTarget = false;
            Invalidate();
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

        if (_orbiting && e.Button == MouseButtons.Right)
        {
            _orbiting = false;
            Cursor = Cursors.Default;
        }
        else if (_panning && e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Cursor = Cursors.Default;
        }
        else if (e.Button == MouseButtons.Left)
        {
            _clickCandidate = false;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _dist *= e.Delta > 0 ? 0.85 : 1.0 / 0.85;
        _dist = Math.Max(50, Math.Min(200000, _dist));
        _autoTarget = false;
        Invalidate();
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
