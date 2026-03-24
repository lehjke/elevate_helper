using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateProcessingService : IElevateProcessingService
{
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
            string message = $"An exception of type {ex.GetType().Name} occurred in makecopiesandrun().";
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
            string message = $"An exception of type {ex.GetType().Name} occurred in get_area().";
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
                ? " (обеденный пик)"
                : " (утренний пик)";
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

        string[] files = Directory.GetFiles(path);
        string? sourceFile = files
            .Where(file => file.EndsWith("01.elvx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .FirstOrDefault();

        if (sourceFile is null)
        {
            throw new FileNotFoundException($"No file ending with '01.elvx' found in '{path}'.");
        }

        string xmlFilePath = Path.Combine(path, sourceFile);
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
        List<string> files = GetElvxFiles(path);
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"No .elvx files found in '{path}'.");
        }

        string baseFileName = files[0];

        if (buildingType is BuildingType.Residence or BuildingType.Hotel)
        {
            ModifyBuildingTypeResidence(Path.Combine(path, baseFileName), buildingType);

            for (int i = 2; i <= copiesCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string newFileName = BuildCopyFileName(baseFileName, i);
                File.Copy(
                    Path.Combine(path, baseFileName),
                    Path.Combine(path, newFileName),
                    overwrite: true);

                files = GetElvxFiles(path);
                string fileForCapacity = files[i - 1];
                ModifyHandlingCapacity(Path.Combine(path, fileForCapacity), i);
            }

            await launcherService.LaunchResidenceAsync(path, morningProgress, cancellationToken);
            return;
        }

        if (buildingType != BuildingType.Office)
        {
            throw new InvalidOperationException($"Unknown building type: {buildingType}");
        }

        string morningPath = Path.Combine(path, "morning");
        Directory.CreateDirectory(morningPath);
        File.Copy(Path.Combine(path, baseFileName), Path.Combine(morningPath, baseFileName), overwrite: true);

        string lunchPath = string.Empty;
        if (includeLunchPeak)
        {
            lunchPath = Path.Combine(path, "lunch");
            Directory.CreateDirectory(lunchPath);
            File.Copy(Path.Combine(path, baseFileName), Path.Combine(lunchPath, baseFileName), overwrite: true);
            ModifyBuildingTypeOffice(Path.Combine(lunchPath, baseFileName), "Lunch");
            ModifyTitle(Path.Combine(lunchPath, baseFileName), "Lunch");
        }

        ModifyBuildingTypeOffice(Path.Combine(morningPath, baseFileName), "Morning");
        ModifyTitle(Path.Combine(morningPath, baseFileName), "Morning");

        for (int i = 2; i <= copiesCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string newFileName = BuildCopyFileName(baseFileName, i);

            File.Copy(
                Path.Combine(morningPath, baseFileName),
                Path.Combine(morningPath, newFileName),
                overwrite: true);

            if (includeLunchPeak)
            {
                File.Copy(
                    Path.Combine(lunchPath, baseFileName),
                    Path.Combine(lunchPath, newFileName),
                    overwrite: true);
            }

            List<string> morningEntries = GetDirectoryEntries(morningPath);
            ModifyHandlingCapacity(Path.Combine(morningPath, morningEntries[i - 1]), i);

            if (includeLunchPeak)
            {
                List<string> lunchEntries = GetDirectoryEntries(lunchPath);
                ModifyHandlingCapacity(Path.Combine(lunchPath, lunchEntries[i - 1]), i);
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
        int trimCount = copyIndex < 10 ? 6 : 7;
        if (sourceFileName.Length <= trimCount)
        {
            throw new InvalidOperationException($"Cannot build copy name from '{sourceFileName}'.");
        }

        string prefix = sourceFileName[..^trimCount];
        return $"{prefix}{copyIndex}.elvx";
    }

    private static List<string> GetElvxFiles(string path)
    {
        return Directory
            .GetFiles(path)
            .Where(file => file.EndsWith(".elvx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
    }

    private static List<string> GetDirectoryEntries(string path)
    {
        return Directory
            .GetFiles(path)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
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
}
