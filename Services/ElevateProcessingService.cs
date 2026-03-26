using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateProcessingService : IElevateProcessingService
{
    private const string GeneratedCopiesManifestFileName = ".elevate-helper.generated-copies.txt";
    private readonly IElevateLauncherService launcherService;

    public ElevateProcessingService()
        : this(new ElevateLauncherService())
    {
    }

    public ElevateProcessingService(IElevateLauncherService launcherService)
    {
        this.launcherService = launcherService;
    }

    public int GetDefaultCopies(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Residence => 8,
            BuildingType.Office => 13,
            BuildingType.Hotel => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(buildingType), buildingType, "Unknown building type."),
        };
    }

    public Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(path, buildingType, includeLunchPeak, progress: null, cancellationToken);
    }

    public Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(path, buildingType, includeLunchPeak, progress, progress, cancellationToken);
    }

    public async Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        int copiesCount = GetDefaultCopies(buildingType);
        return await RunAsync(copiesCount, path, buildingType, includeLunchPeak, morningProgress, lunchProgress, cancellationToken);
    }

    public async Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(copiesCount, path, buildingType, includeLunchPeak, progress: null, cancellationToken);
    }

    public Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(copiesCount, path, buildingType, includeLunchPeak, progress, progress, cancellationToken);
    }

    public async Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProcessingResult.Fail("Path to Elevate files is empty.");
        }

        if (!Directory.Exists(path))
        {
            return ProcessingResult.Fail($"Path does not exist: {path}");
        }

        if (copiesCount < 1)
        {
            return ProcessingResult.Fail("Copy count must be >= 1.");
        }

        try
        {
            await MakeCopiesAndRunAsync(
                buildingType,
                path,
                copiesCount,
                includeLunchPeak,
                morningProgress,
                lunchProgress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            string message = $"An exception of type {ex.GetType().Name} occurred in makecopiesandrun(). {ex.Message}";
            return ProcessingResult.Fail(message, ex);
        }

        try
        {
            if (buildingType == BuildingType.Office)
            {
                if (includeLunchPeak)
                {
                    GetArea(Path.Combine(path, "lunch"));
                }

                GetArea(Path.Combine(path, "morning"));
            }
            else
            {
                GetArea(path);
            }
        }
        catch (Exception ex)
        {
            string message = $"An exception of type {ex.GetType().Name} occurred in get_area(). {ex.Message}";
            return ProcessingResult.Fail(message, ex);
        }

        return ProcessingResult.Ok("OK!");
    }

    public void ModifyHandlingCapacity(string xmlFilePath, int newCapacity)
    {
        XDocument xmlDocument = LoadXml(xmlFilePath);

        XElement? handlingCapacity = xmlDocument
            .Descendants("PassengerData")
            .Elements("Standard")
            .Elements("HandlingCapacity")
            .FirstOrDefault();
        if (handlingCapacity is not null)
        {
            handlingCapacity.Value = newCapacity.ToString(CultureInfo.InvariantCulture);
        }

        IEnumerable<XElement> periods = xmlDocument
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .Where(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));

        foreach (XElement period in periods)
        {
            period.SetAttributeValue("TotalArrivalRate", newCapacity.ToString(CultureInfo.InvariantCulture));
        }

        SaveXml(xmlDocument, xmlFilePath);
    }

    public void ModifyBuildingTypeOffice(string xmlFilePath, string peak)
    {
        XDocument xmlDocument = LoadXml(xmlFilePath);

        IEnumerable<XElement> periods = xmlDocument
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .Where(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));

        foreach (XElement period in periods)
        {
            if (string.Equals(peak, "Morning", StringComparison.Ordinal))
            {
                period.SetAttributeValue("SplitUp", "100");
                period.SetAttributeValue("SplitDown", "0");
                period.SetAttributeValue("SplitInterfloor", "0");
            }
            else if (string.Equals(peak, "Lunch", StringComparison.Ordinal))
            {
                period.SetAttributeValue("SplitUp", "45");
                period.SetAttributeValue("SplitDown", "45");
                period.SetAttributeValue("SplitInterfloor", "10");
            }
        }

        XElement? buildingData = xmlDocument.Descendants("BuildingData").FirstOrDefault();
        if (buildingData is not null)
        {
            buildingData.SetAttributeValue("BuildingType", "1");
        }

        SaveXml(xmlDocument, xmlFilePath);
    }

    public void ModifyBuildingTypeResidence(string xmlFilePath, BuildingType buildingType)
    {
        XDocument xmlDocument = LoadXml(xmlFilePath);

        IEnumerable<XElement> periods = xmlDocument
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .Where(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));

        foreach (XElement period in periods)
        {
            period.SetAttributeValue("SplitUp", "50");
            period.SetAttributeValue("SplitDown", "50");
            period.SetAttributeValue("SplitInterfloor", "0");
        }

        XElement? buildingData = xmlDocument.Descendants("BuildingData").FirstOrDefault();
        if (buildingData is not null)
        {
            string? value = buildingType switch
            {
                BuildingType.Residence => "3",
                BuildingType.Hotel => "2",
                _ => null,
            };

            if (value is not null)
            {
                buildingData.SetAttributeValue("BuildingType", value);
            }
        }

        SaveXml(xmlDocument, xmlFilePath);
    }

    public void ModifyTitle(string xmlFilePath, string peak)
    {
        XDocument xmlDocument = LoadXml(xmlFilePath);

        XElement? jobData = xmlDocument.Descendants("JobData").FirstOrDefault();
        if (jobData is not null)
        {
            string currentTitle = (string?)jobData.Attribute("JobTitle") ?? string.Empty;
            string suffix = string.Equals(peak, "Lunch", StringComparison.Ordinal)
                ? " (\u043E\u0431\u0435\u0434\u0435\u043D\u043D\u044B\u0439 \u043F\u0438\u043A)"
                : " (\u0443\u0442\u0440\u0435\u043D\u043D\u0438\u0439 \u043F\u0438\u043A)";
            jobData.SetAttributeValue("JobTitle", currentTitle + suffix);
        }

        SaveXml(xmlDocument, xmlFilePath);
    }

    public void GetArea(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        List<string> files = GetElvxFiles(path);
        string xmlFilePath = Path.Combine(path, ResolveBaseFileName(path, files));
        string csvFilePath = Path.Combine(path, "floor_area.csv");

        XDocument xmlDocument = LoadXml(xmlFilePath);

        IEnumerable<XElement> cars = xmlDocument
            .Descendants("ElevatorData")
            .Elements("Advanced")
            .Elements("Configuration")
            .Elements("Car");

        List<(string CarId, string FloorArea)> rows = new();
        foreach (XElement car in cars)
        {
            string carId = (string?)car.Attribute("Id") ?? string.Empty;
            string floorArea = (string?)car.Attribute("FloorAreaM2") ?? string.Empty;
            rows.Add((carId, floorArea));
        }

        if (rows.Count == 0)
        {
            return;
        }

        using StreamWriter writer = new(csvFilePath, false, Encoding.UTF8);
        writer.WriteLine("CarId;FloorAreaM2");
        foreach ((string carId, string floorArea) in rows)
        {
            writer.WriteLine($"{EscapeCsvValue(carId)};{EscapeCsvValue(floorArea)}");
        }
    }

    private async Task MakeCopiesAndRunAsync(
        BuildingType buildingType,
        string path,
        int copiesCount,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken)
    {
        DeleteTrackedGeneratedCopies(path);
        List<string> files = GetElvxFiles(path);
        string baseFileName = ResolveBaseFileName(path, files);

        if (buildingType is BuildingType.Residence or BuildingType.Hotel)
        {
            ClearGeneratedOutputs(path);

            string residenceBaseFilePath = Path.Combine(path, baseFileName);
            ModifyBuildingTypeResidence(residenceBaseFilePath, buildingType);
            List<string> generatedCopies = new();

            for (int i = 2; i <= copiesCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string newFileName = BuildCopyFileName(baseFileName, i);
                generatedCopies.Add(newFileName);
                string newFilePath = Path.Combine(path, newFileName);
                EnsureCopyTargetDoesNotExist(newFilePath);
                File.Copy(
                    residenceBaseFilePath,
                    newFilePath,
                    overwrite: false);
                ModifyHandlingCapacity(newFilePath, i);
            }

            SaveTrackedGeneratedCopies(path, generatedCopies);
            await launcherService.LaunchResidenceAsync(path, morningProgress, cancellationToken);
            return;
        }

        if (buildingType != BuildingType.Office)
        {
            throw new InvalidOperationException($"Unknown building type: {buildingType}");
        }

        string officeBaseFilePath = Path.Combine(path, baseFileName);
        string morningPath = Path.Combine(path, "morning");
        ResetScenarioDirectory(morningPath);
        string morningBaseFilePath = Path.Combine(morningPath, baseFileName);
        File.Copy(officeBaseFilePath, morningBaseFilePath, overwrite: true);

        string lunchPath = string.Empty;
        string lunchBaseFilePath = string.Empty;
        if (includeLunchPeak)
        {
            lunchPath = Path.Combine(path, "lunch");
            ResetScenarioDirectory(lunchPath);
            lunchBaseFilePath = Path.Combine(lunchPath, baseFileName);
            File.Copy(officeBaseFilePath, lunchBaseFilePath, overwrite: true);
            ModifyBuildingTypeOffice(lunchBaseFilePath, "Lunch");
            ModifyTitle(lunchBaseFilePath, "Lunch");
        }
        else if (Directory.Exists(Path.Combine(path, "lunch")))
        {
            Directory.Delete(Path.Combine(path, "lunch"), recursive: true);
        }

        ModifyBuildingTypeOffice(morningBaseFilePath, "Morning");
        ModifyTitle(morningBaseFilePath, "Morning");

        for (int i = 2; i <= copiesCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string newFileName = BuildCopyFileName(baseFileName, i);
            string morningCopyPath = Path.Combine(morningPath, newFileName);

            File.Copy(
                morningBaseFilePath,
                morningCopyPath,
                overwrite: false);
            ModifyHandlingCapacity(morningCopyPath, i);

            if (includeLunchPeak)
            {
                string lunchCopyPath = Path.Combine(lunchPath, newFileName);
                File.Copy(
                    lunchBaseFilePath,
                    lunchCopyPath,
                    overwrite: false);
                ModifyHandlingCapacity(lunchCopyPath, i);
            }
        }

        await launcherService.LaunchOfficeAsync(
            path,
            includeLunchPeak,
            morningProgress,
            lunchProgress,
            cancellationToken);
    }

    private static string BuildCopyFileName(string sourceFileName, int copyIndex)
    {
        FileNamingScheme scheme = GetFileNamingScheme(sourceFileName);
        if (string.IsNullOrWhiteSpace(scheme.Prefix))
        {
            throw new InvalidOperationException($"Cannot build copy name from '{sourceFileName}'.");
        }

        string suffix = scheme.DigitWidth > 0
            ? copyIndex.ToString($"D{scheme.DigitWidth}", CultureInfo.InvariantCulture)
            : copyIndex.ToString(CultureInfo.InvariantCulture);
        return $"{scheme.Prefix}{suffix}.elvx";
    }

    private static List<string> GetElvxFiles(string path)
    {
        return Directory
            .EnumerateFiles(path, "*.elvx")
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveBaseFileName(string path, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No .elvx files found in '{path}'.");
        }

        string? indexOneFile = GetUniqueCandidate(files, fileName => GetFileNamingScheme(fileName).SeedIndex == 1);
        if (!string.IsNullOrWhiteSpace(indexOneFile))
        {
            return indexOneFile;
        }

        string? noDigitFile = GetUniqueCandidate(files, fileName => GetFileNamingScheme(fileName).SeedIndex is null);
        if (!string.IsNullOrWhiteSpace(noDigitFile))
        {
            return noDigitFile;
        }

        if (files.Count == 1)
        {
            return files[0];
        }

        throw new InvalidOperationException(
            $"Cannot determine the base .elvx file in '{path}'. Keep only the seed file in the root folder or use a file with index 1.");
    }

    private static string? GetUniqueCandidate(
        IReadOnlyList<string> files,
        Func<string, bool> predicate)
    {
        List<string> matches = files.Where(predicate).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static void ResetScenarioDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void DeleteTrackedGeneratedCopies(string path)
    {
        string manifestPath = GetGeneratedCopiesManifestPath(path);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        foreach (string fileName in File.ReadLines(manifestPath))
        {
            string trimmedFileName = fileName.Trim();
            if (string.IsNullOrWhiteSpace(trimmedFileName))
            {
                continue;
            }

            string candidatePath = Path.Combine(path, trimmedFileName);
            if (File.Exists(candidatePath))
            {
                File.Delete(candidatePath);
            }
        }

        File.Delete(manifestPath);
    }

    private static void ClearGeneratedOutputs(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path))
        {
            string extension = Path.GetExtension(file);
            string fileName = Path.GetFileName(file);
            bool isGeneratedOutput =
                fileName.Equals("batch_results.csv", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("floor_area.csv", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".elvr", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("_elvx.csv", StringComparison.OrdinalIgnoreCase);

            if (isGeneratedOutput)
            {
                File.Delete(file);
            }
        }
    }

    private static void SaveTrackedGeneratedCopies(string path, IEnumerable<string> fileNames)
    {
        string manifestPath = GetGeneratedCopiesManifestPath(path);
        List<string> entries = fileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }

            return;
        }

        File.WriteAllLines(manifestPath, entries);
    }

    private static string GetGeneratedCopiesManifestPath(string path)
    {
        return Path.Combine(path, GeneratedCopiesManifestFileName);
    }

    private static FileNamingScheme GetFileNamingScheme(string sourceFileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(sourceFileName) ?? string.Empty;
        int endIndex = baseName.Length;
        while (endIndex > 0 && char.IsDigit(baseName[endIndex - 1]))
        {
            endIndex--;
        }

        string prefix = baseName[..endIndex];
        string digits = baseName[endIndex..];
        int? seedIndex = digits.Length > 0
            ? int.Parse(digits, CultureInfo.InvariantCulture)
            : null;

        return new FileNamingScheme(prefix, digits.Length, seedIndex);
    }

    private static void EnsureCopyTargetDoesNotExist(string filePath)
    {
        if (File.Exists(filePath))
        {
            throw new InvalidOperationException(
                $"Cannot overwrite existing .elvx file: {filePath}. Remove the conflicting file or clean the folder first.");
        }
    }

    private static XDocument LoadXml(string xmlFilePath)
    {
        if (!File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException($"XML file not found: {xmlFilePath}", xmlFilePath);
        }

        return XDocument.Load(xmlFilePath);
    }

    private static void SaveXml(XDocument xmlDocument, string xmlFilePath)
    {
        xmlDocument.Save(xmlFilePath, SaveOptions.None);
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private sealed record FileNamingScheme(string Prefix, int DigitWidth, int? SeedIndex);
}

