using System.Security.Cryptography;

namespace KuTax.Api.Features.Receipts;

public static class UploadValidation
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Choose a valid relative folder path.");
        var path = value.Replace('\\', '/').Normalize();
        if (path.Length is 0 or > 512 || path.StartsWith('/') || path.Contains(':')
            || path.Any(char.IsControl) || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Choose a valid relative folder path.");
        return path;
    }

    public static async Task<(string Hash, string ContentType, long Size)> CopyAndValidateAsync(
        Stream source, Stream destination, string filename, CancellationToken ct)
    {
        if (filename.Length is 0 or > 200 || filename.IndexOfAny(['/', '\\', ':']) >= 0 || filename.Any(char.IsControl))
            throw new InvalidDataException("Choose a valid file name up to 200 characters.");
        var buffer = new byte[64 * 1024];
        var signature = new byte[8];
        var signatureLength = 0;
        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes) throw new InvalidDataException("Each receipt must be 20 MB or smaller.");
            var take = Math.Min(read, signature.Length - signatureLength);
            buffer.AsSpan(0, take).CopyTo(signature.AsSpan(signatureLength));
            signatureLength += take;
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var type = DetectType(signature.AsSpan(0, signatureLength));
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (type is null || !(type == "application/pdf" && extension == ".pdf"
            || type == "image/png" && extension == ".png" || type == "image/jpeg" && extension is ".jpg" or ".jpeg"))
            throw new InvalidDataException("Use a PDF, JPG or PNG whose contents match its file extension.");
        destination.Position = 0;
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), type, total);
    }

    private static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("%PDF-"u8)) return "application/pdf";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        return null;
    }
}
