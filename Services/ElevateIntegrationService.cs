using System.Diagnostics;
using Microsoft.Win32;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateIntegrationService : IElevateIntegrationService
{
    private static readonly object CacheSync = new();
    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromSeconds(2);
    private static ElevateIntegrationInfo? cachedInfo;
    private static DateTimeOffset cachedAt;

    public ElevateIntegrationInfo GetIntegrationInfo()
    {
        ElevateIntegrationInfo? snapshot = TryGetCachedInfo();
        if (snapshot is not null)
        {
            return snapshot;
        }

        List<string> probedPaths = [];
        HashSet<string> visitedPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, string source) in EnumerateCandidates())
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalizedPath = NormalizePath(path);
            if (!visitedPaths.Add(normalizedPath))
            {
                continue;
            }

            probedPaths.Add(normalizedPath);
            if (File.Exists(normalizedPath))
            {
                string? version = GetProductVersion(normalizedPath);
                ElevateIntegrationInfo detectedInfo = new(
                    IsDetected: true,
                    ExecutablePath: normalizedPath,
                    ProductVersion: version,
                    DetectionSource: source,
                    ProbedPaths: probedPaths);
                UpdateCache(detectedInfo);
                return detectedInfo;
            }
        }

        ElevateIntegrationInfo notFoundInfo = new(
            IsDetected: false,
            ExecutablePath: null,
            ProductVersion: null,
            DetectionSource: "Not found",
            ProbedPaths: probedPaths);
        UpdateCache(notFoundInfo);
        return notFoundInfo;
    }

    private static ElevateIntegrationInfo? TryGetCachedInfo()
    {
        lock (CacheSync)
        {
            if (cachedInfo is null)
            {
                return null;
            }

            TimeSpan cacheDuration = cachedInfo.IsDetected ? PositiveCacheDuration : NegativeCacheDuration;
            if (DateTimeOffset.UtcNow - cachedAt > cacheDuration)
            {
                cachedInfo = null;
                return null;
            }

            if (cachedInfo.IsDetected &&
                (!string.IsNullOrWhiteSpace(cachedInfo.ExecutablePath) && !File.Exists(cachedInfo.ExecutablePath)))
            {
                cachedInfo = null;
                return null;
            }

            return cachedInfo;
        }
    }

    private static void UpdateCache(ElevateIntegrationInfo info)
    {
        lock (CacheSync)
        {
            cachedInfo = info;
            cachedAt = DateTimeOffset.UtcNow;
        }
    }

    private static IEnumerable<(string Path, string Source)> EnumerateCandidates()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("ELEVATE_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return (fromEnv, "Environment variable ELEVATE_EXE_PATH");
        }

        foreach ((string path, string source) in ReadAppPathsCandidates())
        {
            yield return (path, source);
        }

        foreach ((string path, string source) in ReadUninstallCandidates())
        {
            yield return (path, source);
        }

        foreach ((string path, string source) in EnumerateProgramFilesCandidates())
        {
            yield return (path, source);
        }

        foreach (string pathFromPath in EnumeratePathCandidates())
        {
            yield return (pathFromPath, "PATH");
        }
    }

    private static IEnumerable<(string Path, string Source)> ReadAppPathsCandidates()
    {
        const string appPathsSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Elevate.exe";

        foreach (RegistryKey? hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using RegistryKey? key = hive.OpenSubKey(appPathsSubKey);
            if (key is null)
            {
                continue;
            }

            string? raw = key.GetValue(null) as string;
            string? parsed = ParseExecutablePath(raw);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                yield return (parsed, $"Registry App Paths ({hive.Name})");
            }
        }
    }

    private static IEnumerable<(string Path, string Source)> ReadUninstallCandidates()
    {
        string[] uninstallRoots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        foreach (RegistryKey? hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (string root in uninstallRoots)
            {
                using RegistryKey? uninstallKey = hive.OpenSubKey(root);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);
                    if (appKey is null)
                    {
                        continue;
                    }

                    string? displayName = appKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    if (!displayName.Contains("elevate", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!displayName.Contains("peters", StringComparison.OrdinalIgnoreCase))
                    {
                        // Keep fallback candidates with generic Elevate naming later from Program Files.
                        continue;
                    }

                    string? installLocation = appKey.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        yield return (
                            Path.Combine(installLocation, "Elevate.exe"),
                            $"Registry Uninstall ({hive.Name}\\{root})");
                    }

                    string? displayIcon = appKey.GetValue("DisplayIcon") as string;
                    string? parsedIconPath = ParseExecutablePath(displayIcon);
                    if (!string.IsNullOrWhiteSpace(parsedIconPath))
                    {
                        yield return (
                            parsedIconPath,
                            $"Registry DisplayIcon ({hive.Name}\\{root})");
                    }
                }
            }
        }
    }

    private static IEnumerable<(string Path, string Source)> EnumerateProgramFilesCandidates()
    {
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (string? root in new[] { programFilesX86, programFiles })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (string candidate in Directory.EnumerateDirectories(root, "Elevate*"))
            {
                yield return (
                    Path.Combine(candidate, "Elevate.exe"),
                    $"Program Files scan ({root})");
            }

            // Common direct legacy path.
            yield return (
                Path.Combine(root, "Elevate 9", "Elevate.exe"),
                $"Legacy default ({root})");
        }
    }

    private static IEnumerable<string> EnumeratePathCandidates()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            yield break;
        }

        foreach (string rawPath in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string path = rawPath.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            yield return Path.Combine(path, "Elevate.exe");
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return path.Trim().Trim('"');
        }
    }

    private static string? ParseExecutablePath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        string value = rawValue.Trim();
        if (value.StartsWith('"') && value.IndexOf('"', 1) > 0)
        {
            int closingQuoteIndex = value.IndexOf('"', 1);
            if (closingQuoteIndex > 1)
            {
                return value[1..closingQuoteIndex];
            }
        }

        int commaIndex = value.IndexOf(',');
        if (commaIndex > 0)
        {
            value = value[..commaIndex];
        }

        int exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            value = value[..(exeIndex + 4)];
        }

        return value.Trim().Trim('"');
    }

    private static string? GetProductVersion(string filePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(filePath);
            return info.ProductVersion ?? info.FileVersion;
        }
        catch
        {
            return null;
        }
    }
}
