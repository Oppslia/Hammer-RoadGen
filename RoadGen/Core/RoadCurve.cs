using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>Evaluates the road centerline (position, tangent, width and bank) over a
/// global parameter t. The i-th segment spans t = [i, i+1].</summary>
public static class RoadCurve
{
    public static int SegmentCount(IReadOnlyList<RoadPoint> pts) => pts.Count - 1;

    public static Vec3 Position(IReadOnlyList<RoadPoint> pts, double t, bool closed = false)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return Vec3.Zero;
        }

        if (n == 1)
        {
            return pts[0].Position;
        }

        // A closed chain stores its first point again at the end (the seam). Treat the
        // unique points as a ring so the seam becomes a single interior point of the
        // spline (exactly like a normal end-to-end join), and the road flows through
        // it instead of clamping both ends and breaking the loop. The last segment
        // wraps back to the first via modular neighbours.
        GetSegmentAndU(n, t, out int i, out double u);
        if (closed)
        {
            int m = n - 1;
            int c0 = (i - 1 + m) % m;
            int c1 = i;
            int c2 = (i + 1) % m;
            int c3 = (i + 2) % m;
            return CatmullRom.Position(pts[c0].Position, pts[c1].Position, pts[c2].Position, pts[c3].Position, u);
        }

        Vec3 p0 = pts[Math.Max(0, i - 1)].Position;
        Vec3 p1 = pts[i].Position;
        Vec3 p2 = pts[i + 1].Position;
        Vec3 p3 = pts[Math.Min(n - 1, i + 2)].Position;
        return CatmullRom.Position(p0, p1, p2, p3, u);
    }

    public static Vec3 Tangent(IReadOnlyList<RoadPoint> pts, double t, bool closed = false)
    {
        int n = pts.Count;
        if (n < 2)
        {
            return Vec3.UnitX;
        }

        GetSegmentAndU(n, t, out int i, out double u);
        if (closed)
        {
            int m = n - 1;
            int c0 = (i - 1 + m) % m;
            int c1 = i;
            int c2 = (i + 1) % m;
            int c3 = (i + 2) % m;
            return CatmullRom.Tangent(pts[c0].Position, pts[c1].Position, pts[c2].Position, pts[c3].Position, u);
        }

        Vec3 p0 = pts[Math.Max(0, i - 1)].Position;
        Vec3 p1 = pts[i].Position;
        Vec3 p2 = pts[i + 1].Position;
        Vec3 p3 = pts[Math.Min(n - 1, i + 2)].Position;
        return CatmullRom.Tangent(p0, p1, p2, p3, u);
    }

    public static double Width(IReadOnlyList<RoadPoint> pts, double t, bool closed = false)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return 0;
        }

        if (n == 1)
        {
            return pts[0].Width;
        }

        GetSegmentAndU(n, t, out int i, out double u);
        // For a closed loop the last point is the seam (a duplicate of the first),
        // so the closing segment interpolates back to the first point's value and
        // the width is continuous across the join instead of jumping to the last
        // track's own endpoint value.
        int next = closed ? (i + 1) % (n - 1) : i + 1;
        return pts[i].Width + (pts[next].Width - pts[i].Width) * u;
    }

    public static double Bank(IReadOnlyList<RoadPoint> pts, double t, bool closed = false)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return 0;
        }

        if (n == 1)
        {
            return pts[0].BankDegrees;
        }

        GetSegmentAndU(n, t, out int i, out double u);
        int next = closed ? (i + 1) % (n - 1) : i + 1;
        return pts[i].BankDegrees + (pts[next].BankDegrees - pts[i].BankDegrees) * u;
    }

    public static double Thickness(IReadOnlyList<RoadPoint> pts, double t, bool closed = false)
    {
        int n = pts.Count;
        if (n == 0)
        {
            return 0;
        }

        if (n == 1)
        {
            return pts[0].Thickness;
        }

        GetSegmentAndU(n, t, out int i, out double u);
        int next = closed ? (i + 1) % (n - 1) : i + 1;
        return pts[i].Thickness + (pts[next].Thickness - pts[i].Thickness) * u;
    }

    /// <summary>Approximate the curved length of one control-point span, in units.</summary>
    public static double ArcLength(IReadOnlyList<RoadPoint> pts, int segment, bool closed = false)
    {
        const int samples = 32;
        double length = 0;
        Vec3 previous = Position(pts, segment, closed);
        for (int i = 1; i <= samples; i++)
        {
            double t = segment + (double)i / samples;
            Vec3 current = Position(pts, t, closed);
            length += (current - previous).Length;
            previous = current;
        }

        return length;
    }

    private static void GetSegmentAndU(int n, double t, out int segment, out double u)
    {
        double maxT = n - 1;
        if (t <= 0)
        {
            segment = 0;
            u = 0;
            return;
        }

        if (t >= maxT)
        {
            segment = n - 2;
            u = 1;
            return;
        }

        segment = (int)Math.Floor(t);
        if (segment > n - 2)
        {
            segment = n - 2;
        }

        u = t - segment;
    }
}

/// <summary>A right-handed orthonormal frame: T = travel direction, N = up, B = side.</summary>
public readonly struct RoadFrame
{
    public readonly Vec3 T;
    public readonly Vec3 N;
    public readonly Vec3 B;

    public RoadFrame(Vec3 t, Vec3 n, Vec3 b)
    {
        T = t;
        N = n;
        B = b;
    }
}

/// <summary>Computes a smooth, twist-free orientation frame along the curve using
/// parallel transport (double reflection) and applies per-point banking.</summary>
public sealed class FrameWalker
{
    private Vec3 _lastPos;
    private Vec3 _lastTan;
    private Vec3 _lastNormal;
    private bool _started;

    /// <summary>Advance the frame to a new sample and return the banked frame.</summary>
    public RoadFrame Step(Vec3 pos, Vec3 tangent, double bankRadians)
    {
        Vec3 t = tangent.Normalized();
        Vec3 n;
        if (!_started)
        {
            n = InitialNormal(t);
            _started = true;
        }
        else
        {
            n = Transport(_lastPos, _lastTan, _lastNormal, pos, t);
        }

        _lastPos = pos;
        _lastTan = t;
        _lastNormal = n;

        // Apply banking by rotating N around T.
        double c = Math.Cos(bankRadians);
        double s = Math.Sin(bankRadians);
        Vec3 b = Vec3.Cross(t, n).Normalized();
        Vec3 n2 = n * c + b * s;
        Vec3 b2 = Vec3.Cross(t, n2).Normalized();
        return new RoadFrame(t, n2, b2);
    }

    public void Reset()
    {
        _started = false;
    }

    private static Vec3 InitialNormal(Vec3 t)
    {
        Vec3 up = Vec3.UnitZ;
        Vec3 n = up - t * Vec3.Dot(up, t);
        if (n.LengthSq < 1e-8)
        {
            up = Vec3.UnitX;
            n = up - t * Vec3.Dot(up, t);
        }

        return n.Normalized();
    }

    private static Vec3 Transport(Vec3 p0, Vec3 t0, Vec3 n0, Vec3 p1, Vec3 t1)
    {
        Vec3 v1 = p1 - p0;
        double c1 = Vec3.Dot(v1, v1);
        if (c1 < 1e-12)
        {
            return n0;
        }

        Vec3 r = n0 - v1 * (2.0 * Vec3.Dot(v1, n0) / c1);
        Vec3 tt = t0 - v1 * (2.0 * Vec3.Dot(v1, t0) / c1);
        Vec3 v2 = t1 - tt;
        double c2 = Vec3.Dot(v2, v2);
        if (c2 < 1e-12)
        {
            return r.Normalized();
        }

        return (r - v2 * (2.0 * Vec3.Dot(v2, r) / c2)).Normalized();
    }
}
