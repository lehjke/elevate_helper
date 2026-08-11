using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        ElevateHiddenModeLabel: "Hide Elevate",
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
        CalculationFileTitle: "Calculation file",
        CalculationFileHint: "Create or edit the .elvx file for this working folder before starting the calculation.",
        CalculationFileNoPathStatus: "Select a working folder to prepare the calculation file.",
        CalculationFileMissingPathStatus: "The selected folder does not exist.",
        CalculationFileBatchModeStatus: "Project roots are prepared by group folders. Select a group folder to edit one calculation file.",
        CalculationFileExistingStatusFormat: "Existing file: {0}",
        CalculationFileMultipleStatusFormat: "{0} .elvx files found. The editor opens: {1}",
        CalculationFileTemplateStatus: "No .elvx found. The editor will start from the selected building template.",
        OpenEditorWindowButton: "Create / edit ELVX",
        EditorWindowHint: "Open the ELVX editor in a separate window. It uses the current working folder and building type from the main screen.",
        ProjectBatchTitle: "Project batch mode",
        ProjectBatchHint: "Select a project root. Each calculation folder must contain one .elvx file; its building type is read from the file.",
        ProjectBatchPathHeader: "Project root",
        ProjectBatchPathPlaceholder: @"C:\Elevate\Project",
        ProjectBatchParallelRunsHeader: "Parallel runs",
        ProjectBatchUnlimitedRuns: "No limit",
        ProjectBatchOfficeScenariosHeader: "Office scenarios",
        ProjectBatchMorningOnly: "Morning peak only",
        ProjectBatchOfficeScenariosHint: "Applies only to Office groups. Residence and Hotel groups always use one scenario.",
        ProjectBatchRunButton: "Run project",
        ProjectBatchNoJobsMessage: "No valid project folders were found.",
        ProjectBatchLaunchAlreadyPreparingMessage: "A batch launch is already being prepared.",
        ProjectBatchAnalyzingStatus: "Analyzing the project structure…",
        ProjectBatchOverlapFormat: "Batch launch was stopped because working folders '{0}' and '{1}' overlap. Move the root .elvx file into its own folder.",
        ProjectBatchParallelRunsMinimumMessage: "Parallel runs: 1+",
        ProjectBatchStartedFormat: "Project batch started: {0} job(s).",
        ProjectBatchWarningsFormat: "{0} project scan warning(s).",
        ProjectBatchStartedWithWarningsFormat: "Project batch started: {0} job(s). {1} project scan warning(s).",
        ProjectBatchStartedWithOfficeScenarioFormat: "Project batch started: {0} job(s). Office: {1}.",
        ProjectBatchStartedWithWarningsAndOfficeScenarioFormat: "Project batch started: {0} job(s). {1} project scan warning(s). Office: {2}.",
        ProjectBatchWarningFolderMultipleFormat: "Folder '{0}' contains more than one source .elvx file and was skipped.",
        ProjectBatchWarningGroupMultipleFormat: "Group '{0}' contains more than one source .elvx file and was skipped.",
        ProjectBatchWarningTypeMismatchFormat: "File '{0}' declares building type '{1}', but it is stored under '{2}'. The value from the .elvx file will be used.",
        ProjectBatchWarningTypeUnreadableFormat: "Could not read BuildingType from '{0}'. Folder type '{1}' will be used.",
        ProjectBatchOfficeScenarioStatusFormat: "Office: {0}.",
        ProjectBatchUnknownTitle: "Select building types",
        ProjectBatchUnknownHint: "The building type could not be read from these .elvx files. Select a type to include them in the run.",
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
        ProjectBatchPreviewMorningOnly: "morning only",
        ProjectBatchPreviewSingleScenario: "single",
        ProjectBatchPreviewStartButton: "Start",
        ProjectBatchPreviewCancelButton: "Cancel",
        ProjectBatchPreviewWarningsTitle: "Skipped items",
        ProjectBatchPreviewWarningsFormat: "{0} skipped item(s) need attention.",
        EditorTitle: "ELVX Editor",
        EditorHint: "Load an existing .elvx from the working folder or start from the built-in template. The editor keeps linked Elevate sections consistent while you tune the project, analysis, traffic, floors, and lifts before a run.",
        EditorWorkingFolderLabel: "Working folder",
        EditorBuildingTypeLabel: "Building type",
        EditorProjectTabTitle: "Project",
        EditorAnalysisTabTitle: "Analysis",
        EditorBuildingTabTitle: "Building",
        EditorLiftGroupTabTitle: "Lift Group",
        EditorAnalysisHint: "Traffic mode stays fixed from the loaded template or source ELVX and is not edited here.",
        EditorBuildingHint: "The building table mirrors Elevate: floor name, floor-to-floor height, population, entrance share, and entrance flag. Adding or removing a floor updates linked sections.",
        EditorLiftGroupHint: "Configure each lift separately. Adding or removing a lift updates the linked Elevate sections.",
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
        EditorTrafficSplitTitle: "Passenger flow",
        EditorTrafficParametersTitle: "Scenario parameters",
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
        EditorEntranceBiasColumn: "Entrance bias, %",
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
        EditorCabHeightHeader: "Cab depth, mm",
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
        EditorFloorsHint: "Floor names, count, heights, population, and entrance settings are editable. Linked Elevate sections are rebuilt on save.",
        EditorFloorLevelLabel: "Level",
        EditorFloorPopulationLabel: "Population",
        EditorFloorEntranceLabel: "Entrance",
        EditorCarsHint: "Lift count and per-lift parameters are editable. Linked Elevate sections are rebuilt on save.",
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
        EditorBusyStatus: "Working…",
        EditorBusyRunMessage: "Wait for the editor operation to finish before starting a run.",
        EditorUnsavedRunMessage: "Save the ELVX changes in the editor before starting a run.",
        EditorBuildingTypeMismatchFormat: "This file is for “{0}”, but the editor was opened for “{1}”. Change the building type on the main screen and reopen the file.",
        EditorInvalidNumberFormat: "Invalid numeric value in \"{0}\".",
        EditorBaseFloorLevelFormat: "The base floor “{0}” must have a 0 m level.",
        EditorTrafficSplitTotalMessage: "Incoming, outgoing, and interfloor traffic must sum to 100%.",
        EditorMinimumFloorMessage: "The building must contain at least one floor.",
        EditorMinimumLiftMessage: "The group must contain at least one lift.",
        EditorSimulationCountPositiveMessage: "The simulation count must be greater than zero.",
        EditorPercentageRangeFormat: "{0} must be between 0% and 100%.",
        EditorFieldNonNegativeFormat: "{0} cannot be negative.",
        EditorFloorNameRequiredFormat: "Floor {0} must have a name.",
        EditorFloorNameDuplicateFormat: "Floor name “{0}” is duplicated.",
        EditorFloorFieldNonNegativeFormat: "{0} for “{1}” cannot be negative.",
        EditorInterfloorHeightPositiveFormat: "The interfloor height for “{0}” must be greater than zero.",
        EditorEntranceBiasRangeFormat: "The entrance bias for “{0}” must be between 0% and 100%.",
        EditorNonEntranceBiasZeroFormat: "The entrance bias for non-entrance floor “{0}” must be 0%.",
        EditorBuildingTableEmptyMessage: "Building table is empty.",
        EditorEntranceFloorRequiredMessage: "Select at least one entrance floor.",
        EditorEntranceBiasTotalFormat: "Entrance-floor bias must total 100%; it is currently {0}%.",
        EditorLiftRequiredMessage: "Add at least one lift.",
        EditorLiftFieldPositiveFormat: "{0} for “{1}” must be greater than zero.",
        EditorHomeFloorRangeFormat: "The home floor for “{0}” must be between 1 and {1}.",
        EditorServedFloorRequiredFormat: "{0} must serve at least one floor.",
        EditorLiftTitleFormat: "Lift {0}",
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
        ShutdownStoppingStatus: "Stopping jobs and releasing resources…",
        ProcessingModeSingleStatus: "Mode: Single project.",
        ProcessingModeBatchStatus: "Mode: Batch.",
        Ready: "Ready",
        ActiveJobsFormat: "{0} active job(s)",
        NoActiveJobsTitle: "No active jobs",
        NoActiveJobsHint: "Start a batch run to see live progress here.",
        StatusTitle: "Checkup",
        QueuedStatus: "Queued",
        RunningStatus: "Running",
        StoppingStatus: "Stopping...",
        JobStoppingFormat: "{0}: Stopping...",
        NoStoppableJobsMessage: "There are no jobs that can be stopped.",
        StopRequestedFormat: "Stop requested for {0} job(s).",
        JobDismissedFormat: "Job '{0}' was dismissed.",
        JobRestoredFormat: "Job '{0}' was restored.",
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
        ScenarioRunStartedFormat: "{0}: {1} started.",
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
        UpdateProgressTitle: "Installing update",
        UpdatePreparingStatus: "Preparing update...",
        UpdateDownloadingStatus: "Downloading the update...",
        UpdateDownloadingProgressFormat: "Downloading the update... {0:0}%",
        UpdateVerifyingStatus: "Verifying downloaded installer...",
        UpdateStartingInstallerStatus: "Starting installer...",
        UpdateStartedStatus: "The update installer has started. Elevate Helper will close now.");

    private static readonly AppTextCatalog Russian = new(
        WindowTitle: "Elevate Helper",
        AppTitle: "Elevate Helper",
        LanguageLabel: "Язык",
        ElevateHiddenModeLabel: "Скрывать Elevate",
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
        CalculationFileTitle: "Файл расчета",
        CalculationFileHint: "Создайте или измените .elvx-файл для выбранной рабочей папки перед запуском расчета.",
        CalculationFileNoPathStatus: "Выберите рабочую папку, чтобы подготовить файл расчета.",
        CalculationFileMissingPathStatus: "Выбранная папка не существует.",
        CalculationFileBatchModeStatus: "Корень проекта готовится по папкам групп. Выберите папку группы, чтобы изменить один файл расчета.",
        CalculationFileExistingStatusFormat: "Найден файл: {0}",
        CalculationFileMultipleStatusFormat: "Найдено .elvx файлов: {0}. Редактор откроет: {1}",
        CalculationFileTemplateStatus: ".elvx не найден. Редактор стартует из шаблона выбранного типа здания.",
        OpenEditorWindowButton: "Создать / изменить ELVX",
        EditorWindowHint: "Откройте редактор ELVX в отдельном окне. Он использует текущую рабочую папку и тип здания с главного экрана.",
        ProjectBatchTitle: "Пакетный запуск проекта",
        ProjectBatchHint: "Выберите корень проекта. В каждой папке расчета должен быть один .elvx-файл; тип здания определяется из файла.",
        ProjectBatchPathHeader: "Корень проекта",
        ProjectBatchPathPlaceholder: @"C:\Elevate\Проект",
        ProjectBatchParallelRunsHeader: "Параллельных расчетов",
        ProjectBatchUnlimitedRuns: "Без ограничений",
        ProjectBatchOfficeScenariosHeader: "Сценарии для офисов",
        ProjectBatchMorningOnly: "Только утренний пик",
        ProjectBatchOfficeScenariosHint: "Применяется только к группам Office. Res и Hotel всегда запускаются одним сценарием.",
        ProjectBatchRunButton: "Запустить проект",
        ProjectBatchNoJobsMessage: "Не найдено подходящих папок проекта.",
        ProjectBatchLaunchAlreadyPreparingMessage: "Подготовка пакетного запуска уже выполняется.",
        ProjectBatchAnalyzingStatus: "Анализируем структуру проекта…",
        ProjectBatchOverlapFormat: "Пакетный запуск остановлен: рабочие папки «{0}» и «{1}» пересекаются. Переместите корневой .elvx в отдельную папку.",
        ProjectBatchParallelRunsMinimumMessage: "Параллельных расчетов: 1+",
        ProjectBatchStartedFormat: "Пакетный запуск проекта начат: {0} задач.",
        ProjectBatchWarningsFormat: "Предупреждений при сканировании проекта: {0}.",
        ProjectBatchStartedWithWarningsFormat: "Пакетный запуск проекта начат: {0} задач. Предупреждений при сканировании проекта: {1}.",
        ProjectBatchStartedWithOfficeScenarioFormat: "Пакетный запуск проекта начат: {0} задач. Office: {1}.",
        ProjectBatchStartedWithWarningsAndOfficeScenarioFormat: "Пакетный запуск проекта начат: {0} задач. Предупреждений при сканировании проекта: {1}. Office: {2}.",
        ProjectBatchWarningFolderMultipleFormat: "Папка «{0}» содержит больше одного исходного .elvx-файла и пропущена.",
        ProjectBatchWarningGroupMultipleFormat: "Группа «{0}» содержит больше одного исходного .elvx-файла и пропущена.",
        ProjectBatchWarningTypeMismatchFormat: "В файле «{0}» указан тип здания «{1}», но он находится в папке «{2}». Будет использован тип из .elvx-файла.",
        ProjectBatchWarningTypeUnreadableFormat: "Не удалось прочитать BuildingType из файла «{0}». Будет использован тип папки «{1}».",
        ProjectBatchOfficeScenarioStatusFormat: "Office: {0}.",
        ProjectBatchUnknownTitle: "Выбор типов здания",
        ProjectBatchUnknownHint: "В этих .elvx-файлах не удалось определить тип здания. Выберите тип, чтобы включить их в запуск.",
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
        ProjectBatchPreviewMorningOnly: "только утро",
        ProjectBatchPreviewSingleScenario: "один",
        ProjectBatchPreviewStartButton: "Запустить",
        ProjectBatchPreviewCancelButton: "Отмена",
        ProjectBatchPreviewWarningsTitle: "Пропущенные элементы",
        ProjectBatchPreviewWarningsFormat: "Пропущено или требует внимания: {0}.",
        EditorTitle: "Редактор ELVX",
        EditorHint: "Загрузите существующий .elvx из рабочей папки или стартуйте со встроенного шаблона. Редактор сохраняет связность секций Elevate при настройке проекта, анализа, трафика, этажей и лифтов до запуска.",
        EditorWorkingFolderLabel: "Рабочая папка",
        EditorBuildingTypeLabel: "Тип здания",
        EditorProjectTabTitle: "Проект",
        EditorAnalysisTabTitle: "Анализ",
        EditorBuildingTabTitle: "Здание",
        EditorLiftGroupTabTitle: "Лифтовая группа",
        EditorAnalysisHint: "Режим трафика остается фиксированным из загруженного шаблона или исходного ELVX и здесь не редактируется.",
        EditorBuildingHint: "Таблица здания повторяет Elevate: название этажа, межэтажное расстояние, население, долю входа и признак входного этажа. Добавление или удаление этажа обновляет связанные секции.",
        EditorLiftGroupHint: "Каждый лифт настраивается отдельно. Добавление или удаление лифта обновляет связанные секции Elevate.",
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
        EditorTrafficSplitTitle: "Пассажиропоток",
        EditorTrafficParametersTitle: "Параметры сценария",
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
        EditorEntranceBiasColumn: "Вход, %",
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
        EditorFloorsHint: "Можно менять названия, количество, высоту, население и параметры входа этажей. Связанные секции Elevate перестраиваются при сохранении.",
        EditorFloorLevelLabel: "Отметка",
        EditorFloorPopulationLabel: "Население",
        EditorFloorEntranceLabel: "Входной этаж",
        EditorCarsHint: "Можно менять количество лифтов и параметры каждого лифта. Связанные секции Elevate перестраиваются при сохранении.",
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
        EditorBusyStatus: "Выполняется…",
        EditorBusyRunMessage: "Дождитесь завершения операции редактора перед запуском расчета.",
        EditorUnsavedRunMessage: "Сохраните изменения ELVX в редакторе перед запуском расчета.",
        EditorBuildingTypeMismatchFormat: "Файл относится к типу «{0}», а редактор открыт для «{1}». Измените тип здания на главном экране и снова откройте файл.",
        EditorInvalidNumberFormat: "Некорректное числовое значение в поле \"{0}\".",
        EditorBaseFloorLevelFormat: "Отметка нижнего этажа «{0}» должна быть 0 м.",
        EditorTrafficSplitTotalMessage: "Сумма входящего, исходящего и межэтажного потоков должна быть равна 100%.",
        EditorMinimumFloorMessage: "В здании должен остаться хотя бы один этаж.",
        EditorMinimumLiftMessage: "В группе должен остаться хотя бы один лифт.",
        EditorSimulationCountPositiveMessage: "Число симуляций должно быть больше нуля.",
        EditorPercentageRangeFormat: "Поле «{0}» должно быть от 0 до 100 %.",
        EditorFieldNonNegativeFormat: "Поле «{0}» не может быть отрицательным.",
        EditorFloorNameRequiredFormat: "У этажа {0} должно быть название.",
        EditorFloorNameDuplicateFormat: "Название этажа «{0}» повторяется.",
        EditorFloorFieldNonNegativeFormat: "Поле «{0}» у этажа «{1}» не может быть отрицательным.",
        EditorInterfloorHeightPositiveFormat: "Межэтажная высота для этажа «{0}» должна быть больше нуля.",
        EditorEntranceBiasRangeFormat: "Доля входа для этажа «{0}» должна быть от 0 до 100 %.",
        EditorNonEntranceBiasZeroFormat: "У невходного этажа «{0}» доля входа должна быть 0 %.",
        EditorBuildingTableEmptyMessage: "Таблица здания пуста.",
        EditorEntranceFloorRequiredMessage: "Выберите хотя бы один входной этаж.",
        EditorEntranceBiasTotalFormat: "Сумма долей входных этажей должна быть 100 %, сейчас {0} %.",
        EditorLiftRequiredMessage: "Нужно добавить хотя бы один лифт.",
        EditorLiftFieldPositiveFormat: "Поле «{0}» для «{1}» должно быть больше нуля.",
        EditorHomeFloorRangeFormat: "Домашний этаж для «{0}» должен быть от 1 до {1}.",
        EditorServedFloorRequiredFormat: "Для {0} нужно выбрать хотя бы один обслуживаемый этаж.",
        EditorLiftTitleFormat: "Лифт {0}",
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
        ShutdownStoppingStatus: "Останавливаем задачи и освобождаем ресурсы…",
        ProcessingModeSingleStatus: "Режим: Один проект.",
        ProcessingModeBatchStatus: "Режим: Пакет.",
        Ready: "Готово",
        ActiveJobsFormat: "Активных задач: {0}",
        NoActiveJobsTitle: "Нет активных задач",
        NoActiveJobsHint: "Запустите расчет, чтобы увидеть прогресс здесь.",
        StatusTitle: "Проверка",
        QueuedStatus: "В очереди",
        RunningStatus: "Выполняется",
        StoppingStatus: "Остановка...",
        JobStoppingFormat: "{0}: Остановка...",
        NoStoppableJobsMessage: "Нет задач, которые можно остановить.",
        StopRequestedFormat: "Запрошена остановка задач: {0}.",
        JobDismissedFormat: "Задача «{0}» скрыта.",
        JobRestoredFormat: "Задача «{0}» восстановлена.",
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
        ScenarioRunStartedFormat: "{0}: {1} запущена.",
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
        UpdateProgressTitle: "Установка обновления",
        UpdatePreparingStatus: "Подготовка обновления...",
        UpdateDownloadingStatus: "Скачивание обновления...",
        UpdateDownloadingProgressFormat: "Скачивание обновления... {0:0}%",
        UpdateVerifyingStatus: "Проверка установщика...",
        UpdateStartingInstallerStatus: "Запуск установщика...",
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

    public CultureInfo CurrentCulture => currentLanguage == AppLanguage.Russian
        ? CultureInfo.GetCultureInfo("ru-RU")
        : CultureInfo.GetCultureInfo("en-US");

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
            CurrentCulture,
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

        return string.Format(CurrentCulture, CurrentText.JobDetailsFormat, path, mode);
    }

    public string FormatSelectedBuildingType(BuildingType buildingType)
    {
        return string.Format(
            CurrentCulture,
            CurrentText.SelectedBuildingTypeFormat,
            FormatBuildingType(buildingType));
    }

    public string FormatRunStarted(string jobTitle)
    {
        return string.Format(CurrentCulture, CurrentText.RunStartedFormat, jobTitle);
    }

    public string FormatRunCompleted(string jobTitle)
    {
        return string.Format(CurrentCulture, CurrentText.RunCompletedFormat, jobTitle);
    }

    public string FormatRunStopped(string jobTitle)
    {
        return string.Format(CurrentCulture, CurrentText.RunStoppedFormat, jobTitle);
    }

    public string GetQueueSummary(int activeJobs)
    {
        return activeJobs > 0
            ? string.Format(CurrentCulture, CurrentText.ActiveJobsFormat, activeJobs)
            : CurrentText.Ready;
    }

    public string FormatProjectBatchWarning(ProjectBatchWarning warning)
    {
        AppTextCatalog text = CurrentText;
        return warning.Kind switch
        {
            ProjectBatchWarningKind.FolderContainsMultipleSourceFiles => string.Format(
                CurrentCulture,
                text.ProjectBatchWarningFolderMultipleFormat,
                warning.Subject),
            ProjectBatchWarningKind.GroupContainsMultipleSourceFiles => string.Format(
                CurrentCulture,
                text.ProjectBatchWarningGroupMultipleFormat,
                warning.Subject),
            ProjectBatchWarningKind.BuildingTypeMismatch => string.Format(
                CurrentCulture,
                text.ProjectBatchWarningTypeMismatchFormat,
                warning.Subject,
                warning.ActualValue ?? string.Empty,
                warning.ExpectedValue ?? string.Empty),
            ProjectBatchWarningKind.BuildingTypeUnreadable => string.Format(
                CurrentCulture,
                text.ProjectBatchWarningTypeUnreadableFormat,
                warning.Subject,
                warning.ExpectedValue ?? string.Empty),
            _ => warning.Subject,
        };
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
            JobStateKind.Preparing => text.RunningStatus,
            JobStateKind.Running => text.RunningStatus,
            JobStateKind.Reporting => text.ProjectBatchGeneratingReports,
            JobStateKind.Stopping => text.StoppingStatus,
            JobStateKind.Completed => text.CompletedStatus,
            JobStateKind.Stopped => text.StoppedStatus,
            JobStateKind.ReportFailed => text.OperationFailedMessage,
            JobStateKind.Failed => text.OperationFailedMessage,
            _ => text.QueuedStatus,
        };
    }

    public string RelocalizeCatalogMessage(string? message, AppLanguage sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(message) || sourceLanguage == CurrentLanguage)
        {
            return message ?? string.Empty;
        }

        AppTextCatalog sourceCatalog = sourceLanguage == AppLanguage.Russian ? Russian : English;
        AppTextCatalog targetCatalog = CurrentLanguage == AppLanguage.Russian ? Russian : English;
        System.Reflection.PropertyInfo[] properties = typeof(AppTextCatalog)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .ToArray();
        System.Reflection.PropertyInfo[] exactMatches = properties
            .Where(property => string.Equals(
                (string?)property.GetValue(sourceCatalog),
                message,
                StringComparison.Ordinal))
            .ToArray();
        if (exactMatches.Length > 0)
        {
            string[] distinctTargets = exactMatches
                .Select(property => (string?)property.GetValue(targetCatalog) ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinctTargets.Length == 1)
            {
                return distinctTargets[0];
            }
        }

        foreach (System.Reflection.PropertyInfo property in properties
                     .OrderByDescending(property =>
                         ((string?)property.GetValue(sourceCatalog) ?? string.Empty).Length))
        {
            string sourcePattern = (string?)property.GetValue(sourceCatalog) ?? string.Empty;
            string targetPattern = (string?)property.GetValue(targetCatalog) ?? string.Empty;
            if (TryExtractFormatArguments(sourcePattern, message, out object[] arguments))
            {
                bool semanticArgumentMismatch = false;
                for (int index = 0; index < arguments.Length; index++)
                {
                    if (arguments[index] is string argument)
                    {
                        string[] candidatePropertyNames = GetCatalogArgumentCandidateProperties(
                            property.Name,
                            index);
                        if (candidatePropertyNames.Length == 0)
                        {
                            continue;
                        }

                        if (!TryRelocalizeCatalogArgument(
                                argument,
                                candidatePropertyNames,
                                sourceCatalog,
                                targetCatalog,
                                out string localizedArgument))
                        {
                            if (RequiresSemanticCatalogArgument(property.Name, index))
                            {
                                semanticArgumentMismatch = true;
                                break;
                            }

                            continue;
                        }

                        arguments[index] = localizedArgument;
                    }
                }

                if (semanticArgumentMismatch)
                {
                    continue;
                }

                try
                {
                    return string.Format(CurrentCulture, targetPattern, arguments);
                }
                catch (FormatException)
                {
                    // Fall through to the runtime-message translator.
                }
            }
        }

        return CurrentLanguage == AppLanguage.Russian
            ? TranslateRuntimeMessage(message)
            : message;
    }

    private bool TryRelocalizeCatalogArgument(
        string argument,
        IReadOnlyList<string> candidatePropertyNames,
        AppTextCatalog sourceCatalog,
        AppTextCatalog targetCatalog,
        out string localizedArgument)
    {
        localizedArgument = argument;
        foreach (string propertyName in candidatePropertyNames)
        {
            System.Reflection.PropertyInfo? property = typeof(AppTextCatalog).GetProperty(propertyName);
            if (property?.PropertyType != typeof(string))
            {
                continue;
            }

            string sourcePattern = (string?)property.GetValue(sourceCatalog) ?? string.Empty;
            string targetPattern = (string?)property.GetValue(targetCatalog) ?? string.Empty;
            if (string.Equals(sourcePattern, argument, StringComparison.Ordinal))
            {
                localizedArgument = targetPattern;
                return true;
            }

            if (!TryExtractFormatArguments(sourcePattern, argument, out object[] nestedArguments))
            {
                continue;
            }

            bool nestedSemanticMismatch = false;
            for (int index = 0; index < nestedArguments.Length; index++)
            {
                if (nestedArguments[index] is not string nestedArgument)
                {
                    continue;
                }

                string[] nestedCandidatePropertyNames = GetCatalogArgumentCandidateProperties(
                    property.Name,
                    index);
                if (nestedCandidatePropertyNames.Length == 0)
                {
                    continue;
                }

                if (!TryRelocalizeCatalogArgument(
                        nestedArgument,
                        nestedCandidatePropertyNames,
                        sourceCatalog,
                        targetCatalog,
                        out string localizedNestedArgument))
                {
                    if (RequiresSemanticCatalogArgument(property.Name, index))
                    {
                        nestedSemanticMismatch = true;
                        break;
                    }

                    continue;
                }

                nestedArguments[index] = localizedNestedArgument;
            }

            if (nestedSemanticMismatch)
            {
                continue;
            }

            try
            {
                localizedArgument = string.Format(CurrentCulture, targetPattern, nestedArguments);
                return true;
            }
            catch (FormatException)
            {
                // Try the next allowed semantic property.
            }
        }

        return false;
    }

    private static string[] GetCatalogArgumentCandidateProperties(string propertyName, int argumentIndex)
    {
        return (propertyName, argumentIndex) switch
        {
            (nameof(AppTextCatalog.SelectedBuildingTypeFormat), 0) or
            (nameof(AppTextCatalog.JobTitleFormat), 1) or
            (nameof(AppTextCatalog.EditorBuildingTypeMismatchFormat), 0 or 1) or
            (nameof(AppTextCatalog.ProjectBatchWarningTypeMismatchFormat), 1 or 2) or
            (nameof(AppTextCatalog.ProjectBatchWarningTypeUnreadableFormat), 1) =>
            [
                nameof(AppTextCatalog.BuildingTypeOffice),
                nameof(AppTextCatalog.BuildingTypeResidence),
                nameof(AppTextCatalog.BuildingTypeHotel),
            ],
            (nameof(AppTextCatalog.JobDetailsFormat), 1) =>
            [
                nameof(AppTextCatalog.JobModeMorningLunch),
                nameof(AppTextCatalog.JobModeMorningOnly),
                nameof(AppTextCatalog.JobModeSingleScenario),
            ],
            (nameof(AppTextCatalog.ProjectBatchOfficeScenarioStatusFormat), 0) or
            (nameof(AppTextCatalog.ProjectBatchStartedWithOfficeScenarioFormat), 1) or
            (nameof(AppTextCatalog.ProjectBatchStartedWithWarningsAndOfficeScenarioFormat), 2) =>
            [
                nameof(AppTextCatalog.ProjectBatchPreviewMorningLunch),
                nameof(AppTextCatalog.ProjectBatchPreviewMorningOnly),
                nameof(AppTextCatalog.ProjectBatchPreviewSingleScenario),
            ],
            (nameof(AppTextCatalog.EditorInvalidNumberFormat), 0) or
            (nameof(AppTextCatalog.EditorPercentageRangeFormat), 0) or
            (nameof(AppTextCatalog.EditorFieldNonNegativeFormat), 0) or
            (nameof(AppTextCatalog.EditorFloorFieldNonNegativeFormat), 0) or
            (nameof(AppTextCatalog.EditorLiftFieldPositiveFormat), 0) =>
            [
                nameof(AppTextCatalog.EditorSimulationsHeader),
                nameof(AppTextCatalog.EditorLearningRunsHeader),
                nameof(AppTextCatalog.EditorRandomSeedHeader),
                nameof(AppTextCatalog.EditorAbsenteeismHeader),
                nameof(AppTextCatalog.EditorIncomingHeader),
                nameof(AppTextCatalog.EditorOutgoingHeader),
                nameof(AppTextCatalog.EditorInterfloorHeader),
                nameof(AppTextCatalog.EditorHandlingCapacityHeader),
                nameof(AppTextCatalog.EditorLoadingTimeHeader),
                nameof(AppTextCatalog.EditorUnloadingTimeHeader),
                nameof(AppTextCatalog.EditorInterfloorHeightColumn),
                nameof(AppTextCatalog.EditorPopulationColumn),
                nameof(AppTextCatalog.EditorEntranceBiasColumn),
                nameof(AppTextCatalog.EditorCapacityHeader),
                nameof(AppTextCatalog.EditorCabWidthHeader),
                nameof(AppTextCatalog.EditorCabHeightHeader),
                nameof(AppTextCatalog.EditorSpeedHeader),
                nameof(AppTextCatalog.EditorFloorLevelLabel),
                nameof(AppTextCatalog.EditorFloorPopulationLabel),
                nameof(AppTextCatalog.EditorCarCapacityLabel),
                nameof(AppTextCatalog.EditorCarAreaLabel),
                nameof(AppTextCatalog.EditorCarSpeedLabel),
                nameof(AppTextCatalog.EditorCarAccelerationLabel),
                nameof(AppTextCatalog.EditorCarJerkLabel),
                nameof(AppTextCatalog.EditorCarPreOpeningLabel),
                nameof(AppTextCatalog.EditorCarOpenTimeLabel),
                nameof(AppTextCatalog.EditorCarCloseTimeLabel),
                nameof(AppTextCatalog.EditorCarHomeFloorLabel),
            ],
            (nameof(AppTextCatalog.EditorLiftFieldPositiveFormat), 1) or
            (nameof(AppTextCatalog.EditorHomeFloorRangeFormat), 0) or
            (nameof(AppTextCatalog.EditorServedFloorRequiredFormat), 0) =>
            [nameof(AppTextCatalog.EditorLiftTitleFormat)],
            (nameof(AppTextCatalog.JobStoppingFormat), 0) or
            (nameof(AppTextCatalog.JobDismissedFormat), 0) or
            (nameof(AppTextCatalog.JobRestoredFormat), 0) or
            (nameof(AppTextCatalog.RunStartedFormat), 0) or
            (nameof(AppTextCatalog.RunCompletedFormat), 0) or
            (nameof(AppTextCatalog.RunStoppedFormat), 0) or
            (nameof(AppTextCatalog.ScenarioRunStartedFormat), 0) =>
            [nameof(AppTextCatalog.JobTitleFormat)],
            (nameof(AppTextCatalog.ScenarioRunStartedFormat), 1) =>
            [
                nameof(AppTextCatalog.ProgressScenario),
                nameof(AppTextCatalog.MorningScenario),
                nameof(AppTextCatalog.LunchScenario),
            ],
            (nameof(AppTextCatalog.IntegrationFoundFormat), 0) =>
            [nameof(AppTextCatalog.IntegrationVersionFormat)],
            _ => [],
        };
    }

    private static bool RequiresSemanticCatalogArgument(string propertyName, int argumentIndex)
    {
        return (propertyName, argumentIndex) switch
        {
            (nameof(AppTextCatalog.JobTitleFormat), 1) => true,
            (nameof(AppTextCatalog.JobDetailsFormat), 1) => true,
            _ => false,
        };
    }

    private static bool TryExtractFormatArguments(
        string format,
        string message,
        out object[] arguments)
    {
        MatchCollection tokens = Regex.Matches(
            format,
            @"\{(?<index>\d+)(?:,[^}:]+)?(?::[^}]+)?\}",
            RegexOptions.CultureInvariant);
        if (tokens.Count == 0)
        {
            arguments = [];
            return false;
        }

        StringBuilder pattern = new("^");
        HashSet<int> capturedIndexes = [];
        int position = 0;
        int maxIndex = 0;
        foreach (Match token in tokens)
        {
            pattern.Append(Regex.Escape(format[position..token.Index]));
            int index = int.Parse(token.Groups["index"].Value, CultureInfo.InvariantCulture);
            maxIndex = Math.Max(maxIndex, index);
            if (capturedIndexes.Add(index))
            {
                pattern.Append("(?<value").Append(index).Append(">.*?)");
            }
            else
            {
                pattern.Append("\\k<value").Append(index).Append('>');
            }

            position = token.Index + token.Length;
        }

        pattern.Append(Regex.Escape(format[position..])).Append('$');
        Match match = Regex.Match(
            message,
            pattern.ToString(),
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!match.Success)
        {
            arguments = [];
            return false;
        }

        arguments = new object[maxIndex + 1];
        for (int index = 0; index <= maxIndex; index++)
        {
            arguments[index] = match.Groups[$"value{index}"].Value;
        }

        return true;
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

        if (message.Equals(ElevateLauncherService.LicenseExpiredErrorMessage, StringComparison.Ordinal))
        {
            return
                "Elevate не может выполнить расчет: срок действия установленной копии истек. " +
                "Установите или активируйте актуальную лицензионную версию Peters Research Elevate и повторите попытку.";
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

        if (message.StartsWith("Elevate could not open a results file after ", StringComparison.Ordinal) &&
            message.EndsWith(" attempts.", StringComparison.Ordinal))
        {
            return "Elevate не смог открыть файл результатов после автоматических перезапусков расчета.";
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
        string ElevateHiddenModeLabel,
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
        string CalculationFileTitle,
        string CalculationFileHint,
        string CalculationFileNoPathStatus,
        string CalculationFileMissingPathStatus,
        string CalculationFileBatchModeStatus,
        string CalculationFileExistingStatusFormat,
        string CalculationFileMultipleStatusFormat,
        string CalculationFileTemplateStatus,
        string OpenEditorWindowButton,
        string EditorWindowHint,
        string ProjectBatchTitle,
        string ProjectBatchHint,
        string ProjectBatchPathHeader,
        string ProjectBatchPathPlaceholder,
        string ProjectBatchParallelRunsHeader,
        string ProjectBatchUnlimitedRuns,
        string ProjectBatchOfficeScenariosHeader,
        string ProjectBatchMorningOnly,
        string ProjectBatchOfficeScenariosHint,
        string ProjectBatchRunButton,
        string ProjectBatchNoJobsMessage,
        string ProjectBatchLaunchAlreadyPreparingMessage,
        string ProjectBatchAnalyzingStatus,
        string ProjectBatchOverlapFormat,
        string ProjectBatchParallelRunsMinimumMessage,
        string ProjectBatchStartedFormat,
        string ProjectBatchWarningsFormat,
        string ProjectBatchStartedWithWarningsFormat,
        string ProjectBatchStartedWithOfficeScenarioFormat,
        string ProjectBatchStartedWithWarningsAndOfficeScenarioFormat,
        string ProjectBatchWarningFolderMultipleFormat,
        string ProjectBatchWarningGroupMultipleFormat,
        string ProjectBatchWarningTypeMismatchFormat,
        string ProjectBatchWarningTypeUnreadableFormat,
        string ProjectBatchOfficeScenarioStatusFormat,
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
        string ProjectBatchPreviewMorningOnly,
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
        string EditorTrafficSplitTitle,
        string EditorTrafficParametersTitle,
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
        string EditorEntranceBiasColumn,
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
        string EditorBusyStatus,
        string EditorBusyRunMessage,
        string EditorUnsavedRunMessage,
        string EditorBuildingTypeMismatchFormat,
        string EditorInvalidNumberFormat,
        string EditorBaseFloorLevelFormat,
        string EditorTrafficSplitTotalMessage,
        string EditorMinimumFloorMessage,
        string EditorMinimumLiftMessage,
        string EditorSimulationCountPositiveMessage,
        string EditorPercentageRangeFormat,
        string EditorFieldNonNegativeFormat,
        string EditorFloorNameRequiredFormat,
        string EditorFloorNameDuplicateFormat,
        string EditorFloorFieldNonNegativeFormat,
        string EditorInterfloorHeightPositiveFormat,
        string EditorEntranceBiasRangeFormat,
        string EditorNonEntranceBiasZeroFormat,
        string EditorBuildingTableEmptyMessage,
        string EditorEntranceFloorRequiredMessage,
        string EditorEntranceBiasTotalFormat,
        string EditorLiftRequiredMessage,
        string EditorLiftFieldPositiveFormat,
        string EditorHomeFloorRangeFormat,
        string EditorServedFloorRequiredFormat,
        string EditorLiftTitleFormat,
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
        string ShutdownStoppingStatus,
        string ProcessingModeSingleStatus,
        string ProcessingModeBatchStatus,
        string Ready,
        string ActiveJobsFormat,
        string NoActiveJobsTitle,
        string NoActiveJobsHint,
        string StatusTitle,
        string QueuedStatus,
        string RunningStatus,
        string StoppingStatus,
        string JobStoppingFormat,
        string NoStoppableJobsMessage,
        string StopRequestedFormat,
        string JobDismissedFormat,
        string JobRestoredFormat,
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
        string ScenarioRunStartedFormat,
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
        string UpdateProgressTitle,
        string UpdatePreparingStatus,
        string UpdateDownloadingStatus,
        string UpdateDownloadingProgressFormat,
        string UpdateVerifyingStatus,
        string UpdateStartingInstallerStatus,
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
    Preparing,
    Running,
    Reporting,
    Stopping,
    Completed,
    Stopped,
    ReportFailed,
    Failed,
}
