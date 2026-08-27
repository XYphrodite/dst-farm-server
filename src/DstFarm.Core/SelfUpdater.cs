using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DstFarm.Core;

public sealed record ReleaseInfo(string Tag, Version Version, string AssetUrl, long Size, string? Sha256);

/// <summary>Обновление dstfarm из релизов на GitHub.</summary>
public sealed partial class SelfUpdater(string repository = SelfUpdater.DefaultRepository)
{
    public const string DefaultRepository = "XYphrodite/dst-farm-server";
    private const string AssetName = "dstfarm.exe";

    private readonly string repository = repository;

    /// <summary>Куда переименовывается работающий exe: удалить его сразу Windows не даёт.</summary>
    public static string BackupPathFor(string exePath) => exePath + ".old";

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build)
            : new Version(0, 0, 0);

    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var parsed)
            ? new Version(parsed.Major, parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build)
            : null;
    }

    [GeneratedRegex(@"SHA-256[\s\S]{0,80}?([0-9a-fA-F]{64})", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern { get; }

    /// <summary>Контрольная сумма публикуется строкой в описании релиза.</summary>
    public static string? ExtractSha256(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;
        var match = ShaPattern.Match(notes);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    public async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client
            .GetAsync($"https://api.github.com/repos/{repository}/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        var version = ParseTag(tag);
        if (tag is null || version is null)
            return null;

        if (!root.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.GetString() != AssetName)
                continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            if (url is null)
                continue;

            var size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
            return new ReleaseInfo(tag, version, url, size, ExtractSha256(notes));
        }

        return null;
    }

    /// <summary>Качает файл во временный каталог и сверяет контрольную сумму.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<SteamProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        var temp = Path.Combine(Path.GetTempPath(), $"dstfarm-{Guid.NewGuid():N}.exe");
        using var client = CreateClient();
        using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(temp);

            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                progress?.Report(new SteamProgress(
                    "загрузка обновления",
                    total > 0 ? copied * 100.0 / total : 0,
                    copied,
                    total));
            }
        }

        if (release.Sha256 is { } expected)
        {
            var actual = await ComputeSha256Async(temp, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"SHA-256 не совпал: ожидалось {expected}, получено {actual}"));
            }
        }

        return temp;
    }

    /// <summary>
    /// Подменяет exe. Работающий файл удалить нельзя, но переименовать можно,
    /// поэтому старый уезжает в .old и подчищается при следующем запуске.
    /// </summary>
    public static string Apply(string downloadedFile, string targetExe)
    {
        var backup = BackupPathFor(targetExe);
        File.Delete(backup);

        if (File.Exists(targetExe))
            File.Move(targetExe, backup);

        try
        {
            File.Move(downloadedFile, targetExe);
        }
        catch (IOException)
        {
            // Не смогли поставить новый — возвращаем старый на место.
            if (File.Exists(backup) && !File.Exists(targetExe))
                File.Move(backup, targetExe);
            throw;
        }

        return backup;
    }

    /// <summary>Удаляет остатки прошлого обновления. Молча: файл может быть ещё занят.</summary>
    public static void CleanupBackup(string exePath)
    {
        try
        {
            var backup = BackupPathFor(exePath);
            if (File.Exists(backup))
                File.Delete(backup);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dstfarm-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
