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

    public static RoadPreviewMesh Build(IReadOnlyList<RoadPoint> pts, int stepsPerSegment, double thickness)
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

            // Match the export: the road body hangs straight down (world -Z).
            Vec3 down = new Vec3(0, 0, -1) * thickness;
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
