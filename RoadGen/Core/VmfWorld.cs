using System;
using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>One face of a convex brush: an ordered, closed polygon loop (world-space
/// vertices) plus its material, outward-facing plane normal and Hammer texture axes
/// (uaxis/vaxis) used to compute exact in-game UVs.</summary>
public sealed class VmfFace
{
    public readonly List<Vec3> Vertices = new List<Vec3>();
    public string Material = "";
    public Vec3 Normal;

    // Hammer texture axes. Coordinate matches Hammer's CalcTextureCoords:
    // s = dot(P, UAxis) / UScale + UShift, then s / textureWidth.
    public Vec3 UAxis = Vec3.UnitX;
    public Vec3 VAxis = Vec3.UnitY;
    public double UShift;
    public double VShift;
    public double UScale = 1.0;
    public double VScale = 1.0;

    /// <summary>Computes Hammer-exact texture coordinates for a world point, in texture
    /// repeats (un-tiled). Faces wider than one repeat keep their repeat count so the
    /// rasterizer tiles them per pixel; that is what Hammer does with its normalized
    /// s/width coordinate.</summary>
    public void GetUV(Vec3 p, double texW, double texH, out double u, out double v)
    {
        double us = UScale != 0 ? UScale : 1.0;
        double vs = VScale != 0 ? VScale : 1.0;
        double w = texW > 0 ? texW : 1.0;
        double h = texH > 0 ? texH : 1.0;
        u = (Vec3.Dot(UAxis, p) / us + UShift) / w;
        v = (Vec3.Dot(VAxis, p) / vs + VShift) / h;
    }
}

/// <summary>A convex brush reconstructed from a VMF "solid" block — one face per
/// side (displacement sides are handled separately as <see cref="VmfDisplacement"/>).</summary>
public sealed class VmfBrush
{
    public readonly List<VmfFace> Faces = new List<VmfFace>();
}

/// <summary>A displacement surface: a power-of-two grid of world-space vertices, plus
/// the Hammer texture axes of its base face for exact texture mapping.</summary>
public sealed class VmfDisplacement
{
    public Vec3[,] Grid = new Vec3[0, 0];
    public int Power;
    public string Material = "";

    // Hammer texture axes of the displacement's base face. Coordinate matches Hammer's
    // CalcTextureCoords: s = dot(P, UAxis) / UScale + UShift, then s / textureWidth.
    public Vec3 UAxis = Vec3.UnitX;
    public Vec3 VAxis = Vec3.UnitY;
    public double UShift;
    public double VShift;
    public double UScale = 1.0;
    public double VScale = 1.0;

    /// <summary>Computes Hammer-exact texture coordinates for a world point, in texture
    /// repeats (un-tiled), for the rasterizer to tile per pixel.</summary>
    public void GetUV(Vec3 p, double texW, double texH, out double u, out double v)
    {
        double us = UScale != 0 ? UScale : 1.0;
        double vs = VScale != 0 ? VScale : 1.0;
        double w = texW > 0 ? texW : 1.0;
        double h = texH > 0 ? texH : 1.0;
        u = (Vec3.Dot(UAxis, p) / us + UShift) / w;
        v = (Vec3.Dot(VAxis, p) / vs + VShift) / h;
    }
}

/// <summary>The extracted world geometry of a VMF: convex brushes plus displacement
/// surfaces. This is a reference layout only — it carries no road semantics, unlike
/// <c>VmfImporter</c>.</summary>
public sealed class VmfWorld
{
    public readonly List<VmfBrush> Brushes = new List<VmfBrush>();
    public readonly List<VmfDisplacement> Displacements = new List<VmfDisplacement>();
}
