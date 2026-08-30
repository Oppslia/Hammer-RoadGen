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

        List<RoadChain> chains = document.BuildChains();

        foreach (RoadChain chain in chains)
        {
            if (chain.Points.Count < 2)
            {
                continue;
            }

            AppendDisplacementChain(output, chain, ref solidId);
            generatedAnyTrack = true;
        }

        // Edge features (sidewalks, guardrails) follow each chain so a
        // joined road's sidewalk continues through the junction.
        foreach (RoadChain chain in chains)
        {
            AppendEdgeFeatures(output, chain, ref solidId);
        }

        if (!generatedAnyTrack)
        {
            throw new InvalidOperationException("Add at least two control points to a track.");
        }

        output.Append(Vmf.Footer);
        return output.ToString();
    }

    private static void AppendEdgeFeatures(StringBuilder output, RoadChain chain, ref int solidId)
    {
        if (chain.Points.Count < 2)
        {
            return;
        }

        List<ChainFeature> features = chain.CollectFeatures();
        if (features.Count == 0)
        {
            return;
        }

        int power = Math.Clamp(chain.Settings.Power, 2, 4);
        int resolution = 1 << power;
        double maxSegment = Math.Max(1.0, chain.Settings.SegmentLength);
        Vec3 up = new Vec3(0, 0, 1);
        double twist = ClosedLoopTwistOf(chain);

        foreach (ChainFeature chainFeature in features)
        {
            EdgeFeature feature = chainFeature.Feature;
            RoadSettings featureSettings = new RoadSettings
            {
                Material = feature.Material,
                TextureScale = chain.Settings.TextureScale,
                LightmapScale = chain.Settings.LightmapScale,
                Power = power,
                // A left strip stores its columns outer-first, so the builder's
                // "left wall" (column 0) is actually the outer face and vice versa.
                SolidLeft = feature.LeftSide ? feature.SolidOuter : feature.SolidInner,
                SolidRight = feature.LeftSide ? feature.SolidInner : feature.SolidOuter,
                SolidBottom = feature.SolidBottom
            };

            double sign = feature.LeftSide ? -1.0 : 1.0;
            FrameWalker walker = new FrameWalker();
            double textureV = 0;

            for (int segmentIndex = 0; segmentIndex < chain.Points.Count - 1; segmentIndex++)
            {
                double arcLength = RoadCurve.ArcLength(chain.Points, segmentIndex, chain.Closed);
                int subdivision = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

                for (int pieceIndex = 0; pieceIndex < subdivision; pieceIndex++)
                {
                    double t0 = segmentIndex + (double)pieceIndex / subdivision;
                    double t1 = segmentIndex + (double)(pieceIndex + 1) / subdivision;

                    // Always sample so the frame walker advances over the whole chain
                    // (matching the road's frame). Pieces outside this feature's range
                    // are walked but not written.
                    Vec3[,] grid = SampleEdgeGrid(chain.Points, t0, t1, resolution, walker, feature, sign, chainFeature, up, chain.Closed, twist);

                    if (t0 < chainFeature.StartPoint - 1e-9 || t1 > chainFeature.EndPoint - 1 + 1e-9)
                    {
                        continue;
                    }

                    double thicknessStart = FeatureThicknessAt(chainFeature, t0);
                    double thicknessEnd = FeatureThicknessAt(chainFeature, t1);
                    output.Append(DisplacementSegment.Build(solidId++, grid, thicknessStart, thicknessEnd, featureSettings, textureV, out double textureAdvance));
                    textureV += textureAdvance;
                }
            }
        }
    }

    private static Vec3[,] SampleEdgeGrid(
        IReadOnlyList<RoadPoint> points,
        double t0,
        double t1,
        int resolution,
        FrameWalker walker,
        EdgeFeature feature,
        double sign,
        ChainFeature chainFeature,
        Vec3 up,
        bool closed = false,
        double twist = 0)
    {
        int n = resolution + 1;
        Vec3[,] grid = new Vec3[n, n];
        double maxT = Math.Max(1.0, points.Count - 1);

        for (int row = 0; row < n; row++)
        {
            double t = t0 + (t1 - t0) * row / resolution;
            Vec3 pos = RoadCurve.Position(points, t, closed);
            Vec3 tan = RoadCurve.Tangent(points, t, closed);
            double roadWidth = RoadCurve.Width(points, t, closed);
            double bank = RoadCurve.Bank(points, t, closed) * Math.PI / 180.0;
            RoadFrame frame = walker.Step(pos, tan, bank);

            // A closed loop's frame must be twist-corrected identically to the road
            // surface, otherwise the sidewalk decouples from the road edge.
            if (twist != 0)
            {
                frame = RoadSurface.TwistCorrected(frame, t / maxT, twist);
            }

            EdgeFeaturePoint point = FeaturePointAt(chainFeature, t);
            double stripWidth = feature.Kind == EdgeFeatureKind.Guardrail ? 8.0 : Math.Max(0.5, point.Width);
            double topOffset = point.TopOffset;

            // Feature bank tilts the strip across its width (cross-slope).
            double cross = Math.Tan(point.BankDegrees * Math.PI / 180.0) * stripWidth;

            Vec3 edge = pos + frame.B * (sign * roadWidth / 2.0);
            Vec3 inner = edge + frame.B * (sign * feature.Offset) + up * topOffset;
            Vec3 outer = edge + frame.B * (sign * (feature.Offset + stripWidth)) + up * (topOffset + cross);

            for (int col = 0; col < n; col++)
            {
                double u = (double)col / resolution;

                // The displacement base face is anchored at column 0 with its cross
                // direction matching the road's (+B). A left strip runs the other way
                // (-B), so its columns are stored outer-first to keep the brush
                // winding consistent; otherwise Hammer rejects the solid as invalid.
                grid[row, col] = feature.LeftSide
                    ? outer + (inner - outer) * u
                    : inner + (outer - inner) * u;
            }
        }

        return grid;
    }

    private static EdgeFeaturePoint FeaturePointAt(ChainFeature chainFeature, double t)
    {
        double localT = t - chainFeature.StartPoint;
        int n = chainFeature.Points.Count;
        if (n == 0)
        {
            return new EdgeFeaturePoint();
        }

        if (n == 1 || localT <= 0)
        {
            return chainFeature.Points[0];
        }

        if (localT >= n - 1)
        {
            return chainFeature.Points[n - 1];
        }

        int index = (int)Math.Floor(localT);
        if (index > n - 2)
        {
            index = n - 2;
        }

        double u = localT - index;
        EdgeFeaturePoint first = chainFeature.Points[index];
        EdgeFeaturePoint second = chainFeature.Points[index + 1];

        // Linear interpolation between control points, matching the road's own
        // width/bank/thickness interpolation so the strip stays smooth.
        return new EdgeFeaturePoint
        {
            Width = first.Width + (second.Width - first.Width) * u,
            BottomOffset = first.BottomOffset + (second.BottomOffset - first.BottomOffset) * u,
            TopOffset = first.TopOffset + (second.TopOffset - first.TopOffset) * u,
            BankDegrees = first.BankDegrees + (second.BankDegrees - first.BankDegrees) * u
        };
    }

    private static double FeatureThicknessAt(ChainFeature chainFeature, double t)
    {
        EdgeFeaturePoint point = FeaturePointAt(chainFeature, t);
        return point.TopOffset - point.BottomOffset;
    }

    private static void AppendDisplacementChain(StringBuilder output, RoadChain chain, ref int solidId)
    {
        List<RoadPoint> points = chain.Points;

        double textureV = 0;
        FrameWalker walker = new FrameWalker();
        double twist = ClosedLoopTwistOf(chain);

        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            RoadSettings settings = SettingsForSegment(chain, segmentIndex);
            int power = Math.Clamp(settings.Power, 2, 4);
            int resolution = 1 << power;
            double maxSegment = Math.Max(1.0, settings.SegmentLength);

            double arcLength = RoadCurve.ArcLength(points, segmentIndex, chain.Closed);
            int subdivision = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int pieceIndex = 0; pieceIndex < subdivision; pieceIndex++)
            {
                double startT = segmentIndex + (double)pieceIndex / subdivision;
                double endT = segmentIndex + (double)(pieceIndex + 1) / subdivision;
                Vec3[,] grid = RoadSurface.SampleGrid(points, startT, endT, resolution, walker, chain.Closed, twist);
                double thicknessStart = RoadCurve.Thickness(points, startT, chain.Closed);
                double thicknessEnd = RoadCurve.Thickness(points, endT, chain.Closed);
                output.Append(DisplacementSegment.Build(solidId++, grid, thicknessStart, thicknessEnd, settings, textureV, out double textureAdvance));
                textureV += textureAdvance;
            }
        }
    }

    /// <summary>Measure the twist a closed loop accumulates across the seam so the
    /// exported cross-section returns to its starting orientation (matches the
    /// preview).</summary>
    private static double ClosedLoopTwistOf(RoadChain chain)
    {
        if (!chain.Closed || chain.Points.Count < 3)
        {
            return 0;
        }

        // Step a walker over the whole loop (one step per control point is enough to
        // capture the holonomy), and measure the residual rotation at the seam.
        FrameWalker measure = new FrameWalker();
        RoadFrame first = default;
        RoadFrame last = default;
        int count = chain.Points.Count;
        for (int i = 0; i <= count - 1; i++)
        {
            double t = i;
            Vec3 pos = RoadCurve.Position(chain.Points, t, closed: true);
            Vec3 tan = RoadCurve.Tangent(chain.Points, t, closed: true);
            double bank = RoadCurve.Bank(chain.Points, t, closed: true) * Math.PI / 180.0;
            RoadFrame f = measure.Step(pos, tan, bank);
            if (i == 0)
            {
                first = f;
            }

            last = f;
        }

        return RoadSurface.ClosedLoopTwist(first, last);
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
        double twist = ClosedLoopTwistOf(chain);

        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            RoadSettings settings = SettingsForSegment(chain, segmentIndex);
            int power = Math.Clamp(settings.Power, 2, 4);
            int resolution = 1 << power;
            double maxSegment = Math.Max(1.0, settings.SegmentLength);

            double arcLength = RoadCurve.ArcLength(points, segmentIndex, chain.Closed);
            int subdivision = Math.Max(1, (int)Math.Round(arcLength / maxSegment));

            for (int pieceIndex = 0; pieceIndex < subdivision; pieceIndex++)
            {
                double startT = segmentIndex + (double)pieceIndex / subdivision;
                double endT = segmentIndex + (double)(pieceIndex + 1) / subdivision;
                Vec3[,] grid = RoadSurface.SampleGrid(points, startT, endT, resolution, walker, chain.Closed, twist);
                double brushThickness = RoadCurve.Thickness(points, segmentIndex, chain.Closed);
#pragma warning disable CS0618 // BrushSegment is experimental/deprecated
                output.Append(BrushSegment.Build(grid, brushThickness, settings, ref solidId));
#pragma warning restore CS0618
            }
        }
    }
}
