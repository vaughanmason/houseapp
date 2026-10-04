using System.Text.RegularExpressions;

namespace HomeInventory;

/// <summary>
/// Stores uploaded images and PDFs on the local disk (default <c>%LOCALAPPDATA%\HomeInventory\files</c>).
/// Files are saved under generated keys (<c>yyyy/MM/{guid}.ext</c>); the type is decided from the file's
/// leading bytes, never from the client-supplied name or content type.
/// </summary>
public sealed partial class FileStore(string rootPath)
{
    public const long MaxBytes = 20 * 1024 * 1024;

    static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".pdf"] = "application/pdf",
    };

    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}\.(jpg|png|gif|webp|heic|pdf)$")]
    private static partial Regex KeyPattern();

    public string RootPath { get; } = rootPath;

    public static bool IsStoredKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static string ContentTypeFor(string key) => ContentTypes[Path.GetExtension(key)];

    /// <summary>Saves the stream and returns its key, or null with an error when it is too large or not an allowed type.</summary>
    public async Task<(string? Key, string? Error)> SaveAsync(Stream content, long length)
    {
        if (length <= 0) return (null, "The file is empty.");
        if (length > MaxBytes) return (null, "Files must be 20 MB or smaller.");
        var header = new byte[16];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        var extension = Sniff(header.AsSpan(0, read));
        if (extension is null) return (null, "Only JPEG, PNG, GIF, WebP, HEIC images and PDF documents can be uploaded.");

        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = File.Create(path))
        {
            await file.WriteAsync(header.AsMemory(0, read));
            await content.CopyToAsync(file);
            // The declared length can't be trusted on its own; enforce the limit on what was actually written.
            if (file.Length > MaxBytes)
            {
                file.Close();
                File.Delete(path);
                return (null, "Files must be 20 MB or smaller.");
            }
        }
        return (key, null);
    }

    /// <summary>
    /// Writes a file from a backup under its original key. Returns false (and writes nothing) when the key is invalid,
    /// the file already exists, it is too large, or its contents don't match the key's file type.
    /// </summary>
    public async Task<bool> RestoreAsync(string key, Stream content, long length)
    {
        if (!IsStoredKey(key) || File.Exists(FullPath(key)) || length <= 0 || length > MaxBytes) return false;
        var header = new byte[16];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (Sniff(header.AsSpan(0, read)) != Path.GetExtension(key)) return false;
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = File.Create(path))
        {
            await file.WriteAsync(header.AsMemory(0, read));
            await content.CopyToAsync(file);
            if (file.Length <= MaxBytes) return true;
        }
        File.Delete(path);
        return false;
    }

    public Stream? Open(string key) => IsStoredKey(key) && File.Exists(FullPath(key)) ? File.OpenRead(FullPath(key)) : null;

    public void Delete(string key)
    {
        if (IsStoredKey(key) && File.Exists(FullPath(key))) File.Delete(FullPath(key));
    }

    string FullPath(string key) => Path.Combine(RootPath, key.Replace('/', Path.DirectorySeparatorChar));

    static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ".png";
        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)) return ".gif";
        if (h.Length >= 12 && h.StartsWith("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (h.StartsWith("%PDF-"u8)) return ".pdf";
        if (h.Length >= 12 && h[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = h[8..12];
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8) || brand.SequenceEqual("hevc"u8)) return ".heic";
        }
        return null;
    }
}
