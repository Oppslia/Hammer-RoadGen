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
    public static string Build(int solidId, Vec3[,] grid, double thicknessStart, double thicknessEnd, RoadSettings s, double textureVStart, out double textureVAdvance, int textureWidth = 0, int textureHeight = 0)
    {
        int res = grid.GetLength(0) - 1;

        // Hammer face-edit "Fit" mode (per-track RoadSettings.FitTextures): every visible
        // face is mapped so exactly ONE full texture fills it, anchored at its min U/V
        // corner — the same result Hammer produces when you select a face and press Fit.
        // Requires the material's real pixel size (textureWidth/Height); when it isn't
        // known the face keeps the continuous TextureScale mapping instead.
        bool fit = s.FitTextures && textureWidth > 0 && textureHeight > 0;

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
        // and bottoms sew together with no cracks. Thickness is interpolated per
        // point, so the start and end depths can differ and the bottom face tilts
        // to match.
        Vec3 down = new Vec3(0, 0, -1);

        Vec3 A2 = A + down * thicknessStart;
        Vec3 B2 = B + down * thicknessEnd;
        Vec3 C2 = C + down * thicknessEnd;
        Vec3 D2 = A2 + C2 - B2;

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
        // Texture coordinates must reproduce the source-exact mapping (see AppendSide) so
        // the preview and the compiled map agree: U is anchored on the centreline (u=0 in
        // the middle of the road) and V is anchored on the cumulative arc, so the texture
        // flows continuously around curves. Anchoring V to each piece's own local tangent
        // instead would make seams jump by an amount that scales with world position.
        Vec3 vTop = rowDir.Normalized();
        Vec3 uTop = (colDir - vTop * Vec3.Dot(colDir, vTop)).Normalized();
        double texScale = s.TextureScale > 0 ? s.TextureScale : 0.25;
        double topUShift = -Vec3.Dot(uTop, A + colDir * 0.5) / texScale;
        double topVShift = (textureVStart - Vec3.Dot(vTop, A)) / texScale;
        AppendSide(sb, 1, A, B, C, s.Material, uTop, vTop, A, s.TextureScale, s.LightmapScale, leaveOpen: true, uShiftOverride: topUShift, vShiftOverride: topVShift, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);
        AppendDispInfo(sb, res, A, normals, distances, s.Power, Vec3.UnitZ);
        sb.Append("\t\t}\r\n");

        // The side walls are displacements too, so their top edges follow the road's
        // displaced edges. Left/right/bottom are independently optional. The
        // front/back faces are internal seams and stay nodraw.
        // Each wall/bottom face uses its own material when one is set, otherwise it inherits
        // the top material (FaceMaterial resolves a blank override to the top).
        string bottomMaterial = s.SolidBottom ? s.FaceMaterial(s.BottomMaterial) : "TOOLS/TOOLSNODRAW";
        string leftMaterial = s.SolidLeft ? s.FaceMaterial(s.LeftMaterial) : "TOOLS/TOOLSNODRAW";
        string rightMaterial = s.SolidRight ? s.FaceMaterial(s.RightMaterial) : "TOOLS/TOOLSNODRAW";

        // Face 2: bottom, displaced to mirror the top surface shifted straight down.
        // Plane points (B2, A2, D2) keep the same winding as the other faces so the
        // brush stays valid; the displacement itself carries the surface shape.
        // Texture axes match the top face (U across, V along with continuous V), using
        // the same source-exact shifts so the underside shares the top's texture phase.
        double bottomUShift = -Vec3.Dot(uTop, A2 + colDir * 0.5) / texScale;
        double bottomVShift = (textureVStart - Vec3.Dot(vTop, A2)) / texScale;
        AppendSide(sb, 2, B2, A2, D2, bottomMaterial, uTop, vTop, A2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidBottom, uShiftOverride: bottomUShift, vShiftOverride: bottomVShift, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);
        if (s.SolidBottom)
        {
            AppendBottomDispInfo(sb, res, B2, normals, distances, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 3: left wall, displaced to follow the road's left edge.
        AppendSide(sb, 3, A2, B2, B, leftMaterial, null, null, A2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidLeft, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);
        if (s.SolidLeft)
        {
            AppendEdgeDispInfo(sb, res, A2, normals, distances, leftEdge: true, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 4: right wall, displaced to follow the road's right edge.
        AppendSide(sb, 4, C2, D2, D, rightMaterial, null, null, C2, s.TextureScale, s.LightmapScale, leaveOpen: s.SolidRight, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);
        if (s.SolidRight)
        {
            AppendEdgeDispInfo(sb, res, C2, normals, distances, leftEdge: false, s.Power);
            sb.Append("\t\t}\r\n");
        }

        // Face 5: side B-C (front seam, always nodraw).
        AppendSide(sb, 5, B2, C2, C, "TOOLS/TOOLSNODRAW", null, null, B2, s.TextureScale, s.LightmapScale, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);

        // Face 6: side A-D (back seam, always nodraw).
        AppendSide(sb, 6, D2, A2, A, "TOOLS/TOOLSNODRAW", null, null, D2, s.TextureScale, s.LightmapScale, fit: fit, fitTexW: textureWidth, fitTexH: textureHeight);

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
        double? uShiftOverride = null,
        double? vShiftOverride = null,
        bool fit = false,
        int fitTexW = 0,
        int fitTexH = 0)
    {
        // For generic faces we derive texture axes from the face's parallelogram edges.
        // The implied fourth corner is p1 + p3 - p2, so the opposite edge is p3 - p2.
        Vec3 u = uDir ?? (p2 - p1).Normalized();
        Vec3 v = vDir ?? (p3 - p2).Normalized();

        // Source/Hammer texture coordinate convention (verified against Hammer's
        // MapFace::CalcTextureCoords, which DIVIDES by scale):
        //   texU = Dot(point, uAxis) / scale + uShift
        //   texV = Dot(point, vAxis) / scale + vShift
        // so the 4th component of uaxis/vaxis is stored in world/scale units, NOT world
        // units. Default origin-anchored shifts are therefore divided by scale; callers
        // that thread a cumulative arc pass fully-computed overrides (already in /scale).
        double safeScale = scale != 0 ? scale : 1.0;

        double uScaleOut = scale;
        double vScaleOut = scale;
        double uShift;
        double vShift;

        // Hammer face-edit "Fit" (Whole Face): keep the face's own axes but scale so
        // exactly ONE full texture fills the face, anchored at the face's min-U/min-V
        // corner. Measured over the real four corners of the face parallelogram, each
        // axis getting its own scale (U and V can differ for non-square materials).
        if (fit && fitTexW > 0 && fitTexH > 0)
        {
            Vec3 p4 = p1 + p3 - p2;
            double u1 = Vec3.Dot(u, p1), u2 = Vec3.Dot(u, p2), u3 = Vec3.Dot(u, p3), u4 = Vec3.Dot(u, p4);
            double v1 = Vec3.Dot(v, p1), v2 = Vec3.Dot(v, p2), v3 = Vec3.Dot(v, p3), v4 = Vec3.Dot(v, p4);
            double minU = Math.Min(Math.Min(u1, u2), Math.Min(u3, u4));
            double maxU = Math.Max(Math.Max(u1, u2), Math.Max(u3, u4));
            double minV = Math.Min(Math.Min(v1, v2), Math.Min(v3, v4));
            double maxV = Math.Max(Math.Max(v1, v2), Math.Max(v3, v4));
            double fitScaleU = (maxU - minU) / fitTexW;
            double fitScaleV = (maxV - minV) / fitTexH;
            if (fitScaleU > 1e-9 && fitScaleV > 1e-9)
            {
                uShift = -minU / fitScaleU;
                vShift = -minV / fitScaleV;
                uScaleOut = fitScaleU;
                vScaleOut = fitScaleV;
            }
            else
            {
                uShift = uShiftOverride ?? -Vec3.Dot(u, origin) / safeScale;
                vShift = vShiftOverride ?? -Vec3.Dot(v, origin) / safeScale;
            }
        }
        else
        {
            uShift = uShiftOverride ?? -Vec3.Dot(u, origin) / safeScale;
            vShift = vShiftOverride ?? -Vec3.Dot(v, origin) / safeScale;
        }

        sb.Append("\t\tside\r\n\t\t{\r\n");
        sb.Append("\t\t\t\"id\" \"").Append(id).Append("\"\r\n");
        sb.Append("\t\t\t\"plane\" \"").Append(Vmf.Point(p1)).Append(' ').Append(Vmf.Point(p2)).Append(' ').Append(Vmf.Point(p3)).Append("\"\r\n");
        sb.Append("\t\t\t\"material\" \"").Append(material).Append("\"\r\n");
        sb.Append("\t\t\t\"uaxis\" \"[").Append(Vmf.Vec(u)).Append(' ').Append(Vmf.Num(uShift)).Append("] ").Append(Vmf.Num(uScaleOut)).Append("\"\r\n");
        sb.Append("\t\t\t\"vaxis\" \"[").Append(Vmf.Vec(v)).Append(' ').Append(Vmf.Num(vShift)).Append("] ").Append(Vmf.Num(vScaleOut)).Append("\"\r\n");
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
