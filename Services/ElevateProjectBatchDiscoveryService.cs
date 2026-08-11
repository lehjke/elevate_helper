using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateProjectBatchDiscoveryService
{
    private const string GeneratedCopiesManifestFileName = ".elevate-helper.generated-copies.txt";
    private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static readonly Dictionary<string, BuildingType> KnownBuildingTypeFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Office"] = BuildingType.Office,
        ["Res"] = BuildingType.Residence,
        ["Residence"] = BuildingType.Residence,
        ["Hotel"] = BuildingType.Hotel,
    };

    public ProjectBatchDiscoveryResult Discover(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root path is empty.", nameof(projectRoot));
        }

        string normalizedRoot = NormalizeDirectoryPath(projectRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException(normalizedRoot);
        }

        List<ProjectBatchJob> jobs = [];
        List<ProjectBatchWarning> warnings = [];
        HashSet<string> knownTypeDirectories = new(StringComparer.OrdinalIgnoreCase);

        foreach (string typeDirectory in Directory.EnumerateDirectories(normalizedRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string typeFolderName = Path.GetFileName(typeDirectory);
            if (!KnownBuildingTypeFolders.TryGetValue(typeFolderName, out BuildingType buildingType))
            {
                continue;
            }

            knownTypeDirectories.Add(NormalizeDirectoryPath(typeDirectory));
            AddBuildingTypeJobs(normalizedRoot, typeDirectory, typeFolderName, buildingType, jobs, warnings);
        }

        List<string> unclassifiedElvxFiles = Directory
            .EnumerateFiles(normalizedRoot, "*.elvx", RecursiveEnumerationOptions)
            .Where(file => !IsUnderAnyDirectory(file, knownTypeDirectories))
            .Where(file => !IsGeneratedOrScenarioFile(file, normalizedRoot))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .ToList();

        List<string> unknownElvxFiles = [];
        foreach (IGrouping<string, string> folderGroup in unclassifiedElvxFiles.GroupBy(
                     file => Path.GetDirectoryName(file) ?? normalizedRoot,
                     StringComparer.OrdinalIgnoreCase))
        {
            List<string> folderFiles = folderGroup
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string workingFolder = NormalizeDirectoryPath(folderGroup.Key);

            if (folderFiles.Count > 1)
            {
                warnings.Add(new ProjectBatchWarning(
                    workingFolder,
                    ProjectBatchWarningKind.FolderContainsMultipleSourceFiles,
                    GetRelativeGroupName(normalizedRoot, workingFolder)));
                continue;
            }

            string elvxPath = folderFiles[0];
            if (!TryReadBuildingType(elvxPath, out BuildingType buildingType))
            {
                unknownElvxFiles.Add(elvxPath);
                continue;
            }

            jobs.Add(CreateDiscoveredJob(normalizedRoot, workingFolder, elvxPath, buildingType));
        }

        return new ProjectBatchDiscoveryResult(jobs, unknownElvxFiles, warnings);
    }

    private static void AddBuildingTypeJobs(
        string projectRoot,
        string typeDirectory,
        string typeFolderName,
        BuildingType buildingType,
        List<ProjectBatchJob> jobs,
        List<ProjectBatchWarning> warnings)
    {
        IEnumerable<IGrouping<string, string>> folderGroups = Directory
            .EnumerateFiles(typeDirectory, "*.elvx", RecursiveEnumerationOptions)
            .Where(file => !IsGeneratedOrScenarioFile(file, projectRoot))
            .Select(Path.GetFullPath)
            .GroupBy(
                file => NormalizeDirectoryPath(Path.GetDirectoryName(file) ?? typeDirectory),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, string> folderGroup in folderGroups)
        {
            string workingFolder = folderGroup.Key;
            List<string> groupFiles = folderGroup
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string relativeGroupName = Path.GetRelativePath(typeDirectory, workingFolder);
            string groupName = relativeGroupName.Equals(".", StringComparison.Ordinal)
                ? typeFolderName
                : relativeGroupName;

            if (groupFiles.Count > 1)
            {
                warnings.Add(new ProjectBatchWarning(
                    workingFolder,
                    ProjectBatchWarningKind.GroupContainsMultipleSourceFiles,
                    $"{typeFolderName}/{groupName}"));
                continue;
            }

            BuildingType resolvedBuildingType = buildingType;
            string resolvedTypeFolderName = typeFolderName;
            if (TryReadBuildingType(groupFiles[0], out BuildingType fileBuildingType))
            {
                resolvedBuildingType = fileBuildingType;
                resolvedTypeFolderName = GetBuildingTypeFolderName(fileBuildingType);
                if (fileBuildingType != buildingType)
                {
                    warnings.Add(new ProjectBatchWarning(
                        groupFiles[0],
                        ProjectBatchWarningKind.BuildingTypeMismatch,
                        Path.GetFileName(groupFiles[0]),
                        ActualValue: resolvedTypeFolderName,
                        ExpectedValue: typeFolderName));
                }
            }
            else
            {
                warnings.Add(new ProjectBatchWarning(
                    groupFiles[0],
                    ProjectBatchWarningKind.BuildingTypeUnreadable,
                    Path.GetFileName(groupFiles[0]),
                    ExpectedValue: typeFolderName));
            }

            jobs.Add(new ProjectBatchJob(
                projectRoot,
                resolvedTypeFolderName,
                groupName,
                workingFolder,
                groupFiles[0],
                resolvedBuildingType,
                IsManualBuildingType: false));
        }
    }

    private static ProjectBatchJob CreateDiscoveredJob(
        string projectRoot,
        string workingFolder,
        string elvxPath,
        BuildingType buildingType)
    {
        return new ProjectBatchJob(
            projectRoot,
            GetBuildingTypeFolderName(buildingType),
            GetRelativeGroupName(projectRoot, workingFolder),
            workingFolder,
            elvxPath,
            buildingType,
            IsManualBuildingType: false);
    }

    private static string GetBuildingTypeFolderName(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Office => "Office",
            BuildingType.Residence => "Res",
            BuildingType.Hotel => "Hotel",
            _ => "Unknown",
        };
    }

    private static string GetRelativeGroupName(string projectRoot, string workingFolder)
    {
        string relativePath = Path.GetRelativePath(projectRoot, workingFolder);
        return relativePath.Equals(".", StringComparison.Ordinal)
            ? Path.GetFileName(projectRoot)
            : relativePath;
    }

    internal static bool TryReadBuildingType(string elvxPath, out BuildingType buildingType)
    {
        buildingType = default;

        try
        {
            XDocument document = XDocument.Load(elvxPath, LoadOptions.None);
            string? rawValue = (string?)document.Root?
                .Element("BuildingData")?
                .Attribute("BuildingType");

            switch (rawValue?.Trim())
            {
                case "1":
                    buildingType = BuildingType.Office;
                    return true;
                case "2":
                    buildingType = BuildingType.Hotel;
                    return true;
                case "3":
                    buildingType = BuildingType.Residence;
                    return true;
                case string value when value.Equals("Office", StringComparison.OrdinalIgnoreCase):
                    buildingType = BuildingType.Office;
                    return true;
                case string value when value.Equals("Hotel", StringComparison.OrdinalIgnoreCase):
                    buildingType = BuildingType.Hotel;
                    return true;
                case string value when value.Equals("Residential", StringComparison.OrdinalIgnoreCase) ||
                                       value.Equals("Residence", StringComparison.OrdinalIgnoreCase):
                    buildingType = BuildingType.Residence;
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    public static ProjectBatchJob CreateManualJob(string projectRoot, string elvxPath, BuildingType buildingType)
    {
        string normalizedProjectRoot = NormalizeDirectoryPath(projectRoot);
        string fullElvxPath = Path.GetFullPath(elvxPath);
        string workingFolder = Path.GetDirectoryName(fullElvxPath)
            ?? throw new InvalidOperationException($"Cannot resolve folder for {fullElvxPath}.");

        return new ProjectBatchJob(
            normalizedProjectRoot,
            "Manual",
            Path.GetFileName(workingFolder),
            NormalizeDirectoryPath(workingFolder),
            fullElvxPath,
            buildingType,
            IsManualBuildingType: true);
    }

    private static HashSet<string> LoadTrackedGeneratedCopies(string directory)
    {
        HashSet<string> generatedCopies = new(StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(directory, GeneratedCopiesManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return generatedCopies;
        }

        foreach (string line in File.ReadLines(manifestPath))
        {
            if (ElevateProcessingService.TryResolveTrackedGeneratedCopyPath(
                    directory,
                    line,
                    out string generatedCopyPath))
            {
                generatedCopies.Add(Path.GetFileName(generatedCopyPath));
            }
        }

        return generatedCopies;
    }

    private static bool IsGeneratedOrScenarioFile(string filePath, string projectRoot)
    {
        if (IsTrackedGeneratedCopy(filePath))
        {
            return true;
        }

        string normalizedRoot = NormalizeDirectoryPath(projectRoot);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrWhiteSpace(directory) && IsDirectoryInsideRoot(directory, normalizedRoot))
        {
            if (NormalizeDirectoryPath(directory).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (File.Exists(Path.Combine(directory, ElevateScenarioStateService.ManifestFileName)) ||
                IsLegacyScenarioDirectory(directory))
            {
                return true;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return false;
    }

    private static bool IsLegacyScenarioDirectory(string directory)
    {
        string folderName = Path.GetFileName(directory);
        if (!folderName.Equals("morning", StringComparison.OrdinalIgnoreCase) &&
            !folderName.Equals("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? parentDirectory = Directory.GetParent(directory)?.FullName;
        if (string.IsNullOrWhiteSpace(parentDirectory) ||
            !Directory.EnumerateFiles(parentDirectory, "*.elvx", SearchOption.TopDirectoryOnly).Any())
        {
            return false;
        }

        return File.Exists(Path.Combine(directory, "batch_results.csv")) ||
               File.Exists(Path.Combine(directory, "floor_area.csv")) ||
               Directory.EnumerateFiles(directory, "*_elvx.csv", SearchOption.TopDirectoryOnly).Any() ||
               Directory.EnumerateFiles(directory, "*.elvr", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool IsDirectoryInsideRoot(string directory, string projectRoot)
    {
        string normalizedDirectory = NormalizeDirectoryPath(directory);
        string relativePath = Path.GetRelativePath(projectRoot, normalizedDirectory);
        return !Path.IsPathRooted(relativePath) &&
               !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsTrackedGeneratedCopy(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        return LoadTrackedGeneratedCopies(directory).Contains(Path.GetFileName(filePath));
    }

    private static bool IsUnderAnyDirectory(string filePath, IEnumerable<string> directories)
    {
        string normalizedFilePath = Path.GetFullPath(filePath);
        return directories.Any(directory => IsUnderDirectory(normalizedFilePath, directory));
    }

    private static bool IsUnderDirectory(string filePath, string directory)
    {
        string relativePath = Path.GetRelativePath(directory, filePath);
        return !relativePath.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length <= root.Length
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public sealed record ProjectBatchDiscoveryResult(
    IReadOnlyList<ProjectBatchJob> Jobs,
    IReadOnlyList<string> UnknownElvxFiles,
    IReadOnlyList<ProjectBatchWarning> Warnings);

public sealed record ProjectBatchJob(
    string ProjectRoot,
    string BuildingTypeFolderName,
    string GroupName,
    string WorkingFolder,
    string ElvxPath,
    BuildingType BuildingType,
    bool IsManualBuildingType);

public sealed record ProjectBatchWarning(
    string Path,
    ProjectBatchWarningKind Kind,
    string Subject,
    string? ActualValue = null,
    string? ExpectedValue = null);

public enum ProjectBatchWarningKind
{
    FolderContainsMultipleSourceFiles,
    GroupContainsMultipleSourceFiles,
    BuildingTypeMismatch,
    BuildingTypeUnreadable,
}
