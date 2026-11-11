using System.Text.RegularExpressions;

namespace Lobby.Web.Services;

public record AvatarSaveResult(string? FileName, string? Error);

// Avatar files live in App:DataPath/avatars: outside wwwroot (so they are never
// served as static files) and outside the image (a Docker volume, so a redeploy keeps them).
public partial class AvatarStorage
{
    public const int MaxBytes = 2 * 1024 * 1024; // 2 MB per avatar
    // Kestrel stops reading above this. Higher than MaxBytes on purpose, so a 3–10 MB photo still
    // reaches SaveAsync and gets the friendly "máximo 2 MB" message instead of an error page.
    public const int MaxRequestBytes = 10 * 1024 * 1024;

    // Only these three. SVG is never accepted: it is XML and can carry <script>.
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        ["jpg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
    };

    private readonly string _directory;

    public AvatarStorage(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task<AvatarSaveResult> SaveAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return new(null, "Escolha uma imagem.");
        if (file.Length > MaxBytes)
            return new(null, "A imagem deve ter no máximo 2 MB.");

        // Three independent checks. The extension and the content type both come
        // from the client and can be faked, so the file's first bytes decide.
        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (extension == "jpeg") extension = "jpg";
        if (!ContentTypes.TryGetValue(extension, out var expectedType))
            return new(null, "Use uma imagem JPEG, PNG ou WebP.");
        if (!string.Equals(file.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
            return new(null, "Use uma imagem JPEG, PNG ou WebP.");

        var header = new byte[12];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
            if (read < header.Length)
                return new(null, "O arquivo não é uma imagem válida.");
        }
        var detected = DetectFormat(header);
        if (detected != extension)
            return new(null, "O arquivo não é uma imagem válida.");

        // The name on disk is ours: a fresh guid plus the extension we detected.
        // The original file name is never used, so it can't cause path traversal or overwrite.
        var fileName = $"{Guid.NewGuid():N}.{detected}";
        await using (var target = File.Create(Path.Combine(_directory, fileName)))
        {
            await file.CopyToAsync(target);
        }
        return new(fileName, null);
    }

    public static string? DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "png";
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return "webp";
        return null;
    }

    // The avatar endpoint only accepts names we could have generated.
    // This also rules out "../", slashes and any other path tricks.
    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)\\z")] // \z, not $: $ also matches before a trailing newline
    private static partial Regex FileNameRegex();

    public static bool IsValidFileName(string fileName) => FileNameRegex().IsMatch(fileName);

    public string? GetPath(string fileName)
    {
        if (!IsValidFileName(fileName)) return null;
        var path = Path.Combine(_directory, fileName);
        return File.Exists(path) ? path : null;
    }

    public static string ContentTypeFor(string fileName) => ContentTypes[Path.GetExtension(fileName).TrimStart('.')];

    public void Delete(string? fileName)
    {
        if (fileName is null || !IsValidFileName(fileName)) return;
        File.Delete(Path.Combine(_directory, fileName));
    }
}
