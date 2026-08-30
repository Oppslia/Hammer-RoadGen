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

        /// <summary>Road parameter at each end of this segment, used to look up the
        /// interpolated thickness for the preview boxes.</summary>
        public double T0;
        public double T1;
    }

    /// <summary>Subdivide the road exactly like the exporter and return one entry
    /// per displacement segment.</summary>
    public static List<Segment> Compute(IReadOnlyList<RoadPoint> pts, RoadSettings s, bool closed = false)
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
            double arcLength = RoadCurve.ArcLength(pts, seg, closed);
            int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int k = 0; k < subdiv; k++)
            {
                double t0 = seg + (double)k / subdiv;
                double t1 = seg + (double)(k + 1) / subdiv;

                Vec3 pos0 = RoadCurve.Position(pts, t0, closed);
                Vec3 tan0 = RoadCurve.Tangent(pts, t0, closed);
                double half0 = RoadCurve.Width(pts, t0, closed) / 2.0;
                double bank0 = RoadCurve.Bank(pts, t0, closed) * Math.PI / 180.0;
                RoadFrame f0 = walker.Step(pos0, tan0, bank0);

                Vec3 pos1 = RoadCurve.Position(pts, t1, closed);
                Vec3 tan1 = RoadCurve.Tangent(pts, t1, closed);
                double half1 = RoadCurve.Width(pts, t1, closed) / 2.0;
                double bank1 = RoadCurve.Bank(pts, t1, closed) * Math.PI / 180.0;
                RoadFrame f1 = walker.Step(pos1, tan1, bank1);

                Vec3 a = pos0 - f0.B * half0; // left @ t0
                Vec3 b = pos1 - f1.B * half1; // left @ t1
                Vec3 c = pos1 + f1.B * half1; // right @ t1

                result.Add(new Segment
                {
                    A = a,
                    B = b,
                    C = c,
                    D = a + c - b, // parallelogram completion, as Hammer reconstructs it
                    T0 = t0,
                    T1 = t1
                });
            }
        }

        return result;
    }

    /// <summary>Number of displacement brushes the exporter would produce at the
    /// given segment length. Matches RoadGenerator.GenerateVmf exactly.</summary>
    public static int CountSegments(IReadOnlyList<RoadPoint> pts, double segmentLength, bool closed = false)
    {
        if (pts.Count < 2)
        {
            return 0;
        }

        double maxSegment = Math.Max(1.0, segmentLength);
        int total = 0;
        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double arcLength = RoadCurve.ArcLength(pts, seg, closed);
            total += Math.Max(1, (int)Math.Round(arcLength / maxSegment));
        }

        return total;
    }

    /// <summary>Find the next segment length (above <paramref name="current"/>) that
    /// actually reduces the brush count. Increasing the optimization scale only
    /// changes the output at these breakpoints, so this lets the UI jump straight to
    /// the next useful value instead of stepping through values that do nothing.</summary>
    public static double NextBreakpoint(IReadOnlyList<RoadPoint> pts, double current, out int nextCount, bool closed = false)
    {
        nextCount = 0;
        if (pts.Count < 2)
        {
            return current;
        }

        current = Math.Max(1.0, current);
        int currentCount = CountSegments(pts, current, closed);

        // For each span, subdiv = round(arcLength / L). It drops from k to k-1 when
        // L passes arcLength / (k - 0.5). Collect every such threshold above the
        // current value, then take the smallest one that lowers the total.
        var candidates = new SortedSet<double>();
        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double a = RoadCurve.ArcLength(pts, seg, closed);
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
            int at = CountSegments(pts, t, closed);
            if (at < currentCount)
            {
                nextCount = at;
                return t;
            }

            // Math.Round uses banker's rounding, so when n is even the drop lands
            // just above the exact half-integer boundary. Nudge past it.
            double bump = Math.Max(0.01, t * 1e-6);
            int above = CountSegments(pts, t + bump, closed);
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
    public static double PreviousBreakpoint(IReadOnlyList<RoadPoint> pts, double current, out int prevCount, bool closed = false)
    {
        prevCount = 0;
        if (pts.Count < 2)
        {
            return current;
        }

        current = Math.Max(1.0, current);
        int currentCount = CountSegments(pts, current, closed);
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
            double a = RoadCurve.ArcLength(pts, seg, closed);
            int k = Math.Max(1, (int)Math.Round(a / current));
            double t = a / (k + 0.5);
            if (t >= current)
            {
                continue;
            }

            double bump = Math.Max(0.01, t * 1e-6);
            if (CountSegments(pts, t, closed) > currentCount && t > best)
            {
                best = t;
            }

            double below = t - bump;
            if (below < current && CountSegments(pts, below, closed) > currentCount && below > best)
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
        prevCount = CountSegments(pts, best, closed);
        if (prevCount <= currentCount)
        {
            prevCount = currentCount;
            return current;
        }

        return best;
    }

    /// <summary>Subdivide one edge feature exactly like the exporter and return one
    /// entry per displacement segment (the strip's top-face base parallelogram).
    /// Each segment uses its own track's segment length, so a joined chain's sidewalk
    /// optimization per span matches what that track produces on its own.</summary>
    public static List<Segment> ComputeFeatureSegments(RoadChain chain, ChainFeature chainFeature)
    {
        IReadOnlyList<RoadPoint> pts = chain.Points;
        bool closed = chain.Closed;
        var result = new List<Segment>();
        if (pts.Count < 2 || chainFeature == null || chainFeature.Points.Count == 0)
        {
            return result;
        }

        var walker = new FrameWalker();
        EdgeFeature feature = chainFeature.Feature;
        double sign = feature.LeftSide ? -1.0 : 1.0;
        Vec3 up = new Vec3(0, 0, 1);

        int startPoint = Math.Clamp(chainFeature.StartPoint, 0, pts.Count - 1);
        int endPoint = Math.Clamp(chainFeature.EndPoint, startPoint + 1, pts.Count);

        // Same twist correction as the road/sidewalk preview so the segments stay
        // glued to the edge on a closed loop. Measure over the same samples the
        // build walker steps (per-segment piece boundaries) so the measured twist
        // matches the frames the segments actually carry, rather than a coarse
        // control-point walker.
        double twist = 0;
        if (closed && pts.Count >= 3)
        {
            FrameWalker measure = new FrameWalker();
            RoadFrame first = default;
            RoadFrame last = default;
            for (int seg = 0; seg < pts.Count - 1; seg++)
            {
                double maxSegment = Math.Max(1.0, SettingsForSegment(chain, seg).SegmentLength);
                double arcLength = RoadCurve.ArcLength(pts, seg, true);
                int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));
                for (int k = 0; k <= subdiv; k++)
                {
                    double tj = seg + (double)k / subdiv;
                    Vec3 pos = RoadCurve.Position(pts, tj, true);
                    Vec3 tan = RoadCurve.Tangent(pts, tj, true);
                    double bank = RoadCurve.Bank(pts, tj, true) * Math.PI / 180.0;
                    RoadFrame f = measure.Step(pos, tan, bank);
                    if (seg == 0 && k == 0)
                    {
                        first = f;
                    }

                    last = f;
                }
            }

            twist = RoadSurface.ClosedLoopTwist(first, last);
        }

        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double maxSegment = Math.Max(1.0, SettingsForSegment(chain, seg).SegmentLength);
            double arcLength = RoadCurve.ArcLength(pts, seg, closed);
            int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int k = 0; k < subdiv; k++)
            {
                double t0 = seg + (double)k / subdiv;
                double t1 = seg + (double)(k + 1) / subdiv;

                // Always advance the frame over the whole chain so the orientation
                // here matches the road's (parallel transport accumulates).
                RoadFrame frame0 = StepFrame(walker, pts, t0, closed, twist);
                RoadFrame frame1 = StepFrame(walker, pts, t1, closed, twist);

                if (t0 < startPoint - 1e-9 || t1 > endPoint - 1 + 1e-9)
                {
                    continue;
                }

                Vec3 inner0 = SampleInnerEdge(pts, t0, frame0, feature, sign, chainFeature, up, closed);
                Vec3 outer0 = SampleOuterEdge(pts, t0, frame0, feature, sign, chainFeature, up, closed);
                Vec3 inner1 = SampleInnerEdge(pts, t1, frame1, feature, sign, chainFeature, up, closed);
                Vec3 outer1 = SampleOuterEdge(pts, t1, frame1, feature, sign, chainFeature, up, closed);

                result.Add(new Segment
                {
                    A = inner0,
                    B = inner1,
                    C = outer1,
                    D = inner0 + outer1 - inner1,
                    T0 = t0,
                    T1 = t1
                });
            }
        }

        return result;
    }

    /// <summary>The settings of the track that owns a chain segment, so a joined
    /// chain's edge features use each track's own optimization (segment length,
    /// resolution) rather than the chain's single (first) settings.</summary>
    private static RoadSettings SettingsForSegment(RoadChain chain, int segmentIndex)
    {
        foreach (ChainSpan span in chain.Spans)
        {
            if (segmentIndex >= span.StartPoint && segmentIndex < span.EndPoint - 1)
            {
                return span.Track.Settings;
            }
        }

        return chain.Settings;
    }

    private static RoadFrame StepFrame(FrameWalker walker, IReadOnlyList<RoadPoint> pts, double t, bool closed = false, double twist = 0)
    {
        Vec3 pos = RoadCurve.Position(pts, t, closed);
        Vec3 tan = RoadCurve.Tangent(pts, t, closed);
        double bank = RoadCurve.Bank(pts, t, closed) * Math.PI / 180.0;
        RoadFrame frame = walker.Step(pos, tan, bank);
        if (twist != 0)
        {
            double maxT = Math.Max(1.0, pts.Count - 1);
            frame = RoadSurface.TwistCorrected(frame, t / maxT, twist);
        }

        return frame;
    }

    private static Vec3 SampleInnerEdge(IReadOnlyList<RoadPoint> pts, double t, RoadFrame frame, EdgeFeature feature, double sign, ChainFeature chainFeature, Vec3 up, bool closed = false)
    {
        Vec3 pos = RoadCurve.Position(pts, t, closed);
        double roadWidth = RoadCurve.Width(pts, t, closed);
        Vec3 edge = pos + frame.B * (sign * roadWidth / 2.0);
        EdgeFeaturePoint point = chainFeature.PointAt(t);
        double topOffset = point.TopOffset;
        return edge + frame.B * (sign * feature.Offset) + up * topOffset;
    }

    private static Vec3 SampleOuterEdge(IReadOnlyList<RoadPoint> pts, double t, RoadFrame frame, EdgeFeature feature, double sign, ChainFeature chainFeature, Vec3 up, bool closed = false)
    {
        Vec3 pos = RoadCurve.Position(pts, t, closed);
        double roadWidth = RoadCurve.Width(pts, t, closed);
        Vec3 edge = pos + frame.B * (sign * roadWidth / 2.0);
        EdgeFeaturePoint point = chainFeature.PointAt(t);
        double stripWidth = feature.Kind == EdgeFeatureKind.Guardrail ? 8.0 : Math.Max(0.5, point.Width);
        double topOffset = point.TopOffset;
        double cross = Math.Tan(point.BankDegrees * Math.PI / 180.0) * stripWidth;
        return edge + frame.B * (sign * (feature.Offset + stripWidth)) + up * (topOffset + cross);
    }
}
