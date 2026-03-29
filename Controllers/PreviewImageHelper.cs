namespace ValidasiTugasAkhir.MainService.Controllers;

internal static class PreviewImageHelper
{
    private static readonly string[] PreferredImageExtensions =
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".webp"
    };

    public static string? ResolveConfiguredImagesDirectory(string storagePath, string fullStoragePath, string? imagesPath)
    {
        if (string.IsNullOrWhiteSpace(imagesPath))
            return null;

        var rawPath = imagesPath.Trim();
        var resolved = Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Combine(storagePath, rawPath));

        if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            return null;

        var extension = Path.GetExtension(resolved);
        if (!string.IsNullOrWhiteSpace(extension) && AllowedImageExtensions.Contains(extension))
        {
            var dir = Path.GetDirectoryName(resolved);
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            resolved = Path.GetFullPath(dir);
            if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return resolved;
    }

    public static IReadOnlyList<string> BuildSafeCandidateDirectories(string fullStoragePath, IEnumerable<string?> candidateDirs)
    {
        var safeDirs = new List<string>();

        foreach (var dir in candidateDirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var resolved = Path.GetFullPath(dir);
            if (!resolved.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!safeDirs.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                safeDirs.Add(resolved);
        }

        return safeDirs;
    }

    public static bool IsAllowedImageExtension(string? extension)
        => string.IsNullOrWhiteSpace(extension) || AllowedImageExtensions.Contains(extension);

    public static string? ResolveImageFile(IEnumerable<string> candidateDirs, string imageName)
    {
        var extension = Path.GetExtension(imageName);
        var stem = Path.GetFileNameWithoutExtension(imageName);
        var hasExtension = !string.IsNullOrWhiteSpace(extension);

        foreach (var dir in candidateDirs.Where(Directory.Exists))
        {
            if (hasExtension)
            {
                var exact = Path.Combine(dir, imageName);
                if (File.Exists(exact))
                    return exact;

                if (!string.IsNullOrWhiteSpace(stem))
                {
                    foreach (var fallbackExtension in PreferredImageExtensions.Where(ext => !ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    {
                        var fallback = Path.Combine(dir, $"{stem}{fallbackExtension}");
                        if (File.Exists(fallback))
                            return fallback;
                    }
                }

                continue;
            }

            foreach (var preferredExtension in PreferredImageExtensions)
            {
                var candidate = Path.Combine(dir, $"{imageName}{preferredExtension}");
                if (File.Exists(candidate))
                    return candidate;
            }

            var direct = Path.Combine(dir, imageName);
            if (File.Exists(direct))
                return direct;
        }

        return null;
    }

    public static List<int> EnumerateAvailablePages(IEnumerable<string> candidateDirs)
    {
        var pages = new SortedSet<int>();

        foreach (var dir in candidateDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var extension = Path.GetExtension(file);
                if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
                    continue;

                var pageNumber = TryGetTrailingNumber(Path.GetFileNameWithoutExtension(file));
                if (pageNumber.HasValue && pageNumber.Value > 0)
                    pages.Add(pageNumber.Value);
            }
        }

        return pages.ToList();
    }

    public static string GetImageContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static int? TryGetTrailingNumber(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var index = name.Length - 1;
        while (index >= 0 && char.IsDigit(name[index]))
            index--;

        var start = index + 1;
        if (start >= name.Length)
            return null;

        var digits = name[start..];
        return int.TryParse(digits, out var number) ? number : null;
    }
}
