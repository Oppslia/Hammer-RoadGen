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

        // The road body hangs straight down from the surface. Using a fixed
        // vertical offset (rather than a per-segment surface normal) keeps the
        // thickness direction identical across segments, so adjacent side walls
        // and bottoms sew together with no cracks.
        Vec3 down = new Vec3(0, 0, -1);

        Vec3 A2 = A + down * s.Thickness;
        Vec3 B2 = B + down * s.Thickness;
        Vec3 C2 = C + down * s.Thickness;
        Vec3 D2 = D + down * s.Thickness;

        // Precompute the top surface displacement (normals + distances relative to
        // the flat base parallelogram). These are reused so the side-wall
        // displacements follow the road's displaced edges.
        int n = res + 1;
        Vec3[,] normals = new Vec3[n, n];
        double[,] distances = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Vec3 delta = grid[r, c] - BasePoint(res, r, c, A, rowDir, colDir);
                double len = delta.Length;
                normals[r, c] = len < 1e-9 ? Vec3.UnitZ : delta / len;
                distances[r, c] = len;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("\tsolid\r\n\t{\r\n");
        sb.Append("\t\t\"id\" \"").Append(solidId).Append("\"\r\n");

        // Face 1: top face with the displacement. V follows the road, U across.
        Vec3 vTop = rowDir.Normalized();
        Vec3 uTop = (colDir - vTop * Vec3.Dot(colDir, vTop)).Normalized();
        double vShift = textureVStart - Vec3.Dot(vTop, A);
        AppendSide(sb, 1, A, B, C, s.Material, uTop, vTop, A, s.TextureScale, s.LightmapScale, leaveOpen: true, vShiftOverride: vShift);
        AppendDispInfo(sb, res, A, normals, distances, s.Power, Vec3.UnitZ);
        sb.Append("\t\t}\r\n");

        // The side walls are displacements too, so their top edges follow the road's
        // displaced edges. Left/right/bottom are independently optional. The
        // front/back faces are internal seams and stay nodraw.
        string bottomMaterial = s.SolidBottom ? s.Material : "TOOLS/TOOLSNODRAW";
        string leftMaterial = s.SolidLeft ? s.Material : "TOOLS/TOOLSNODRAW";
        string rightMaterial = s.SolidRight ? s.Material : "TOOLS/TOOLSNODRAW";

        // Face 2: bottom, displaced to mirror the top surface shifted straight down.
        // Plane points (B2, A2, D2) keep the same winding as the other faces so the
        // brush stays valid; the displacement itself carries the surface shape.
        // Texture axes match the top face (U across, V along with continuous V) so
        // the underside texture stays aligned with the road.
        double vShiftBottom = textureVStart - Vec3.Dot(vTop, A2);
        AppendSide(sb, 2, B2, A2, D2, bottomMaterial, uTop, vTop, A2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidBottom, vShiftOverride: vShiftBottom);
        if (s.SolidBottom)
        {
            AppendBottomDispInfo(sb, res, B2, normals, distances, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 3: left wall, displaced to follow the road's left edge.
        AppendSide(sb, 3, A2, B2, B, leftMaterial, null, null, A2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidLeft);
        if (s.SolidLeft)
        {
            AppendEdgeDispInfo(sb, res, A2, normals, distances, leftEdge: true, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 4: right wall, displaced to follow the road's right edge.
        AppendSide(sb, 4, C2, D2, D, rightMaterial, null, null, C2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidRight);
        if (s.SolidRight)
        {
            AppendEdgeDispInfo(sb, res, C2, normals, distances, leftEdge: false, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 5: side B-C (front seam, always nodraw).
        AppendSide(sb, 5, B2, C2, C, "TOOLS/TOOLSNODRAW", null, null, B2, s.TextureScale, s.LightmapScale);

        // Face 6: side A-D (back seam, always nodraw).
        AppendSide(sb, 6, D2, A2, A, "TOOLS/TOOLSNODRAW", null, null, D2, s.TextureScale, s.LightmapScale);

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

    private static void AppendDispInfo(StringBuilder sb, int res, Vec3 start, Vec3[,] normals, double[,] distances, int power, Vec3 offsetNormal)
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
                Vec3 normal = normals[row, col];
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
                double dist = distances[row, col];
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

        // offset_normals (vertex normals used for lighting/smoothing)
        sb.Append("\t\t\toffset_normals\r\n\t\t\t\t{\r\n");
        for (int row = 0; row < n; row++)
        {
            sb.Append("\t\t\t\t\t\"row").Append(row).Append("\" \"");
            for (int col = 0; col < n; col++)
            {
                sb.Append(Vmf.Vec(offsetNormal));
                sb.Append(' ');
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

    private static void AppendEdgeDispInfo(StringBuilder sb, int res, Vec3 start, Vec3[,] topNormals, double[,] topDistances, bool leftEdge, int power)
    {
        int n = res + 1;
        Vec3[,] normals = new Vec3[n, n];
        double[,] distances = new double[n, n];

        for (int r = 0; r < n; r++)
        {
            // Wall rows run along the road; the right wall runs in the opposite
            // road direction, so its rows are reversed.
            int topRow = leftEdge ? r : res - r;
            int topCol = leftEdge ? 0 : res;
            for (int c = 0; c < n; c++)
            {
                // Extrude the road edge straight down: every column carries the
                // full edge displacement, so the wall follows the road's curve
                // and adjacent segments sew together.
                normals[r, c] = topNormals[topRow, topCol];
                distances[r, c] = topDistances[topRow, topCol];
            }
        }

        AppendDispInfo(sb, res, start, normals, distances, power, Vec3.UnitZ);
    }

    private static void AppendBottomDispInfo(StringBuilder sb, int res, Vec3 start, Vec3[,] topNormals, double[,] topDistances, int power)
    {
        int n = res + 1;
        Vec3[,] normals = new Vec3[n, n];
        double[,] distances = new double[n, n];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                // The bottom face runs back-to-front along the road (its rows are
                // reversed relative to the top), so it mirrors the top surface.
                normals[r, c] = topNormals[res - r, c];
                distances[r, c] = topDistances[res - r, c];
            }
        }

        AppendDispInfo(sb, res, start, normals, distances, power, new Vec3(0, 0, -1));
    }

    private static Vec3 BasePoint(int res, int row, int col, Vec3 start, Vec3 rowDir, Vec3 colDir)
    {
        return start + rowDir * (row / (double)res) + colDir * (col / (double)res);
    }
}
