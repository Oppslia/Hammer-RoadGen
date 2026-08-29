using System;
using System.Collections.Generic;
using System.Text;

namespace RoadGen.Core;

/// <summary>Turns a road document into a complete VMF file.</summary>
public static class RoadGenerator
{
    public static string GenerateVmf(RoadDocument document)
    {
        StringBuilder output = new StringBuilder();
        output.Append(Vmf.Header);

        int solidId = 2;
        bool generatedAnyTrack = false;

        foreach (RoadChain chain in document.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            AppendDisplacementChain(output, chain, ref solidId);
            generatedAnyTrack = true;
        }

        if (!generatedAnyTrack)
        {
            throw new InvalidOperationException("Add at least two control points to a track.");
        }

        output.Append(Vmf.Footer);
        return output.ToString();
    }

    private static void AppendDisplacementChain(StringBuilder output, RoadChain chain, ref int solidId)
    {
        List<RoadPoint> points = chain.Points;

        double textureV = 0;
        FrameWalker walker = new FrameWalker();

        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            RoadSettings settings = SettingsForSegment(chain, segmentIndex);
            int power = Math.Clamp(settings.Power, 2, 4);
            int resolution = 1 << power;
            double maxSegment = Math.Max(1.0, settings.SegmentLength);

            double arcLength = RoadCurve.ArcLength(points, segmentIndex);
            int subdivision = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int pieceIndex = 0; pieceIndex < subdivision; pieceIndex++)
            {
                double startT = segmentIndex + (double)pieceIndex / subdivision;
                double endT = segmentIndex + (double)(pieceIndex + 1) / subdivision;
                Vec3[,] grid = RoadSurface.SampleGrid(points, startT, endT, resolution, walker);
                double thicknessStart = RoadCurve.Thickness(points, startT);
                double thicknessEnd = RoadCurve.Thickness(points, endT);
                output.Append(DisplacementSegment.Build(solidId++, grid, thicknessStart, thicknessEnd, settings, textureV, out double textureAdvance));
                textureV += textureAdvance;
            }
        }
    }

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

    /// <summary>Turns every track into a VMF file made of plain solid brushes
    /// (two tetrahedra per sampled cell) instead of displacement surfaces.</summary>
    [Obsolete("Experimental brush export. Disabled by default; uncomment the UI wiring to use it.")]
    public static string GenerateBrushes(RoadDocument document)
    {
        StringBuilder output = new StringBuilder();
        output.Append(Vmf.Header);

        int solidId = 2;
        bool generatedAnyTrack = false;

        foreach (RoadChain chain in document.BuildChains())
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            AppendBrushChain(output, chain, ref solidId);
            generatedAnyTrack = true;
        }

        if (!generatedAnyTrack)
        {
            throw new InvalidOperationException("Add at least two control points to a track.");
        }

        output.Append(Vmf.Footer);
        return output.ToString();
    }

    private static void AppendBrushChain(StringBuilder output, RoadChain chain, ref int solidId)
    {
        List<RoadPoint> points = chain.Points;

        FrameWalker walker = new FrameWalker();

        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            RoadSettings settings = SettingsForSegment(chain, segmentIndex);
            int power = Math.Clamp(settings.Power, 2, 4);
            int resolution = 1 << power;
            double maxSegment = Math.Max(1.0, settings.SegmentLength);

            double arcLength = RoadCurve.ArcLength(points, segmentIndex);
            int subdivision = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int pieceIndex = 0; pieceIndex < subdivision; pieceIndex++)
            {
                double startT = segmentIndex + (double)pieceIndex / subdivision;
                double endT = segmentIndex + (double)(pieceIndex + 1) / subdivision;
                Vec3[,] grid = RoadSurface.SampleGrid(points, startT, endT, resolution, walker);
                double brushThickness = RoadCurve.Thickness(points, segmentIndex);
#pragma warning disable CS0618 // BrushSegment is experimental/deprecated
                output.Append(BrushSegment.Build(grid, brushThickness, settings, ref solidId));
#pragma warning restore CS0618
            }
        }
    }
}
