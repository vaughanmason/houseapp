using ImageMagick;

namespace HomeInventory;

/// <summary>Image conversion and thumbnails via Magick.NET (which bundles libheif, so iPhone HEIC photos decode on every platform).</summary>
public static class ImageTools
{
    static ImageTools()
    {
        // Refuse absurd dimensions so a crafted "image bomb" can't exhaust memory.
        ResourceLimits.Width = 20_000;
        ResourceLimits.Height = 20_000;
    }

    /// <summary>Re-encodes an image (e.g. HEIC) as JPEG, applying its EXIF orientation.</summary>
    public static void ConvertToJpeg(string sourcePath, string targetPath)
    {
        using var image = new MagickImage(sourcePath);
        image.AutoOrient();
        image.Quality = 90;
        image.Write(targetPath, MagickFormat.Jpeg);
    }

    /// <summary>Writes a JPEG thumbnail no larger than <paramref name="size"/> pixels, without metadata (e.g. GPS location).</summary>
    public static void WriteThumbnail(string sourcePath, string targetPath, int size)
    {
        using var image = new MagickImage(sourcePath);
        image.AutoOrient();
        image.Thumbnail(new MagickGeometry((uint)size, (uint)size));
        image.Strip();
        image.Quality = 80;
        // Write to a temporary file first so concurrent requests never read a half-written thumbnail.
        var temporary = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        image.Write(temporary, MagickFormat.Jpeg);
        File.Move(temporary, targetPath, overwrite: true);
    }

    /// <summary>Raw 24-bit RGB pixels, e.g. for barcode decoding.</summary>
    public static (byte[] Pixels, int Width, int Height) ReadRgb(Stream content)
    {
        using var image = new MagickImage(content);
        image.AutoOrient();
        if (Math.Max(image.Width, image.Height) > 2000) image.Resize(new MagickGeometry(2000, 2000)); // plenty for barcodes, much faster
        using var pixels = image.GetPixels();
        return (pixels.ToByteArray(PixelMapping.RGB) ?? [], (int)image.Width, (int)image.Height);
    }
}
