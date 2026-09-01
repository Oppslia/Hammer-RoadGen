using System.Collections.Generic;

namespace RoadGen.Core;

/// <summary>One face of a convex brush: an ordered, closed polygon loop (world-space
/// vertices) plus its material and outward-facing plane normal.</summary>
public sealed class VmfFace
{
    public readonly List<Vec3> Vertices = new List<Vec3>();
    public string Material = "";
    public Vec3 Normal;
}

/// <summary>A convex brush reconstructed from a VMF "solid" block — one face per
/// side (displacement sides are handled separately as <see cref="VmfDisplacement"/>).</summary>
public sealed class VmfBrush
{
    public readonly List<VmfFace> Faces = new List<VmfFace>();
}

/// <summary>A displacement surface: a power-of-two grid of world-space vertices.</summary>
public sealed class VmfDisplacement
{
    public Vec3[,] Grid = new Vec3[0, 0];
    public int Power;
    public string Material = "";
}

/// <summary>The extracted world geometry of a VMF: convex brushes plus displacement
/// surfaces. This is a reference layout only — it carries no road semantics, unlike
/// <c>VmfImporter</c>.</summary>
public sealed class VmfWorld
{
    public readonly List<VmfBrush> Brushes = new List<VmfBrush>();
    public readonly List<VmfDisplacement> Displacements = new List<VmfDisplacement>();
}
