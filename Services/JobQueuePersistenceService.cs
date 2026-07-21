using System.Text.Json;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

internal sealed class JobQueuePersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string queuePath;
    private string? lastSerializedState;

    public JobQueuePersistenceService(string? queuePath = null)
    {
        this.queuePath = queuePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElevateHelper",
            "active-jobs.json");
    }

    public IReadOnlyList<PersistedJobSnapshot> LoadInterruptedJobs()
    {
        try
        {
            if (!File.Exists(queuePath))
            {
                return [];
            }

            string json = File.ReadAllText(queuePath);
            IReadOnlyList<PersistedJobSnapshot> snapshots =
                JsonSerializer.Deserialize<List<PersistedJobSnapshot>>(json, JsonOptions) ?? [];
            return snapshots;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public void ClearInterruptedJobs()
    {
        if (TryDelete(queuePath))
        {
            lastSerializedState = null;
        }
    }

    public void SaveActiveJobs(IEnumerable<PersistedJobSnapshot> snapshots)
    {
        List<PersistedJobSnapshot> activeJobs = snapshots
            .OrderBy(snapshot => snapshot.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string json = JsonSerializer.Serialize(activeJobs, JsonOptions);
        if (string.Equals(lastSerializedState, json, StringComparison.Ordinal))
        {
            return;
        }

        if (activeJobs.Count == 0)
        {
            lastSerializedState = TryDelete(queuePath) ? json : null;
            return;
        }

        string temporaryPath = $"{queuePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? directory = Path.GetDirectoryName(queuePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, queuePath, overwrite: true);
            lastSerializedState = json;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            lastSerializedState = null;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
