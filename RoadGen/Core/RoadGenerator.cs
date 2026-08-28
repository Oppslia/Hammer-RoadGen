using System;
using System.Collections.Generic;
using System.Text;

namespace RoadGen.Core;

/// <summary>Turns a road document into a complete VMF file.</summary>
public static class RoadGenerator
{
    public static string GenerateVmf(RoadDocument doc)
    {
        var pts = doc.Points;
        var s = doc.Settings;

        if (pts.Count < 2)
        {
            throw new InvalidOperationException("The road needs at least two control points.");
        }

        if (s.Power < 2 || s.Power > 4)
        {
            throw new InvalidOperationException("Displacement power must be 2, 3 or 4.");
        }

        int res = 1 << s.Power;
        double maxSegment = Math.Max(1.0, s.SegmentLength);

        var sb = new StringBuilder();
        sb.Append(Vmf.Header);

        int solidId = 2;
        double textureV = 0;
        var walker = new FrameWalker();

        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double arcLength = ApproximateArcLength(pts, seg);
            int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int k = 0; k < subdiv; k++)
            {
                double t0 = seg + (double)k / subdiv;
                double t1 = seg + (double)(k + 1) / subdiv;
                Vec3[,] grid = RoadSurface.SampleGrid(pts, t0, t1, res, walker);
                sb.Append(DisplacementSegment.Build(solidId++, grid, s, textureV, out double advance));
                textureV += advance;
            }
        }

        sb.Append(Vmf.Footer);
        return sb.ToString();
    }

    /// <summary>Turns a road document into a VMF file made of plain solid brushes
    /// (two tetrahedra per sampled cell) instead of displacement surfaces.</summary>
    [Obsolete("Experimental brush export. Disabled by default; uncomment the UI wiring to use it.")]
    public static string GenerateBrushes(RoadDocument doc)
    {
        var pts = doc.Points;
        var s = doc.Settings;

        if (pts.Count < 2)
        {
            throw new InvalidOperationException("The road needs at least two control points.");
        }

        int power = Math.Clamp(s.Power, 2, 4);
        int res = 1 << power;
        double maxSegment = Math.Max(1.0, s.SegmentLength);

        var sb = new StringBuilder();
        sb.Append(Vmf.Header);

        int solidId = 2;
        var walker = new FrameWalker();

        for (int seg = 0; seg < pts.Count - 1; seg++)
        {
            double arcLength = ApproximateArcLength(pts, seg);
            int subdiv = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int k = 0; k < subdiv; k++)
            {
                double t0 = seg + (double)k / subdiv;
                double t1 = seg + (double)(k + 1) / subdiv;
                Vec3[,] grid = RoadSurface.SampleGrid(pts, t0, t1, res, walker);
#pragma warning disable CS0618 // BrushSegment is experimental/deprecated
                sb.Append(BrushSegment.Build(grid, s, ref solidId));
#pragma warning restore CS0618
            }
        }

        sb.Append(Vmf.Footer);
        return sb.ToString();
    }

    private static double ApproximateArcLength(IReadOnlyList<RoadPoint> pts, int segment)
    {
        const int samples = 32;
        double length = 0;
        Vec3 previous = RoadCurve.Position(pts, segment);
        for (int i = 1; i <= samples; i++)
        {
            double t = segment + (double)i / samples;
            Vec3 current = RoadCurve.Position(pts, t);
            length += (current - previous).Length;
            previous = current;
        }

        return length;
    }
}
