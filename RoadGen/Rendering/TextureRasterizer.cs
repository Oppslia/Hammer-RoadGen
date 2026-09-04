using System;
using RoadGen.Core;

namespace RoadGen.Rendering;

/// <summary>Software rasterizer that draws perspective-correct, texture-mapped triangles into
/// a <see cref="FrameBuffer"/> with a depth buffer. No backface culling: the reference layout
/// must be visible from any angle, including from inside a brush.</summary>
public static class TextureRasterizer
{
    private const float Near = 1.0f; // matches Viewport3D.Project's "cz &lt; 1" cutoff

    /// <summary>World-space size of one missing-texture checkerboard cell. Viewport3D sets the
    /// fallback U/V to the point's distance along the face's Hammer axes in these units.</summary>
    public const float FallbackCellSize = 128f;

    /// <summary>Average of the magenta/black checker colors; used for the anti-aliased blend so
    /// distant missing-texture faces read as a stable dark magenta instead of aliasing.</summary>
    private const int AverageCheckerColor = unchecked((int)0xFF800080);

    /// <summary>A world-space vertex plus its tiling texture coordinate.</summary>
    public readonly struct Vertex
    {
        public readonly Vec3 P;
        public readonly float U, V;

        public Vertex(Vec3 p, float u, float v)
        {
            P = p;
            U = u;
            V = v;
        }
    }

    private struct ViewVert
    {
        public float X, Y, Z; // view space
        public float U, V;
    }

    /// <summary>Fills a texture-mapped triangle into the frame buffer. <paramref name="tex"/>
    /// is an ARGB int[] (same byte layout as the frame buffer) of size texW * texH.</summary>
    public static void FillTriangle(FrameBuffer frameBuffer,
        Vertex va, Vertex vb, Vertex vc,
        Vec3 eye, Vec3 forward, Vec3 right, Vec3 up, float focal,
        int screenWidth, int screenHeight, int[] tex, int texW, int texH,
        bool fallback = false)
    {
        ViewVert a = ToView(va, eye, forward, right, up);
        ViewVert b = ToView(vb, eye, forward, right, up);
        ViewVert c = ToView(vc, eye, forward, right, up);

        // Clip against the near plane, then fan-triangulate the result.
        Span<ViewVert> polygon = stackalloc ViewVert[8];
        int count = ClipPolygon(a, b, c, polygon);
        if (count < 3)
        {
            return;
        }

        for (int i = 1; i + 1 < count; i++)
        {
            RasterizeProjected(frameBuffer, polygon[0], polygon[i], polygon[i + 1],
                focal, screenWidth, screenHeight, tex, texW, texH, fallback);
        }
    }

    private static ViewVert ToView(in Vertex v, Vec3 eye, Vec3 forward, Vec3 right, Vec3 up)
    {
        Vec3 d = v.P - eye;
        return new ViewVert
        {
            X = (float)Vec3.Dot(d, right),
            Y = (float)Vec3.Dot(d, up),
            Z = (float)Vec3.Dot(d, forward),
            U = v.U,
            V = v.V
        };
    }

    /// <summary>Sutherland–Hodgman clip of a triangle against the near plane (z &gt;= Near).</summary>
    private static int ClipPolygon(in ViewVert a, in ViewVert b, in ViewVert c, Span<ViewVert> output)
    {
        Span<ViewVert> input = stackalloc ViewVert[3];
        input[0] = a;
        input[1] = b;
        input[2] = c;

        Span<ViewVert> result = stackalloc ViewVert[8];
        int count = 0;

        for (int i = 0; i < input.Length; i++)
        {
            ViewVert current = input[i];
            ViewVert next = input[(i + 1) % input.Length];
            bool currentInside = current.Z >= Near;
            bool nextInside = next.Z >= Near;

            if (currentInside)
            {
                result[count++] = current;
            }

            if (currentInside != nextInside)
            {
                float t = (Near - current.Z) / (next.Z - current.Z);
                result[count++] = Lerp(current, next, t);
            }
        }

        for (int i = 0; i < count; i++)
        {
            output[i] = result[i];
        }

        return count;
    }

    private static ViewVert Lerp(in ViewVert a, in ViewVert b, float t) => new ViewVert
    {
        X = a.X + (b.X - a.X) * t,
        Y = a.Y + (b.Y - a.Y) * t,
        Z = a.Z + (b.Z - a.Z) * t,
        U = a.U + (b.U - a.U) * t,
        V = a.V + (b.V - a.V) * t
    };

    private static void RasterizeProjected(FrameBuffer frameBuffer,
        in ViewVert a, in ViewVert b, in ViewVert c,
        float focal, int screenWidth, int screenHeight, int[] tex, int texW, int texH,
        bool fallback)
    {
        float cx = screenWidth / 2f, cy = screenHeight / 2f;

        // Project to screen and compute the perspective attributes per vertex.
        float ax = cx + a.X * focal / a.Z, ay = cy - a.Y * focal / a.Z;
        float bx = cx + b.X * focal / b.Z, by = cy - b.Y * focal / b.Z;
        float cxx = cx + c.X * focal / c.Z, cyy = cy - c.Y * focal / c.Z;
        float aw = 1f / a.Z, bw = 1f / b.Z, cw = 1f / c.Z;
        float au = a.U * aw, av = a.V * aw;
        float bu = b.U * bw, bv = b.V * bw;
        float cu = c.U * cw, cv = c.V * cw;

        // Clipped bounding box.
        int minX = (int)MathF.Min(ax, MathF.Min(bx, cxx));
        int maxX = (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cxx)));
        int minY = (int)MathF.Min(ay, MathF.Min(by, cyy));
        int maxY = (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cyy)));
        if (minX < 0) minX = 0;
        if (minY < 0) minY = 0;
        if (maxX > screenWidth - 1) maxX = screenWidth - 1;
        if (maxY > screenHeight - 1) maxY = screenHeight - 1;
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        // Edge functions (barycentric weights), winding independent.
        float e0dx = bx - ax, e0dy = by - ay; // edge a -> b
        float e1dx = cxx - bx, e1dy = cyy - by; // edge b -> c
        float e2dx = ax - cxx, e2dy = ay - cyy; // edge c -> a
        float area = e0dx * e1dy - e0dy * e1dx;
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        float invArea = 1f / area;

        int[] pixels = frameBuffer.Pixels;
        float[] depth = frameBuffer.Depth;

        for (int y = minY; y <= maxY; y++)
        {
            float fy = y + 0.5f;
            int row = y * screenWidth;
            for (int x = minX; x <= maxX; x++)
            {
                float fx = x + 0.5f;
                float w0 = (e1dx * (fy - by) - e1dy * (fx - bx)) * invArea;
                float w1 = (e2dx * (fy - cyy) - e2dy * (fx - cxx)) * invArea;
                float w2 = (e0dx * (fy - ay) - e0dy * (fx - ax)) * invArea;

                // Inside test, independent of winding: all three weights must share a sign.
                if ((w0 < 0f || w1 < 0f || w2 < 0f) && (w0 > 0f || w1 > 0f || w2 > 0f))
                {
                    continue;
                }

                float wInv = w0 * aw + w1 * bw + w2 * cw;
                if (wInv <= 0f)
                {
                    continue;
                }

                int index = row + x;
                if (wInv <= depth[index])
                {
                    continue; // already covered by something closer
                }

                float u = (w0 * au + w1 * bu + w2 * cu) / wInv;
                float v = (w0 * av + w1 * bv + w2 * cv) / wInv;
                u -= MathF.Floor(u); // tile
                v -= MathF.Floor(v);
                int tx = (int)(u * texW);
                int ty = (int)(v * texH);
                if (tx >= texW) tx = texW - 1;
                if (ty >= texH) ty = texH - 1;
                if (tx < 0) tx = 0;
                if (ty < 0) ty = 0;

                if (fallback)
                {
                    // Missing-texture checkerboard. The caller set U/V to this point's distance
                    // along the face's own Hammer axes in FallbackCellSize units, and perspective
                    // interpolation of those dot products is exact (they are affine in world
                    // position), so the checker is a static, face-aligned grid that stays glued
                    // to the world and grows when zooming in. Once cells shrink below ~2 screen
                    // px we blend to a flat average color so distant/small faces never alias
                    // into stripes or solid pink.
                    float pixPerCell = FallbackCellSize * focal * wInv;
                    float t = Math.Clamp((pixPerCell - 1.5f) / 3.5f, 0f, 1f);
                    if (t <= 0f)
                    {
                        pixels[index] = AverageCheckerColor;
                    }
                    else
                    {
                        int color = tex[ty * texW + tx];
                        if (t < 1f)
                        {
                            color = BlendTowardAverage(color, t);
                        }

                        pixels[index] = color;
                    }
                }
                else
                {
                    // Bilinear (smooth) sampling: keeps magnified textures from looking blocky/
                    // pixelated, which the Hammer UV scale can make large faces do.
                    pixels[index] = SampleBilinear(tex, texW, texH, u, v);
                }
                depth[index] = wInv;
            }
        }
    }

    /// <summary>Blends a checker color toward the average magenta/black color by (1 - t); the
    /// anti-aliasing term used once checker cells approach pixel size.</summary>
    private static int BlendTowardAverage(int color, float t)
    {
        float m = 1f - t; // 1 = fully average, 0 = fully original
        int r = (color >> 16) & 0xFF;
        int g = (color >> 8) & 0xFF;
        int b = color & 0xFF;
        r = (int)(r + (128 - r) * m);
        g = (int)(g + (0 - g) * m);
        b = (int)(b + (128 - b) * m);
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    /// <summary>Bilinear (smooth) texture sample with tiling wrap. Smooths the chunky,
    /// pixelated look that nearest-neighbour sampling produces when a texture is magnified.</summary>
    private static int SampleBilinear(int[] tex, int texW, int texH, float u, float v)
    {
        float fu = u * texW - 0.5f;
        float fv = v * texH - 0.5f;
        int x0 = (int)MathF.Floor(fu);
        int y0 = (int)MathF.Floor(fv);
        float fx = fu - x0;
        float fy = fv - y0;

        int wx0 = Mod(x0, texW), wx1 = Mod(x0 + 1, texW);
        int wy0 = Mod(y0, texH), wy1 = Mod(y0 + 1, texH);
        int c00 = tex[wy0 * texW + wx0];
        int c10 = tex[wy0 * texW + wx1];
        int c01 = tex[wy1 * texW + wx0];
        int c11 = tex[wy1 * texW + wx1];

        float f00 = (1f - fx) * (1f - fy);
        float f10 = fx * (1f - fy);
        float f01 = (1f - fx) * fy;
        float f11 = fx * fy;

        int a = (int)(((c00 >> 24) & 0xFF) * f00 + ((c10 >> 24) & 0xFF) * f10 + ((c01 >> 24) & 0xFF) * f01 + ((c11 >> 24) & 0xFF) * f11);
        int r = (int)(((c00 >> 16) & 0xFF) * f00 + ((c10 >> 16) & 0xFF) * f10 + ((c01 >> 16) & 0xFF) * f01 + ((c11 >> 16) & 0xFF) * f11);
        int g = (int)(((c00 >> 8) & 0xFF) * f00 + ((c10 >> 8) & 0xFF) * f10 + ((c01 >> 8) & 0xFF) * f01 + ((c11 >> 8) & 0xFF) * f11);
        int b = (int)((c00 & 0xFF) * f00 + (c10 & 0xFF) * f10 + (c01 & 0xFF) * f01 + (c11 & 0xFF) * f11);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }
}
