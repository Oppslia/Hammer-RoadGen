using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>Samples the road surface as a grid of 3D points. The surface is
/// S(t, u) = center(t) + B(t) * (u * width(t) / 2), with u in [-1, 1].</summary>
public static class RoadSurface
{
    /// <summary>Sample a displacement-sized grid over the parameter range [t0, t1].
    /// Row runs along the road (t), column runs across the road (u).
    /// The supplied frame walker keeps the orientation continuous across segments.</summary>
    public static Vec3[,] SampleGrid(
        IReadOnlyList<RoadPoint> pts,
        double t0,
        double t1,
        int resolution,
        FrameWalker walker)
    {
        int n = resolution + 1;
        Vec3[,] grid = new Vec3[n, n];

        for (int row = 0; row < n; row++)
        {
            double t = t0 + (t1 - t0) * row / resolution;
            Vec3 pos = RoadCurve.Position(pts, t);
            Vec3 tan = RoadCurve.Tangent(pts, t);
            double width = RoadCurve.Width(pts, t);
            double bank = RoadCurve.Bank(pts, t) * Math.PI / 180.0;
            RoadFrame frame = walker.Step(pos, tan, bank);

            double half = width / 2.0;
            for (int col = 0; col < n; col++)
            {
                double u = -1.0 + 2.0 * col / resolution;
                grid[row, col] = pos + frame.B * (u * half);
            }
        }

        return grid;
    }
}

/// <summary>Lightweight polyline sampling used by the preview viewports.</summary>
public sealed class RoadPreviewMesh
{
    public readonly List<Vec3> Center = new List<Vec3>();
    public readonly List<Vec3> Left = new List<Vec3>();
    public readonly List<Vec3> Right = new List<Vec3>();
    public readonly List<Vec3> BottomCenter = new List<Vec3>();
    public readonly List<Vec3> BottomLeft = new List<Vec3>();
    public readonly List<Vec3> BottomRight = new List<Vec3>();

    public static RoadPreviewMesh Build(IReadOnlyList<RoadPoint> pts, int stepsPerSegment)
    {
        var mesh = new RoadPreviewMesh();
        if (pts.Count < 2)
        {
            return mesh;
        }

        if (stepsPerSegment < 1)
        {
            stepsPerSegment = 1;
        }

        int total = (pts.Count - 1) * stepsPerSegment;
        var walker = new FrameWalker();

        for (int i = 0; i <= total; i++)
        {
            double t = (double)i / stepsPerSegment;
            Vec3 pos = RoadCurve.Position(pts, t);
            Vec3 tan = RoadCurve.Tangent(pts, t);
            double width = RoadCurve.Width(pts, t);
            double bank = RoadCurve.Bank(pts, t) * Math.PI / 180.0;
            RoadFrame frame = walker.Step(pos, tan, bank);

            // Match the export: the road body hangs straight down (world -Z),
            // at this sample's interpolated thickness.
            Vec3 down = new Vec3(0, 0, -1) * RoadCurve.Thickness(pts, t);
            double half = width / 2.0;

            mesh.Center.Add(pos);
            mesh.Left.Add(pos - frame.B * half);
            mesh.Right.Add(pos + frame.B * half);
            mesh.BottomCenter.Add(pos + down);
            mesh.BottomLeft.Add(pos - frame.B * half + down);
            mesh.BottomRight.Add(pos + frame.B * half + down);
        }

        return mesh;
    }
}

/// <summary>Polyline sampling for an edge feature (sidewalk/guardrail). Four
/// parallel lines: inner/outer edge at the top and bottom of the strip.</summary>
public sealed class EdgePreviewMesh
{
    public readonly List<Vec3> InnerTop = new List<Vec3>();
    public readonly List<Vec3> OuterTop = new List<Vec3>();
    public readonly List<Vec3> InnerBase = new List<Vec3>();
    public readonly List<Vec3> OuterBase = new List<Vec3>();

    public static EdgePreviewMesh Build(IReadOnlyList<RoadPoint> pts, int stepsPerSegment, ChainFeature chainFeature)
    {
        EdgePreviewMesh mesh = new EdgePreviewMesh();
        if (pts.Count < 2 || chainFeature == null || chainFeature.Points.Count == 0)
        {
            return mesh;
        }

        if (stepsPerSegment < 1)
        {
            stepsPerSegment = 1;
        }

        int startPoint = Math.Clamp(chainFeature.StartPoint, 0, pts.Count - 1);
        int endPoint = Math.Clamp(chainFeature.EndPoint, startPoint + 1, pts.Count);
        EdgeFeature feature = chainFeature.Feature;
        double sign = feature.LeftSide ? -1.0 : 1.0;

        int total = (pts.Count - 1) * stepsPerSegment;
        FrameWalker walker = new FrameWalker();
        Vec3 up = new Vec3(0, 0, 1);

        for (int i = 0; i <= total; i++)
        {
            double t = (double)i / stepsPerSegment;

            // Step the frame over the whole chain so the orientation here matches the
            // road's frame (parallel transport + banking accumulate along the chain).
            Vec3 pos = RoadCurve.Position(pts, t);
            Vec3 tan = RoadCurve.Tangent(pts, t);
            double roadWidth = RoadCurve.Width(pts, t);
            double bank = RoadCurve.Bank(pts, t) * Math.PI / 180.0;
            RoadFrame frame = walker.Step(pos, tan, bank);

            if (t < startPoint - 1e-9 || t > endPoint - 1 + 1e-9)
            {
                continue;
            }

            EdgeFeaturePoint point = FeaturePointAt(chainFeature.Points, chainFeature.StartPoint, t);
            double stripWidth = feature.Kind == EdgeFeatureKind.Guardrail ? 8.0 : Math.Max(0.5, point.Width);
            double topOffset = point.TopOffset;
            double bottomOffset = point.BottomOffset;
            double cross = Math.Tan(point.BankDegrees * Math.PI / 180.0) * stripWidth;

            Vec3 edge = pos + frame.B * (sign * roadWidth / 2.0);
            Vec3 inner = edge + frame.B * (sign * feature.Offset);
            Vec3 outer = edge + frame.B * (sign * (feature.Offset + stripWidth));

            mesh.InnerTop.Add(inner + up * topOffset);
            mesh.OuterTop.Add(outer + up * (topOffset + cross));
            mesh.InnerBase.Add(inner + up * bottomOffset);
            mesh.OuterBase.Add(outer + up * (bottomOffset + cross));
        }

        return mesh;
    }

    private static EdgeFeaturePoint FeaturePointAt(IReadOnlyList<EdgeFeaturePoint> points, int featureStartIndex, double t)
    {
        double localT = t - featureStartIndex;
        int n = points.Count;
        if (n == 0)
        {
            return new EdgeFeaturePoint();
        }

        if (n == 1 || localT <= 0)
        {
            return points[0];
        }

        if (localT >= n - 1)
        {
            return points[n - 1];
        }

        int index = (int)Math.Floor(localT);
        if (index > n - 2)
        {
            index = n - 2;
        }

        double u = localT - index;
        EdgeFeaturePoint first = points[index];
        EdgeFeaturePoint second = points[index + 1];

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
}
