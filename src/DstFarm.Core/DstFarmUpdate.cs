using System.Reflection;
using System.Text.RegularExpressions;
using SelfUpdateKit;

namespace DstFarm.Core;

/// <summary>
/// dstfarm wiring for the shared SelfUpdateKit: fixed repository, asset names for both
/// variants, installed version, retired-binary cleanup and progress mapping. Update rules
/// (mandatory checksum, staged probe with rollback) come from the library, not from here.
/// The repository is fixed in code on purpose: a configurable download origin would be
/// the shortest path from a stray setting to an executable of somebody else's choosing.
/// </summary>
internal static class DstFarmUpdate
{
    private const string Repository = "XYphrodite/dst-farm-server";
    internal const string FullAssetName = "dstfarm.exe";
    internal const string LightAssetName = "dstfarm-light.exe";
    internal const string InstalledFileName = "dstfarm.exe";
    internal const string VariantMarker = ".dstfarm-variant";

    // Major of the .NET runtime the light build targets. Bump together with the app's
    // target framework; must match Test-DotNetRuntimeLine in install.ps1.
    internal const string DotNetMajor = "10";

    // Console apps run on Microsoft.NETCore.App. A machine with the desktop bundle
    // installed qualifies too, because that bundle includes the base runtime.
    private static readonly Regex RuntimeLine = new(
        "^Microsoft\\.(NETCore|WindowsDesktop)\\.App\\s+" + DotNetMajor + "\\.",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal enum Variant
    {
        Full,
        Light,
    }

    internal static string AssetName(Variant variant) =>
        variant == Variant.Light ? LightAssetName : FullAssetName;

    public static ReleaseSourceOptions Options(Variant variant) => new()
    {
        Repository = Repository,
        ExecutableAssetNames = { AssetName(variant) },
        ChecksumMode = ChecksumMode.ReleaseNotes,
    };

    /// <summary>
    /// Which build the installation came from. The installer records it next to the
    /// executable; an installation from before variants defaults to the full build, so
    /// an update never silently changes what kind of build is installed.
    /// </summary>
    public static Variant InstalledVariant(string? processPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(processPath) && Path.IsPathFullyQualified(processPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(processPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    var marker = Path.Combine(directory, VariantMarker);
                    if (File.Exists(marker) &&
                        string.Equals(File.ReadAllText(marker).Trim(), "light", StringComparison.OrdinalIgnoreCase))
                        return Variant.Light;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return Variant.Full;
    }

    /// <summary>
    /// Mirrors Test-DotNetRuntimeLine in install.ps1: true when a `dotnet --list-runtimes`
    /// line offers a runtime the light build runs on. Kept here so the test suite pins
    /// the same semantics the installer auto-picks by.
    /// </summary>
    internal static bool IsRuntimeLine(string? line) =>
        !string.IsNullOrWhiteSpace(line) && RuntimeLine.IsMatch(line);

    /// <summary>
    /// Narrows release notes to the selected asset's checksum block. The shared notes
    /// parser takes the first hash after "SHA-256", so handing it the whole body would
    /// verify every variant against the first block's hash.
    /// </summary>
    internal static string SelectChecksumNotes(string? notes, string assetName)
    {
        if (!string.IsNullOrWhiteSpace(notes) && !string.IsNullOrWhiteSpace(assetName))
        {
            var marker = "SHA-256 `" + assetName + "`";
            var at = notes.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
                return notes.Substring(at);
        }
        throw new InvalidDataException($"The release carries no SHA-256 checksum for `{assetName}`.");
    }

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
            var variant = InstalledVariant(exePath);
            var options = Options(variant);
            using var inner = new GitHubReleaseSource(options);
            var source = new VariantNotesSource(inner, AssetName(variant));
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
            var variant = InstalledVariant(exePath);
            var options = Options(variant);
            using var inner = new GitHubReleaseSource(options);
            var source = new VariantNotesSource(inner, AssetName(variant));
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

    /// <summary>
    /// Scopes the release notes to the installed variant's checksum block; downloads
    /// and checksum reads pass through untouched.
    /// </summary>
    private sealed class VariantNotesSource : IReleaseSource
    {
        private readonly IReleaseSource _inner;
        private readonly string _assetName;

        public VariantNotesSource(IReleaseSource inner, string assetName)
        {
            _inner = inner;
            _assetName = assetName;
        }

        public async Task<ReleaseDescriptor> ResolveAsync(string? tag, CancellationToken cancellationToken)
        {
            var release = await _inner.ResolveAsync(tag, cancellationToken).ConfigureAwait(false);
            return release with { Notes = SelectChecksumNotes(release.Notes, _assetName) };
        }

        public Task DownloadAsync(
            Uri address,
            string destinationPath,
            CancellationToken cancellationToken,
            Action<long, long?>? progress = null) =>
            _inner.DownloadAsync(address, destinationPath, cancellationToken, progress);

        public Task<string> ReadTextAsync(Uri address, CancellationToken cancellationToken) =>
            _inner.ReadTextAsync(address, cancellationToken);
    }
}
