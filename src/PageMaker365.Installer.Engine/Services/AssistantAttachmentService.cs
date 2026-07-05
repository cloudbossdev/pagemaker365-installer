using System.Security.Cryptography;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AssistantAttachmentService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".txt",
        ".log",
        ".json",
        ".md"
    };

    public async Task<AssistantAttachment> ImportAsync(
        string sourcePath,
        string attachmentRoot,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected assistant attachment was not found.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"Unsupported assistant attachment type: {extension}");
        }

        Directory.CreateDirectory(attachmentRoot);
        var fileName = CreateStoredFileName(sourcePath);
        var storedPath = Path.Combine(attachmentRoot, fileName);

        await using (var source = File.OpenRead(sourcePath))
        await using (var target = File.Create(storedPath))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var info = new FileInfo(storedPath);
        return new AssistantAttachment
        {
            FileName = Path.GetFileName(sourcePath),
            ContentType = GetContentType(extension),
            OriginalPath = sourcePath,
            StoredPath = storedPath,
            SizeBytes = info.Length,
            Sha256 = await ComputeHashAsync(storedPath, cancellationToken),
            IsImage = IsImageExtension(extension)
        };
    }

    private static string CreateStoredFileName(string sourcePath)
    {
        var safeName = string.Concat(Path.GetFileName(sourcePath)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var prefix = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}";
        return $"{prefix}-{safeName}";
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".txt" or ".log" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static bool IsImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}
