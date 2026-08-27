using System;
using System.Text;

namespace RoadGen.Core;

/// <summary>Builds one displacement brush (a "solid" block) from a sampled surface grid.
///
/// The base brush face is a planar parallelogram anchored at the grid's first corner.
/// This matches how Hammer reconstructs a face from its three stored plane points
/// (the fourth corner is always the parallelogram completion), so the displacement
/// normals and distances map exactly onto the base face and adjacent segments sew
/// together with no cracks.</summary>
public static class DisplacementSegment
{
    public static string Build(int solidId, Vec3[,] grid, RoadSettings s, double textureVStart, out double textureVAdvance)
    {
        int res = grid.GetLength(0) - 1;

        // Three anchor corners; the fourth is implied (parallelogram completion).
        Vec3 A = grid[0, 0].Rounded(6);
        Vec3 B = grid[res, 0].Rounded(6);
        Vec3 C = grid[res, res].Rounded(6);
        Vec3 D = A + C - B;

        Vec3 rowDir = B - A; // along the road
        Vec3 colDir = C - B; // across the road

        // The texture V coordinate advances by the face edge length so it stays
        // continuous across adjacent segments (no per-segment texture reset).
        textureVAdvance = rowDir.Length;

        // Face normal; make it point downward so the brush body sits below the road.
        Vec3 down = Vec3.Cross(rowDir, colDir).Normalized();
        if (Vec3.Dot(down, Vec3.UnitZ) > 0)
        {
            down = -down;
        }

        Vec3 A2 = A + down * s.Thickness;
        Vec3 B2 = B + down * s.Thickness;
        Vec3 C2 = C + down * s.Thickness;
        Vec3 D2 = D + down * s.Thickness;

        StringBuilder sb = new StringBuilder();
        sb.Append("\tsolid\r\n\t{\r\n");
        sb.Append("\t\t\"id\" \"").Append(solidId).Append("\"\r\n");

        // Face 1: top face, with the displacement attached. The dispinfo block
        // must be emitted inside the side block, before its closing brace.
        // Texture axes: V follows the road, U runs across the width (orthogonal to V).
        Vec3 vTop = rowDir.Normalized();
        Vec3 uTop = (colDir - vTop * Vec3.Dot(colDir, vTop)).Normalized();
        double vShift = textureVStart - Vec3.Dot(vTop, A);
        AppendSide(sb, 1, A, B, C, s.Material, uTop, vTop, A, s.TextureScale, s.LightmapScale, leaveOpen: true, vShiftOverride: vShift);
        AppendDispInfo(sb, grid, res, A, rowDir, colDir, s.Power);
        sb.Append("\t\t}\r\n");

        // Face 2: bottom.
        AppendSide(sb, 2, B2, A2, D2, s.Material, colDir.Normalized(), rowDir.Normalized(), B2, s.TextureScale, s.LightmapScale);

        // Face 3: side A-B.
        AppendSide(sb, 3, A2, B2, B, s.Material, null, null, A2, s.TextureScale, s.LightmapScale);

        // Face 4: side D-C.
        AppendSide(sb, 4, C2, D2, D, s.Material, null, null, C2, s.TextureScale, s.LightmapScale);

        // Face 5: side B-C.
        AppendSide(sb, 5, B2, C2, C, s.Material, null, null, B2, s.TextureScale, s.LightmapScale);

        // Face 6: side A-D.
        AppendSide(sb, 6, D2, A2, A, s.Material, null, null, D2, s.TextureScale, s.LightmapScale);

        sb.Append("\t\teditor\r\n\t\t{\r\n");
        sb.Append("\t\t\t\"color\" \"0 173 190\"\r\n");
        sb.Append("\t\t\t\"visgroupshown\" \"1\"\r\n");
        sb.Append("\t\t\t\"visgroupautoshown\" \"1\"\r\n");
        sb.Append("\t\t}\r\n");
        sb.Append("\t}\r\n");
        return sb.ToString();
    }

    private static void AppendSide(
        StringBuilder sb,
        int id,
        Vec3 p1,
        Vec3 p2,
        Vec3 p3,
        string material,
        Vec3? uDir,
        Vec3? vDir,
        Vec3 origin,
        double scale,
        int lightmapScale,
        bool leaveOpen = false,
        double? vShiftOverride = null)
    {
        // For generic faces we derive texture axes from the face's parallelogram edges.
        // The implied fourth corner is p1 + p3 - p2, so the opposite edge is p3 - p2.
        Vec3 u = uDir ?? (p2 - p1).Normalized();
        Vec3 v = vDir ?? (p3 - p2).Normalized();

        // Source texture coordinate convention (VBSP scales the whole vector):
        //   texU = (Dot(point, uAxis) + uShift) * scale
        //   texV = (Dot(point, vAxis) + vShift) * scale
        // The 4th component of uaxis/vaxis is therefore in world units.
        double uShift = -Vec3.Dot(u, origin);
        double vShift = vShiftOverride ?? -Vec3.Dot(v, origin);

        sb.Append("\t\tside\r\n\t\t{\r\n");
        sb.Append("\t\t\t\"id\" \"").Append(id).Append("\"\r\n");
        sb.Append("\t\t\t\"plane\" \"").Append(Vmf.Point(p1)).Append(' ').Append(Vmf.Point(p2)).Append(' ').Append(Vmf.Point(p3)).Append("\"\r\n");
        sb.Append("\t\t\t\"material\" \"").Append(material).Append("\"\r\n");
        sb.Append("\t\t\t\"uaxis\" \"[").Append(Vmf.Vec(u)).Append(' ').Append(Vmf.Num(uShift)).Append("] ").Append(Vmf.Num(scale)).Append("\"\r\n");
        sb.Append("\t\t\t\"vaxis\" \"[").Append(Vmf.Vec(v)).Append(' ').Append(Vmf.Num(vShift)).Append("] ").Append(Vmf.Num(scale)).Append("\"\r\n");
        sb.Append("\t\t\t\"rotation\" \"0\"\r\n");
        sb.Append("\t\t\t\"lightmapscale\" \"").Append(lightmapScale).Append("\"\r\n");
        sb.Append("\t\t\t\"smoothing_groups\" \"0\"\r\n");
        if (!leaveOpen)
        {
            sb.Append("\t\t}\r\n");
        }
    }

    private static void AppendDispInfo(StringBuilder sb, Vec3[,] grid, int res, Vec3 start, Vec3 rowDir, Vec3 colDir, int power)
    {
        int n = res + 1;

        sb.Append("\t\t\tdispinfo\r\n\t\t\t{\r\n");
        sb.Append("\t\t\t\t\"power\" \"").Append(power).Append("\"\r\n");
        sb.Append("\t\t\t\t\"startposition\" \"[").Append(Vmf.Vec(start)).Append("]\"\r\n");
        sb.Append("\t\t\t\t\"flags\" \"0\"\r\n");
        sb.Append("\t\t\t\t\"elevation\" \"0\"\r\n");
        sb.Append("\t\t\t\t\"subdiv\" \"0\"\r\n");

        // normals
        sb.Append("\t\t\t\tnormals\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                Vec3 normal = NormalAt(grid, res, row, col, start, rowDir, colDir);
                sb.Append(Vmf.Vec(normal));
                sb.Append(' ');
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // distances
        sb.Append("\t\t\t\tdistances\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                double dist = DistanceAt(grid, res, row, col, start, rowDir, colDir);
                sb.Append(Vmf.Num(dist));
                sb.Append(' ');
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // offsets (always zero)
        sb.Append("\t\t\t\toffsets\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                sb.Append("0 0 0 ");
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // offset_normals (kept as unit Z, matching the original Twister output)
        sb.Append("\t\t\t\toffset_normals\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                sb.Append("0 0 1 ");
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // alphas (all zero)
        sb.Append("\t\t\t\talphas\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                sb.Append("0 ");
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // triangle_tags: res rows, 2 * res entries each.
        int tagsPerRow = 2 * res;
        sb.Append("\t\t\t\ttriangle_tags\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < res; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int i = 0; i < tagsPerRow; i++)
            {
                sb.Append("0 ");
            }

            sb.Length -= 1;
            sb.Append("\"\r\n");
        }

        sb.Append("\t\t\t\t}\r\n");

        // allowed_verts
        sb.Append("\t\t\t\tallowed_verts\r\n\t\t\t\t{\r\n");
        sb.Append("\t\t\t\t\t\"10\" \"-1 -1 -1 -1 -1 -1 -1 -1 -1 -1\"\r\n");
        sb.Append("\t\t\t\t}\r\n");

        sb.Append("\t\t\t}\r\n");
    }

    private static Vec3 BasePoint(int res, int row, int col, Vec3 start, Vec3 rowDir, Vec3 colDir)
    {
        return start + rowDir * (row / (double)res) + colDir * (col / (double)res);
    }

    private static Vec3 NormalAt(Vec3[,] grid, int res, int row, int col, Vec3 start, Vec3 rowDir, Vec3 colDir)
    {
        Vec3 delta = grid[row, col] - BasePoint(res, row, col, start, rowDir, colDir);
        double len = delta.Length;
        if (len < 1e-9)
        {
            return Vec3.UnitZ;
        }

        return delta / len;
    }

    private static double DistanceAt(Vec3[,] grid, int res, int row, int col, Vec3 start, Vec3 rowDir, Vec3 colDir)
    {
        Vec3 delta = grid[row, col] - BasePoint(res, row, col, start, rowDir, colDir);
        return delta.Length;
    }
}
