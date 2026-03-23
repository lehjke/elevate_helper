using System.Diagnostics;
using System.Runtime.InteropServices;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateReportService : IElevateReportService
{
    private const string TDrive = "T:";
    private const string SharedRootFolderName =
        "\u041A\u0440\u0443\u043F\u043D\u044B\u0435 \u043F\u0440\u043E\u0435\u043A\u0442\u044B \u0438 \u0432\u044B\u0441\u043E\u0442\u043D\u043E\u0435 \u0441\u0442\u0440\u043E\u0438\u0442\u0435\u043B\u044C\u0441\u0442\u0432\u043E";
    private const string MeteorRelativePath =
        "\u0421\u043F\u0435\u0446\u0438\u0444\u0438\u043A\u0430\u0446\u0438\u0438\\ELEVATE\\Meteor";
    private const string TempOutputRelativePath = "_Ele_temp";

    public async Task<ProcessingResult> PrintReportAsync(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => PrintReportInternal(path, buildingType, cancellationToken),
            cancellationToken);
    }

    private static ProcessingResult PrintReportInternal(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProcessingResult.Fail("Path is empty.");
        }

        if (!Directory.Exists(path))
        {
            return ProcessingResult.Fail($"Path does not exist: {path}");
        }

        string batchResultsPath = Path.Combine(path, "batch_results.csv");
        if (!File.Exists(batchResultsPath))
        {
            return ProcessingResult.Fail($"batch_results.csv not found: {batchResultsPath}");
        }

        string? repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return ProcessingResult.Fail("Cannot find repository root containing .example\\KIP.xlam.");
        }

        string exampleFolder = Path.Combine(repositoryRoot, ".example");
        string kipPath = Path.Combine(exampleFolder, "KIP.xlam");
        if (!File.Exists(kipPath))
        {
            return ProcessingResult.Fail($"KIP.xlam not found: {kipPath}");
        }

        string expectedTemplateName = GetTemplateName(buildingType);
        if (!File.Exists(Path.Combine(exampleFolder, expectedTemplateName)))
        {
            return ProcessingResult.Fail($"Template not found: {Path.Combine(exampleFolder, expectedTemplateName)}");
        }

        bool mappedByService = false;
        string tDriveRoot = string.Empty;
        string runtimeRoot = Path.Combine(Path.GetTempPath(), "ElevateHelperWinUI", "KIPRuntime");
        string sharedRootPath = string.Empty;
        string meteorFolder = string.Empty;
        string outputFolder = string.Empty;
        object? excel = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!PrepareTDrive(runtimeRoot, out mappedByService, out tDriveRoot, out string mapError))
            {
                return ProcessingResult.Fail(mapError);
            }

            sharedRootPath = Path.Combine(tDriveRoot, SharedRootFolderName);
            meteorFolder = Path.Combine(sharedRootPath, MeteorRelativePath);
            outputFolder = Path.Combine(sharedRootPath, TempOutputRelativePath);
            Directory.CreateDirectory(meteorFolder);
            Directory.CreateDirectory(outputFolder);

            CopyTemplates(exampleFolder, meteorFolder);
            if (!TrySetBuildingType(batchResultsPath, buildingType, out string setBuildingTypeError))
            {
                return ProcessingResult.Fail(setBuildingTypeError);
            }

            DateTime beforeMacroUtc = DateTime.UtcNow;

            Type? excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType is null)
            {
                return ProcessingResult.Fail("Microsoft Excel COM is not available.");
            }

            excel = Activator.CreateInstance(excelType);
            if (excel is null)
            {
                return ProcessingResult.Fail("Unable to create Excel COM object.");
            }

            dynamic excelApp = excel;
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            excelApp.ScreenUpdating = false;

            excelApp.Workbooks.Open(kipPath);
            excelApp.Workbooks.Open(batchResultsPath);
            excelApp.Run("KIP.xlam!ElevateReportV1");
            excelApp.Workbooks.Close(false);
            excelApp.Quit();

            string? generatedReport = FindLatestGeneratedReport(outputFolder, beforeMacroUtc);
            if (generatedReport is null)
            {
                return ProcessingResult.Fail("Macro finished but report file was not generated.");
            }

            string destinationPath = Path.Combine(path, Path.GetFileName(generatedReport));
            File.Copy(generatedReport, destinationPath, overwrite: true);

            return ProcessingResult.Ok($"Report generated: {destinationPath}");
        }
        catch (Exception ex)
        {
            return ProcessingResult.Fail("An exception occurred while running KIP.xlam VBA report generation.", ex);
        }
        finally
        {
            if (excel is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(excel);
                }
                catch
                {
                    // Ignore COM release errors.
                }
            }

            if (mappedByService)
            {
                _ = ExecuteSubstCommand($"{TDrive} /D", out _);
            }
        }
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string kipPath = Path.Combine(current.FullName, ".example", "KIP.xlam");
            if (File.Exists(kipPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetTemplateName(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Office => "Office.xlsx",
            BuildingType.Hotel => "Hotel.xlsx",
            BuildingType.Residence => "Residential.xlsx",
            _ => throw new ArgumentOutOfRangeException(nameof(buildingType), buildingType, "Unsupported building type."),
        };
    }

    private static string GetBuildingTypeToken(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Office => "Office",
            BuildingType.Hotel => "Hotel",
            BuildingType.Residence => "Residential",
            _ => throw new ArgumentOutOfRangeException(nameof(buildingType), buildingType, "Unsupported building type."),
        };
    }

    private static bool TrySetBuildingType(
        string batchResultsPath,
        BuildingType buildingType,
        out string error)
    {
        error = string.Empty;
        string[] lines;

        try
        {
            lines = File.ReadAllLines(batchResultsPath);
        }
        catch (Exception ex)
        {
            error = $"Failed to read batch_results.csv: {ex.Message}";
            return false;
        }

        if (lines.Length == 0)
        {
            error = "batch_results.csv is empty.";
            return false;
        }

        char delimiter = DetectDelimiter(lines);
        string buildingTypeToken = GetBuildingTypeToken(buildingType);
        bool updated = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(delimiter);
            if (parts.Length < 2)
            {
                continue;
            }

            string key = parts[0].Trim().Trim('"');
            if (!key.Equals("BuildingType", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts[1] = buildingTypeToken;
            lines[i] = string.Join(delimiter, parts);
            updated = true;
            break;
        }

        if (!updated)
        {
            string newLine = $"BuildingType{delimiter}{buildingTypeToken}";
            lines = [.. lines, newLine];
        }

        try
        {
            File.WriteAllLines(batchResultsPath, lines);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to write batch_results.csv: {ex.Message}";
            return false;
        }
    }

    private static char DetectDelimiter(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Contains(';'))
            {
                return ';';
            }

            if (line.Contains(','))
            {
                return ',';
            }
        }

        return ';';
    }

    private static bool PrepareTDrive(
        string runtimeRoot,
        out bool mappedByService,
        out string tDriveRoot,
        out string error)
    {
        mappedByService = false;
        tDriveRoot = $"{TDrive}\\";
        error = string.Empty;

        if (Directory.Exists(tDriveRoot))
        {
            return true;
        }

        Directory.CreateDirectory(runtimeRoot);
        if (!ExecuteSubstCommand($"{TDrive} \"{runtimeRoot}\"", out string substError))
        {
            error = $"Unable to map {TDrive} for KIP macro runtime. {substError}";
            return false;
        }

        mappedByService = true;
        tDriveRoot = $"{TDrive}\\";
        return true;
    }

    private static void CopyTemplates(string sourceExampleFolder, string meteorFolder)
    {
        string[] templateNames = ["Hotel.xlsx", "Office.xlsx", "Residential.xlsx"];
        foreach (string templateName in templateNames)
        {
            string source = Path.Combine(sourceExampleFolder, templateName);
            string destination = Path.Combine(meteorFolder, templateName);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static string? FindLatestGeneratedReport(string outputFolder, DateTime afterUtc)
    {
        string[] candidates = Directory.GetFiles(outputFolder, "*.xlsx", SearchOption.TopDirectoryOnly);
        return candidates
            .Select(file => new FileInfo(file))
            .Where(file => file.LastWriteTimeUtc >= afterUtc.AddMinutes(-1))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static bool ExecuteSubstCommand(string arguments, out string error)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/c subst {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        using Process process = new() { StartInfo = startInfo };
        _ = process.Start();
        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            error = string.Empty;
            return true;
        }

        error = $"{stdOut} {stdErr}".Trim();
        return false;
    }
}
