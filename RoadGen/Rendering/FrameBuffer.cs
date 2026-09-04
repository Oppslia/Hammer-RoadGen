using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RoadGen.Rendering;

/// <summary>A small ARGB pixel buffer plus a float depth buffer for software rasterization.
/// The pixel buffer is blitted to a <see cref="Graphics"/> context when complete. The depth
/// buffer stores 1/z (larger = closer), so it is initialized to zero ("nothing drawn").</summary>
public sealed class FrameBuffer
{
    private Bitmap _bitmap;
    private int[] _pixels = Array.Empty<int>();
    private float[] _depth = Array.Empty<float>();
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;

    public int[] Pixels => _pixels;
    public float[] Depth => _depth;

    /// <summary>Ensures the buffers match the given size (reused across frames).</summary>
    public void Resize(int width, int height)
    {
        if (width == _width && height == _height)
        {
            return;
        }

        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _pixels = new int[_width * _height];
        _depth = new float[_width * _height];
        _bitmap?.Dispose();
        _bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
    }

    /// <summary>Clears both buffers for a fresh frame.</summary>
    public void Clear()
    {
        Array.Clear(_pixels, 0, _pixels.Length);
        Array.Clear(_depth, 0, _depth.Length);
    }

    /// <summary>Copies the pixel buffer into the cached bitmap and draws it at (x, y).</summary>
    public void Blit(Graphics g, int x, int y)
    {
        if (_bitmap == null)
        {
            return;
        }

        BitmapData data = _bitmap.LockBits(
            new Rectangle(0, 0, _width, _height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(_pixels, 0, data.Scan0, _pixels.Length);
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        g.DrawImageUnscaled(_bitmap, x, y);
    }
}
