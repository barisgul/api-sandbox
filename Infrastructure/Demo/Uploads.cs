using System.Security.Cryptography;

namespace AuthSandbox.Infrastructure.Demo;

public class UploadedFileInfo
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>Validates uploaded PDF / PNG / JPEG files by declared type, magic bytes and size.</summary>
public static class Uploads
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, byte[]> Signatures = new()
    {
        ["application/pdf"] = "%PDF"u8.ToArray(),
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47],
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
    };

    public record Result(UploadedFileInfo? File, string? ErrorCode, string? ErrorDetail, bool IsFormatError = false);

    public static async Task<Result> ReadAsync(IFormFile file, long maxBytes = DefaultMaxBytes)
    {
        if (file.Length == 0) return new(null, "EMPTY_FILE", "The uploaded file is empty.", IsFormatError: true);
        if (file.Length > maxBytes) return new(null, "FILE_TOO_LARGE", $"Files may be at most {maxBytes / 1024 / 1024} MB.");

        var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? "";
        if (!Signatures.TryGetValue(contentType, out var signature))
            return new(null, "UNSUPPORTED_FILE_FORMAT", $"Only PDF, PNG or JPEG files are accepted (got '{contentType}').");

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (!bytes.AsSpan().StartsWith(signature))
            return new(null, "FILE_CONTENT_MISMATCH", $"File content does not match declared type '{contentType}'.");

        return new(new UploadedFileInfo
        {
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        }, null, null);
    }
}
