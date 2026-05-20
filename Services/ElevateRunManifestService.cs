using System.Text.Json;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

internal sealed class ElevateRunManifestService
{
    public const string ManifestFileName = ".elevate-helper-run.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public ElevateRunManifest Create(
        string workingFolder,
        BuildingType buildingType,
        bool includeLunchPeak,
        int copiesCount)
    {
        ElevateRunManifest manifest = new()
        {
            RunId = Guid.NewGuid().ToString("N"),
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = ElevateRunManifestStatus.Running,
            WorkingFolder = Path.GetFullPath(workingFolder),
            BuildingType = buildingType,
            IncludeLunchPeak = includeLunchPeak,
            CopiesCount = copiesCount,
        };

        Save(manifest);
        return manifest;
    }

    public ElevateRunManifestStep StartStep(ElevateRunManifest manifest, string name)
    {
        ElevateRunManifestStep step = new()
        {
            Name = name,
            Status = ElevateRunManifestStatus.Running,
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        manifest.Steps.Add(step);
        Save(manifest);
        return step;
    }

    public void CompleteStep(ElevateRunManifest manifest, ElevateRunManifestStep step)
    {
        step.Status = ElevateRunManifestStatus.Completed;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        Save(manifest);
    }

    public void FailStep(ElevateRunManifest manifest, ElevateRunManifestStep step, string message)
    {
        step.Status = ElevateRunManifestStatus.Failed;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        step.ErrorMessage = message;
        Save(manifest);
    }

    public void StopStep(ElevateRunManifest manifest, ElevateRunManifestStep step, string message)
    {
        step.Status = ElevateRunManifestStatus.Stopped;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        step.ErrorMessage = message;
        Save(manifest);
    }

    public void SetArtifacts(ElevateRunManifest manifest, IEnumerable<ElevateRunManifestArtifact> artifacts)
    {
        manifest.Artifacts = artifacts
            .OrderBy(artifact => artifact.Scenario ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Save(manifest);
    }

    public void Complete(ElevateRunManifest manifest)
    {
        manifest.Status = ElevateRunManifestStatus.Completed;
        manifest.CompletedAtUtc = DateTimeOffset.UtcNow;
        manifest.ErrorMessage = null;
        Save(manifest);
    }

    public void Fail(ElevateRunManifest manifest, string message)
    {
        manifest.Status = ElevateRunManifestStatus.Failed;
        manifest.CompletedAtUtc = DateTimeOffset.UtcNow;
        manifest.ErrorMessage = message;
        Save(manifest);
    }

    public void Stop(ElevateRunManifest manifest, string message)
    {
        manifest.Status = ElevateRunManifestStatus.Stopped;
        manifest.CompletedAtUtc = DateTimeOffset.UtcNow;
        manifest.ErrorMessage = message;
        Save(manifest);
    }

    public static string GetManifestPath(string workingFolder)
    {
        return Path.Combine(workingFolder, ManifestFileName);
    }

    public static string GetHistoryFolderPath(string workingFolder)
    {
        return Path.Combine(workingFolder, ".elevate-helper-runs");
    }

    public static string GetHistoryManifestPath(string workingFolder, string runId)
    {
        return Path.Combine(GetHistoryFolderPath(workingFolder), $"{runId}.json");
    }

    public ElevateRunManifest? GetLatest(string workingFolder)
    {
        return ReadManifest(GetManifestPath(workingFolder)) ?? GetHistory(workingFolder).FirstOrDefault();
    }

    public IReadOnlyList<ElevateRunManifest> GetHistory(string workingFolder)
    {
        string historyPath = GetHistoryFolderPath(workingFolder);
        if (!Directory.Exists(historyPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(historyPath, "*.json")
            .Select(ReadManifest)
            .Where(manifest => manifest is not null)
            .Cast<ElevateRunManifest>()
            .OrderByDescending(manifest => manifest.StartedAtUtc)
            .ThenByDescending(manifest => manifest.RunId, StringComparer.Ordinal)
            .ToList();
    }

    private static void Save(ElevateRunManifest manifest)
    {
        try
        {
            string manifestPath = GetManifestPath(manifest.WorkingFolder);
            string json = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(manifestPath, json);

            if (!string.IsNullOrWhiteSpace(manifest.RunId))
            {
                string historyPath = GetHistoryFolderPath(manifest.WorkingFolder);
                Directory.CreateDirectory(historyPath);
                File.WriteAllText(GetHistoryManifestPath(manifest.WorkingFolder, manifest.RunId), json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The manifest is diagnostic state; processing should not fail only because it could not be written.
        }
    }

    private static ElevateRunManifest? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ElevateRunManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
