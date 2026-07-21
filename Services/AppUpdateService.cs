using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ElevateHelperWinUI.Services;

public sealed class AppUpdateService
{
    internal const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/lehjke/elevate_helper/releases/latest";
    private const string UserAgent = "ElevateHelperWinUI";
    private readonly HttpClient httpClient;

    public AppUpdateService()
        : this(CreateHttpClient())
    {
    }

    internal AppUpdateService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public string CurrentVersion => GetCurrentVersion();

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, GitHubLatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;

        string latestTag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            return null;
        }

        AppUpdateAsset? setupAsset = SelectSetupAsset(ReadAssets(root));
        if (setupAsset is null)
        {
            return null;
        }

        string currentVersion = CurrentVersion;
        if (!IsUpdateAvailable(currentVersion, latestTag))
        {
            return null;
        }

        return new AppUpdateInfo(
            currentVersion,
            NormalizeVersion(latestTag),
            latestTag,
            ReadString(root, "html_url"),
            setupAsset);
    }

    public async Task<string> DownloadAndStartUpdateAsync(
        AppUpdateInfo updateInfo,
        CancellationToken cancellationToken = default)
    {
        return await DownloadAndStartUpdateAsync(updateInfo, progress: null, cancellationToken);
    }

    public async Task<string> DownloadAndStartUpdateAsync(
        AppUpdateInfo updateInfo,
        IProgress<AppUpdateProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElevateHelper",
            "updates",
            updateInfo.LatestTag);
        Directory.CreateDirectory(updateDirectory);

        progress?.Report(new AppUpdateProgress(AppUpdateProgressStage.Preparing));

        string installerPath = Path.Combine(updateDirectory, SanitizeFileName(updateInfo.SetupAsset.Name));
        using HttpResponseMessage response = await httpClient.GetAsync(
            updateInfo.SetupAsset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        long bytesReceived = 0;
        await using (FileStream output = File.Create(installerPath))
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesReceived += bytesRead;
                progress?.Report(new AppUpdateProgress(
                    AppUpdateProgressStage.Downloading,
                    bytesReceived,
                    totalBytes));
            }
        }

        progress?.Report(new AppUpdateProgress(AppUpdateProgressStage.Verifying, bytesReceived, totalBytes));
        VerifyDownloadedInstaller(installerPath, updateInfo.SetupAsset);

        progress?.Report(new AppUpdateProgress(AppUpdateProgressStage.StartingInstaller, bytesReceived, totalBytes));
        ProcessStartInfo startInfo = new()
        {
            FileName = installerPath,
            Arguments = BuildSilentInstallerArguments(),
            UseShellExecute = true,
        };
        using Process installerProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The update installer could not be started.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        installerProcess.Refresh();
        if (installerProcess.HasExited && installerProcess.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The update installer exited with code {installerProcess.ExitCode}.");
        }

        return installerPath;
    }

    internal static string BuildSilentInstallerArguments()
    {
        return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
    }

    internal static bool IsUpdateAvailable(string currentVersion, string latestTag)
    {
        string current = NormalizeVersion(currentVersion);
        string latest = NormalizeVersion(latestTag);
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest))
        {
            return false;
        }

        if (Version.TryParse(current, out Version? currentParsed) &&
            Version.TryParse(latest, out Version? latestParsed))
        {
            return latestParsed > currentParsed;
        }

        if (TryParseSemanticVersion(current, out SemanticVersion? currentSemantic) &&
            TryParseSemanticVersion(latest, out SemanticVersion? latestSemantic) &&
            currentSemantic is not null &&
            latestSemantic is not null)
        {
            return CompareSemanticVersions(latestSemantic, currentSemantic) > 0;
        }

        return !string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        string normalized = version.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        int metadataIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        return normalized.Trim();
    }

    internal static AppUpdateAsset? SelectSetupAsset(IEnumerable<AppUpdateAsset> assets)
    {
        return assets
            .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset => asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    private static string GetCurrentVersion()
    {
        Assembly assembly = typeof(AppUpdateService).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        string version = NormalizeVersion(informationalVersion);
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static IReadOnlyList<AppUpdateAsset> ReadAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<AppUpdateAsset> assets = [];
        foreach (JsonElement assetElement in assetsElement.EnumerateArray())
        {
            string name = ReadString(assetElement, "name");
            string downloadUrl = ReadString(assetElement, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            assets.Add(new AppUpdateAsset(name, downloadUrl, ReadString(assetElement, "digest")));
        }

        return assets;
    }

    internal static bool IsSha256DigestMatch(string filePath, string expectedDigest)
    {
        string expected = NormalizeSha256Digest(expectedDigest);
        if (!IsSha256Digest(expected))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(filePath);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return string.Empty;
        }

        string normalized = digest.Trim();
        const string sha256Prefix = "sha256:";
        if (normalized.StartsWith(sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[sha256Prefix.Length..];
        }

        return normalized
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static void VerifyDownloadedInstaller(string installerPath, AppUpdateAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Digest))
        {
            throw new InvalidOperationException("Release asset digest is missing. Update was not started.");
        }

        if (!IsSha256DigestMatch(installerPath, asset.Digest))
        {
            throw new InvalidOperationException("Downloaded update installer failed SHA-256 verification.");
        }
    }

    private static bool IsSha256Digest(string digest)
    {
        return digest.Length == 64 &&
               digest.All(ch => Uri.IsHexDigit(ch));
    }

    private static bool TryParseSemanticVersion(string version, out SemanticVersion? semanticVersion)
    {
        semanticVersion = null;
        int prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);
        string core = prereleaseIndex >= 0 ? version[..prereleaseIndex] : version;
        string prerelease = prereleaseIndex >= 0 ? version[(prereleaseIndex + 1)..] : string.Empty;

        if (!Version.TryParse(core, out Version? parsed))
        {
            return false;
        }

        semanticVersion = new SemanticVersion(
            parsed.Major,
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0),
            prerelease);
        return true;
    }

    private static int CompareSemanticVersions(SemanticVersion left, SemanticVersion right)
    {
        int coreCompare = left.Major.CompareTo(right.Major);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        coreCompare = left.Minor.CompareTo(right.Minor);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        coreCompare = left.Patch.CompareTo(right.Patch);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        coreCompare = left.Revision.CompareTo(right.Revision);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        return ComparePrerelease(left.Prerelease, right.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        bool leftIsRelease = string.IsNullOrWhiteSpace(left);
        bool rightIsRelease = string.IsNullOrWhiteSpace(right);
        if (leftIsRelease && rightIsRelease)
        {
            return 0;
        }

        if (leftIsRelease)
        {
            return 1;
        }

        if (rightIsRelease)
        {
            return -1;
        }

        string[] leftParts = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        string[] rightParts = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int length = Math.Max(leftParts.Length, rightParts.Length);
        for (int index = 0; index < length; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            int partCompare = ComparePrereleasePart(leftParts[index], rightParts[index]);
            if (partCompare != 0)
            {
                return partCompare;
            }
        }

        return 0;
    }

    private static int ComparePrereleasePart(string left, string right)
    {
        bool leftIsNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
        bool rightIsNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);
        if (leftIsNumber && rightIsNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (leftIsNumber)
        {
            return -1;
        }

        if (rightIsNumber)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "ElevateHelper-update.exe" : sanitized;
    }
}

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string LatestTag,
    string ReleaseUrl,
    AppUpdateAsset SetupAsset);

public sealed record AppUpdateAsset(string Name, string DownloadUrl, string Digest);

public sealed record AppUpdateProgress(
    AppUpdateProgressStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null)
{
    public double? Percentage => TotalBytes is > 0
        ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
        : null;
}

public enum AppUpdateProgressStage
{
    Preparing,
    Downloading,
    Verifying,
    StartingInstaller,
}

internal sealed record SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    int Revision,
    string Prerelease);
