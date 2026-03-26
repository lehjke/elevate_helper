using System.Globalization;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class AppLocalizationService
{
    private static readonly string SettingsDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ElevateHelper");
    private static readonly string LanguageSettingsPath = Path.Combine(SettingsDirectoryPath, "language.txt");
    private static readonly AppTextCatalog English = new(
        WindowTitle: "Elevate Helper",
        AppTitle: "Elevate Helper",
        LanguageLabel: "Language",
        FolderTitle: "Elevate Folder",
        FolderHint: "Select the folder that contains the .elvx batch files. Reports are written next to the source files.",
        WorkingFolderHeader: "Working folder",
        WorkingFolderPlaceholder: @"C:\Elevate\ProjectA",
        BrowseButton: "Browse",
        BuildingTypeTitle: "Building Type",
        BuildingTypeHint: "Choose the building type before launching a run or printing a report.",
        BuildingTypeOffice: "Office",
        BuildingTypeResidence: "Residence",
        BuildingTypeHotel: "Hotel",
        ActionsTitle: "Actions",
        ActionsHint: "Run the batch, or print the report directly after the calculations complete.",
        RunButton: "Run",
        RunMorningButton: "Run Morning",
        ExitButton: "Exit",
        ReportButton: "Print Report",
        MorningReportButton: "Morning Report",
        LunchReportButton: "Lunch Report",
        QueueTitle: "Run Queue",
        Ready: "Ready",
        ActiveJobsFormat: "{0} active job(s)",
        NoActiveJobsTitle: "No active jobs",
        NoActiveJobsHint: "Start a batch run to see live progress here.",
        StatusTitle: "Checkup",
        QueuedStatus: "Queued",
        RunningStatus: "Running",
        CompletedStatus: "Completed",
        ProgressScenario: "Progress",
        MorningScenario: "Morning",
        LunchScenario: "Lunch",
        PathRequiredMessage: "Enter the path to the Elevate folder.",
        FolderMissingMessage: "The specified folder does not exist.",
        BuildingTypeRequiredMessage: "Select a building type.",
        OfficeMorningOnlyMessage: "Run Morning Only is available only for Office.",
        SelectedBuildingTypeFormat: "Selected building type: {0}.",
        RunStartedFormat: "{0} started.",
        RunCompletedFormat: "{0} completed successfully.",
        GeneratingReport: "Generating report...",
        GeneratingMorningReport: "Generating morning report...",
        GeneratingLunchReport: "Generating lunch report...",
        ReportGenerated: "Report generated successfully.",
        MorningReportGenerated: "Morning report generated successfully.",
        LunchReportGenerated: "Lunch report generated successfully.",
        OperationFailedMessage: "Operation failed.",
        ReportBusyMessage: "Report generation is already in progress.",
        IntegrationMissingLaunch: "Peters Research Elevate is not detected. Install Elevate or set ELEVATE_EXE_PATH.",
        IntegrationMissingCheck: "Elevate was not found. Check installation or define ELEVATE_EXE_PATH.",
        IntegrationFoundFormat: "Elevate found.{0} Path: {1}",
        IntegrationVersionFormat: " Version: {0}.",
        JobTitleFormat: "Job {0} - {1}",
        JobDetailsFormat: "{0} - {1}",
        JobModeMorningLunch: "morning + lunch",
        JobModeMorningOnly: "morning only",
        JobModeSingleScenario: "single scenario");

    private static readonly AppTextCatalog Russian = new(
        WindowTitle: "Elevate Helper",
        AppTitle: "Elevate Helper",
        LanguageLabel: "Язык",
        FolderTitle: "Папка Elevate",
        FolderHint: "Выберите папку, в которой находятся batch-файлы .elvx. Отчеты сохраняются рядом с исходными файлами.",
        WorkingFolderHeader: "Рабочая папка",
        WorkingFolderPlaceholder: @"C:\Elevate\Проект",
        BrowseButton: "Обзор",
        BuildingTypeTitle: "Тип здания",
        BuildingTypeHint: "Выберите тип здания перед запуском расчета или формированием отчета.",
        BuildingTypeOffice: "Офис",
        BuildingTypeResidence: "Жилье",
        BuildingTypeHotel: "Гостиница",
        ActionsTitle: "Действия",
        ActionsHint: "Запустите batch-расчет или сформируйте отчет после завершения вычислений.",
        RunButton: "Запуск",
        RunMorningButton: "Утренний пик",
        ExitButton: "Выход",
        ReportButton: "Печать отчета",
        MorningReportButton: "Утренний отчет",
        LunchReportButton: "Обеденный отчет",
        QueueTitle: "Очередь задач",
        Ready: "Готово",
        ActiveJobsFormat: "Активных задач: {0}",
        NoActiveJobsTitle: "Нет активных задач",
        NoActiveJobsHint: "Запустите расчет, чтобы увидеть прогресс здесь.",
        StatusTitle: "Проверка",
        QueuedStatus: "В очереди",
        RunningStatus: "Выполняется",
        CompletedStatus: "Завершено",
        ProgressScenario: "Прогресс",
        MorningScenario: "Утро",
        LunchScenario: "Обед",
        PathRequiredMessage: "Укажите путь к папке Elevate.",
        FolderMissingMessage: "Указанная папка не существует.",
        BuildingTypeRequiredMessage: "Выберите тип здания.",
        OfficeMorningOnlyMessage: "Отдельный запуск утреннего пика доступен только для Office.",
        SelectedBuildingTypeFormat: "Выбран тип здания: {0}.",
        RunStartedFormat: "{0} запущена.",
        RunCompletedFormat: "{0} завершена успешно.",
        GeneratingReport: "Формирование отчета...",
        GeneratingMorningReport: "Формирование утреннего отчета...",
        GeneratingLunchReport: "Формирование обеденного отчета...",
        ReportGenerated: "Отчет успешно сформирован.",
        MorningReportGenerated: "Утренний отчет успешно сформирован.",
        LunchReportGenerated: "Обеденный отчет успешно сформирован.",
        OperationFailedMessage: "Операция завершилась ошибкой.",
        ReportBusyMessage: "Формирование отчета уже выполняется.",
        IntegrationMissingLaunch: "Peters Research Elevate не найден. Установите Elevate или задайте ELEVATE_EXE_PATH.",
        IntegrationMissingCheck: "Elevate не найден. Проверьте установку или задайте ELEVATE_EXE_PATH.",
        IntegrationFoundFormat: "Elevate найден.{0} Путь: {1}",
        IntegrationVersionFormat: " Версия: {0}.",
        JobTitleFormat: "Задача {0} - {1}",
        JobDetailsFormat: "{0} - {1}",
        JobModeMorningLunch: "утренний + обеденный пик",
        JobModeMorningOnly: "только утренний пик",
        JobModeSingleScenario: "один сценарий");

    public static AppLocalizationService Instance { get; } = new();

    private readonly bool persistSelection;
    private AppLanguage currentLanguage;

    public AppLocalizationService()
        : this(persistSelection: true)
    {
    }

    internal AppLocalizationService(bool persistSelection)
    {
        this.persistSelection = persistSelection;
        currentLanguage = LoadInitialLanguage();
    }

    public event EventHandler? LanguageChanged;

    public AppLanguage CurrentLanguage => currentLanguage;

    public AppTextCatalog CurrentText => currentLanguage == AppLanguage.Russian ? Russian : English;

    public void SetLanguage(AppLanguage language)
    {
        if (currentLanguage == language)
        {
            return;
        }

        currentLanguage = language;
        PersistLanguage(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string FormatBuildingType(BuildingType buildingType)
    {
        AppTextCatalog text = CurrentText;
        return buildingType switch
        {
            BuildingType.Office => text.BuildingTypeOffice,
            BuildingType.Residence => text.BuildingTypeResidence,
            BuildingType.Hotel => text.BuildingTypeHotel,
            _ => buildingType.ToString(),
        };
    }

    public string FormatJobTitle(int jobId, BuildingType buildingType)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            CurrentText.JobTitleFormat,
            jobId,
            FormatBuildingType(buildingType));
    }

    public string FormatJobDetails(string path, BuildingType buildingType, bool includeLunchPeak)
    {
        if (buildingType != BuildingType.Office)
        {
            return path;
        }

        string mode = buildingType == BuildingType.Office
            ? includeLunchPeak
                ? CurrentText.JobModeMorningLunch
                : CurrentText.JobModeMorningOnly
            : CurrentText.JobModeSingleScenario;

        return string.Format(CultureInfo.CurrentCulture, CurrentText.JobDetailsFormat, path, mode);
    }

    public string FormatSelectedBuildingType(BuildingType buildingType)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            CurrentText.SelectedBuildingTypeFormat,
            FormatBuildingType(buildingType));
    }

    public string FormatRunStarted(string jobTitle)
    {
        return string.Format(CultureInfo.CurrentCulture, CurrentText.RunStartedFormat, jobTitle);
    }

    public string FormatRunCompleted(string jobTitle)
    {
        return string.Format(CultureInfo.CurrentCulture, CurrentText.RunCompletedFormat, jobTitle);
    }

    public string GetQueueSummary(int activeJobs)
    {
        return activeJobs > 0
            ? string.Format(CultureInfo.CurrentCulture, CurrentText.ActiveJobsFormat, activeJobs)
            : CurrentText.Ready;
    }

    public string GetScenarioLabel(JobScenarioKind scenarioKind)
    {
        AppTextCatalog text = CurrentText;
        return scenarioKind switch
        {
            JobScenarioKind.Morning => text.MorningScenario,
            JobScenarioKind.Lunch => text.LunchScenario,
            _ => text.ProgressScenario,
        };
    }

    public string GetJobStateLabel(JobStateKind stateKind)
    {
        AppTextCatalog text = CurrentText;
        return stateKind switch
        {
            JobStateKind.Running => text.RunningStatus,
            JobStateKind.Completed => text.CompletedStatus,
            _ => text.QueuedStatus,
        };
    }

    public string TranslateRuntimeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || CurrentLanguage != AppLanguage.Russian)
        {
            return message ?? string.Empty;
        }

        if (message.Equals("Path to Elevate files is empty.", StringComparison.Ordinal))
        {
            return "Путь к файлам Elevate не указан.";
        }

        if (message.StartsWith("Path does not exist: ", StringComparison.Ordinal))
        {
            return "Путь не существует: " + message["Path does not exist: ".Length..];
        }

        if (message.Equals("Copy count must be >= 1.", StringComparison.Ordinal))
        {
            return "Количество копий должно быть не меньше 1.";
        }

        if (message.Equals("OK!", StringComparison.Ordinal))
        {
            return "Готово.";
        }

        if (message.Equals("Unable to start Elevate.exe.", StringComparison.Ordinal))
        {
            return "Не удалось запустить Elevate.exe.";
        }

        if (message.StartsWith("No .elvx files found in '", StringComparison.Ordinal) && message.EndsWith("'.", StringComparison.Ordinal))
        {
            return "В папке не найдены файлы .elvx: " + message["No .elvx files found in '".Length..^2];
        }

        if (message.Equals("Elevate main window did not appear.", StringComparison.Ordinal))
        {
            return "Главное окно Elevate не появилось.";
        }

        if (message.Equals("Unable to open the Elevate Run Batch dialog.", StringComparison.Ordinal))
        {
            return "Не удалось открыть диалог Elevate Run Batch.";
        }

        if (message.Equals("Run Batch folder input was not found.", StringComparison.Ordinal))
        {
            return "В диалоге Run Batch не найдено поле ввода папки.";
        }

        if (message.Equals("Run Batch confirmation button was not found.", StringComparison.Ordinal))
        {
            return "В диалоге Run Batch не найдена кнопка подтверждения.";
        }

        if (message.Equals("Run Batch dialog did not open.", StringComparison.Ordinal))
        {
            return "Диалог Run Batch не открылся.";
        }

        if (message.Equals("Run Batch dialog did not close after folder submission.", StringComparison.Ordinal))
        {
            return "Диалог Run Batch не закрылся после отправки папки.";
        }

        if (message.Equals("Elevate exited before batch processing started.", StringComparison.Ordinal))
        {
            return "Elevate завершился до начала batch-расчета.";
        }

        if (message.Equals("Run Batch was submitted but calculation did not start.", StringComparison.Ordinal))
        {
            return "Папка была отправлена в Run Batch, но расчет не стартовал.";
        }

        if (message.Equals("Elevate exited before batch processing completed.", StringComparison.Ordinal))
        {
            return "Elevate завершился до окончания batch-расчета.";
        }

        if (message.StartsWith("An exception of type ", StringComparison.Ordinal) &&
            message.Contains(" occurred in makecopiesandrun().", StringComparison.Ordinal))
        {
            string suffix = message["An exception of type ".Length..];
            int markerIndex = suffix.IndexOf(" occurred in makecopiesandrun().", StringComparison.Ordinal);
            string typeName = markerIndex >= 0 ? suffix[..markerIndex] : suffix;
            string tail = markerIndex >= 0
                ? suffix[(markerIndex + " occurred in makecopiesandrun().".Length)..].Trim()
                : string.Empty;
            return string.IsNullOrWhiteSpace(tail)
                ? $"Во время подготовки и запуска batch-расчета возникло исключение типа {typeName}."
                : $"Во время подготовки и запуска batch-расчета возникло исключение типа {typeName}. {TranslateRuntimeMessage(tail)}";
        }

        if (message.StartsWith("An exception of type ", StringComparison.Ordinal) &&
            message.Contains(" occurred in get_area().", StringComparison.Ordinal))
        {
            string suffix = message["An exception of type ".Length..];
            int markerIndex = suffix.IndexOf(" occurred in get_area().", StringComparison.Ordinal);
            string typeName = markerIndex >= 0 ? suffix[..markerIndex] : suffix;
            string tail = markerIndex >= 0
                ? suffix[(markerIndex + " occurred in get_area().".Length)..].Trim()
                : string.Empty;
            return string.IsNullOrWhiteSpace(tail)
                ? $"Во время формирования floor_area.csv возникло исключение типа {typeName}."
                : $"Во время формирования floor_area.csv возникло исключение типа {typeName}. {TranslateRuntimeMessage(tail)}";
        }

        if (message.StartsWith("No file ending with '01.elvx' found in '", StringComparison.Ordinal) &&
            message.EndsWith("'.", StringComparison.Ordinal))
        {
            return "В папке не найден файл, оканчивающийся на '01.elvx': " +
                   message["No file ending with '01.elvx' found in '".Length..^2];
        }

        if (message.StartsWith("Unknown building type: ", StringComparison.Ordinal))
        {
            return "Неизвестный тип здания: " + message["Unknown building type: ".Length..];
        }

        if (message.StartsWith("Cannot determine the base .elvx file in '", StringComparison.Ordinal))
        {
            return "Не удалось определить базовый .elvx-файл. Оставьте в корневой папке только исходный файл проекта или файл с индексом 1.";
        }

        if (message.StartsWith("Cannot overwrite existing .elvx file: ", StringComparison.Ordinal))
        {
            return "Нельзя перезаписать существующий .elvx-файл: " + message["Cannot overwrite existing .elvx file: ".Length..];
        }

        if (message.Equals("Path is empty.", StringComparison.Ordinal))
        {
            return "Путь не указан.";
        }

        if (message.StartsWith("batch_results.csv not found: ", StringComparison.Ordinal))
        {
            return "Файл batch_results.csv не найден: " + message["batch_results.csv not found: ".Length..];
        }

        if (message.Equals("Cannot find repository root containing .example folder.", StringComparison.Ordinal))
        {
            return "Не удалось найти корень репозитория с папкой .example.";
        }

        if (message.StartsWith("Template not found: ", StringComparison.Ordinal))
        {
            return "Шаблон не найден: " + message["Template not found: ".Length..];
        }

        if (message.Equals("batch_results.csv does not contain valid project file name (A2).", StringComparison.Ordinal))
        {
            return "В batch_results.csv отсутствует корректное имя project-файла в A2.";
        }

        if (message.StartsWith("Project source not found: ", StringComparison.Ordinal))
        {
            return "Не найден исходный файл проекта для отчета: " + message["Project source not found: ".Length..];
        }

        if (message.Equals("No data rows found in batch_results.csv.", StringComparison.Ordinal))
        {
            return "В batch_results.csv не найдены строки с данными.";
        }

        if (message.Equals("Report generation was canceled.", StringComparison.Ordinal))
        {
            return "Формирование отчета отменено.";
        }

        if (message.Equals("An exception occurred while generating the report without VBA macro.", StringComparison.Ordinal))
        {
            return "При формировании отчета без VBA-макроса произошла ошибка.";
        }

        return message;
    }

    private static AppLanguage GetDefaultLanguage()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;
    }

    private AppLanguage LoadInitialLanguage()
    {
        if (!persistSelection)
        {
            return GetDefaultLanguage();
        }

        try
        {
            if (File.Exists(LanguageSettingsPath))
            {
                string persistedValue = File.ReadAllText(LanguageSettingsPath).Trim();
                if (Enum.TryParse(persistedValue, ignoreCase: true, out AppLanguage persistedLanguage))
                {
                    return persistedLanguage;
                }
            }
        }
        catch
        {
            // Fall back to the OS language if the settings file cannot be read.
        }

        return GetDefaultLanguage();
    }

    private void PersistLanguage(AppLanguage language)
    {
        if (!persistSelection)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
            File.WriteAllText(LanguageSettingsPath, language.ToString());
        }
        catch
        {
            // Ignore persistence failures and keep the in-memory language.
        }
    }

    public sealed record AppTextCatalog(
        string WindowTitle,
        string AppTitle,
        string LanguageLabel,
        string FolderTitle,
        string FolderHint,
        string WorkingFolderHeader,
        string WorkingFolderPlaceholder,
        string BrowseButton,
        string BuildingTypeTitle,
        string BuildingTypeHint,
        string BuildingTypeOffice,
        string BuildingTypeResidence,
        string BuildingTypeHotel,
        string ActionsTitle,
        string ActionsHint,
        string RunButton,
        string RunMorningButton,
        string ExitButton,
        string ReportButton,
        string MorningReportButton,
        string LunchReportButton,
        string QueueTitle,
        string Ready,
        string ActiveJobsFormat,
        string NoActiveJobsTitle,
        string NoActiveJobsHint,
        string StatusTitle,
        string QueuedStatus,
        string RunningStatus,
        string CompletedStatus,
        string ProgressScenario,
        string MorningScenario,
        string LunchScenario,
        string PathRequiredMessage,
        string FolderMissingMessage,
        string BuildingTypeRequiredMessage,
        string OfficeMorningOnlyMessage,
        string SelectedBuildingTypeFormat,
        string RunStartedFormat,
        string RunCompletedFormat,
        string GeneratingReport,
        string GeneratingMorningReport,
        string GeneratingLunchReport,
        string ReportGenerated,
        string MorningReportGenerated,
        string LunchReportGenerated,
        string OperationFailedMessage,
        string ReportBusyMessage,
        string IntegrationMissingLaunch,
        string IntegrationMissingCheck,
        string IntegrationFoundFormat,
        string IntegrationVersionFormat,
        string JobTitleFormat,
        string JobDetailsFormat,
        string JobModeMorningLunch,
        string JobModeMorningOnly,
        string JobModeSingleScenario);
}

public enum JobScenarioKind
{
    Progress,
    Morning,
    Lunch,
}

public enum JobStateKind
{
    Queued,
    Running,
    Completed,
}

