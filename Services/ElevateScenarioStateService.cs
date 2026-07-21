using System.Security.Cryptography;
using System.Text.Json;

namespace ElevateHelperWinUI.Services;

internal sealed class ElevateScenarioStateService
{
    internal const string ManifestFileName = ".elevate-helper-scenario.json";
    private const int SchemaVersion = 2;
    private const string RulesVersion = "office-scenario-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public ElevateScenarioFingerprint CreateFingerprint(
        string sourcePath,
        string sourceFileName,
        string peak,
        int copiesCount)
    {
        using FileStream source = File.OpenRead(sourcePath);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        return new ElevateScenarioFingerprint(
            sourceFileName,
            sourceSha256,
            peak,
            copiesCount,
            RulesVersion);
    }

    public bool IsCurrent(string scenarioPath, ElevateScenarioFingerprint expected)
    {
        ElevateScenarioState? state = Load(scenarioPath);
        if (state is null ||
            state.SchemaVersion != SchemaVersion ||
            state.Fingerprint != expected ||
            state.ManagedFiles is null ||
            state.ManagedFileSha256 is null)
        {
            return false;
        }

        Dictionary<string, string> expectedHashes = new(
            state.ManagedFileSha256,
            StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in state.ManagedFiles)
        {
            if (!TryResolveManagedFilePath(scenarioPath, fileName, out string managedPath) ||
                !File.Exists(managedPath) ||
                !expectedHashes.TryGetValue(fileName, out string? expectedHash))
            {
                return false;
            }

            try
            {
                if (!ComputeSha256(managedPath).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return state.ManagedFiles.Count > 0;
    }

    public IReadOnlyList<string> GetManagedFiles(string scenarioPath)
    {
        return Load(scenarioPath)?.ManagedFiles ?? [];
    }

    public bool HasManifest(string scenarioPath)
    {
        return File.Exists(Path.Combine(scenarioPath, ManifestFileName));
    }

    public void Save(
        string scenarioPath,
        ElevateScenarioFingerprint fingerprint,
        IEnumerable<string> managedFiles)
    {
        Directory.CreateDirectory(scenarioPath);
        List<string> normalizedFiles = managedFiles
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .Where(fileName => TryResolveManagedFilePath(scenarioPath, fileName, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Dictionary<string, string> managedFileHashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in normalizedFiles)
        {
            _ = TryResolveManagedFilePath(scenarioPath, fileName, out string managedPath);
            managedFileHashes[fileName] = ComputeSha256(managedPath);
        }

        ElevateScenarioState state = new(
            SchemaVersion,
            fingerprint,
            normalizedFiles,
            managedFileHashes);

        string manifestPath = Path.Combine(scenarioPath, ManifestFileName);
        string temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public void DeleteManifest(string scenarioPath)
    {
        TryDelete(Path.Combine(scenarioPath, ManifestFileName));
    }

    private static ElevateScenarioState? Load(string scenarioPath)
    {
        string manifestPath = Path.Combine(scenarioPath, ManifestFileName);
        try
        {
            return File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<ElevateScenarioState>(
                    File.ReadAllText(manifestPath),
                    JsonOptions)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream source = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }

    private static bool TryResolveManagedFilePath(
        string scenarioPath,
        string? fileName,
        out string managedPath)
    {
        managedPath = string.Empty;
        string candidateName = fileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidateName) ||
            Path.IsPathRooted(candidateName) ||
            !Path.GetFileName(candidateName).Equals(candidateName, StringComparison.Ordinal) ||
            candidateName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string normalizedScenarioPath = Path.GetFullPath(scenarioPath);
        string candidatePath = Path.GetFullPath(Path.Combine(normalizedScenarioPath, candidateName));
        string relativePath = Path.GetRelativePath(normalizedScenarioPath, candidatePath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        managedPath = candidatePath;
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record ElevateScenarioFingerprint(
    string SourceFileName,
    string SourceSha256,
    string Peak,
    int CopiesCount,
    string RulesVersion);

internal sealed record ElevateScenarioState(
    int SchemaVersion,
    ElevateScenarioFingerprint Fingerprint,
    IReadOnlyList<string> ManagedFiles,
    IReadOnlyDictionary<string, string>? ManagedFileSha256 = null);
