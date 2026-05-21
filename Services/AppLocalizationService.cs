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
        FolderHint: "Select the folder that contains the .elvx batch files. Reports are written to the selected project folder.",
        WorkingFolderHeader: "Working folder",
        WorkingFolderPlaceholder: @"C:\Elevate\ProjectA",
        BrowseButton: "Browse",
        BuildingTypeTitle: "Building Type",
        BuildingTypeHint: "Choose the building type before launching a run or printing a report.",
        BuildingTypeOffice: "Office",
        BuildingTypeResidence: "Residence",
        BuildingTypeHotel: "Hotel",
        OpenEditorWindowButton: "Open Editor",
        EditorWindowHint: "Open the ELVX editor in a separate window. It uses the current working folder and building type from the main screen.",
        ProjectBatchTitle: "Project batch mode",
        ProjectBatchHint: "Select a project root with Office, Res, and Hotel folders. Each group folder must contain one .elvx file.",
        ProjectBatchPathHeader: "Project root",
        ProjectBatchPathPlaceholder: @"C:\Elevate\Project",
        ProjectBatchParallelRunsHeader: "Parallel runs",
        ProjectBatchUnlimitedRuns: "No limit",
        ProjectBatchRunButton: "Run project",
        ProjectBatchNoJobsMessage: "No valid project folders were found.",
        ProjectBatchStartedFormat: "Project batch started: {0} job(s).",
        ProjectBatchWarningsFormat: "{0} project scan warning(s).",
        ProjectBatchUnknownTitle: "Select building types",
        ProjectBatchUnknownHint: "These .elvx files are outside Office, Res, or Hotel. Select a building type to include them in the run.",
        ProjectBatchUnknownPrimaryButton: "Include selected",
        ProjectBatchUnknownSecondaryButton: "Skip",
        ProjectBatchUnknownCloseButton: "Cancel",
        ProjectBatchRetryButton: "Retry",
        StopJobButton: "Stop",
        DismissJobButton: "Remove from queue",
        ProjectBatchGeneratingReports: "Generating job reports...",
        ProjectBatchPreviewTitle: "Review project batch",
        ProjectBatchPreviewBuildingTypeHeader: "Building type",
        ProjectBatchPreviewPathHeader: "Path",
        ProjectBatchPreviewFileCountHeader: "Files",
        ProjectBatchPreviewScenariosHeader: "Scenarios",
        ProjectBatchPreviewMorningLunch: "morning + lunch",
        ProjectBatchPreviewSingleScenario: "single",
        ProjectBatchPreviewStartButton: "Start",
        ProjectBatchPreviewCancelButton: "Cancel",
        ProjectBatchPreviewWarningsTitle: "Skipped items",
        ProjectBatchPreviewWarningsFormat: "{0} skipped item(s) need attention.",
        EditorTitle: "ELVX Editor",
        EditorHint: "Load an existing .elvx from the working folder or start from the built-in template. This editor keeps Elevate topology intact and lets you tune project, analysis, traffic, and existing lift parameters before the batch run.",
        EditorWorkingFolderLabel: "Working folder",
        EditorBuildingTypeLabel: "Building type",
        EditorProjectTabTitle: "Project",
        EditorAnalysisTabTitle: "Analysis",
        EditorBuildingTabTitle: "Building",
        EditorLiftGroupTabTitle: "Lift Group",
        EditorAnalysisHint: "Traffic mode stays fixed from the loaded template or source ELVX and is not edited here.",
        EditorBuildingHint: "The building table mirrors Elevate: floor name, floor-to-floor height, population, and entrance flag.",
        EditorLiftGroupHint: "One common parameter set is applied to every lift in the current group.",
        LoadEditorButton: "Load Existing",
        LoadEditorTemplateButton: "Load Template",
        SaveEditorButton: "Save ELVX",
        EditorCloseButton: "Close",
        EditorSourceLabel: "Source",
        EditorOutputLabel: "Output",
        EditorProjectSectionTitle: "Project",
        EditorAnalysisSectionTitle: "Analysis",
        EditorBuildingSectionTitle: "Building",
        EditorTrafficSectionTitle: "Traffic",
        EditorLiftGroupSectionTitle: "Lift Group",
        EditorJobTitleHeader: "Project title",
        EditorJobNoHeader: "Job number",
        EditorCalculationTitleHeader: "Location / calculation title",
        EditorMadeByHeader: "Made by",
        EditorCheckedByHeader: "Checked by",
        EditorCompanyHeader: "Company",
        EditorDispatcherHeader: "Dispatcher algorithm",
        EditorTrafficModeHeader: "Traffic mode",
        EditorSimulationsHeader: "Simulations per configuration",
        EditorLearningRunsHeader: "Learning runs",
        EditorRandomSeedHeader: "Random seed",
        EditorAbsenteeismHeader: "Absenteeism, %",
        EditorFloorNameColumn: "Floor",
        EditorInterfloorHeightColumn: "Interfloor, m",
        EditorPopulationColumn: "Population",
        EditorEntranceColumn: "Entrance",
        EditorAddFloorAboveButton: "Add above",
        EditorAddFloorBelowButton: "Add below",
        EditorSortTopFirstButton: "Top first",
        EditorSortBottomFirstButton: "Bottom first",
        EditorLiftCountLabel: "Lift count",
        EditorAddLiftButton: "Add lift",
        EditorRemoveLiftButton: "Remove lift",
        EditorCapacityHeader: "Capacity, kg",
        EditorCabWidthHeader: "Cab width, mm",
        EditorCabHeightHeader: "Cab height, mm",
        EditorCabAreaHeader: "Cab area, m²",
        EditorSpeedHeader: "Speed, m/s",
        EditorAccelerationHeader: "Acceleration, m/s²",
        EditorJerkHeader: "Jerk, m/s³",
        EditorDoorWidthHeader: "Door width, mm",
        EditorDoorOpeningHeader: "Door opening type",
        EditorDoorOpeningCentral: "Central opening",
        EditorDoorOpeningTelescopic: "Telescopic",
        EditorDoorOpenHeader: "Door opening, s",
        EditorDoorCloseHeader: "Door closing, s",
        EditorIncomingHeader: "Incoming, %",
        EditorOutgoingHeader: "Outgoing, %",
        EditorInterfloorHeader: "Interfloor, %",
        EditorHandlingCapacityHeader: "Handling capacity, %",
        EditorLoadingTimeHeader: "Loading time, s",
        EditorUnloadingTimeHeader: "Unloading time, s",
        EditorFloorsHint: "The current slice edits existing floors only. Floor names and count stay fixed to keep linked Elevate sections consistent.",
        EditorFloorLevelLabel: "Level",
        EditorFloorPopulationLabel: "Population",
        EditorFloorEntranceLabel: "Entrance",
        EditorCarsHint: "The current slice edits existing cars only. Car count and IDs stay fixed to preserve Elevate topology.",
        EditorCarCapacityLabel: "Capacity, kg",
        EditorCarAreaLabel: "Floor area, m²",
        EditorCarSpeedLabel: "Speed, m/s",
        EditorCarAccelerationLabel: "Acceleration, m/s²",
        EditorCarJerkLabel: "Jerk, m/s³",
        EditorCarPreOpeningLabel: "Door pre-opening, s",
        EditorCarOpenTimeLabel: "Door opening, s",
        EditorCarCloseTimeLabel: "Door closing, s",
        EditorCarHomeFloorLabel: "Home floor",
        EditorLoadSuccessFormat: "ELVX loaded: {0}",
        EditorSaveSuccessFormat: "ELVX saved: {0}",
        EditorNotLoadedMessage: "Load an ELVX file or template before saving.",
        EditorExistingFileMissingMessage: "No .elvx files were found in the working folder.",
        EditorInvalidNumberFormat: "Invalid numeric value in \"{0}\".",
        EditorTrafficSplitTotalMessage: "Incoming, outgoing, and interfloor traffic must sum to 100%.",
        ActionsTitle: "Actions",
        ActionsHint: "Run the batch, or print the report directly after the calculations complete.",
        RunButton: "Run",
        RunMorningButton: "Run Morning",
        ExitButton: "Exit",
        ReportButton: "Print Report",
        PrintReportsButton: "Print Reports",
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
        StoppingStatus: "Stopping...",
        CompletedStatus: "Completed",
        StoppedStatus: "Stopped early",
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
        RunStoppedFormat: "{0} stopped early. You can print a report from completed Elevate results.",
        GeneratingReport: "Generating report...",
        GeneratingReports: "Generating reports...",
        GeneratingMorningReport: "Generating morning report...",
        GeneratingLunchReport: "Generating lunch report...",
        ReportGenerated: "Report generated successfully.",
        ReportsGenerated: "Reports generated successfully.",
        MorningReportGenerated: "Morning report generated successfully.",
        LunchReportGenerated: "Lunch report generated successfully.",
        OperationFailedMessage: "Operation failed.",
        ReportBusyMessage: "Report generation is already in progress.",
        StoppedRunNoResultsMessage: "No completed Elevate results were found for this job yet.",
        RunFolderBusyMessage: "A batch run is already active for this folder: {0}",
        IntegrationMissingLaunch: "Peters Research Elevate is not detected. Install Elevate or set ELEVATE_EXE_PATH.",
        IntegrationMissingCheck: "Elevate was not found. Check installation or define ELEVATE_EXE_PATH.",
        IntegrationFoundFormat: "Elevate found.{0} Path: {1}",
        IntegrationVersionFormat: " Version: {0}.",
        JobTitleFormat: "Job {0} - {1}",
        JobDetailsFormat: "{0} - {1}",
        JobModeMorningLunch: "morning + lunch",
        JobModeMorningOnly: "morning only",
        JobModeSingleScenario: "single scenario",
        UpdateAvailableTitle: "Update available",
        UpdateAvailableMessageFormat: "Installed version: {0}\nLatest version: {1}\n\nDo you want to download and install the update now?",
        UpdateInstallButton: "Update",
        UpdateLaterButton: "Later",
        UpdateDownloadingStatus: "Downloading the update...",
        UpdateStartedStatus: "The update installer has started. Elevate Helper will close now.");

    private static readonly AppTextCatalog Russian = new(
        WindowTitle: "Elevate Helper",
        AppTitle: "Elevate Helper",
        LanguageLabel: "Язык",
        FolderTitle: "Папка Elevate",
        FolderHint: "Выберите папку, в которой находятся batch-файлы .elvx. Отчеты сохраняются в выбранную папку проекта.",
        WorkingFolderHeader: "Рабочая папка",
        WorkingFolderPlaceholder: @"C:\Elevate\Проект",
        BrowseButton: "Обзор",
        BuildingTypeTitle: "Тип здания",
        BuildingTypeHint: "Выберите тип здания перед запуском расчета или формированием отчета.",
        BuildingTypeOffice: "Офис",
        BuildingTypeResidence: "Жилье",
        BuildingTypeHotel: "Гостиница",
        OpenEditorWindowButton: "Открыть редактор",
        EditorWindowHint: "Откройте редактор ELVX в отдельном окне. Он использует текущую рабочую папку и тип здания с главного экрана.",
        ProjectBatchTitle: "Пакетный запуск проекта",
        ProjectBatchHint: "Выберите корень проекта с папками Office, Res и Hotel. В каждой папке группы должен быть один .elvx-файл.",
        ProjectBatchPathHeader: "Корень проекта",
        ProjectBatchPathPlaceholder: @"C:\Elevate\Проект",
        ProjectBatchParallelRunsHeader: "Параллельных расчетов",
        ProjectBatchUnlimitedRuns: "Без ограничений",
        ProjectBatchRunButton: "Запустить проект",
        ProjectBatchNoJobsMessage: "Не найдено подходящих папок проекта.",
        ProjectBatchStartedFormat: "Пакетный запуск проекта начат: {0} задач.",
        ProjectBatchWarningsFormat: "Предупреждений при сканировании проекта: {0}.",
        ProjectBatchUnknownTitle: "Выбор типов здания",
        ProjectBatchUnknownHint: "Эти .elvx-файлы находятся вне Office, Res или Hotel. Выберите тип здания, чтобы включить их в запуск.",
        ProjectBatchUnknownPrimaryButton: "Включить выбранные",
        ProjectBatchUnknownSecondaryButton: "Пропустить",
        ProjectBatchUnknownCloseButton: "Отмена",
        ProjectBatchRetryButton: "Повторить",
        StopJobButton: "Остановить",
        DismissJobButton: "Убрать из очереди",
        ProjectBatchGeneratingReports: "Формирование отчетов задачи...",
        ProjectBatchPreviewTitle: "Проверка пакетного запуска",
        ProjectBatchPreviewBuildingTypeHeader: "Тип здания",
        ProjectBatchPreviewPathHeader: "Путь",
        ProjectBatchPreviewFileCountHeader: "Файлы",
        ProjectBatchPreviewScenariosHeader: "Сценарии",
        ProjectBatchPreviewMorningLunch: "утро + обед",
        ProjectBatchPreviewSingleScenario: "один",
        ProjectBatchPreviewStartButton: "Запустить",
        ProjectBatchPreviewCancelButton: "Отмена",
        ProjectBatchPreviewWarningsTitle: "Пропущенные элементы",
        ProjectBatchPreviewWarningsFormat: "Пропущено или требует внимания: {0}.",
        EditorTitle: "Редактор ELVX",
        EditorHint: "Загрузите существующий .elvx из рабочей папки или стартуйте с встроенного шаблона. Редактор сохраняет топологию Elevate и позволяет настраивать проект, анализ, трафик и существующую лифтовую группу до batch-расчета.",
        EditorWorkingFolderLabel: "Рабочая папка",
        EditorBuildingTypeLabel: "Тип здания",
        EditorProjectTabTitle: "Проект",
        EditorAnalysisTabTitle: "Анализ",
        EditorBuildingTabTitle: "Здание",
        EditorLiftGroupTabTitle: "Лифтовая группа",
        EditorAnalysisHint: "Режим трафика остается фиксированным из загруженного шаблона или исходного ELVX и здесь не редактируется.",
        EditorBuildingHint: "Таблица здания повторяет Elevate: название этажа, межэтажное расстояние, население и признак входного этажа.",
        EditorLiftGroupHint: "Один общий набор параметров применяется ко всем лифтам текущей группы.",
        LoadEditorButton: "Загрузить существующий",
        LoadEditorTemplateButton: "Загрузить шаблон",
        SaveEditorButton: "Сохранить ELVX",
        EditorCloseButton: "Закрыть",
        EditorSourceLabel: "Источник",
        EditorOutputLabel: "Выходной файл",
        EditorProjectSectionTitle: "Проект",
        EditorAnalysisSectionTitle: "Анализ",
        EditorBuildingSectionTitle: "Здание",
        EditorTrafficSectionTitle: "Трафик",
        EditorLiftGroupSectionTitle: "Лифтовая группа",
        EditorJobTitleHeader: "Название проекта",
        EditorJobNoHeader: "Номер расчета",
        EditorCalculationTitleHeader: "Локация / заголовок расчета",
        EditorMadeByHeader: "Исполнитель",
        EditorCheckedByHeader: "Проверил",
        EditorCompanyHeader: "Компания",
        EditorDispatcherHeader: "Алгоритм диспетчеризации",
        EditorTrafficModeHeader: "Режим трафика",
        EditorSimulationsHeader: "Число симуляций на конфигурацию",
        EditorLearningRunsHeader: "Обучающие прогоны",
        EditorRandomSeedHeader: "Seed случайных чисел",
        EditorAbsenteeismHeader: "Абсентеизм, %",
        EditorFloorNameColumn: "Этаж",
        EditorInterfloorHeightColumn: "Межэтажное, м",
        EditorPopulationColumn: "Население",
        EditorEntranceColumn: "Входной",
        EditorAddFloorAboveButton: "Добавить сверху",
        EditorAddFloorBelowButton: "Добавить снизу",
        EditorSortTopFirstButton: "Сначала верхние",
        EditorSortBottomFirstButton: "Сначала нижние",
        EditorLiftCountLabel: "Количество лифтов",
        EditorAddLiftButton: "Добавить лифт",
        EditorRemoveLiftButton: "Удалить лифт",
        EditorCapacityHeader: "Грузоподъемность, кг",
        EditorCabWidthHeader: "Ширина кабины, мм",
        EditorCabHeightHeader: "Глубина кабины, мм",
        EditorCabAreaHeader: "Площадь кабины, м²",
        EditorSpeedHeader: "Скорость, м/с",
        EditorAccelerationHeader: "Ускорение, м/с²",
        EditorJerkHeader: "Рывок, м/с³",
        EditorDoorWidthHeader: "Ширина дверей, мм",
        EditorDoorOpeningHeader: "Тип открывания",
        EditorDoorOpeningCentral: "Центральное",
        EditorDoorOpeningTelescopic: "Телескопическое",
        EditorDoorOpenHeader: "Открывание дверей, с",
        EditorDoorCloseHeader: "Закрывание дверей, с",
        EditorIncomingHeader: "Входящий поток, %",
        EditorOutgoingHeader: "Исходящий поток, %",
        EditorInterfloorHeader: "Межэтажный поток, %",
        EditorHandlingCapacityHeader: "Провозная способность, %",
        EditorLoadingTimeHeader: "Время загрузки, с",
        EditorUnloadingTimeHeader: "Время выгрузки, с",
        EditorFloorsHint: "В текущем срезе редактируются только существующие этажи. Имена этажей и их количество пока фиксированы, чтобы не ломать связанные разделы Elevate.",
        EditorFloorLevelLabel: "Отметка",
        EditorFloorPopulationLabel: "Население",
        EditorFloorEntranceLabel: "Входной этаж",
        EditorCarsHint: "В текущем срезе редактируются только существующие кабины. Количество лифтов и их идентификаторы остаются фиксированными, чтобы не ломать топологию Elevate.",
        EditorCarCapacityLabel: "Грузоподъемность, кг",
        EditorCarAreaLabel: "Площадь кабины, м²",
        EditorCarSpeedLabel: "Скорость, м/с",
        EditorCarAccelerationLabel: "Ускорение, м/с²",
        EditorCarJerkLabel: "Рывок, м/с³",
        EditorCarPreOpeningLabel: "Предоткрывание дверей, с",
        EditorCarOpenTimeLabel: "Открывание дверей, с",
        EditorCarCloseTimeLabel: "Закрывание дверей, с",
        EditorCarHomeFloorLabel: "Домашний этаж",
        EditorLoadSuccessFormat: "ELVX загружен: {0}",
        EditorSaveSuccessFormat: "ELVX сохранен: {0}",
        EditorNotLoadedMessage: "Сначала загрузите ELVX-файл или шаблон.",
        EditorExistingFileMissingMessage: "В рабочей папке не найдено ни одного .elvx-файла.",
        EditorInvalidNumberFormat: "Некорректное числовое значение в поле \"{0}\".",
        EditorTrafficSplitTotalMessage: "Сумма входящего, исходящего и межэтажного потоков должна быть равна 100%.",
        ActionsTitle: "Действия",
        ActionsHint: "Запустите batch-расчет или сформируйте отчет после завершения вычислений.",
        RunButton: "Запуск",
        RunMorningButton: "Утренний пик",
        ExitButton: "Выход",
        ReportButton: "Печать отчета",
        PrintReportsButton: "Печать отчетов",
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
        StoppingStatus: "Остановка...",
        CompletedStatus: "Завершено",
        StoppedStatus: "Остановлено досрочно",
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
        RunStoppedFormat: "{0} остановлена досрочно. Можно сформировать отчет по уже рассчитанным данным Elevate.",
        GeneratingReport: "Формирование отчета...",
        GeneratingReports: "Формирование отчетов...",
        GeneratingMorningReport: "Формирование утреннего отчета...",
        GeneratingLunchReport: "Формирование обеденного отчета...",
        ReportGenerated: "Отчет успешно сформирован.",
        ReportsGenerated: "Отчеты успешно сформированы.",
        MorningReportGenerated: "Утренний отчет успешно сформирован.",
        LunchReportGenerated: "Обеденный отчет успешно сформирован.",
        OperationFailedMessage: "Операция завершилась ошибкой.",
        ReportBusyMessage: "Формирование отчета уже выполняется.",
        StoppedRunNoResultsMessage: "Для этой задачи пока не найдено завершенных результатов Elevate.",
        RunFolderBusyMessage: "Для этой папки уже выполняется batch-расчет: {0}",
        IntegrationMissingLaunch: "Peters Research Elevate не найден. Установите Elevate или задайте ELEVATE_EXE_PATH.",
        IntegrationMissingCheck: "Elevate не найден. Проверьте установку или задайте ELEVATE_EXE_PATH.",
        IntegrationFoundFormat: "Elevate найден.{0} Путь: {1}",
        IntegrationVersionFormat: " Версия: {0}.",
        JobTitleFormat: "Задача {0} - {1}",
        JobDetailsFormat: "{0} - {1}",
        JobModeMorningLunch: "утренний + обеденный пик",
        JobModeMorningOnly: "только утренний пик",
        JobModeSingleScenario: "один сценарий",
        UpdateAvailableTitle: "Доступно обновление",
        UpdateAvailableMessageFormat: "Установленная версия: {0}\nПоследняя версия: {1}\n\nСкачать и установить обновление сейчас?",
        UpdateInstallButton: "Обновить",
        UpdateLaterButton: "Позже",
        UpdateDownloadingStatus: "Скачивание обновления...",
        UpdateStartedStatus: "Установщик обновления запущен. Elevate Helper сейчас закроется.");

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

    public string FormatRunStopped(string jobTitle)
    {
        return string.Format(CultureInfo.CurrentCulture, CurrentText.RunStoppedFormat, jobTitle);
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
            JobStateKind.Stopping => text.StoppingStatus,
            JobStateKind.Completed => text.CompletedStatus,
            JobStateKind.Stopped => text.StoppedStatus,
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
        string OpenEditorWindowButton,
        string EditorWindowHint,
        string ProjectBatchTitle,
        string ProjectBatchHint,
        string ProjectBatchPathHeader,
        string ProjectBatchPathPlaceholder,
        string ProjectBatchParallelRunsHeader,
        string ProjectBatchUnlimitedRuns,
        string ProjectBatchRunButton,
        string ProjectBatchNoJobsMessage,
        string ProjectBatchStartedFormat,
        string ProjectBatchWarningsFormat,
        string ProjectBatchUnknownTitle,
        string ProjectBatchUnknownHint,
        string ProjectBatchUnknownPrimaryButton,
        string ProjectBatchUnknownSecondaryButton,
        string ProjectBatchUnknownCloseButton,
        string ProjectBatchRetryButton,
        string StopJobButton,
        string DismissJobButton,
        string ProjectBatchGeneratingReports,
        string ProjectBatchPreviewTitle,
        string ProjectBatchPreviewBuildingTypeHeader,
        string ProjectBatchPreviewPathHeader,
        string ProjectBatchPreviewFileCountHeader,
        string ProjectBatchPreviewScenariosHeader,
        string ProjectBatchPreviewMorningLunch,
        string ProjectBatchPreviewSingleScenario,
        string ProjectBatchPreviewStartButton,
        string ProjectBatchPreviewCancelButton,
        string ProjectBatchPreviewWarningsTitle,
        string ProjectBatchPreviewWarningsFormat,
        string EditorTitle,
        string EditorHint,
        string EditorWorkingFolderLabel,
        string EditorBuildingTypeLabel,
        string EditorProjectTabTitle,
        string EditorAnalysisTabTitle,
        string EditorBuildingTabTitle,
        string EditorLiftGroupTabTitle,
        string EditorAnalysisHint,
        string EditorBuildingHint,
        string EditorLiftGroupHint,
        string LoadEditorButton,
        string LoadEditorTemplateButton,
        string SaveEditorButton,
        string EditorCloseButton,
        string EditorSourceLabel,
        string EditorOutputLabel,
        string EditorProjectSectionTitle,
        string EditorAnalysisSectionTitle,
        string EditorBuildingSectionTitle,
        string EditorTrafficSectionTitle,
        string EditorLiftGroupSectionTitle,
        string EditorJobTitleHeader,
        string EditorJobNoHeader,
        string EditorCalculationTitleHeader,
        string EditorMadeByHeader,
        string EditorCheckedByHeader,
        string EditorCompanyHeader,
        string EditorDispatcherHeader,
        string EditorTrafficModeHeader,
        string EditorSimulationsHeader,
        string EditorLearningRunsHeader,
        string EditorRandomSeedHeader,
        string EditorAbsenteeismHeader,
        string EditorFloorNameColumn,
        string EditorInterfloorHeightColumn,
        string EditorPopulationColumn,
        string EditorEntranceColumn,
        string EditorAddFloorAboveButton,
        string EditorAddFloorBelowButton,
        string EditorSortTopFirstButton,
        string EditorSortBottomFirstButton,
        string EditorLiftCountLabel,
        string EditorAddLiftButton,
        string EditorRemoveLiftButton,
        string EditorCapacityHeader,
        string EditorCabWidthHeader,
        string EditorCabHeightHeader,
        string EditorCabAreaHeader,
        string EditorSpeedHeader,
        string EditorAccelerationHeader,
        string EditorJerkHeader,
        string EditorDoorWidthHeader,
        string EditorDoorOpeningHeader,
        string EditorDoorOpeningCentral,
        string EditorDoorOpeningTelescopic,
        string EditorDoorOpenHeader,
        string EditorDoorCloseHeader,
        string EditorIncomingHeader,
        string EditorOutgoingHeader,
        string EditorInterfloorHeader,
        string EditorHandlingCapacityHeader,
        string EditorLoadingTimeHeader,
        string EditorUnloadingTimeHeader,
        string EditorFloorsHint,
        string EditorFloorLevelLabel,
        string EditorFloorPopulationLabel,
        string EditorFloorEntranceLabel,
        string EditorCarsHint,
        string EditorCarCapacityLabel,
        string EditorCarAreaLabel,
        string EditorCarSpeedLabel,
        string EditorCarAccelerationLabel,
        string EditorCarJerkLabel,
        string EditorCarPreOpeningLabel,
        string EditorCarOpenTimeLabel,
        string EditorCarCloseTimeLabel,
        string EditorCarHomeFloorLabel,
        string EditorLoadSuccessFormat,
        string EditorSaveSuccessFormat,
        string EditorNotLoadedMessage,
        string EditorExistingFileMissingMessage,
        string EditorInvalidNumberFormat,
        string EditorTrafficSplitTotalMessage,
        string ActionsTitle,
        string ActionsHint,
        string RunButton,
        string RunMorningButton,
        string ExitButton,
        string ReportButton,
        string PrintReportsButton,
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
        string StoppingStatus,
        string CompletedStatus,
        string StoppedStatus,
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
        string RunStoppedFormat,
        string GeneratingReport,
        string GeneratingReports,
        string GeneratingMorningReport,
        string GeneratingLunchReport,
        string ReportGenerated,
        string ReportsGenerated,
        string MorningReportGenerated,
        string LunchReportGenerated,
        string OperationFailedMessage,
        string ReportBusyMessage,
        string StoppedRunNoResultsMessage,
        string RunFolderBusyMessage,
        string IntegrationMissingLaunch,
        string IntegrationMissingCheck,
        string IntegrationFoundFormat,
        string IntegrationVersionFormat,
        string JobTitleFormat,
        string JobDetailsFormat,
        string JobModeMorningLunch,
        string JobModeMorningOnly,
        string JobModeSingleScenario,
        string UpdateAvailableTitle,
        string UpdateAvailableMessageFormat,
        string UpdateInstallButton,
        string UpdateLaterButton,
        string UpdateDownloadingStatus,
        string UpdateStartedStatus);
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
    Stopping,
    Completed,
    Stopped,
}
