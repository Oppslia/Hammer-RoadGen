using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>Computes the displacement segment boundaries that the exporter will
/// produce, so the preview can draw the same layout the VMF export uses.</summary>
public static class SegmentLayout
{
    public sealed class Segment
    {
        /// <summary>Top-face base parallelogram corners, exactly as written to the
        /// VMF: A = left@t0, B = left@t1, C = right@t1, D = A + C - B. Hammer
        /// reconstructs the fourth corner as this parallelogram completion, so the
        /// brushes overlap each other on curves instead of touching edge-to-edge.</summary>
        public Vec3 A;
        public Vec3 B;
        public Vec3 C;
        public Vec3 D;
    }

    /// <summary>Subdivide the road exactly like the exporter and return one entry
    /// per displacement segment.</summary>
    public static List<Segment> Compute(IReadOnlyList<RoadPoint> pts, RoadSettings s)
    {
        var result = new List<Segment>();
        if (pts.Count < 2)
        {
            return result;
        }

        double maxSegment = Math.Max(1.0, s.SegmentLength);
        var walker = new FrameWalker();

        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double arcLength = RoadCurve.ArcLength(pts, seg);
            int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int k = 0; k < subdiv; k++)
            {
                double t0 = seg + (double)k / subdiv;
                double t1 = seg + (double)(k + 1) / subdiv;

                Vec3 pos0 = RoadCurve.Position(pts, t0);
                Vec3 tan0 = RoadCurve.Tangent(pts, t0);
                double half0 = RoadCurve.Width(pts, t0) / 2.0;
                double bank0 = RoadCurve.Bank(pts, t0) * Math.PI / 180.0;
                RoadFrame f0 = walker.Step(pos0, tan0, bank0);

                Vec3 pos1 = RoadCurve.Position(pts, t1);
                Vec3 tan1 = RoadCurve.Tangent(pts, t1);
                double half1 = RoadCurve.Width(pts, t1) / 2.0;
                double bank1 = RoadCurve.Bank(pts, t1) * Math.PI / 180.0;
                RoadFrame f1 = walker.Step(pos1, tan1, bank1);

                Vec3 a = pos0 - f0.B * half0; // left @ t0
                Vec3 b = pos1 - f1.B * half1; // left @ t1
                Vec3 c = pos1 + f1.B * half1; // right @ t1

                result.Add(new Segment
                {
                    A = a,
                    B = b,
                    C = c,
                    D = a + c - b // parallelogram completion, as Hammer reconstructs it
                });
            }
        }

        return result;
    }

    /// <summary>Number of displacement brushes the exporter would produce at the
    /// given segment length. Matches RoadGenerator.GenerateVmf exactly.</summary>
    public static int CountSegments(IReadOnlyList<RoadPoint> pts, double segmentLength)
    {
        if (pts.Count < 2)
        {
            return 0;
        }

        double maxSegment = Math.Max(1.0, segmentLength);
        int total = 0;
        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double arcLength = RoadCurve.ArcLength(pts, seg);
            total += Math.Max(1, (int)Math.Round(arcLength / maxSegment));
        }

        return total;
    }

    /// <summary>Find the next segment length (above <paramref name="current"/>) that
    /// actually reduces the brush count. Increasing the optimization scale only
    /// changes the output at these breakpoints, so this lets the UI jump straight to
    /// the next useful value instead of stepping through values that do nothing.</summary>
    public static double NextBreakpoint(IReadOnlyList<RoadPoint> pts, double current, out int nextCount)
    {
        nextCount = 0;
        if (pts.Count < 2)
        {
            return current;
        }

        current = Math.Max(1.0, current);
        int currentCount = CountSegments(pts, current);

        // For each span, subdiv = round(arcLength / L). It drops from k to k-1 when
        // L passes arcLength / (k - 0.5). Collect every such threshold above the
        // current value, then take the smallest one that lowers the total.
        var candidates = new SortedSet<double>();
        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double a = RoadCurve.ArcLength(pts, seg);
            int k = Math.Max(1, (int)Math.Round(a / current));
            for (int n = 2; n <= k; n++)
            {
                double t = a / (n - 0.5);
                if (t > current)
                {
                    candidates.Add(t);
                }
            }
        }

        foreach (double t in candidates)
        {
            int at = CountSegments(pts, t);
            if (at < currentCount)
            {
                nextCount = at;
                return t;
            }

            // Math.Round uses banker's rounding, so when n is even the drop lands
            // just above the exact half-integer boundary. Nudge past it.
            double bump = Math.Max(0.01, t * 1e-6);
            int above = CountSegments(pts, t + bump);
            if (above < currentCount)
            {
                nextCount = above;
                return t + bump;
            }
        }

        nextCount = currentCount;
        return current;
    }

    /// <summary>Find the previous segment length (below <paramref name="current"/>) that
    /// increases the brush count — the closest "un-optimize" step. This is the mirror
    /// of <see cref="NextBreakpoint"/>.</summary>
    public static double PreviousBreakpoint(IReadOnlyList<RoadPoint> pts, double current, out int prevCount)
    {
        prevCount = 0;
        if (pts.Count < 2)
        {
            return current;
        }

        current = Math.Max(1.0, current);
        int currentCount = CountSegments(pts, current);
        if (current <= 1.0)
        {
            prevCount = currentCount;
            return current;
        }

        // For each span, subdiv grows from k to k+1 when L drops below
        // arcLength / (k + 0.5). The next breakpoint going down is the largest
        // such threshold still below the current value.
        double best = double.NegativeInfinity;
        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double a = RoadCurve.ArcLength(pts, seg);
            int k = Math.Max(1, (int)Math.Round(a / current));
            double t = a / (k + 0.5);
            if (t >= current)
            {
                continue;
            }

            double bump = Math.Max(0.01, t * 1e-6);
            if (CountSegments(pts, t) > currentCount && t > best)
            {
                best = t;
            }

            double below = t - bump;
            if (below < current && CountSegments(pts, below) > currentCount && below > best)
            {
                best = below;
            }
        }

        if (best == double.NegativeInfinity)
        {
            prevCount = currentCount;
            return current;
        }

        // The segment length floor is 1; clamp any sub-unit breakpoint up to 1.
        best = Math.Max(1.0, best);
        prevCount = CountSegments(pts, best);
        if (prevCount <= currentCount)
        {
            prevCount = currentCount;
            return current;
        }

        return best;
    }
}
