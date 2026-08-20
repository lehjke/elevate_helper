namespace ElevateHelperWinUI.Services;

internal static class GeneratedReportPublisher
{
    public static void Publish(string temporaryPath, string destinationPath)
    {
        PublishCore([new PublicationFile(temporaryPath, destinationPath)]);
    }

    // Retained for the legacy paired-report path. The redesigned report flow publishes
    // only the PDF through the two-argument overload above.
    public static void Publish(
        string temporaryExcelPath,
        string destinationExcelPath,
        string temporaryPdfPath,
        string destinationPdfPath)
    {
        PublishCore(
        [
            new PublicationFile(temporaryExcelPath, destinationExcelPath),
            new PublicationFile(temporaryPdfPath, destinationPdfPath),
        ]);
    }

    private static void PublishCore(IReadOnlyList<PublicationFile> files)
    {
        foreach (PublicationFile file in files)
        {
            ValidateTemporaryFile(file.TemporaryPath);
        }
        RecoverInterruptedPublication(files.Select(file => file.DestinationPath).ToArray());

        List<PublicationState> states = files.Select(file => new PublicationState(file)).ToList();
        bool publicationCompleted = false;
        string transactionId = Guid.NewGuid().ToString("N");

        try
        {
            foreach (PublicationState state in states)
            {
                state.BackupPath = MoveDestinationToBackup(state.File.DestinationPath, transactionId);
            }

            foreach (PublicationState state in states)
            {
                File.Move(state.File.TemporaryPath, state.File.DestinationPath);
                state.Published = true;
            }
            publicationCompleted = true;
        }
        catch (Exception publicationException)
        {
            List<Exception> rollbackErrors = [];
            for (int index = states.Count - 1; index >= 0; index--)
            {
                PublicationState state = states[index];
                if (TryRollBackDestination(
                        state.File.DestinationPath,
                        state.BackupPath,
                        state.Published,
                        out Exception? rollbackError))
                {
                    state.BackupPath = null;
                }
                else if (rollbackError is not null)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException(
                    "Report publication failed and one or more previous files could not be restored. Backup files were preserved.",
                    [publicationException, .. rollbackErrors]);
            }

            throw;
        }
        finally
        {
            if (publicationCompleted)
            {
                foreach (PublicationState state in states)
                {
                    TryDeleteBackup(state.BackupPath);
                }
            }
        }
    }

    private static void ValidateTemporaryFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Generated temporary report file was not found.", path);
        }
    }

    private static string? MoveDestinationToBackup(string destinationPath, string transactionId)
    {
        if (!File.Exists(destinationPath))
        {
            return null;
        }

        string backupPath = BuildBackupPath(destinationPath, transactionId);
        File.Move(destinationPath, backupPath);
        return backupPath;
    }

    private static void RecoverInterruptedPublication(IReadOnlyList<string> destinationPaths)
    {
        Dictionary<string, string>[] backupsByDestination = destinationPaths
            .Select(FindBackups)
            .ToArray();
        IEnumerable<string> sharedTransactionIds = backupsByDestination.Length == 0
            ? []
            : backupsByDestination
                .Skip(1)
                .Aggregate(
                    backupsByDestination[0].Keys.AsEnumerable(),
                    (current, backups) => current.Intersect(backups.Keys, StringComparer.OrdinalIgnoreCase));

        foreach (string transactionId in sharedTransactionIds.ToList())
        {
            bool publicationLooksComplete = destinationPaths.All(File.Exists);
            if (publicationLooksComplete)
            {
                foreach (Dictionary<string, string> backups in backupsByDestination)
                {
                    DeleteIfExists(backups[transactionId]);
                }
            }
            else
            {
                foreach (string destinationPath in destinationPaths)
                {
                    DeleteIfExists(destinationPath);
                }
                for (int index = 0; index < destinationPaths.Count; index++)
                {
                    File.Move(backupsByDestination[index][transactionId], destinationPaths[index]);
                }
            }

            foreach (Dictionary<string, string> backups in backupsByDestination)
            {
                backups.Remove(transactionId);
            }
        }

        for (int index = 0; index < destinationPaths.Count; index++)
        {
            RecoverIndependentBackups(destinationPaths[index], backupsByDestination[index].Values);
        }
    }

    private static void RecoverIndependentBackups(
        string destinationPath,
        IEnumerable<string> backupPaths)
    {
        List<string> backups = backupPaths
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (backups.Count == 0)
        {
            return;
        }

        if (!File.Exists(destinationPath))
        {
            File.Move(backups[0], destinationPath);
            backups.RemoveAt(0);
        }

        foreach (string staleBackup in backups)
        {
            DeleteIfExists(staleBackup);
        }
    }

    private static Dictionary<string, string> FindBackups(string destinationPath)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string fileName = Path.GetFileName(destinationPath);
        string prefix = $"{fileName}.elevate-helper-";
        const string suffix = ".bak";
        Dictionary<string, string> backups = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(
                     directory,
                     $"{fileName}.elevate-helper-*.bak",
                     SearchOption.TopDirectoryOnly))
        {
            string backupFileName = Path.GetFileName(path);
            if (!backupFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !backupFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string transactionId = backupFileName[prefix.Length..^suffix.Length];
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                backups[transactionId] = path;
            }
        }

        return backups;
    }

    private static string BuildBackupPath(string destinationPath, string transactionId)
    {
        return $"{destinationPath}.elevate-helper-{transactionId}.bak";
    }

    private static bool TryRollBackDestination(
        string destinationPath,
        string? backupPath,
        bool replacementWasPublished,
        out Exception? error)
    {
        try
        {
            if (replacementWasPublished)
            {
                DeleteIfExists(destinationPath);
            }

            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
            {
                File.Move(backupPath, destinationPath, overwrite: true);
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex;
            return false;
        }
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteBackup(string? path)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct PublicationFile(string TemporaryPath, string DestinationPath);

    private sealed class PublicationState(PublicationFile file)
    {
        public PublicationFile File { get; } = file;

        public string? BackupPath { get; set; }

        public bool Published { get; set; }
    }
}
