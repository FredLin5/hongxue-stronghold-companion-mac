// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Minimal cross-platform raster image: decode (PNG/BMP/JPEG), high-quality
/// resize, crop, compose and encode (PNG/BMP).
///
/// The Windows build used <c>System.Drawing.Common</c> (GDI+) for these operations,
/// which only runs on Windows. The pixel layout is kept identical to GDI+'s
/// 32bppArgb bitmaps (B, G, R, A in memory, straight alpha), so the existing
/// colour heuristics keep working unchanged on both platforms.
/// </summary>
public sealed class Raster : IDisposable
{
    private static readonly SKImageInfo InfoTemplate =
        new(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);

    private readonly SKBitmap _bitmap;
    private byte[]? _pixels;

    private Raster(SKBitmap bitmap) => _bitmap = bitmap;

    public int Width => _bitmap.Width;
    public int Height => _bitmap.Height;
    internal SKBitmap Bitmap => _bitmap;

    public static Raster Create(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return new Raster(new SKBitmap(info));
    }

    /// <summary>Decodes PNG/BMP/JPEG/GIF/WEBP bytes; returns <c>null</c> when the data is not an image.</summary>
    public static Raster? Decode(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            using var skillData = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(skillData);
            if (codec is null) return null;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            if (codec.GetPixels(info, bitmap.GetPixels()) is var result
                && result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                bitmap.Dispose();
                return null;
            }
            return new Raster(bitmap);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Straight BGRA pixels, row-major, no padding beyond <see cref="RowLength"/>*Height.</summary>
    public byte[] PixelsBgra
    {
        get
        {
            if (_pixels is null)
            {
                int rowBytes = _bitmap.RowBytes;
                var buffer = new byte[rowBytes * Height];
                var source = _bitmap.GetPixels();
                if (source != IntPtr.Zero) Marshal.Copy(source, buffer, 0, buffer.Length);
                _pixels = buffer;
            }
            return _pixels;
        }
    }

    /// <summary>Row stride of <see cref="PixelsBgra"/> in bytes (4 × width for BGRA8888).</summary>
    public int RowLength => _bitmap.RowBytes;

    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return (0, 0, 0);
        var pixels = PixelsBgra;
        int offset = y * RowLength + x * 4;
        return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    public Raster Clone() => new(_bitmap.Copy()!);

    public Raster Crop(int x, int y, int width, int height)
    {
        var result = Create(Math.Max(1, width), Math.Max(1, height));
        using var canvas = new SKCanvas(result._bitmap);
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(_bitmap, new SKRect(x, y, x + width, y + height),
            new SKRect(0, 0, result.Width, result.Height), paint);
        return result;
    }

    /// <summary>High-quality (cubic) resize — matches GDI+'s HighQualityBicubic intent.</summary>
    public Raster Resize(int width, int height)
    {
        if (width == Width && height == Height) return Clone();
        var result = Create(Math.Max(1, width), Math.Max(1, height));
        using var canvas = new SKCanvas(result._bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(_bitmap, new SKRect(0, 0, Width, Height),
            new SKRect(0, 0, result.Width, result.Height), paint);
        return result;
    }

    /// <summary>
    /// Draws a rectangle of <paramref name="source"/> onto this image.
    /// An optional 5×4 GDI+-style colour matrix (row = source channel, column = dest channel)
    /// is applied, which keeps the shop reader's red-channel contrast filter identical.
    /// </summary>
    public void Draw(Raster source, SKRect destination, SKRect sourceRect, float[]? gdiColorMatrix = null)
    {
        _pixels = null; // pixel cache is stale once the bitmap changes
        using var canvas = new SKCanvas(_bitmap);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        if (gdiColorMatrix is { Length: 25 })
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(ToSkiaColorMatrix(gdiColorMatrix));
        canvas.DrawBitmap(source._bitmap, sourceRect, destination, paint);
    }

    /// <summary>Converts a GDI+ 5×5 <c>ColorMatrix</c> into Skia's 4×5 row-major layout.</summary>
    private static float[] ToSkiaColorMatrix(float[] gdi)
    {
        // GDI+: dest = Σ source[m[sourceRow, destColumn]] + m[4, destColumn]
        // Skia: row(dest) mixes source channels in the order R, G, B, A.
        var result = new float[20];
        for (int destChannel = 0; destChannel < 4; destChannel++)
        {
            result[destChannel * 5 + 0] = gdi[0 * 5 + destChannel];
            result[destChannel * 5 + 1] = gdi[1 * 5 + destChannel];
            result[destChannel * 5 + 2] = gdi[2 * 5 + destChannel];
            result[destChannel * 5 + 3] = gdi[3 * 5 + destChannel];
            result[destChannel * 5 + 4] = gdi[4 * 5 + destChannel];
        }
        return result;
    }

    public void ClearBlack()
    {
        var pixels = PixelsBgra;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 0;
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        WriteBack();
    }

    private void WriteBack()
    {
        if (_pixels is null) return;
        var target = _bitmap.GetPixels();
        if (target != IntPtr.Zero) Marshal.Copy(_pixels, 0, target, _pixels.Length);
    }

    public byte[] EncodePng()
    {
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>24-bit bottom-up BMP, the format MaaFramework/OpenCV reads natively.</summary>
    public byte[] EncodeBmp()
    {
        int width = Width, height = Height;
        int rowSize = (width * 3 + 3) & ~3;
        int pixelDataSize = rowSize * height;
        var buffer = new byte[14 + 40 + pixelDataSize];
        using var ms = new MemoryStream(buffer);
        using var writer = new BinaryWriter(ms);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(14 + 40 + pixelDataSize);
        writer.Write(0);
        writer.Write(14 + 40);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelDataSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        var pixels = PixelsBgra;
        var row = new byte[rowSize];
        for (int y = height - 1; y >= 0; y--)
        {
            Array.Clear(row, 0, rowSize);
            int sourceOffset = y * RowLength;
            for (int x = 0; x < width; x++)
            {
                int target = x * 3;
                int source = sourceOffset + x * 4;
                row[target] = pixels[source];
                row[target + 1] = pixels[source + 1];
                row[target + 2] = pixels[source + 2];
            }
            writer.Write(row);
        }

        return buffer;
    }

    public void Dispose() => _bitmap.Dispose();
}
