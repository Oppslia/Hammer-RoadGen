using System;
using System.Text;

namespace RoadGen.Core;

/// <summary>Builds plain solid brushes (one tetrahedron per triangle) from a sampled
/// surface grid. This is the "brush" export mode: instead of displacement surfaces,
/// the road is tessellated into convex 4-sided solids extruded along the surface
/// normal by the supplied thickness.</summary>
[Obsolete("Experimental brush export. Disabled by default; uncomment the UI wiring to use it.")]
public static class BrushSegment
{
    public static string Build(Vec3[,] grid, double thickness, RoadSettings s, ref int solidId)
    {
        int res = grid.GetLength(0) - 1;
        StringBuilder sb = new StringBuilder();

        for (int row = 0; row < res; row++)
        {
            for (int col = 0; col < res; col++)
            {
                // Two triangles per grid cell, wound consistently.
                AppendTriangle(sb, solidId++, grid[row, col], grid[row + 1, col], grid[row + 1, col + 1], s, thickness);
                AppendTriangle(sb, solidId++, grid[row, col], grid[row + 1, col + 1], grid[row, col + 1], s, thickness);
            }
        }

        return sb.ToString();
    }

    private static void AppendTriangle(StringBuilder sb, int solidId, Vec3 a, Vec3 b, Vec3 c, RoadSettings s, double thickness)
    {
        Vec3 normal = Vec3.Cross(b - a, c - a);
        if (normal.LengthSq < 1e-12)
        {
            return; // degenerate triangle, skip
        }

        normal = normal.Normalized();

        // Extrude downward so the brush body sits below the road surface.
        if (normal.Z > 0)
        {
            normal = -normal;
        }

        double depth = Math.Max(1.0, thickness);
        Vec3 apex = (a + b + c) / 3.0 + normal * depth;

        Vec3 A = a.Rounded(6);
        Vec3 B = b.Rounded(6);
        Vec3 C = c.Rounded(6);
        Vec3 D = apex.Rounded(6);

        sb.Append("\tsolid\r\n\t{\r\n");
        sb.Append("\t\t\"id\" \"").Append(solidId).Append("\"\r\n");

        // Face 1: the road surface triangle; U runs across, V runs along the road.
        Vec3 uTop = (C - B).Normalized();
        Vec3 vTop = (B - A).Normalized();
        AppendFace(sb, 1, A, B, C, D, s, uTop, vTop, A);

        // Remaining three faces close the tetrahedron.
        AppendFace(sb, 2, A, C, D, B, s, null, null, A);
        AppendFace(sb, 3, A, D, B, C, s, null, null, A);
        AppendFace(sb, 4, B, D, C, A, s, null, null, B);

        sb.Append("\t\teditor\r\n\t\t{\r\n");
        sb.Append("\t\t\t\"color\" \"0 173 190\"\r\n");
        sb.Append("\t\t\t\"visgroupshown\" \"1\"\r\n");
        sb.Append("\t\t\t\"visgroupautoshown\" \"1\"\r\n");
        sb.Append("\t\t}\r\n");
        sb.Append("\t}\r\n");
    }

    private static void AppendFace(
        StringBuilder sb,
        int id,
        Vec3 p,
        Vec3 q,
        Vec3 r,
        Vec3 opposite,
        RoadSettings s,
        Vec3? uDir,
        Vec3? vDir,
        Vec3 origin)
    {
        Vec3 normal = Vec3.Cross(q - p, r - p);

        // Hammer stores each face as three plane points whose cross-product
        // normal points INTO the brush (toward the opposite vertex). Orient
        // each face that way so the solid is valid, not inside-out.
        if (Vec3.Dot(normal, opposite - p) < 0)
        {
            (q, r) = (r, q);
        }

        Vec3 u = uDir ?? (q - p).Normalized();
        Vec3 v = vDir ?? (r - q).Normalized();
        double uShift = -Vec3.Dot(u, origin);
        double vShift = -Vec3.Dot(v, origin);

        sb.Append("\t\tside\r\n\t\t{\r\n");
        sb.Append("\t\t\t\"id\" \"").Append(id).Append("\"\r\n");
        sb.Append("\t\t\t\"plane\" \"").Append(Vmf.Point(p)).Append(' ').Append(Vmf.Point(q)).Append(' ').Append(Vmf.Point(r)).Append("\"\r\n");
        sb.Append("\t\t\t\"material\" \"").Append(s.Material).Append("\"\r\n");
        sb.Append("\t\t\t\"uaxis\" \"[").Append(Vmf.Vec(u)).Append(' ').Append(Vmf.Num(uShift)).Append("] ").Append(Vmf.Num(s.TextureScale)).Append("\"\r\n");
        sb.Append("\t\t\t\"vaxis\" \"[").Append(Vmf.Vec(v)).Append(' ').Append(Vmf.Num(vShift)).Append("] ").Append(Vmf.Num(s.TextureScale)).Append("\"\r\n");
        sb.Append("\t\t\t\"rotation\" \"0\"\r\n");
        sb.Append("\t\t\t\"lightmapscale\" \"").Append(s.LightmapScale).Append("\"\r\n");
        sb.Append("\t\t\t\"smoothing_groups\" \"0\"\r\n");
        sb.Append("\t\t}\r\n");
    }
}
