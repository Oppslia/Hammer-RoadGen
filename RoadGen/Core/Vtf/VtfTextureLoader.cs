using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using VTFLib;

namespace RoadGen.Core.Vtf;

/// <summary>Decodes Valve VTF textures into <see cref="Bitmap"/>s using the native
/// VTFLib library (vendored as the VTFLib.NET git submodule).</summary>
public static class VtfTextureLoader
{
    private static bool _initialized;

    /// <summary>Initializes the native VTFLib DLL. Safe to call more than once.</summary>
    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = VTFAPI.Initialize();
    }

    /// <summary>Loads and decodes a .vtf file into a 32-bit ARGB bitmap, or null on failure.</summary>
    public static Bitmap LoadFromFile(string vtfPath)
    {
        try
        {
            return LoadFromBytes(File.ReadAllBytes(vtfPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodes in-memory .vtf bytes (e.g. extracted from a VPK) into a bitmap, or null on failure.</summary>
    public static Bitmap LoadFromBytes(byte[] vtfData)
    {
        EnsureInitialized();

        uint image = 0;
        try
        {
            if (!VTFFile.CreateImage(ref image) || !VTFFile.BindImage(image))
            {
                return null;
            }

            if (!VTFFile.ImageLoadLump(vtfData, (uint)vtfData.Length, false))
            {
                return null;
            }

            uint width = VTFFile.ImageGetWidth();
            uint height = VTFFile.ImageGetHeight();
            if (width == 0 || height == 0)
            {
                return null;
            }

            VTFImageFormat format = VTFFile.ImageGetFormat();

            // Size of the mip-0 image data only (DXT/BC and uncompressed alike).
            uint mip0Size = VTFFile.ImageComputeImageSize(width, height, 1, 1, format);
            if (mip0Size == 0)
            {
                return null;
            }

            IntPtr source = VTFFile.ImageGetData(0, 0, 0, 0);
            if (source == IntPtr.Zero)
            {
                return null;
            }

            byte[] raw = new byte[mip0Size];
            Marshal.Copy(source, raw, 0, raw.Length);

            // One-shot: decompresses DXT1/3/5, BC4/5 and BC7 to RGBA8888.
            byte[] rgba = new byte[width * height * 4];
            if (!VTFFile.ImageConvertToRGBA8888(raw, rgba, width, height, format))
            {
                return null;
            }

            return BitmapFromRgba(rgba, (int)width, (int)height);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (image != 0)
            {
                VTFFile.DeleteImage(image);
            }
        }
    }

    /// <summary>Wraps RGBA8888 bytes in a 32-bit ARGB bitmap (GDI+ stores BGRA in memory).</summary>
    private static Bitmap BitmapFromRgba(byte[] rgba, int width, int height)
    {
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i + 0] = rgba[i + 2]; // B
                bgra[i + 1] = rgba[i + 1]; // G
                bgra[i + 2] = rgba[i + 0]; // R
                // Force opaque. Source materials like $selfillum store an illumination mask in
                // the texture's alpha (0 = not lit, 255 = lit), not real transparency. Drawing
                // that alpha would make unlit wall pixels transparent/black. In-game an opaque
                // material ignores alpha and shows the base RGB.
                bgra[i + 3] = 0xFF;
            }

            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
