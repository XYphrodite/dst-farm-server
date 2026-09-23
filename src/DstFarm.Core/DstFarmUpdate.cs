using System.Reflection;
using SelfUpdateKit;

namespace DstFarm.Core;

/// <summary>
/// dstfarm wiring for the shared SelfUpdateKit: fixed repository, asset names, installed
/// version, retired-binary cleanup and progress mapping. Update rules (mandatory checksum,
/// staged probe with rollback) come from the library, not from here.
/// </summary>
internal static class DstFarmUpdate
{
    private const string Repository = "XYphrodite/dst-farm-server";
    private const string AssetName = "dstfarm.exe";
    private const string InstalledFileName = "dstfarm.exe";

    public static ReleaseSourceOptions Options() => new()
    {
        Repository = Repository,
        ExecutableAssetNames = { AssetName },
        ChecksumMode = ChecksumMode.ReleaseNotes,
    };

    public static ReleaseVersion CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null
                ? new ReleaseVersion(0, 0, 0)
                : new ReleaseVersion(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
        }
    }

    /// <summary>
    /// Under `dotnet run` the process path is the host, not this tool. Replacing whatever
    /// happens to be hosting the CLI is never the intent, so the command declines instead.
    /// </summary>
    public static string RequireInstalledPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath) ||
            !string.Equals(Path.GetFileName(processPath), InstalledFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T(
                "самообновление доступно только для установленного dstfarm.exe",
                "self-update is only available for an installed dstfarm.exe."));
        return Path.GetFullPath(processPath);
    }

    /// <summary>
    /// Removes binaries retired by earlier updates. Also collects the legacy single
    /// `.old` from before SelfUpdateKit, once, best effort.
    /// </summary>
    public static void CleanupRetired(string exePath)
    {
        new ExecutableReplacer().RemoveRetiredCopies(exePath);
        try
        {
            var legacy = exePath + ".old";
            if (File.Exists(legacy))
                File.Delete(legacy);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static async Task<SelfUpdateReport> CheckAsync(string exePath, CancellationToken cancellationToken)
    {
        try
        {
            var options = Options();
            using var source = new GitHubReleaseSource(options);
            var service = new SelfUpdateService(exePath, CurrentVersion, source, options);
            return await service.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }

    public static async Task<SelfUpdateReport> ApplyAsync(
        string exePath,
        SelfUpdateRequest request,
        Action<SelfUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = Options();
            using var source = new GitHubReleaseSource(options);
            var service = new SelfUpdateService(exePath, CurrentVersion, source, options);
            return await service.UpdateAsync(request, cancellationToken, progress).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }

    /// <summary>Maps library phases onto the progress bar dstfarm already draws.</summary>
    public static SteamProgress ToSteamProgress(SelfUpdateProgress report, SteamProgress current)
    {
        return report.Phase switch
        {
            SelfUpdatePhase.Downloading when report.TotalBytes is > 0 =>
                new SteamProgress(
                    Loc.T("загрузка обновления", "downloading update"),
                    report.ReceivedBytes is { } received ? received * 100.0 / report.TotalBytes.Value : current.Percent,
                    report.ReceivedBytes ?? current.BytesDone,
                    report.TotalBytes.Value),
            SelfUpdatePhase.Downloading =>
                new SteamProgress(
                    Loc.T("загрузка обновления", "downloading update"),
                    current.Percent,
                    report.ReceivedBytes ?? current.BytesDone,
                    0),
            SelfUpdatePhase.Verifying =>
                new SteamProgress(Loc.T("проверка обновления", "verifying update"), 100, current.BytesDone, current.BytesTotal),
            SelfUpdatePhase.Installing =>
                new SteamProgress(Loc.T("установка обновления", "installing update"), 100, current.BytesDone, current.BytesTotal),
            SelfUpdatePhase.ReleaseAvailable =>
                current with { State = Loc.T("релиз найден", "release found") },
            _ =>
                current with { State = Loc.T("проверка обновления", "checking for updates") },
        };
    }
}
