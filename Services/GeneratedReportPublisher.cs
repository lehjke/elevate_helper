namespace ElevateHelperWinUI.Services;

internal static class GeneratedReportPublisher
{
    public static void Publish(
        string temporaryExcelPath,
        string destinationExcelPath,
        string temporaryPdfPath,
        string destinationPdfPath)
    {
        ValidateTemporaryFile(temporaryExcelPath);
        ValidateTemporaryFile(temporaryPdfPath);
        RecoverInterruptedPublication(destinationExcelPath, destinationPdfPath);

        string? excelBackupPath = null;
        string? pdfBackupPath = null;
        bool excelPublished = false;
        bool pdfPublished = false;
        bool publicationCompleted = false;
        string transactionId = Guid.NewGuid().ToString("N");

        try
        {
            excelBackupPath = MoveDestinationToBackup(destinationExcelPath, transactionId);
            pdfBackupPath = MoveDestinationToBackup(destinationPdfPath, transactionId);

            File.Move(temporaryExcelPath, destinationExcelPath);
            excelPublished = true;
            File.Move(temporaryPdfPath, destinationPdfPath);
            pdfPublished = true;
            publicationCompleted = true;
        }
        catch (Exception publicationException)
        {
            List<Exception> rollbackErrors = [];
            if (TryRollBackDestination(
                    destinationPdfPath,
                    pdfBackupPath,
                    pdfPublished,
                    out Exception? pdfRollbackError))
            {
                pdfBackupPath = null;
            }
            else if (pdfRollbackError is not null)
            {
                rollbackErrors.Add(pdfRollbackError);
            }

            if (TryRollBackDestination(
                    destinationExcelPath,
                    excelBackupPath,
                    excelPublished,
                    out Exception? excelRollbackError))
            {
                excelBackupPath = null;
            }
            else if (excelRollbackError is not null)
            {
                rollbackErrors.Add(excelRollbackError);
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
                TryDeleteBackup(excelBackupPath);
                TryDeleteBackup(pdfBackupPath);
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

    private static void RecoverInterruptedPublication(
        string destinationExcelPath,
        string destinationPdfPath)
    {
        Dictionary<string, string> excelBackups = FindBackups(destinationExcelPath);
        Dictionary<string, string> pdfBackups = FindBackups(destinationPdfPath);

        foreach (string transactionId in excelBackups.Keys
                     .Intersect(pdfBackups.Keys, StringComparer.OrdinalIgnoreCase)
                     .ToList())
        {
            string excelBackup = excelBackups[transactionId];
            string pdfBackup = pdfBackups[transactionId];
            bool publicationLooksComplete =
                File.Exists(destinationExcelPath) && File.Exists(destinationPdfPath);
            if (publicationLooksComplete)
            {
                DeleteIfExists(excelBackup);
                DeleteIfExists(pdfBackup);
            }
            else
            {
                DeleteIfExists(destinationExcelPath);
                DeleteIfExists(destinationPdfPath);
                File.Move(excelBackup, destinationExcelPath);
                File.Move(pdfBackup, destinationPdfPath);
            }

            excelBackups.Remove(transactionId);
            pdfBackups.Remove(transactionId);
        }

        RecoverIndependentBackups(destinationExcelPath, excelBackups.Values);
        RecoverIndependentBackups(destinationPdfPath, pdfBackups.Values);
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
}
