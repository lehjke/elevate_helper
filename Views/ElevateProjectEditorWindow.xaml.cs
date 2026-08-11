using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ElevateHelperWinUI.Views
{
    public sealed partial class ElevateProjectEditorWindow : Window
    {
        private const int EditorWindowWidth = 1120;
        private const int EditorWindowHeight = 840;
        private const int WorkAreaMargin = 32;
        private const double CompactLayoutBreakpoint = 760d;

        private readonly AppLocalizationService localizationService = AppLocalizationService.Instance;
        private readonly IElevateProjectEditorService projectEditorService = new ElevateProjectEditorService();
        private readonly LiftGroupRulesService liftGroupRulesService = new LiftGroupRulesService();
        private readonly ObservableCollection<BuildingFloorRowViewModel> floorRows = new ObservableCollection<BuildingFloorRowViewModel>();
        private readonly ObservableCollection<BuildingFloorRowViewModel> displayedFloorRows = new ObservableCollection<BuildingFloorRowViewModel>();
        private readonly ObservableCollection<LiftCarRowViewModel> liftRows = new ObservableCollection<LiftCarRowViewModel>();
        private readonly ObservableCollection<DispatcherOption> dispatcherOptions = new ObservableCollection<DispatcherOption>();
        private readonly CancellationTokenSource lifetimeSource = new();
        private readonly List<Control> busyDisabledControls = new();
        private readonly string workingFolder;
        private readonly BuildingType buildingType;

        private ElevateProjectEditorDocument? loadedDocument;
        private EditorSection currentSection = EditorSection.Project;
        private FloorDisplayOrder currentFloorDisplayOrder = FloorDisplayOrder.BottomFirst;
        private string preservedTrafficMode = string.Empty;
        private int preservedLearningRuns;
        private int preservedRandomSeed;
        private string preservedLogoFile = string.Empty;
        private bool suppressDirtyTracking = true;
        private bool isDirty;
        private bool isBusy;
        private bool closeAllowed;
        private bool closePromptInProgress;
        private bool dialogOpen;
        private ContentDialog? activeDialog;
        private TaskCompletionSource<bool>? busyCompletionSource;
        private bool compactLayoutApplied;
        private AppLanguage statusMessageLanguage;
        private FrameworkElement? activeValidationTarget;
        private LiftCarRowViewModel? activeServedFloorsValidationRow;
        private string? activeValidationMessage;
        private AppLanguage activeValidationMessageLanguage;

        public event EventHandler? DocumentSaved;

        public bool HasUnsavedChanges => isDirty;

        public bool IsBusy => isBusy;

        public ElevateProjectEditorWindow(string workingFolder, BuildingType buildingType)
        {
            this.workingFolder = Path.GetFullPath(workingFolder);
            this.buildingType = buildingType;

            InitializeComponent();

            FloorsItemsControl.ItemsSource = displayedFloorRows;
            LiftItemsControl.ItemsSource = liftRows;
            DispatcherComboBox.ItemsSource = dispatcherOptions;
            ConfigureDirtyTracking();

            localizationService.LanguageChanged += OnLanguageChanged;
            RootGrid.Loaded += OnRootGridLoaded;
            Closed += OnClosed;
            AppWindow.Closing += OnAppWindowClosing;

            ConfigureWindow();
            ApplyStaticContext();
            ApplyLocalizedText();
            ResetEditorState();
            statusMessageLanguage = localizationService.CurrentLanguage;
            ShowSection(EditorSection.Project);
            suppressDirtyTracking = false;
            SetDirty(false);
        }

        private AppLocalizationService.AppTextCatalog Text
        {
            get { return localizationService.CurrentText; }
        }

        private bool IsRussian => localizationService.CurrentLanguage == AppLanguage.Russian;

        private string UndoChangesLabel => IsRussian ? "Отменить изменения" : "Undo changes";

        private string BusyLabel => Text.EditorBusyStatus;

        private string UnsavedChangesTitle => IsRussian ? "Несохраненные изменения" : "Unsaved changes";

        private string UnsavedChangesMessage => IsRussian
            ? "Несохраненные изменения будут потеряны. Продолжить?"
            : "Your unsaved changes will be lost. Continue?";

        private string DiscardChangesLabel => IsRussian ? "Не сохранять" : "Discard";

        private string SelectElvxTitle => IsRussian ? "Выберите ELVX-файл" : "Select an ELVX file";

        private string SelectElvxMessage => IsRussian
            ? "В рабочей папке найдено несколько ELVX-файлов. Выберите источник."
            : "Multiple ELVX files were found in the working folder. Select a source.";

        private string OverwriteTitle => IsRussian ? "Файл уже существует" : "File already exists";

        private string OverwriteMessage(string path) => IsRussian
            ? $"Файл «{path}» уже существует. Перезаписать его?"
            : $"The file “{path}” already exists. Overwrite it?";

        private string RemoveLiftMessage(string liftTitle) => IsRussian
            ? $"Удалить «{liftTitle}»? Изменение можно отменить до сохранения."
            : $"Remove “{liftTitle}”? You can undo this change before saving.";

        private string RemoveFloorLabel => IsRussian ? "Удалить этаж" : "Remove floor";

        private string RemoveFloorMessage(string floorTitle) => IsRussian
            ? $"Удалить этаж «{floorTitle}»? Изменение можно отменить до сохранения."
            : $"Remove floor “{floorTitle}”? You can undo this change before saving.";

        private string MinimumFloorMessage => Text.EditorMinimumFloorMessage;

        private string MinimumLiftMessage => Text.EditorMinimumLiftMessage;

        private string ServedFloorsTitle => IsRussian ? "Обслуживаемые этажи" : "Floors served";

        private string SelectedStateLabel => IsRussian ? "Выбрано" : "Selected";

        private string AnalysisRuleHint => IsRussian
            ? "Алгоритм диспетчеризации задается типом здания. Здесь можно изменить число симуляций."
            : "The dispatcher algorithm follows the building type. You can change the simulation count here.";

        private string AbsenteeismRuleHint => IsRussian
            ? "Для офисного сценария правило обработки использует фиксированные 20 %."
            : "The office workflow uses a fixed 20% absenteeism rule.";

        public void AllowCloseForShutdown()
        {
            closeAllowed = true;
        }

        public async Task PrepareForShutdownAsync(CancellationToken cancellationToken)
        {
            closeAllowed = true;
            lifetimeSource.Cancel();
            activeDialog?.Hide();

            Task? busyTask = busyCompletionSource?.Task;
            if (busyTask is not null)
            {
                await busyTask.WaitAsync(cancellationToken);
            }
        }

        public async Task<bool> TryCloseAsync()
        {
            if (closeAllowed)
            {
                return true;
            }

            if (isBusy)
            {
                SetStatus(BusyLabel, InfoBarSeverity.Informational);
                Activate();
                return false;
            }

            if (!await ConfirmDiscardChangesAsync())
            {
                Activate();
                return false;
            }

            closeAllowed = true;
            Close();
            return true;
        }

        private void ConfigureWindow()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            ConfigureWindowSize(1d);
        }

        private void ConfigureWindowSize(double rasterizationScale)
        {
            DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            RectInt32 workArea = displayArea.WorkArea;
            int margin = Math.Max(WorkAreaMargin, (int)Math.Round(WorkAreaMargin * rasterizationScale));
            int width = Math.Min(
                (int)Math.Round(EditorWindowWidth * rasterizationScale),
                Math.Max(1, workArea.Width - (margin * 2)));
            int height = Math.Min(
                (int)Math.Round(EditorWindowHeight * rasterizationScale),
                Math.Max(1, workArea.Height - (margin * 2)));
            AppWindow.ResizeClient(new SizeInt32(width, height));

            int x = workArea.X + Math.Max(0, (workArea.Width - AppWindow.Size.Width) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - AppWindow.Size.Height) / 2);
            AppWindow.Move(new PointInt32(x, y));
        }

        private void ApplyStaticContext()
        {
            WorkingFolderValueTextBlock.Text = workingFolder;
            BuildingTypeValueTextBlock.Text = localizationService.FormatBuildingType(buildingType);
            AbsenteeismCard.Visibility = buildingType == BuildingType.Office ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyLocalizedText()
        {
            ClearValidationHelpText(preserveActiveValidation: true);
            Title = Text.EditorTitle;
            HeroTitleTextBlock.Text = Text.EditorTitle;
            WorkingFolderLabelTextBlock.Text = Text.EditorWorkingFolderLabel;
            BuildingTypeLabelTextBlock.Text = Text.EditorBuildingTypeLabel;
            SourceLabelTextBlock.Text = Text.EditorSourceLabel;
            OutputLabelTextBlock.Text = Text.EditorOutputLabel;

            LoadExistingButton.Content = Text.LoadEditorButton;
            LoadTemplateButton.Content = Text.LoadEditorTemplateButton;
            SaveButton.Content = Text.SaveEditorButton;
            UndoButton.Content = UndoChangesLabel;
            CloseButton.Content = Text.EditorCloseButton;

            ProjectTabButton.Content = Text.EditorProjectTabTitle;
            AnalysisTabButton.Content = Text.EditorAnalysisTabTitle;
            TrafficTabButton.Content = Text.EditorTrafficSectionTitle;
            BuildingTabButton.Content = Text.EditorBuildingTabTitle;
            LiftGroupTabButton.Content = Text.EditorLiftGroupTabTitle;

            ProjectTitleLabelTextBlock.Text = Text.EditorJobTitleHeader;
            ProjectNumberLabelTextBlock.Text = Text.EditorJobNoHeader;
            ProjectCalculationTitleLabelTextBlock.Text = Text.EditorCalculationTitleHeader;
            ProjectMadeByLabelTextBlock.Text = Text.EditorMadeByHeader;
            ProjectCheckedByLabelTextBlock.Text = Text.EditorCheckedByHeader;
            ProjectCompanyLabelTextBlock.Text = Text.EditorCompanyHeader;
            DispatcherLabelTextBlock.Text = Text.EditorDispatcherHeader;
            SimulationsLabelTextBlock.Text = Text.EditorSimulationsHeader;
            AbsenteeismLabelTextBlock.Text = Text.EditorAbsenteeismHeader;
            AnalysisRuleHintTextBlock.Text = AnalysisRuleHint;
            AbsenteeismRuleHintTextBlock.Text = AbsenteeismRuleHint;

            TrafficSplitTitleTextBlock.Text = Text.EditorTrafficSplitTitle;
            IncomingLabelTextBlock.Text = Text.EditorIncomingHeader;
            OutgoingLabelTextBlock.Text = Text.EditorOutgoingHeader;
            InterfloorLabelTextBlock.Text = Text.EditorInterfloorHeader;
            TrafficParametersTitleTextBlock.Text = Text.EditorTrafficParametersTitle;
            HandlingCapacityLabelTextBlock.Text = Text.EditorHandlingCapacityHeader;
            LoadingTimeLabelTextBlock.Text = Text.EditorLoadingTimeHeader;
            UnloadingTimeLabelTextBlock.Text = Text.EditorUnloadingTimeHeader;

            FloorNameColumnTextBlock.Text = Text.EditorFloorNameColumn;
            InterfloorHeightColumnTextBlock.Text = Text.EditorInterfloorHeightColumn;
            PopulationColumnTextBlock.Text = Text.EditorPopulationColumn;
            EntranceBiasColumnTextBlock.Text = Text.EditorEntranceBiasColumn;
            EntranceColumnTextBlock.Text = Text.EditorEntranceColumn;
            FloorActionsColumnTextBlock.Text = RemoveFloorLabel;
            AddFloorAboveButton.Content = Text.EditorAddFloorAboveButton;
            AddFloorBelowButton.Content = Text.EditorAddFloorBelowButton;
            SortTopFirstButton.Content = Text.EditorSortTopFirstButton;
            SortBottomFirstButton.Content = Text.EditorSortBottomFirstButton;

            AddLiftButton.Content = Text.EditorAddLiftButton;
            StatusInfoBar.Title = Text.StatusTitle;
            BusyTextBlock.Text = BusyLabel;
            AutomationProperties.SetName(BusyProgressRing, BusyLabel);
            if (!StatusInfoBar.IsOpen)
            {
                StatusInfoBar.Message = Text.Ready;
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
            }

            AutomationProperties.SetName(StatusInfoBar, $"{Text.StatusTitle}: {StatusInfoBar.Message}");

            ToolTipService.SetToolTip(LoadExistingButton, $"{Text.LoadEditorButton} (Ctrl+O)");
            ToolTipService.SetToolTip(SaveButton, $"{Text.SaveEditorButton} (Ctrl+S)");
            ToolTipService.SetToolTip(UndoButton, $"{UndoChangesLabel} (Ctrl+Z)");
            ToolTipService.SetToolTip(CloseButton, $"{Text.EditorCloseButton} (Ctrl+W)");

            SetAutomationName(ProjectTitleTextBox, Text.EditorJobTitleHeader);
            SetAutomationName(ProjectNumberTextBox, Text.EditorJobNoHeader);
            SetAutomationName(ProjectCalculationTitleTextBox, Text.EditorCalculationTitleHeader);
            SetAutomationName(ProjectCompanyTextBox, Text.EditorCompanyHeader);
            SetAutomationName(ProjectMadeByTextBox, Text.EditorMadeByHeader);
            SetAutomationName(ProjectCheckedByTextBox, Text.EditorCheckedByHeader);
            SetAutomationName(DispatcherComboBox, Text.EditorDispatcherHeader);
            AutomationProperties.SetHelpText(DispatcherComboBox, AnalysisRuleHint);
            SetAutomationName(SimulationCountTextBox, Text.EditorSimulationsHeader);
            SetAutomationName(AbsenteeismTextBox, Text.EditorAbsenteeismHeader);
            AutomationProperties.SetHelpText(AbsenteeismTextBox, AbsenteeismRuleHint);
            SetAutomationName(IncomingTextBox, Text.EditorIncomingHeader);
            SetAutomationName(OutgoingTextBox, Text.EditorOutgoingHeader);
            SetAutomationName(InterfloorTextBox, Text.EditorInterfloorHeader);
            SetAutomationName(HandlingCapacityTextBox, Text.EditorHandlingCapacityHeader);
            SetAutomationName(LoadingTimeTextBox, Text.EditorLoadingTimeHeader);
            SetAutomationName(UnloadingTimeTextBox, Text.EditorUnloadingTimeHeader);

            RebuildDispatcherOptions();
            ApplyLocalizationToFloorRows();
            ApplyLocalizationToLiftRows();
            RefreshLiftCountSummary();
            ApplyFloorSortButtonStyles();
            ApplySectionVisuals();
            UpdateDirtyVisuals();
        }

        private void ResetEditorState()
        {
            bool previousSuppressDirtyTracking = suppressDirtyTracking;
            suppressDirtyTracking = true;
            try
            {
                loadedDocument = null;
                currentFloorDisplayOrder = FloorDisplayOrder.BottomFirst;
                SourceValueTextBlock.Text = "-";
                OutputValueTextBlock.Text = "-";
                ProjectTitleTextBox.Text = string.Empty;
                ProjectNumberTextBox.Text = string.Empty;
                ProjectCalculationTitleTextBox.Text = string.Empty;
                ProjectMadeByTextBox.Text = string.Empty;
                ProjectCheckedByTextBox.Text = string.Empty;
                ProjectCompanyTextBox.Text = string.Empty;
                preservedLogoFile = string.Empty;
                SimulationCountTextBox.Text = "10";
                AbsenteeismTextBox.Text = buildingType == BuildingType.Office ? "20" : string.Empty;
                IncomingTextBox.Text = "100";
                OutgoingTextBox.Text = "0";
                InterfloorTextBox.Text = "0";
                HandlingCapacityTextBox.Text = "12";
                LoadingTimeTextBox.Text = "1";
                UnloadingTimeTextBox.Text = "1";

                List<ElevateProjectEditorFloor> fallbackFloors = BuildFallbackFloors();
                ApplyBuildingRows(fallbackFloors);
                ClearLiftRows();
                AddDefaultLiftRow(fallbackFloors);
                RefreshLiftCountSummary();
            }
            finally
            {
                suppressDirtyTracking = previousSuppressDirtyTracking;
                SetDirty(false);
            }
        }

        private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= OnRootGridLoaded;
            ConfigureWindowSize(RootGrid.XamlRoot?.RasterizationScale ?? 1d);
            UpdateResponsiveLayout(RootGrid.ActualWidth);
            await LoadInitialDocumentAsync();
        }

        private async Task LoadInitialDocumentAsync()
        {
            if (isBusy)
            {
                return;
            }

            CancellationToken operationToken = lifetimeSource.Token;
            SetBusy(true);
            try
            {
                IReadOnlyList<string> existingFiles = await Task.Run(GetExistingElvxPaths, operationToken);
                if (existingFiles.Count > 0)
                {
                    string? existingFilePath = await SelectExistingElvxPathAsync(existingFiles);
                    if (string.IsNullOrWhiteSpace(existingFilePath))
                    {
                        SetStatus(Text.Ready, InfoBarSeverity.Informational);
                        return;
                    }

                    ElevateProjectEditorDocument document = await Task.Run(
                        () => projectEditorService.LoadFile(existingFilePath, operationToken),
                        operationToken);
                    if (!DocumentMatchesEditorBuildingType(document))
                    {
                        return;
                    }

                    ApplyLoadedDocument(document);
                    SetStatus(
                        string.Format(localizationService.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(existingFilePath)),
                        InfoBarSeverity.Success);
                    return;
                }

                ElevateProjectEditorDocument templateDocument = await Task.Run(
                    () => projectEditorService.LoadTemplate(buildingType, operationToken),
                    operationToken);
                ApplyLoadedDocument(templateDocument);
                SetStatus(
                    string.Format(localizationService.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(templateDocument.TemplatePath ?? string.Empty)),
                    InfoBarSeverity.Informational);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnLoadExistingButtonClick(object sender, RoutedEventArgs e)
        {
            await LoadExistingDocumentAsync();
        }

        private async Task LoadExistingDocumentAsync()
        {
            if (isBusy || !await ConfirmDiscardChangesAsync())
            {
                return;
            }

            CancellationToken operationToken = lifetimeSource.Token;
            SetBusy(true);
            try
            {
                IReadOnlyList<string> existingFiles = await Task.Run(GetExistingElvxPaths, operationToken);
                if (existingFiles.Count == 0)
                {
                    SetStatus(Text.EditorExistingFileMissingMessage, InfoBarSeverity.Warning);
                    return;
                }

                string? filePath = await SelectExistingElvxPathAsync(existingFiles);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return;
                }

                ElevateProjectEditorDocument document = await Task.Run(
                    () => projectEditorService.LoadFile(filePath, operationToken),
                    operationToken);
                if (!DocumentMatchesEditorBuildingType(document))
                {
                    return;
                }

                ApplyLoadedDocument(document);
                SetStatus(string.Format(localizationService.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(filePath)), InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnLoadTemplateButtonClick(object sender, RoutedEventArgs e)
        {
            await LoadTemplateDocumentAsync();
        }

        private async Task LoadTemplateDocumentAsync()
        {
            if (isBusy || !await ConfirmDiscardChangesAsync())
            {
                return;
            }

            CancellationToken operationToken = lifetimeSource.Token;
            try
            {
                SetBusy(true);
                ElevateProjectEditorDocument document = await Task.Run(
                    () => projectEditorService.LoadTemplate(buildingType, operationToken),
                    operationToken);
                ApplyLoadedDocument(document);
                SetStatus(string.Format(localizationService.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(document.TemplatePath ?? string.Empty)), InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnSaveButtonClick(object sender, RoutedEventArgs e)
        {
            await SaveDocumentAsync();
        }

        private async Task SaveDocumentAsync()
        {
            if (isBusy)
            {
                return;
            }

            if (loadedDocument == null)
            {
                SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
                return;
            }

            ElevateProjectEditorDocument? document;
            if (!TryBuildDocument(out document) || document == null)
            {
                return;
            }

            CancellationToken operationToken = lifetimeSource.Token;
            try
            {
                string outputPath = ResolveOutputPath(document);
                bool savesBackToLoadedSource = PathsEqual(outputPath, loadedDocument.SourcePath);
                if (File.Exists(outputPath) && !savesBackToLoadedSource && !await ConfirmOverwriteAsync(outputPath))
                {
                    return;
                }

                SetBusy(true);
                ProcessingResult result = await Task.Run(
                    () => projectEditorService.SaveAsync(document, outputPath, operationToken),
                    operationToken);
                if (!result.Success)
                {
                    SetStatus(
                        localizationService.TranslateRuntimeMessage(result.Message),
                        InfoBarSeverity.Error);
                    return;
                }

                ElevateProjectEditorDocument refreshedDocument = await Task.Run(
                    () => projectEditorService.LoadFile(outputPath, operationToken),
                    operationToken);
                ApplyLoadedDocument(refreshedDocument);
                DocumentSaved?.Invoke(this, EventArgs.Empty);
                SetStatus(string.Format(localizationService.CurrentCulture, Text.EditorSaveSuccessFormat, Path.GetFileName(outputPath)), InfoBarSeverity.Success);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnProjectTabButtonClick(object sender, RoutedEventArgs e)
        {
            ShowSection(EditorSection.Project);
        }

        private void OnAnalysisTabButtonClick(object sender, RoutedEventArgs e)
        {
            ShowSection(EditorSection.Analysis);
        }

        private void OnTrafficTabButtonClick(object sender, RoutedEventArgs e)
        {
            ShowSection(EditorSection.Traffic);
        }

        private void OnBuildingTabButtonClick(object sender, RoutedEventArgs e)
        {
            ShowSection(EditorSection.Building);
        }

        private void OnAddFloorAboveButtonClick(object sender, RoutedEventArgs e)
        {
            AddFloor(isTopFloor: true);
        }

        private void OnAddFloorBelowButtonClick(object sender, RoutedEventArgs e)
        {
            AddFloor(isTopFloor: false);
        }

        private async void OnRemoveFloorButtonClick(object sender, RoutedEventArgs e)
        {
            if (floorRows.Count <= 1)
            {
                SetValidationStatus(MinimumFloorMessage, EditorSection.Building, FloorsItemsControl);
                return;
            }

            BuildingFloorRowViewModel? row = (sender as Button)?.Tag as BuildingFloorRowViewModel;
            if (row is null || !await ConfirmRemoveFloorAsync(row))
            {
                return;
            }

            int removedIndex = floorRows.IndexOf(row);
            if (removedIndex < 0)
            {
                return;
            }

            int removedFloorIndex = removedIndex + 1;
            row.PropertyChanged -= OnBuildingFloorRowPropertyChanged;
            floorRows.RemoveAt(removedIndex);

            if (removedIndex == 0 && floorRows.Count > 0)
            {
                floorRows[0].InterfloorHeightText = "0";
            }

            IReadOnlyList<ElevateProjectEditorFloor> remainingFloors = BuildFloorDraft();
            foreach (LiftCarRowViewModel liftRow in liftRows)
            {
                ServedFloorRowViewModel? removedServedFloor = liftRow.ServedFloors.FirstOrDefault(floor => floor.FloorIndex == removedFloorIndex);
                if (removedServedFloor is not null)
                {
                    removedServedFloor.PropertyChanged -= OnServedFloorPropertyChanged;
                    liftRow.ServedFloors.Remove(removedServedFloor);
                }

                for (int floorIndex = 0; floorIndex < liftRow.ServedFloors.Count && floorIndex < floorRows.Count; floorIndex++)
                {
                    ServedFloorRowViewModel servedFloor = liftRow.ServedFloors[floorIndex];
                    servedFloor.FloorIndex = floorIndex + 1;
                    servedFloor.FloorName = floorRows[floorIndex].FloorName;
                }

                int homeFloor = ParseIntOrDefault(liftRow.HomeFloor, 1);
                if (homeFloor > removedFloorIndex)
                {
                    liftRow.HomeFloor = (homeFloor - 1).ToString(CultureInfo.InvariantCulture);
                }
                else if (homeFloor == removedFloorIndex)
                {
                    liftRow.HomeFloor = ResolveFallbackHomeFloor(remainingFloors);
                }
            }

            RebuildDisplayedFloorRows();
            SyncLiftRowsWithFloors();
            MarkDirty();

            BuildingFloorRowViewModel nextRow = floorRows[Math.Min(removedIndex, floorRows.Count - 1)];
            _ = DispatcherQueue.TryEnqueue(() => FocusFloorRow(nextRow));
        }

        private void OnSortTopFirstButtonClick(object sender, RoutedEventArgs e)
        {
            currentFloorDisplayOrder = FloorDisplayOrder.TopFirst;
            RebuildDisplayedFloorRows();
            ApplyFloorSortButtonStyles();
        }

        private void OnSortBottomFirstButtonClick(object sender, RoutedEventArgs e)
        {
            currentFloorDisplayOrder = FloorDisplayOrder.BottomFirst;
            RebuildDisplayedFloorRows();
            ApplyFloorSortButtonStyles();
        }

        private void OnLiftGroupTabButtonClick(object sender, RoutedEventArgs e)
        {
            ShowSection(EditorSection.LiftGroup);
        }

        private void ShowSection(EditorSection section)
        {
            currentSection = section;
            ProjectPanel.Visibility = section == EditorSection.Project ? Visibility.Visible : Visibility.Collapsed;
            AnalysisPanel.Visibility = section == EditorSection.Analysis ? Visibility.Visible : Visibility.Collapsed;
            TrafficPanel.Visibility = section == EditorSection.Traffic ? Visibility.Visible : Visibility.Collapsed;
            BuildingPanel.Visibility = section == EditorSection.Building ? Visibility.Visible : Visibility.Collapsed;
            LiftGroupPanel.Visibility = section == EditorSection.LiftGroup ? Visibility.Visible : Visibility.Collapsed;
            ApplySectionVisuals();
        }

        private void ApplySectionVisuals()
        {
            SetTabButtonStyle(ProjectTabButton, currentSection == EditorSection.Project);
            SetTabButtonStyle(AnalysisTabButton, currentSection == EditorSection.Analysis);
            SetTabButtonStyle(TrafficTabButton, currentSection == EditorSection.Traffic);
            SetTabButtonStyle(BuildingTabButton, currentSection == EditorSection.Building);
            SetTabButtonStyle(LiftGroupTabButton, currentSection == EditorSection.LiftGroup);
        }

        private void SetTabButtonStyle(Button button, bool isSelected)
        {
            if (Application.Current.Resources.TryGetValue(isSelected ? "PrimaryActionButtonStyle" : "ActionButtonStyle", out object styleObject) &&
                styleObject is Style style)
            {
                button.Style = style;
            }

            AutomationProperties.SetItemStatus(button, isSelected ? SelectedStateLabel : string.Empty);
        }

        private void ApplyFloorSortButtonStyles()
        {
            SetTabButtonStyle(SortTopFirstButton, currentFloorDisplayOrder == FloorDisplayOrder.TopFirst);
            SetTabButtonStyle(SortBottomFirstButton, currentFloorDisplayOrder == FloorDisplayOrder.BottomFirst);
        }

        private void OnAddLiftButtonClick(object sender, RoutedEventArgs e)
        {
            AddDefaultLiftRow(BuildFloorDraft());
            MarkDirty();
            LiftCarRowViewModel? row = liftRows.LastOrDefault();
            if (row is not null)
            {
                _ = DispatcherQueue.TryEnqueue(() => FocusLiftRow(row));
            }
        }

        private async void OnRemoveLiftCardButtonClick(object sender, RoutedEventArgs e)
        {
            if (liftRows.Count <= 1)
            {
                SetValidationStatus(MinimumLiftMessage, EditorSection.LiftGroup, LiftItemsControl);
                return;
            }

            Button? button = sender as Button;
            LiftCarRowViewModel? row = button?.Tag as LiftCarRowViewModel;
            if (row == null)
            {
                return;
            }

            if (!await ConfirmRemoveLiftAsync(row))
            {
                return;
            }

            DetachLiftRow(row);
            liftRows.Remove(row);
            RefreshLiftTitles();
            RefreshLiftCountSummary();
            RebuildLiftGroupTables();
            MarkDirty();
            AddLiftButton.Focus(FocusState.Programmatic);
        }

        private void OnUndoButtonClick(object sender, RoutedEventArgs e)
        {
            UndoChanges();
        }

        private void UndoChanges()
        {
            if (!HasUnsavedChanges || loadedDocument is null || isBusy)
            {
                return;
            }

            ApplyLoadedDocument(loadedDocument);
            SetStatus(Text.Ready, InfoBarSeverity.Informational);
        }

        private void OnEditorFieldTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is DependencyObject changedElement &&
                !ReferenceEquals(sender, AbsenteeismTextBox))
            {
                AutomationProperties.SetHelpText(changedElement, string.Empty);
            }

            if (ReferenceEquals(sender, IncomingTextBox) ||
                ReferenceEquals(sender, OutgoingTextBox) ||
                ReferenceEquals(sender, InterfloorTextBox))
            {
                AutomationProperties.SetHelpText(IncomingTextBox, string.Empty);
                AutomationProperties.SetHelpText(OutgoingTextBox, string.Empty);
                AutomationProperties.SetHelpText(InterfloorTextBox, string.Empty);
            }

            if (ReferenceEquals(sender, ProjectTitleTextBox) || ReferenceEquals(sender, ProjectNumberTextBox))
            {
                UpdateOutputPreview();
            }

            MarkDirty();
        }

        private void OnDispatcherSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MarkDirty();
        }

        private async void OnSaveKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await SaveDocumentAsync();
        }

        private async void OnOpenKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await LoadExistingDocumentAsync();
        }

        private void OnCloseKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            Close();
        }

        private void OnUndoKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            UndoChanges();
        }

        private void ApplyLoadedDocument(ElevateProjectEditorDocument document)
        {
            bool previousSuppressDirtyTracking = suppressDirtyTracking;
            suppressDirtyTracking = true;
            try
            {
                loadedDocument = document;
                preservedTrafficMode = document.Analysis.TrafficMode;
                preservedLearningRuns = document.Analysis.LearningRuns;
                preservedRandomSeed = document.Analysis.RandomSeed;
                preservedLogoFile = document.Job.LogoFile;

                SourceValueTextBlock.Text = document.SourcePath ?? document.TemplatePath ?? "-";
                ProjectTitleTextBox.Text = document.Job.Title;
                ProjectNumberTextBox.Text = document.Job.Number;
                ProjectCalculationTitleTextBox.Text = document.Job.CalculationTitle;
                ProjectMadeByTextBox.Text = document.Job.MadeBy;
                ProjectCheckedByTextBox.Text = document.Job.CheckedBy;
                ProjectCompanyTextBox.Text = document.Job.Company;
                SimulationCountTextBox.Text = document.Analysis.SimulationsPerConfiguration.ToString(CultureInfo.InvariantCulture);
                AbsenteeismTextBox.Text = FormatEditableNumber(ResolveEffectiveAbsenteeismPercent());
                IncomingTextBox.Text = FormatEditableNumber(document.Traffic.IncomingPercent);
                OutgoingTextBox.Text = FormatEditableNumber(document.Traffic.OutgoingPercent);
                InterfloorTextBox.Text = FormatEditableNumber(document.Traffic.InterfloorPercent);
                HandlingCapacityTextBox.Text = FormatEditableNumber(document.Traffic.HandlingCapacity);
                LoadingTimeTextBox.Text = FormatEditableNumber(document.Traffic.LoadingTimeSeconds);
                UnloadingTimeTextBox.Text = FormatEditableNumber(document.Traffic.UnloadingTimeSeconds);

                ApplyDispatcherSelection(ResolveEffectiveDispatcherAlgorithmName());
                ApplyBuildingRows(document.Floors);
                ApplyLiftRows(document);
                UpdateOutputPreview();
            }
            finally
            {
                suppressDirtyTracking = previousSuppressDirtyTracking;
                SetDirty(false);
            }
        }

        private bool DocumentMatchesEditorBuildingType(ElevateProjectEditorDocument document)
        {
            if (document.BuildingType == buildingType)
            {
                return true;
            }

            string fileType = localizationService.FormatBuildingType(document.BuildingType);
            string editorType = localizationService.FormatBuildingType(buildingType);
            SetStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorBuildingTypeMismatchFormat,
                    fileType,
                    editorType),
                InfoBarSeverity.Error);
            return false;
        }

        private void ApplyDispatcherSelection(string algorithmName)
        {
            DispatcherOption? option = dispatcherOptions.FirstOrDefault(candidate => string.Equals(candidate.Value, algorithmName, StringComparison.OrdinalIgnoreCase));
            DispatcherComboBox.SelectedItem = option ?? dispatcherOptions.FirstOrDefault();
        }

        private string ResolveEffectiveDispatcherAlgorithmName()
        {
            return buildingType == BuildingType.Office
                ? "Mixed Control (Enhanced ACA)"
                : "Group Collective";
        }

        private double ResolveEffectiveAbsenteeismPercent()
        {
            return buildingType == BuildingType.Office ? 20d : 0d;
        }

        private void ApplyBuildingRows(IReadOnlyList<ElevateProjectEditorFloor> floors)
        {
            foreach (BuildingFloorRowViewModel existingRow in floorRows)
            {
                existingRow.PropertyChanged -= OnBuildingFloorRowPropertyChanged;
            }

            floorRows.Clear();
            displayedFloorRows.Clear();
            double previousLevel = 0d;
            for (int i = 0; i < floors.Count; i++)
            {
                ElevateProjectEditorFloor floor = floors[i];
                double interfloorHeight = i == 0 ? floor.FloorLevel : floor.FloorLevel - previousLevel;
                previousLevel = floor.FloorLevel;
                BuildingFloorRowViewModel row = new BuildingFloorRowViewModel
                {
                    SourceFloorName = floor.SourceFloorName,
                    FloorName = floor.FloorName,
                    InterfloorHeightText = FormatEditableNumber(interfloorHeight),
                    PopulationText = FormatEditableNumber(floor.Population),
                    EntranceBiasText = FormatEditableNumber(floor.EntranceBiasPercent),
                    EntranceFloor = floor.EntranceFloor,
                };
                ApplyLocalizationToFloorRow(row);
                row.PropertyChanged += OnBuildingFloorRowPropertyChanged;
                floorRows.Add(row);
            }

            RebuildDisplayedFloorRows();
            SyncLiftRowsWithFloors();
        }

        private void ApplyLocalizationToFloorRows()
        {
            foreach (BuildingFloorRowViewModel row in floorRows)
            {
                ApplyLocalizationToFloorRow(row);
            }
        }

        private void ApplyLocalizationToFloorRow(BuildingFloorRowViewModel row)
        {
            row.FloorNameLabel = Text.EditorFloorNameColumn;
            row.InterfloorHeightLabel = Text.EditorInterfloorHeightColumn;
            row.PopulationLabel = Text.EditorPopulationColumn;
            row.EntranceBiasLabel = Text.EditorEntranceBiasColumn;
            row.EntranceLabel = Text.EditorEntranceColumn;
            row.RemoveFloorLabel = RemoveFloorLabel;
        }

        private void RebuildDisplayedFloorRows()
        {
            displayedFloorRows.Clear();
            IEnumerable<BuildingFloorRowViewModel> orderedRows = currentFloorDisplayOrder == FloorDisplayOrder.TopFirst
                ? floorRows.Reverse()
                : floorRows;

            foreach (BuildingFloorRowViewModel row in orderedRows)
            {
                displayedFloorRows.Add(row);
            }
        }

        private void AddFloor(bool isTopFloor)
        {
            BuildingFloorRowViewModel seed = isTopFloor
                ? floorRows.LastOrDefault() ?? CreateDefaultFloorRow()
                : floorRows.FirstOrDefault() ?? CreateDefaultFloorRow();
            string suggestedInterfloorHeight = ResolveSuggestedInterfloorHeightText(seed.InterfloorHeightText);

            BuildingFloorRowViewModel newRow = new BuildingFloorRowViewModel
            {
                SourceFloorName = string.Empty,
                FloorName = SuggestFloorName(isTopFloor),
                InterfloorHeightText = isTopFloor ? suggestedInterfloorHeight : "0",
                PopulationText = isTopFloor ? seed.PopulationText : "0",
                EntranceBiasText = "0",
                EntranceFloor = false,
            };
            ApplyLocalizationToFloorRow(newRow);
            newRow.PropertyChanged += OnBuildingFloorRowPropertyChanged;

            if (isTopFloor)
            {
                floorRows.Add(newRow);
                foreach (LiftCarRowViewModel liftRow in liftRows)
                {
                    liftRow.ServedFloors.Add(new ServedFloorRowViewModel
                    {
                        FloorIndex = floorRows.Count,
                        FloorName = newRow.FloorName,
                        IsServed = true,
                    });
                }
            }
            else
            {
                BuildingFloorRowViewModel? previousBottomRow = floorRows.FirstOrDefault();
                floorRows.Insert(0, newRow);
                if (previousBottomRow is not null)
                {
                    previousBottomRow.InterfloorHeightText = suggestedInterfloorHeight;
                }

                foreach (LiftCarRowViewModel liftRow in liftRows)
                {
                    foreach (ServedFloorRowViewModel servedFloor in liftRow.ServedFloors)
                    {
                        servedFloor.FloorIndex += 1;
                    }

                    liftRow.ServedFloors.Insert(0, new ServedFloorRowViewModel
                    {
                        FloorIndex = 1,
                        FloorName = newRow.FloorName,
                        IsServed = true,
                    });

                    int parsedHomeFloor = ParseIntOrDefault(liftRow.HomeFloor, 1);
                    liftRow.HomeFloor = (parsedHomeFloor + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            RebuildDisplayedFloorRows();
            SyncLiftRowsWithFloors();
            MarkDirty();
            _ = DispatcherQueue.TryEnqueue(() => FocusFloorRow(newRow));
        }

        private void OnBuildingFloorRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            AutomationProperties.SetHelpText(FloorsItemsControl, string.Empty);
            ClearValidationHelpTextInTree(FloorsItemsControl);
            MarkDirty();
            if (e.PropertyName == nameof(BuildingFloorRowViewModel.FloorName))
            {
                SyncLiftRowsWithFloors();
            }
        }

        private void SyncLiftRowsWithFloors()
        {
            IReadOnlyList<ElevateProjectEditorFloor> floors = BuildFloorDraft();
            for (int liftIndex = 0; liftIndex < liftRows.Count; liftIndex++)
            {
                LiftCarRowViewModel liftRow = liftRows[liftIndex];
                HashSet<int> servedFloorIndexes = liftRow.ServedFloors
                    .Where(floor => floor.IsServed)
                    .Select(floor => floor.FloorIndex)
                    .ToHashSet();

                foreach (ServedFloorRowViewModel existingFloor in liftRow.ServedFloors)
                {
                    existingFloor.PropertyChanged -= OnServedFloorPropertyChanged;
                }

                liftRow.ServedFloors.Clear();
                for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
                {
                    ElevateProjectEditorFloor floor = floors[floorIndex];
                    ServedFloorRowViewModel servedFloor = new ServedFloorRowViewModel
                    {
                        FloorIndex = floorIndex + 1,
                        FloorName = floor.FloorName,
                        IsServed = servedFloorIndexes.Count == 0 || servedFloorIndexes.Contains(floorIndex + 1),
                    };
                    servedFloor.PropertyChanged += OnServedFloorPropertyChanged;
                    liftRow.ServedFloors.Add(servedFloor);
                }

                int homeFloor = ParseIntOrDefault(liftRow.HomeFloor, ParseIntOrDefault(ResolveFallbackHomeFloor(floors), 1));
                if (homeFloor < 1 || homeFloor > floors.Count)
                {
                    liftRow.HomeFloor = ResolveFallbackHomeFloor(floors);
                }
            }

            RebuildLiftGroupTables();
        }

        private BuildingFloorRowViewModel CreateDefaultFloorRow()
        {
            BuildingFloorRowViewModel row = new BuildingFloorRowViewModel
            {
                SourceFloorName = string.Empty,
                FloorName = IsRussian ? "Этаж 1" : "Level 1",
                InterfloorHeightText = "3.9",
                PopulationText = "150",
                EntranceBiasText = "100",
                EntranceFloor = true,
            };
            ApplyLocalizationToFloorRow(row);
            return row;
        }

        private string SuggestFloorName(bool isTopFloor)
        {
            List<int> numericLevels = floorRows
                .Select(row => TryExtractTrailingInteger(row.FloorName))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            if (numericLevels.Count > 0)
            {
                int nextValue = isTopFloor ? numericLevels.Max() + 1 : numericLevels.Min() - 1;
                return IsRussian ? $"Этаж {nextValue}" : $"Level {nextValue}";
            }

            return isTopFloor
                ? (IsRussian ? $"Этаж {floorRows.Count + 1}" : $"Level {floorRows.Count + 1}")
                : (IsRussian ? $"Этаж {Math.Min(0, 1 - floorRows.Count)}" : $"Level {Math.Min(0, 1 - floorRows.Count)}");
        }

        private string ResolveSuggestedInterfloorHeightText(string? preferredValue)
        {
            if (TryParseFlexibleDoubleInternal(preferredValue, out double parsedPreferredValue) &&
                parsedPreferredValue > 0)
            {
                return FormatEditableNumber(parsedPreferredValue);
            }

            double fallbackValue = floorRows
                .Select(row => ParseFlexibleDouble(row.InterfloorHeightText))
                .FirstOrDefault(value => value > 0);
            if (fallbackValue <= 0)
            {
                fallbackValue = 3.9d;
            }

            return FormatEditableNumber(fallbackValue);
        }

        private void ApplyLiftRows(ElevateProjectEditorDocument document)
        {
            ClearLiftRows();
            foreach (ElevateProjectEditorCar car in document.Cars)
            {
                LiftCarRowViewModel row = CreateLiftRow(car, document.Floors, null);
                AttachLiftRow(row);
                liftRows.Add(row);
            }

            if (liftRows.Count == 0)
            {
                AddDefaultLiftRow(document.Floors);
            }
            else
            {
                RefreshLiftTitles();
                RefreshLiftCountSummary();
                RebuildLiftGroupTables();
            }
        }

        private LiftCarRowViewModel CreateLiftRow(ElevateProjectEditorCar car, IReadOnlyList<ElevateProjectEditorFloor> floors, LiftCarRowViewModel? source)
        {
            Tuple<int, int> fallbackCabinDimensions = source == null
                ? EstimateCabinDimensions(car.FloorAreaM2)
                : Tuple.Create(ParseIntOrDefault(source.CabWidthText, 1600), ParseIntOrDefault(source.CabHeightText, 2100));
            int cabinWidth = source == null && car.CabinWidthMm > 0
                ? car.CabinWidthMm
                : fallbackCabinDimensions.Item1;
            int cabinDepth = source == null && car.CabinDepthMm > 0
                ? car.CabinDepthMm
                : fallbackCabinDimensions.Item2;

            DoorOpeningKind openingKind;
            int doorWidth;
            if (source == null && car.DoorWidthMm > 0)
            {
                openingKind = car.DoorOpeningKind;
                doorWidth = car.DoorWidthMm;
            }
            else if (source == null)
            {
                if (!liftGroupRulesService.TryResolveDoorSpecification(
                        car.DoorPreOpening,
                        car.DoorOpenTime,
                        car.DoorCloseTime,
                        out doorWidth,
                        out openingKind))
                {
                    openingKind = DoorOpeningKind.Central;
                    doorWidth = 1000;
                }
            }
            else
            {
                openingKind = source.SelectedDoorOpening?.Kind ?? DoorOpeningKind.Central;
                doorWidth = source.SelectedDoorWidth;
            }

            List<int> servedIndexes = car.ServedFloorIndexes != null && car.ServedFloorIndexes.Count > 0
                ? new List<int>(car.ServedFloorIndexes)
                : Enumerable.Range(1, floors.Count).ToList();

            LiftCarRowViewModel row = new LiftCarRowViewModel
            {
                Id = string.IsNullOrWhiteSpace(car.Id) ? (liftRows.Count + 1).ToString(CultureInfo.InvariantCulture) : car.Id,
                HomeShaft = ResolveAvailableHomeShaft(car.HomeShaft),
                TemplateXml = car.TemplateXml,
                CapacityOption = NormalizeCapacity(car.CapacityKg),
                CabWidthText = cabinWidth.ToString(CultureInfo.InvariantCulture),
                CabHeightText = cabinDepth.ToString(CultureInfo.InvariantCulture),
                SpeedOption = NormalizeSpeed(car.Speed),
                SelectedDoorWidth = doorWidth,
                HomeFloor = ResolveHomeFloor(car.HomeFloor, floors),
            };

            foreach (string option in liftGroupRulesService.GetCapacityOptions())
            {
                row.CapacityOptions.Add(option);
            }
            AddCurrentOptionIfMissing(row.CapacityOptions, row.CapacityOption);

            foreach (string option in liftGroupRulesService.GetSpeedOptions())
            {
                row.SpeedOptions.Add(option);
            }
            AddCurrentOptionIfMissing(row.SpeedOptions, row.SpeedOption);

            foreach (int option in liftGroupRulesService.GetDoorWidthOptions())
            {
                row.DoorWidthOptions.Add(option);
            }

            for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
            {
                ElevateProjectEditorFloor floor = floors[floorIndex];
                row.ServedFloors.Add(new ServedFloorRowViewModel
                {
                    FloorIndex = floorIndex + 1,
                    FloorName = floor.FloorName,
                    IsServed = servedIndexes.Contains(floorIndex + 1),
                });
            }

            ApplyLocalizationToLiftRow(row, openingKind);
            return row;
        }

        private void AddDefaultLiftRow(IReadOnlyList<ElevateProjectEditorFloor> floors)
        {
            LiftCarRowViewModel row;
            if (liftRows.Count > 0)
            {
                row = CloneLiftRow(liftRows[liftRows.Count - 1]);
            }
            else
            {
                List<ElevateProjectEditorFloor> effectiveFloors = floors.Count > 0 ? new List<ElevateProjectEditorFloor>(floors) : BuildFloorDraft();
                if (effectiveFloors.Count == 0)
                {
                    effectiveFloors = BuildFallbackFloors();
                }

                ElevateProjectEditorCar baselineCar = loadedDocument?.Cars.LastOrDefault() ?? new ElevateProjectEditorCar
                {
                    Id = "1",
                    HomeShaft = "1",
                    CabinWidthMm = 1600,
                    CabinDepthMm = 2100,
                    CapacityKg = "1050.000000",
                    FloorAreaM2 = liftGroupRulesService.ResolveCarAreaSquareMeters(1600, 2100).ToString("0.000000", CultureInfo.InvariantCulture),
                    Speed = "2.500000",
                    Acceleration = "0.900000",
                    Jerk = "1.000000",
                    DoorPreOpening = "0.500000",
                    DoorWidthMm = 1000,
                    DoorOpeningKind = DoorOpeningKind.Central,
                    DoorOpenTime = "1.800000",
                    DoorCloseTime = "2.900000",
                    HomeFloor = ResolveFallbackHomeFloor(effectiveFloors),
                    ServedFloorIndexes = Enumerable.Range(1, effectiveFloors.Count).ToList(),
                };

                row = CreateLiftRow(baselineCar, effectiveFloors, null);
            }

            AttachLiftRow(row);
            liftRows.Add(row);
            RefreshLiftTitles();
            RefreshLiftCountSummary();
            RebuildLiftGroupTables();
        }

        private LiftCarRowViewModel CloneLiftRow(LiftCarRowViewModel source)
        {
            LiftCarRowViewModel clone = new LiftCarRowViewModel
            {
                Id = (liftRows.Count + 1).ToString(CultureInfo.InvariantCulture),
                HomeShaft = ResolveNextHomeShaft(),
                TemplateXml = source.TemplateXml,
                CapacityOption = source.CapacityOption,
                CabWidthText = source.CabWidthText,
                CabHeightText = source.CabHeightText,
                SpeedOption = source.SpeedOption,
                SelectedDoorWidth = source.SelectedDoorWidth,
                HomeFloor = source.HomeFloor,
            };

            foreach (string option in source.CapacityOptions)
            {
                clone.CapacityOptions.Add(option);
            }

            foreach (string option in source.SpeedOptions)
            {
                clone.SpeedOptions.Add(option);
            }

            foreach (int option in source.DoorWidthOptions)
            {
                clone.DoorWidthOptions.Add(option);
            }

            foreach (ServedFloorRowViewModel floor in source.ServedFloors)
            {
                clone.ServedFloors.Add(new ServedFloorRowViewModel
                {
                    FloorIndex = floor.FloorIndex,
                    FloorName = floor.FloorName,
                    IsServed = floor.IsServed,
                });
            }

            ApplyLocalizationToLiftRow(clone, source.SelectedDoorOpening?.Kind ?? DoorOpeningKind.Central);
            return clone;
        }

        private void ApplyLocalizationToLiftRows()
        {
            foreach (LiftCarRowViewModel row in liftRows)
            {
                ApplyLocalizationToLiftRow(row, row.SelectedDoorOpening?.Kind ?? DoorOpeningKind.Central);
            }

            RefreshLiftTitles();
            RebuildLiftGroupTables();
        }

        private void ApplyLocalizationToLiftRow(LiftCarRowViewModel row, DoorOpeningKind selectedKind)
        {
            row.CapacityLabel = Text.EditorCapacityHeader;
            row.CabWidthLabel = Text.EditorCabWidthHeader;
            row.CabHeightLabel = Text.EditorCabHeightHeader;
            row.SpeedLabel = Text.EditorSpeedHeader;
            row.DoorWidthLabel = Text.EditorDoorWidthHeader;
            row.DoorOpeningLabel = Text.EditorDoorOpeningHeader;
            row.HomeFloorLabel = Text.EditorCarHomeFloorLabel;
            row.ServedFloorsLabel = ServedFloorsTitle;
            row.RemoveButtonLabel = Text.EditorRemoveLiftButton;

            row.DoorOpeningOptions.Clear();
            row.DoorOpeningOptions.Add(new DoorOpeningOption(DoorOpeningKind.Central, Text.EditorDoorOpeningCentral));
            row.DoorOpeningOptions.Add(new DoorOpeningOption(DoorOpeningKind.Telescopic, Text.EditorDoorOpeningTelescopic));
            row.SelectedDoorOpening = row.DoorOpeningOptions.FirstOrDefault(option => option.Kind == selectedKind) ?? row.DoorOpeningOptions.FirstOrDefault();
        }

        private void RebuildLiftGroupTables()
        {
            LiftGroupTablesPanel.Children.Clear();
            if (liftRows.Count == 0)
            {
                return;
            }

            LiftGroupTablesPanel.Children.Add(CreateServedFloorsTableCard());
        }

        private Border CreateServedFloorsTableCard()
        {
            Grid table = CreateLiftTable(labelColumnWidth: 220, valueColumnWidth: 86);
            AddLiftTableHeader(table, Text.EditorFloorNameColumn);

            int rowIndex = 1;
            foreach (BuildingFloorRowViewModel floorRow in floorRows)
            {
                EnsureGridRow(table, rowIndex);
                AddTableLabel(table, rowIndex, floorRow.FloorName);
                for (int liftIndex = 0; liftIndex < liftRows.Count; liftIndex++)
                {
                    ServedFloorRowViewModel? servedFloor = liftRows[liftIndex].ServedFloors.FirstOrDefault(floor => floor.FloorIndex == rowIndex);
                    if (servedFloor is not null)
                    {
                        FrameworkElement cell = CreateServiceCell(servedFloor, liftRows[liftIndex], floorRow.FloorName);
                        Grid.SetRow(cell, rowIndex);
                        Grid.SetColumn(cell, liftIndex + 1);
                        table.Children.Add(cell);
                    }
                }

                rowIndex++;
            }

            return CreateTableCard(ServedFloorsTitle, table);
        }

        private Grid CreateLiftTable(double labelColumnWidth, double valueColumnWidth)
        {
            Grid table = new()
            {
                ColumnSpacing = 8,
                RowSpacing = 8,
            };
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelColumnWidth) });
            foreach (LiftCarRowViewModel _ in liftRows)
            {
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueColumnWidth) });
            }

            return table;
        }

        private void AddLiftTableHeader(Grid table, string labelHeader)
        {
            EnsureGridRow(table, 0);
            AddHeaderCell(table, 0, 0, labelHeader);
            for (int index = 0; index < liftRows.Count; index++)
            {
                TextBlock title = new()
                {
                    MaxWidth = 72,
                    Text = liftRows[index].Title,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetRow(title, 0);
                Grid.SetColumn(title, index + 1);
                table.Children.Add(title);
            }
        }

        private Border CreateTableCard(string title, Grid table)
        {
            StackPanel panel = new()
            {
                Spacing = 12,
            };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            panel.Children.Add(table);

            Border border = new()
            {
                Child = panel,
                Style = ResolveStyle("SectionCardStyle"),
            };
            return border;
        }

        private static void EnsureGridRow(Grid table, int rowIndex)
        {
            while (table.RowDefinitions.Count <= rowIndex)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }

        private void AddHeaderCell(Grid table, int rowIndex, int columnIndex, string text)
        {
            TextBlock header = new()
            {
                Text = text,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            Grid.SetRow(header, rowIndex);
            Grid.SetColumn(header, columnIndex);
            table.Children.Add(header);
        }

        private void AddTableLabel(Grid table, int rowIndex, string text)
        {
            TextBlock label = new()
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            Grid.SetRow(label, rowIndex);
            Grid.SetColumn(label, 0);
            table.Children.Add(label);
        }

        private FrameworkElement CreateServiceCell(ServedFloorRowViewModel servedFloor, LiftCarRowViewModel lift, string floorName)
        {
            CheckBox checkBox = new()
            {
                MinWidth = 40,
                MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Tag = lift,
                UseSystemFocusVisuals = true,
            };
            checkBox.SetBinding(CheckBox.IsCheckedProperty, CreateTwoWayBinding(servedFloor, nameof(ServedFloorRowViewModel.IsServed)));
            AutomationProperties.SetName(checkBox, $"{ServedFloorsTitle}: {lift.Title}, {floorName}");
            ToolTipService.SetToolTip(checkBox, $"{lift.Title} — {floorName}");
            return checkBox;
        }

        private void ClearLiftRows()
        {
            foreach (LiftCarRowViewModel row in liftRows)
            {
                DetachLiftRow(row);
            }

            liftRows.Clear();
        }

        private void AttachLiftRow(LiftCarRowViewModel row)
        {
            row.PropertyChanged -= OnLiftRowPropertyChanged;
            row.PropertyChanged += OnLiftRowPropertyChanged;
            foreach (ServedFloorRowViewModel floor in row.ServedFloors)
            {
                floor.PropertyChanged -= OnServedFloorPropertyChanged;
                floor.PropertyChanged += OnServedFloorPropertyChanged;
            }
        }

        private void DetachLiftRow(LiftCarRowViewModel row)
        {
            row.PropertyChanged -= OnLiftRowPropertyChanged;
            foreach (ServedFloorRowViewModel floor in row.ServedFloors)
            {
                floor.PropertyChanged -= OnServedFloorPropertyChanged;
            }
        }

        private void OnLiftRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(LiftCarRowViewModel.CapacityOption)
                or nameof(LiftCarRowViewModel.CabWidthText)
                or nameof(LiftCarRowViewModel.CabHeightText)
                or nameof(LiftCarRowViewModel.SpeedOption)
                or nameof(LiftCarRowViewModel.SelectedDoorWidth)
                or nameof(LiftCarRowViewModel.SelectedDoorOpening)
                or nameof(LiftCarRowViewModel.HomeFloor))
            {
                AutomationProperties.SetHelpText(LiftItemsControl, string.Empty);
                ClearValidationHelpTextInTree(LiftItemsControl);
                MarkDirty();
            }
        }

        private void OnServedFloorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServedFloorRowViewModel.IsServed))
            {
                AutomationProperties.SetHelpText(LiftItemsControl, string.Empty);
                ClearValidationHelpTextInTree(LiftGroupTablesPanel);
                MarkDirty();
            }
        }

        private static Binding CreateTwoWayBinding(object source, string path)
        {
            return new Binding
            {
                Source = source,
                Path = new PropertyPath(path),
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            };
        }

        private static Style? ResolveStyle(string key)
        {
            return Application.Current.Resources.TryGetValue(key, out object styleObject) && styleObject is Style style
                ? style
                : null;
        }

        private bool TryBuildDocument(out ElevateProjectEditorDocument? document)
        {
            document = null;
            ClearValidationHelpText();
            if (loadedDocument == null)
            {
                SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
                return false;
            }

            if (!TryParseInt(SimulationCountTextBox.Text, Text.EditorSimulationsHeader, out int simulationCount, SimulationCountTextBox, EditorSection.Analysis))
            {
                return false;
            }

            if (simulationCount <= 0)
            {
                SetValidationStatus(
                    Text.EditorSimulationCountPositiveMessage,
                    EditorSection.Analysis,
                    SimulationCountTextBox);
                return false;
            }

            if (!TryBuildTraffic(out ElevateProjectEditorTrafficSection traffic) ||
                !TryBuildFloors(out List<ElevateProjectEditorFloor> floors) ||
                !TryBuildCars(floors, out List<ElevateProjectEditorCar> cars))
            {
                return false;
            }

            document = new ElevateProjectEditorDocument
            {
                SourcePath = loadedDocument.SourcePath,
                TemplatePath = loadedDocument.TemplatePath,
                BuildingType = buildingType,
                Job = new ElevateProjectEditorJobSection
                {
                    Title = ProjectTitleTextBox.Text?.Trim() ?? string.Empty,
                    Number = ProjectNumberTextBox.Text?.Trim() ?? string.Empty,
                    CalculationTitle = ProjectCalculationTitleTextBox.Text?.Trim() ?? string.Empty,
                    MadeBy = ProjectMadeByTextBox.Text?.Trim() ?? string.Empty,
                    CheckedBy = ProjectCheckedByTextBox.Text?.Trim() ?? string.Empty,
                    Company = ProjectCompanyTextBox.Text?.Trim() ?? string.Empty,
                    LogoFile = preservedLogoFile,
                },
                Analysis = new ElevateProjectEditorAnalysisSection
                {
                    DispatcherAlgorithmName = ResolveEffectiveDispatcherAlgorithmName(),
                    TrafficMode = preservedTrafficMode,
                    SimulationsPerConfiguration = simulationCount,
                    LearningRuns = preservedLearningRuns,
                    RandomSeed = preservedRandomSeed,
                },
                Building = new ElevateProjectEditorBuildingSection
                {
                    BuildingType = buildingType,
                    AbsenteeismPercent = ResolveEffectiveAbsenteeismPercent(),
                    NumberOfFloors = floors.Count,
                },
                Traffic = traffic,
                Floors = floors,
                Cars = cars,
            };

            return true;
        }

        private bool TryBuildTraffic(out ElevateProjectEditorTrafficSection traffic)
        {
            traffic = new ElevateProjectEditorTrafficSection();

            if (!TryParseDouble(IncomingTextBox.Text, Text.EditorIncomingHeader, out double incoming, false, IncomingTextBox, EditorSection.Traffic) ||
                !TryParseDouble(OutgoingTextBox.Text, Text.EditorOutgoingHeader, out double outgoing, false, OutgoingTextBox, EditorSection.Traffic) ||
                !TryParseDouble(InterfloorTextBox.Text, Text.EditorInterfloorHeader, out double interfloor, false, InterfloorTextBox, EditorSection.Traffic) ||
                !TryParseDouble(HandlingCapacityTextBox.Text, Text.EditorHandlingCapacityHeader, out double handlingCapacity, false, HandlingCapacityTextBox, EditorSection.Traffic) ||
                !TryParseDouble(LoadingTimeTextBox.Text, Text.EditorLoadingTimeHeader, out double loadingTime, false, LoadingTimeTextBox, EditorSection.Traffic) ||
                !TryParseDouble(UnloadingTimeTextBox.Text, Text.EditorUnloadingTimeHeader, out double unloadingTime, false, UnloadingTimeTextBox, EditorSection.Traffic))
            {
                return false;
            }

            if (!TryValidatePercentage(incoming, Text.EditorIncomingHeader, IncomingTextBox) ||
                !TryValidatePercentage(outgoing, Text.EditorOutgoingHeader, OutgoingTextBox) ||
                !TryValidatePercentage(interfloor, Text.EditorInterfloorHeader, InterfloorTextBox))
            {
                return false;
            }

            if (handlingCapacity < 0d || loadingTime < 0d || unloadingTime < 0d)
            {
                FrameworkElement target = handlingCapacity < 0d
                    ? HandlingCapacityTextBox
                    : loadingTime < 0d
                        ? LoadingTimeTextBox
                        : UnloadingTimeTextBox;
                string fieldName = ReferenceEquals(target, HandlingCapacityTextBox)
                    ? Text.EditorHandlingCapacityHeader
                    : ReferenceEquals(target, LoadingTimeTextBox)
                        ? Text.EditorLoadingTimeHeader
                        : Text.EditorUnloadingTimeHeader;
                SetValidationStatus(
                    string.Format(
                        localizationService.CurrentCulture,
                        Text.EditorFieldNonNegativeFormat,
                        fieldName),
                    EditorSection.Traffic,
                    target);
                return false;
            }

            double splitTotal = incoming + outgoing + interfloor;
            if (Math.Abs(splitTotal - 100d) > 0.01d)
            {
                SetValidationStatus(Text.EditorTrafficSplitTotalMessage, EditorSection.Traffic, IncomingTextBox);
                return false;
            }

            traffic = new ElevateProjectEditorTrafficSection
            {
                IncomingPercent = incoming,
                OutgoingPercent = outgoing,
                InterfloorPercent = interfloor,
                HandlingCapacity = handlingCapacity,
                LoadingTimeSeconds = loadingTime,
                UnloadingTimeSeconds = unloadingTime,
            };

            return true;
        }

        private bool TryBuildFloors(out List<ElevateProjectEditorFloor> floors)
        {
            floors = new List<ElevateProjectEditorFloor>();
            double currentLevel = 0d;
            HashSet<string> floorNames = new(StringComparer.OrdinalIgnoreCase);
            int entranceFloorCount = 0;
            double entranceBiasTotal = 0d;

            for (int rowIndex = 0; rowIndex < floorRows.Count; rowIndex++)
            {
                BuildingFloorRowViewModel row = floorRows[rowIndex];
                string floorName = row.FloorName.Trim();
                if (string.IsNullOrWhiteSpace(floorName))
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorFloorNameRequiredFormat,
                            rowIndex + 1),
                        row,
                        row.FloorNameAutomationName);
                    return false;
                }

                if (!floorNames.Add(floorName))
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorFloorNameDuplicateFormat,
                            floorName),
                        row,
                        row.FloorNameAutomationName);
                    return false;
                }

                if (!TryParseFloorDouble(
                        row.InterfloorHeightText,
                        row.InterfloorHeightLabel,
                        row,
                        row.InterfloorHeightAutomationName,
                        out double interfloorHeight) ||
                    !TryParseFloorDouble(
                        row.PopulationText,
                        row.PopulationLabel,
                        row,
                        row.PopulationAutomationName,
                        out double population) ||
                    !TryParseFloorDouble(
                        row.EntranceBiasText,
                        Text.EditorEntranceBiasColumn,
                        row,
                        row.EntranceBiasAutomationName,
                        out double entranceBias))
                {
                    return false;
                }

                if ((rowIndex > 0 && interfloorHeight < 0d) || population < 0d)
                {
                    string fieldName = interfloorHeight < 0d ? row.InterfloorHeightLabel : row.PopulationLabel;
                    string automationName = interfloorHeight < 0d
                        ? row.InterfloorHeightAutomationName
                        : row.PopulationAutomationName;
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorFloorFieldNonNegativeFormat,
                            fieldName,
                            floorName),
                        row,
                        automationName);
                    return false;
                }

                if (rowIndex == 0 && Math.Abs(interfloorHeight) > 1e-9d)
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorBaseFloorLevelFormat,
                            floorName),
                        row,
                        row.InterfloorHeightAutomationName);
                    return false;
                }

                if (rowIndex > 0 && interfloorHeight <= 0d)
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorInterfloorHeightPositiveFormat,
                            floorName),
                        row,
                        row.InterfloorHeightAutomationName);
                    return false;
                }

                if (entranceBias < 0d || entranceBias > 100d)
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorEntranceBiasRangeFormat,
                            floorName),
                        row,
                        row.EntranceBiasAutomationName);
                    return false;
                }

                if (row.EntranceFloor)
                {
                    entranceFloorCount++;
                    entranceBiasTotal += entranceBias;
                }
                else if (Math.Abs(entranceBias) > 0.01d)
                {
                    SetFloorValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorNonEntranceBiasZeroFormat,
                            floorName),
                        row,
                        row.EntranceBiasAutomationName);
                    return false;
                }

                currentLevel += interfloorHeight;
                floors.Add(new ElevateProjectEditorFloor
                {
                    FloorIndex = floors.Count + 1,
                    SourceFloorName = row.SourceFloorName,
                    FloorName = floorName,
                    InterfloorHeight = interfloorHeight,
                    FloorLevel = currentLevel,
                    Population = population,
                    EntranceBiasPercent = entranceBias,
                    EntranceFloor = row.EntranceFloor,
                });
            }

            if (floors.Count == 0)
            {
                SetValidationStatus(Text.EditorBuildingTableEmptyMessage, EditorSection.Building, AddFloorAboveButton);
                return false;
            }

            if (entranceFloorCount == 0)
            {
                BuildingFloorRowViewModel firstRow = floorRows[0];
                SetFloorValidationStatus(
                    Text.EditorEntranceFloorRequiredMessage,
                    firstRow,
                    firstRow.EntranceAutomationName);
                return false;
            }

            if (Math.Abs(entranceBiasTotal - 100d) > 0.01d)
            {
                string formattedTotal = entranceBiasTotal.ToString("0.##", localizationService.CurrentCulture);
                BuildingFloorRowViewModel firstEntranceRow = floorRows.First(row => row.EntranceFloor);
                SetFloorValidationStatus(
                    string.Format(
                        localizationService.CurrentCulture,
                        Text.EditorEntranceBiasTotalFormat,
                        formattedTotal),
                    firstEntranceRow,
                    firstEntranceRow.EntranceBiasAutomationName);
                return false;
            }

            return true;
        }

        private bool TryBuildCars(IReadOnlyList<ElevateProjectEditorFloor> floors, out List<ElevateProjectEditorCar> cars)
        {
            cars = new List<ElevateProjectEditorCar>();
            if (liftRows.Count == 0)
            {
                SetValidationStatus(Text.EditorLiftRequiredMessage, EditorSection.LiftGroup, AddLiftButton);
                return false;
            }

            foreach (LiftCarRowViewModel row in liftRows)
            {
                if (!TryParseFlexibleDoubleInternal(row.CapacityOption, out double capacity) ||
                    !double.IsFinite(capacity) ||
                    capacity <= 0d)
                {
                    SetLiftValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorLiftFieldPositiveFormat,
                            row.CapacityLabel,
                            row.Title),
                        row,
                        row.CapacityAutomationName);
                    return false;
                }

                int cabinWidth = ParseIntOrDefault(row.CabWidthText, 0);
                int cabinHeight = ParseIntOrDefault(row.CabHeightText, 0);
                if (cabinWidth <= 0)
                {
                    SetLiftValidationStatus(
                        string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, row.CabWidthLabel),
                        row,
                        row.CabWidthAutomationName);
                    return false;
                }

                if (cabinHeight <= 0)
                {
                    SetLiftValidationStatus(
                        string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, row.CabHeightLabel),
                        row,
                        row.CabHeightAutomationName);
                    return false;
                }

                if (!TryParseFlexibleDoubleInternal(row.SpeedOption, out double speed) ||
                    !double.IsFinite(speed) ||
                    speed <= 0d)
                {
                    SetLiftValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorLiftFieldPositiveFormat,
                            row.SpeedLabel,
                            row.Title),
                        row,
                        row.SpeedAutomationName);
                    return false;
                }

                if (!TryParseLiftInt(
                        row.HomeFloor,
                        row.HomeFloorLabel,
                        row,
                        row.HomeFloorAutomationName,
                        out int homeFloor))
                {
                    return false;
                }

                if (homeFloor < 1 || homeFloor > floors.Count)
                {
                    SetLiftValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorHomeFloorRangeFormat,
                            row.Title,
                            floors.Count),
                        row,
                        row.HomeFloorAutomationName);
                    return false;
                }

                List<int> servedFloorIndexes = row.ServedFloors.Where(floor => floor.IsServed).Select(floor => floor.FloorIndex).Distinct().OrderBy(floorIndex => floorIndex).ToList();
                if (servedFloorIndexes.Count == 0)
                {
                    SetServedFloorsValidationStatus(
                        string.Format(
                            localizationService.CurrentCulture,
                            Text.EditorServedFloorRequiredFormat,
                            row.Title),
                        row);
                    return false;
                }

                MotionProfile motionProfile = liftGroupRulesService.ResolveMotionProfile(row.SpeedOption);
                DoorOpeningKind openingKind = row.SelectedDoorOpening?.Kind ?? DoorOpeningKind.Central;
                DoorProfile doorProfile = liftGroupRulesService.ResolveDoorProfile(row.SelectedDoorWidth, openingKind);

                cars.Add(new ElevateProjectEditorCar
                {
                    Id = row.Id,
                    HomeShaft = row.HomeShaft,
                    TemplateXml = row.TemplateXml,
                    CabinWidthMm = cabinWidth,
                    CabinDepthMm = cabinHeight,
                    CapacityKg = capacity.ToString("0.000000", CultureInfo.InvariantCulture),
                    FloorAreaM2 = liftGroupRulesService.ResolveCarAreaSquareMeters(cabinWidth, cabinHeight).ToString("0.000000", CultureInfo.InvariantCulture),
                    Speed = speed.ToString("0.000000", CultureInfo.InvariantCulture),
                    Acceleration = motionProfile.Acceleration,
                    Jerk = motionProfile.Jerk,
                    DoorPreOpening = doorProfile.DoorPreOpening,
                    DoorWidthMm = row.SelectedDoorWidth,
                    DoorOpeningKind = openingKind,
                    DoorOpenTime = doorProfile.DoorOpenTime,
                    DoorCloseTime = doorProfile.DoorCloseTime,
                    HomeFloor = homeFloor.ToString(CultureInfo.InvariantCulture),
                    ServedFloorIndexes = servedFloorIndexes,
                });
            }

            return true;
        }

        private List<ElevateProjectEditorFloor> BuildFloorDraft()
        {
            List<ElevateProjectEditorFloor> floors = new List<ElevateProjectEditorFloor>();
            double currentLevel = 0d;
            foreach (BuildingFloorRowViewModel row in floorRows)
            {
                double interfloorHeight = ParseFlexibleDouble(row.InterfloorHeightText);
                currentLevel += interfloorHeight;
                floors.Add(new ElevateProjectEditorFloor
                {
                    FloorIndex = floors.Count + 1,
                    SourceFloorName = row.SourceFloorName,
                    FloorName = row.FloorName,
                    InterfloorHeight = interfloorHeight,
                    FloorLevel = currentLevel,
                    Population = ParseFlexibleDouble(row.PopulationText),
                    EntranceBiasPercent = ParseFlexibleDouble(row.EntranceBiasText),
                    EntranceFloor = row.EntranceFloor,
                });
            }

            return floors;
        }

        private List<ElevateProjectEditorFloor> BuildFallbackFloors()
        {
            string floorPrefix = IsRussian ? "Этаж" : "Level";
            return new List<ElevateProjectEditorFloor>
            {
                new ElevateProjectEditorFloor { FloorIndex = 1, SourceFloorName = string.Empty, FloorName = $"{floorPrefix} 1", InterfloorHeight = 0d, FloorLevel = 0d, Population = 0d, EntranceFloor = true, EntranceBiasPercent = 100d },
                new ElevateProjectEditorFloor { FloorIndex = 2, SourceFloorName = string.Empty, FloorName = $"{floorPrefix} 2", InterfloorHeight = 3.9d, FloorLevel = 3.9d, Population = 150d, EntranceFloor = false, EntranceBiasPercent = 0d },
                new ElevateProjectEditorFloor { FloorIndex = 3, SourceFloorName = string.Empty, FloorName = $"{floorPrefix} 3", InterfloorHeight = 3.9d, FloorLevel = 7.8d, Population = 150d, EntranceFloor = false, EntranceBiasPercent = 0d },
            };
        }

        private string ResolveOutputPath(ElevateProjectEditorDocument document)
        {
            string suggestedFileName = projectEditorService.SuggestFileName(document);
            string suggestedOutputPath = Path.Combine(workingFolder, suggestedFileName);
            if (!string.IsNullOrWhiteSpace(document.SourcePath) &&
                File.Exists(document.SourcePath) &&
                string.Equals(Path.GetDirectoryName(document.SourcePath), workingFolder, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(document.SourcePath), suggestedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return document.SourcePath;
            }

            return suggestedOutputPath;
        }

        private void UpdateOutputPreview()
        {
            if (loadedDocument == null)
            {
                OutputValueTextBlock.Text = "-";
                return;
            }

            ElevateProjectEditorDocument previewDocument = new ElevateProjectEditorDocument
            {
                SourcePath = loadedDocument.SourcePath,
                TemplatePath = loadedDocument.TemplatePath,
                BuildingType = buildingType,
                Job = new ElevateProjectEditorJobSection
                {
                    Title = ProjectTitleTextBox.Text?.Trim() ?? string.Empty,
                    Number = ProjectNumberTextBox.Text?.Trim() ?? string.Empty,
                },
            };

            OutputValueTextBlock.Text = ResolveOutputPath(previewDocument);
        }

        private IReadOnlyList<string> GetExistingElvxPaths()
        {
            string[] topLevelFiles = Directory.GetFiles(workingFolder, "*.elvx", SearchOption.TopDirectoryOnly);
            IEnumerable<string> candidates = topLevelFiles.Length > 0
                ? topLevelFiles
                : Directory
                    .GetFiles(workingFolder, "*.elvx", SearchOption.AllDirectories)
                    .Where(path => !IsKnownBatchFolder(Path.GetDirectoryName(path)));

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => !path.EndsWith("01.elvx", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<string?> SelectExistingElvxPathAsync(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                return null;
            }

            if (paths.Count == 1)
            {
                return paths[0];
            }

            ComboBox fileComboBox = new()
            {
                MinWidth = 280,
                MaxWidth = 680,
                MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0,
            };
            AutomationProperties.SetName(fileComboBox, Text.EditorSourceLabel);
            foreach (string path in paths)
            {
                fileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetRelativePath(workingFolder, path),
                    Tag = path,
                });
            }

            StackPanel content = new() { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = SelectElvxMessage,
                TextWrapping = TextWrapping.WrapWholeWords,
            });
            content.Children.Add(fileComboBox);

            ContentDialog dialog = new()
            {
                Title = SelectElvxTitle,
                Content = content,
                PrimaryButtonText = Text.LoadEditorButton,
                CloseButtonText = Text.ProjectBatchPreviewCancelButton,
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await ShowContentDialogAsync(dialog);
            return result == ContentDialogResult.Primary && fileComboBox.SelectedItem is ComboBoxItem { Tag: string selectedPath }
                ? selectedPath
                : null;
        }

        private static bool IsKnownBatchFolder(string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            string folderName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return folderName.Equals("morning", StringComparison.OrdinalIgnoreCase) || folderName.Equals("lunch", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ConfirmDiscardChangesAsync()
        {
            if (!HasUnsavedChanges)
            {
                return true;
            }

            ContentDialog dialog = new()
            {
                Title = UnsavedChangesTitle,
                Content = UnsavedChangesMessage,
                PrimaryButtonText = DiscardChangesLabel,
                CloseButtonText = Text.ProjectBatchPreviewCancelButton,
                DefaultButton = ContentDialogButton.Close,
            };
            return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmOverwriteAsync(string outputPath)
        {
            ContentDialog dialog = new()
            {
                Title = OverwriteTitle,
                Content = OverwriteMessage(outputPath),
                PrimaryButtonText = Text.SaveEditorButton,
                CloseButtonText = Text.ProjectBatchPreviewCancelButton,
                DefaultButton = ContentDialogButton.Close,
            };
            return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmRemoveLiftAsync(LiftCarRowViewModel row)
        {
            ContentDialog dialog = new()
            {
                Title = Text.EditorRemoveLiftButton,
                Content = RemoveLiftMessage(row.Title),
                PrimaryButtonText = Text.EditorRemoveLiftButton,
                CloseButtonText = Text.ProjectBatchPreviewCancelButton,
                DefaultButton = ContentDialogButton.Close,
            };
            return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary;
        }

        private async Task<bool> ConfirmRemoveFloorAsync(BuildingFloorRowViewModel row)
        {
            ContentDialog dialog = new()
            {
                Title = RemoveFloorLabel,
                Content = RemoveFloorMessage(row.FloorName),
                PrimaryButtonText = RemoveFloorLabel,
                CloseButtonText = Text.ProjectBatchPreviewCancelButton,
                DefaultButton = ContentDialogButton.Close,
            };
            return await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary;
        }

        private async Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
        {
            if (dialogOpen || RootGrid.XamlRoot is null || lifetimeSource.IsCancellationRequested)
            {
                return ContentDialogResult.None;
            }

            dialogOpen = true;
            activeDialog = dialog;
            dialog.XamlRoot = RootGrid.XamlRoot;
            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                if (ReferenceEquals(activeDialog, dialog))
                {
                    activeDialog = null;
                }

                dialogOpen = false;
            }
        }

        private void ConfigureDirtyTracking()
        {
            TextBox[] trackedTextBoxes =
            {
                ProjectTitleTextBox,
                ProjectNumberTextBox,
                ProjectCalculationTitleTextBox,
                ProjectMadeByTextBox,
                ProjectCheckedByTextBox,
                ProjectCompanyTextBox,
                SimulationCountTextBox,
                AbsenteeismTextBox,
                IncomingTextBox,
                OutgoingTextBox,
                InterfloorTextBox,
                HandlingCapacityTextBox,
                LoadingTimeTextBox,
                UnloadingTimeTextBox,
            };

            foreach (TextBox textBox in trackedTextBoxes)
            {
                textBox.TextChanged += OnEditorFieldTextChanged;
            }

            DispatcherComboBox.SelectionChanged += OnDispatcherSelectionChanged;
        }

        private static void SetAutomationName(DependencyObject element, string name)
        {
            AutomationProperties.SetName(element, name);
        }

        private void MarkDirty()
        {
            if (suppressDirtyTracking || loadedDocument is null)
            {
                return;
            }

            ClearActiveValidationState();
            SetDirty(true);
        }

        private void SetDirty(bool value)
        {
            isDirty = value;
            UpdateDirtyVisuals();
        }

        private void UpdateDirtyVisuals()
        {
            string dirtySuffix = HasUnsavedChanges ? " *" : string.Empty;
            Title = Text.EditorTitle + dirtySuffix;
            HeroTitleTextBlock.Text = Text.EditorTitle + dirtySuffix;
            SaveButton.IsEnabled = loadedDocument is not null && !isBusy;
            UndoButton.IsEnabled = loadedDocument is not null && HasUnsavedChanges && !isBusy;
            AutomationProperties.SetHelpText(
                UndoButton,
                HasUnsavedChanges ? UndoChangesLabel : Text.Ready);
        }

        private void SetBusy(bool value)
        {
            if (value && !isBusy)
            {
                busyCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            isBusy = value;
            BusyStatePanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            BusyProgressRing.IsActive = value;
            LoadExistingButton.IsEnabled = !value;
            LoadTemplateButton.IsEnabled = !value;
            if (value)
            {
                DisableDescendantControls(TabsCard);
                DisableDescendantControls(SectionContentBorder);
            }
            else
            {
                foreach (Control control in busyDisabledControls)
                {
                    control.IsEnabled = true;
                }

                busyDisabledControls.Clear();
            }

            UpdateDirtyVisuals();

            if (!value)
            {
                TaskCompletionSource<bool>? completionSource = busyCompletionSource;
                busyCompletionSource = null;
                completionSource?.TrySetResult(true);
            }
        }

        private void DisableDescendantControls(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is Control control && control.IsEnabled)
                {
                    control.IsEnabled = false;
                    busyDisabledControls.Add(control);
                }

                DisableDescendantControls(child);
            }
        }

        private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (closeAllowed)
            {
                return;
            }

            if (isBusy)
            {
                args.Cancel = true;
                SetStatus(BusyLabel, InfoBarSeverity.Informational);
                return;
            }

            if (!HasUnsavedChanges)
            {
                return;
            }

            args.Cancel = true;
            if (closePromptInProgress)
            {
                return;
            }

            closePromptInProgress = true;
            try
            {
                if (await ConfirmDiscardChangesAsync())
                {
                    closeAllowed = true;
                    Close();
                }
            }
            finally
            {
                closePromptInProgress = false;
            }
        }

        private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private void UpdateResponsiveLayout(double width)
        {
            if (width <= 0)
            {
                return;
            }

            bool compact = width < CompactLayoutBreakpoint;
            if (compact == compactLayoutApplied)
            {
                return;
            }

            compactLayoutApplied = compact;
            ContentStackPanel.Margin = compact ? new Thickness(12, 12, 12, 20) : new Thickness(20, 18, 20, 24);
            StatusInfoBar.Margin = compact ? new Thickness(12, 10, 12, 0) : new Thickness(20, 12, 20, 0);

            HeroSideColumn.Width = compact ? new GridLength(0) : new GridLength(320);
            Grid.SetRow(HeroSidePanel, compact ? 1 : 0);
            Grid.SetColumn(HeroSidePanel, compact ? 0 : 1);

            ProjectRightColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            ArrangeProjectFields(compact);
            ArrangeTrafficFields(compact);
            ArrangeCommands(compact);
            ArrangeTabs(compact);
        }

        private void ArrangeProjectFields(bool compact)
        {
            FrameworkElement[] panels =
            {
                ProjectTitleFieldPanel,
                ProjectNumberFieldPanel,
                ProjectCalculationFieldPanel,
                ProjectCompanyFieldPanel,
                ProjectMadeByFieldPanel,
                ProjectCheckedByFieldPanel,
            };

            for (int index = 0; index < panels.Length; index++)
            {
                Grid.SetRow(panels[index], compact ? index : index / 2);
                Grid.SetColumn(panels[index], compact ? 0 : index % 2);
            }
        }

        private void ArrangeTrafficFields(bool compact)
        {
            TrafficSplitMiddleColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            TrafficSplitRightColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            TrafficParametersMiddleColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            TrafficParametersRightColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

            FrameworkElement[] splitFields = { IncomingFieldPanel, OutgoingFieldPanel, InterfloorFieldPanel };
            FrameworkElement[] parameterFields = { HandlingCapacityFieldPanel, LoadingTimeFieldPanel, UnloadingTimeFieldPanel };
            for (int index = 0; index < splitFields.Length; index++)
            {
                Grid.SetRow(splitFields[index], compact ? index : 0);
                Grid.SetColumn(splitFields[index], compact ? 0 : index);
                Grid.SetRow(parameterFields[index], compact ? index : 0);
                Grid.SetColumn(parameterFields[index], compact ? 0 : index);
            }
        }

        private void ArrangeCommands(bool compact)
        {
            for (int index = 0; index < CommandGrid.ColumnDefinitions.Count; index++)
            {
                CommandGrid.ColumnDefinitions[index].Width = compact
                    ? (index < 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0))
                    : (index == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto);
            }

            PlaceInGrid(LoadExistingButton, 0, 0);
            PlaceInGrid(LoadTemplateButton, 0, 1);
            PlaceInGrid(SaveButton, 0, 2);
            PlaceInGrid(UndoButton, compact ? 1 : 0, compact ? 0 : 3);
            PlaceInGrid(BusyStatePanel, compact ? 1 : 0, compact ? 1 : 4);
            PlaceInGrid(CloseButton, compact ? 1 : 0, compact ? 2 : 5);
        }

        private void ArrangeTabs(bool compact)
        {
            for (int index = 0; index < TabGrid.ColumnDefinitions.Count; index++)
            {
                TabGrid.ColumnDefinitions[index].Width = compact && index >= 3
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);
            }

            PlaceInGrid(ProjectTabButton, 0, 0);
            PlaceInGrid(AnalysisTabButton, 0, 1);
            PlaceInGrid(TrafficTabButton, 0, 2);
            PlaceInGrid(BuildingTabButton, compact ? 1 : 0, compact ? 0 : 3);
            PlaceInGrid(LiftGroupTabButton, compact ? 1 : 0, compact ? 1 : 4, compact ? 2 : 1);
        }

        private static void PlaceInGrid(FrameworkElement element, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
        }

        private void SetValidationStatus(string message, EditorSection section, FrameworkElement target)
        {
            ShowSection(section);
            SetStatus(message, InfoBarSeverity.Warning);
            AutomationProperties.SetHelpText(target, message);
            SetActiveValidation(target, message);
            _ = DispatcherQueue.TryEnqueue(() => FocusValidationTarget(target));
        }

        private void SetFloorValidationStatus(
            string message,
            BuildingFloorRowViewModel row,
            string automationName)
        {
            ShowSection(EditorSection.Building);
            SetStatus(message, InfoBarSeverity.Warning);
            FrameworkElement target = ResolveRowValidationTarget(
                FloorsItemsControl,
                row,
                automationName);
            AutomationProperties.SetHelpText(target, message);
            SetActiveValidation(target, message);
            _ = DispatcherQueue.TryEnqueue(() => FocusValidationTarget(target));
        }

        private void SetLiftValidationStatus(
            string message,
            LiftCarRowViewModel row,
            string automationName)
        {
            ShowSection(EditorSection.LiftGroup);
            SetStatus(message, InfoBarSeverity.Warning);
            FrameworkElement target = ResolveRowValidationTarget(
                LiftItemsControl,
                row,
                automationName);
            AutomationProperties.SetHelpText(target, message);
            SetActiveValidation(target, message);
            _ = DispatcherQueue.TryEnqueue(() => FocusValidationTarget(target));
        }

        private void SetServedFloorsValidationStatus(string message, LiftCarRowViewModel row)
        {
            ShowSection(EditorSection.LiftGroup);
            SetStatus(message, InfoBarSeverity.Warning);
            LiftGroupTablesPanel.UpdateLayout();
            FrameworkElement target = FindDescendant(
                    LiftGroupTablesPanel,
                    element => element is CheckBox && ReferenceEquals(element.Tag, row))
                ?? LiftGroupTablesPanel;
            AutomationProperties.SetHelpText(target, message);
            SetActiveValidation(target, message, row);
            _ = DispatcherQueue.TryEnqueue(() => FocusValidationTarget(target));
        }

        private void SetActiveValidation(
            FrameworkElement target,
            string message,
            LiftCarRowViewModel? servedFloorsRow = null)
        {
            activeValidationTarget = target;
            activeServedFloorsValidationRow = servedFloorsRow;
            activeValidationMessage = message;
            activeValidationMessageLanguage = localizationService.CurrentLanguage;
        }

        private void ClearActiveValidationState()
        {
            if (activeValidationTarget is not null)
            {
                AutomationProperties.SetHelpText(activeValidationTarget, string.Empty);
            }

            activeValidationTarget = null;
            activeServedFloorsValidationRow = null;
            activeValidationMessage = null;
        }

        private FrameworkElement? ResolveActiveValidationTarget()
        {
            if (activeServedFloorsValidationRow is null)
            {
                return activeValidationTarget;
            }

            LiftGroupTablesPanel.UpdateLayout();
            return FindDescendant(
                    LiftGroupTablesPanel,
                    element => element is CheckBox &&
                        ReferenceEquals(element.Tag, activeServedFloorsValidationRow))
                ?? LiftGroupTablesPanel;
        }

        private void ClearValidationHelpText(bool preserveActiveValidation = false)
        {
            DependencyObject[] validationTargets =
            {
                SimulationCountTextBox,
                IncomingTextBox,
                OutgoingTextBox,
                InterfloorTextBox,
                HandlingCapacityTextBox,
                LoadingTimeTextBox,
                UnloadingTimeTextBox,
                FloorsItemsControl,
                LiftItemsControl,
                AddLiftButton,
            };

            foreach (DependencyObject target in validationTargets)
            {
                AutomationProperties.SetHelpText(target, string.Empty);
            }

            ClearValidationHelpTextInTree(FloorsItemsControl);
            ClearValidationHelpTextInTree(LiftItemsControl);
            ClearValidationHelpTextInTree(LiftGroupTablesPanel);
            if (!preserveActiveValidation)
            {
                ClearActiveValidationState();
            }
        }

        private static void ClearValidationHelpTextInTree(DependencyObject root)
        {
            AutomationProperties.SetHelpText(root, string.Empty);
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                ClearValidationHelpTextInTree(VisualTreeHelper.GetChild(root, index));
            }
        }

        private static void FocusValidationTarget(FrameworkElement target)
        {
            target.StartBringIntoView();
            Control? control = target as Control ?? FindFirstFocusableControl(target);
            control?.Focus(FocusState.Programmatic);
        }

        private void FocusFloorRow(BuildingFloorRowViewModel row)
        {
            ShowSection(EditorSection.Building);
            FocusValidationTarget(ResolveRowValidationTarget(
                FloorsItemsControl,
                row,
                row.FloorNameAutomationName));
        }

        private void FocusLiftRow(LiftCarRowViewModel row)
        {
            ShowSection(EditorSection.LiftGroup);
            FocusValidationTarget(ResolveRowValidationTarget(
                LiftItemsControl,
                row,
                row.CapacityAutomationName));
        }

        private static FrameworkElement ResolveRowValidationTarget(
            ItemsControl itemsControl,
            object row,
            string automationName)
        {
            itemsControl.UpdateLayout();
            DependencyObject root = itemsControl.ContainerFromItem(row) ?? itemsControl;
            return FindDescendant(
                    root,
                    element => string.Equals(
                        AutomationProperties.GetName(element),
                        automationName,
                        StringComparison.Ordinal))
                ?? (root as FrameworkElement ?? itemsControl);
        }

        private static FrameworkElement? FindDescendant(
            DependencyObject root,
            Func<FrameworkElement, bool> predicate)
        {
            if (root is FrameworkElement frameworkElement && predicate(frameworkElement))
            {
                return frameworkElement;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                FrameworkElement? match = FindDescendant(
                    VisualTreeHelper.GetChild(root, index),
                    predicate);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Control? FindFirstFocusableControl(DependencyObject root)
        {
            if (root is Control control && control.IsEnabled && control.IsTabStop && control.Visibility == Visibility.Visible)
            {
                return control;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                Control? candidate = FindFirstFocusableControl(VisualTreeHelper.GetChild(root, index));
                if (candidate is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void RefreshLiftTitles()
        {
            for (int index = 0; index < liftRows.Count; index++)
            {
                LiftCarRowViewModel row = liftRows[index];
                row.Id = (index + 1).ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(row.HomeShaft))
                {
                    row.HomeShaft = row.Id;
                }

                row.Title = string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorLiftTitleFormat,
                    index + 1);
            }
        }

        private string ResolveNextHomeShaft()
        {
            HashSet<int> usedShafts = new();
            foreach (LiftCarRowViewModel row in liftRows)
            {
                int shaft = ParseIntOrDefault(row.HomeShaft, 0);
                if (shaft > 0)
                {
                    usedShafts.Add(shaft);
                }
            }

            int candidate = 1;
            while (candidate < int.MaxValue && usedShafts.Contains(candidate))
            {
                candidate++;
            }

            return candidate.ToString(CultureInfo.InvariantCulture);
        }

        private string ResolveAvailableHomeShaft(string? preferredHomeShaft)
        {
            int preferred = ParseIntOrDefault(preferredHomeShaft, 0);
            bool isAvailable = preferred > 0 && liftRows.All(row =>
                ParseIntOrDefault(row.HomeShaft, 0) != preferred);
            return isAvailable
                ? preferred.ToString(CultureInfo.InvariantCulture)
                : ResolveNextHomeShaft();
        }

        private void RefreshLiftCountSummary()
        {
            UpdateLiveRegionText(
                LiftCountSummaryTextBlock,
                string.Format(
                    localizationService.CurrentCulture,
                    "{0}: {1}",
                    Text.EditorLiftCountLabel,
                    liftRows.Count));
        }

        private void RebuildDispatcherOptions()
        {
            string? selectedValue = (DispatcherComboBox.SelectedItem as DispatcherOption)?.Value;
            dispatcherOptions.Clear();
            dispatcherOptions.Add(new DispatcherOption("Group Collective", BuildDispatcherDisplay("Групповая собирательная", "Group Collective")));
            dispatcherOptions.Add(new DispatcherOption("Mixed Control (Enhanced ACA)", BuildDispatcherDisplay("DDS", "Mixed Control (Enhanced ACA)")));
            dispatcherOptions.Add(new DispatcherOption("Double Deck Destination Control", BuildDispatcherDisplay("DDS Double Deck", "Double Deck Destination Control")));
            DispatcherComboBox.SelectedItem = dispatcherOptions.FirstOrDefault(option => option.Value == selectedValue) ?? dispatcherOptions.FirstOrDefault();
        }

        private void SetStatus(string message, InfoBarSeverity severity)
        {
            ClearActiveValidationState();
            AutomationProperties.SetLiveSetting(
                StatusInfoBar,
                severity is InfoBarSeverity.Error or InfoBarSeverity.Warning
                    ? AutomationLiveSetting.Assertive
                    : AutomationLiveSetting.Polite);
            StatusInfoBar.IsOpen = false;
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            statusMessageLanguage = localizationService.CurrentLanguage;
            AutomationProperties.SetName(StatusInfoBar, $"{Text.StatusTitle}: {message}");
            StatusInfoBar.IsOpen = true;
            RaiseLiveRegionChanged(StatusInfoBar);
        }

        private static void UpdateLiveRegionText(TextBlock textBlock, string text)
        {
            if (string.Equals(textBlock.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            textBlock.Text = text;
            RaiseLiveRegionChanged(textBlock);
        }

        private static void RaiseLiveRegionChanged(FrameworkElement element)
        {
            AutomationPeer? peer =
                FrameworkElementAutomationPeer.FromElement(element) ??
                FrameworkElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private string BuildExceptionMessage(Exception exception)
        {
            return Text.OperationFailedMessage + " " + localizationService.TranslateRuntimeMessage(exception.Message);
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(() => OnLanguageChanged(sender, e));
                return;
            }

            bool previousSuppressDirtyTracking = suppressDirtyTracking;
            bool statusWasOpen = StatusInfoBar.IsOpen;
            string previousStatusMessage = StatusInfoBar.Message;
            InfoBarSeverity previousStatusSeverity = StatusInfoBar.Severity;
            AppLanguage previousStatusLanguage = statusMessageLanguage;
            suppressDirtyTracking = true;
            try
            {
                ApplyStaticContext();
                ApplyLocalizedText();
                string localizedStatusMessage = isBusy
                    ? BusyLabel
                    : localizationService.RelocalizeCatalogMessage(
                        previousStatusMessage,
                        previousStatusLanguage);
                InfoBarSeverity localizedStatusSeverity = isBusy
                    ? InfoBarSeverity.Informational
                    : previousStatusSeverity;
                StatusInfoBar.Title = Text.StatusTitle;
                StatusInfoBar.Message = localizedStatusMessage;
                StatusInfoBar.Severity = localizedStatusSeverity;
                statusMessageLanguage = localizationService.CurrentLanguage;
                AutomationProperties.SetLiveSetting(
                    StatusInfoBar,
                    localizedStatusSeverity is InfoBarSeverity.Error or InfoBarSeverity.Warning
                        ? AutomationLiveSetting.Assertive
                        : AutomationLiveSetting.Polite);
                StatusInfoBar.IsOpen = statusWasOpen;
                AutomationProperties.SetName(StatusInfoBar, $"{Text.StatusTitle}: {localizedStatusMessage}");
                FrameworkElement? currentValidationTarget = ResolveActiveValidationTarget();
                if (currentValidationTarget is not null &&
                    !string.IsNullOrWhiteSpace(activeValidationMessage))
                {
                    string localizedValidationMessage = localizationService.RelocalizeCatalogMessage(
                        activeValidationMessage,
                        activeValidationMessageLanguage);
                    activeValidationTarget = currentValidationTarget;
                    AutomationProperties.SetHelpText(currentValidationTarget, localizedValidationMessage);
                    activeValidationMessage = localizedValidationMessage;
                    activeValidationMessageLanguage = localizationService.CurrentLanguage;
                }

                if (statusWasOpen)
                {
                    RaiseLiveRegionChanged(StatusInfoBar);
                }
            }
            finally
            {
                suppressDirtyTracking = previousSuppressDirtyTracking;
                UpdateDirtyVisuals();
            }
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            lifetimeSource.Cancel();
            localizationService.LanguageChanged -= OnLanguageChanged;
            AppWindow.Closing -= OnAppWindowClosing;
            foreach (BuildingFloorRowViewModel row in floorRows)
            {
                row.PropertyChanged -= OnBuildingFloorRowPropertyChanged;
            }

            foreach (LiftCarRowViewModel row in liftRows)
            {
                DetachLiftRow(row);
            }

            lifetimeSource.Dispose();
        }

        private string ResolveHomeFloor(string? preferredHomeFloor, IReadOnlyList<ElevateProjectEditorFloor> floors)
        {
            if (int.TryParse(preferredHomeFloor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHomeFloor) &&
                parsedHomeFloor >= 1 &&
                parsedHomeFloor <= floors.Count)
            {
                return parsedHomeFloor.ToString(CultureInfo.InvariantCulture);
            }

            return ResolveFallbackHomeFloor(floors);
        }

        private string ResolveFallbackHomeFloor(IReadOnlyList<ElevateProjectEditorFloor> floors)
        {
            for (int index = floors.Count - 1; index >= 0; index--)
            {
                if (floors[index].EntranceFloor)
                {
                    return (index + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            return "1";
        }

        private static Tuple<int, int> EstimateCabinDimensions(string areaText)
        {
            double area = Math.Max(ParseFlexibleDouble(areaText), 0.01d);
            int width = (int)(Math.Round(Math.Sqrt(area) * 1000d / 50d, MidpointRounding.AwayFromZero) * 50d);
            width = Math.Max(1000, width);
            int height = (int)Math.Round((area * 1000000d) / width, MidpointRounding.AwayFromZero);
            height = Math.Max(1000, height);
            return Tuple.Create(width, height);
        }

        private static string NormalizeCapacity(string numericText)
        {
            double value = ParseFlexibleDouble(numericText);
            return !double.IsFinite(value) || value <= 0
                ? "1050"
                : ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeSpeed(string numericText)
        {
            double value = ParseFlexibleDouble(numericText);
            if (!double.IsFinite(value) || value <= 0)
            {
                value = 2.5d;
            }

            return value.ToString("0.0##", CultureInfo.InvariantCulture);
        }

        private static void AddCurrentOptionIfMissing(ObservableCollection<string> options, string currentOption)
        {
            if (!options.Any(option => string.Equals(option, currentOption, StringComparison.Ordinal)))
            {
                options.Add(currentOption);
            }
        }

        private static double ParseFlexibleDouble(string? text)
        {
            return TryParseFlexibleDoubleInternal(text, out double value) ? value : 0d;
        }

        private static bool TryParseFlexibleDoubleInternal(string? text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out value);
        }

        private bool TryParseDouble(
            string? text,
            string fieldName,
            out double value,
            bool allowEmpty,
            FrameworkElement? target = null,
            EditorSection? section = null)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(text))
            {
                value = 0d;
                return true;
            }

            if (!TryParseFlexibleDoubleInternal(text, out value) || !double.IsFinite(value))
            {
                string message = string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName);
                if (target is not null && section.HasValue)
                {
                    SetValidationStatus(message, section.Value, target);
                }
                else
                {
                    SetStatus(message, InfoBarSeverity.Warning);
                }

                return false;
            }

            return true;
        }

        private bool TryParseFloorDouble(
            string? text,
            string fieldName,
            BuildingFloorRowViewModel row,
            string automationName,
            out double value)
        {
            if (TryParseFlexibleDoubleInternal(text, out value) && double.IsFinite(value))
            {
                return true;
            }

            SetFloorValidationStatus(
                string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName),
                row,
                automationName);
            return false;
        }

        private bool TryValidatePercentage(double value, string fieldName, FrameworkElement target)
        {
            if (value >= 0d && value <= 100d)
            {
                return true;
            }

            SetValidationStatus(
                string.Format(
                    localizationService.CurrentCulture,
                    Text.EditorPercentageRangeFormat,
                    fieldName),
                EditorSection.Traffic,
                target);
            return false;
        }

        private bool TryParseInt(
            string? text,
            string fieldName,
            out int value,
            FrameworkElement? target = null,
            EditorSection? section = null)
        {
            value = 0;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out value))
            {
                return true;
            }

            string message = string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName);
            if (target is not null && section.HasValue)
            {
                SetValidationStatus(message, section.Value, target);
            }
            else
            {
                SetStatus(message, InfoBarSeverity.Warning);
            }

            return false;
        }

        private bool TryParseLiftInt(
            string? text,
            string fieldName,
            LiftCarRowViewModel row,
            string automationName,
            out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out value))
            {
                return true;
            }

            SetLiftValidationStatus(
                string.Format(localizationService.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName),
                row,
                automationName);
            return false;
        }

        private static int ParseIntOrDefault(string? text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int invariantValue) ||
                   int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out invariantValue)
                ? invariantValue
                : fallback;
        }

        private static int? TryExtractTrailingInteger(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string trimmed = text.Trim();
            int separatorIndex = trimmed.LastIndexOf(' ');
            string candidate = separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
            return int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out int invariantValue) ||
                   int.TryParse(candidate, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out invariantValue)
                ? invariantValue
                : null;
        }

        private string FormatEditableNumber(double value)
        {
            return value.ToString("0.###", localizationService.CurrentCulture);
        }

        private string BuildDispatcherDisplay(string russianLabel, string elevateName)
        {
            return localizationService.CurrentLanguage == AppLanguage.Russian ? $"{russianLabel} ({elevateName})" : elevateName;
        }
    }

    public sealed class BuildingFloorRowViewModel : INotifyPropertyChanged
    {
        private string sourceFloorName = string.Empty;
        private string floorName = string.Empty;
        private string interfloorHeightText = string.Empty;
        private string populationText = string.Empty;
        private string entranceBiasText = string.Empty;
        private bool entranceFloor;
        private string floorNameLabel = string.Empty;
        private string interfloorHeightLabel = string.Empty;
        private string populationLabel = string.Empty;
        private string entranceBiasLabel = string.Empty;
        private string entranceLabel = string.Empty;
        private string removeFloorLabel = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SourceFloorName
        {
            get => sourceFloorName;
            set => SetProperty(ref sourceFloorName, value, nameof(SourceFloorName));
        }

        public string FloorName
        {
            get => floorName;
            set
            {
                SetProperty(ref floorName, value, nameof(FloorName));
                RaiseAutomationNames();
            }
        }

        public string InterfloorHeightText
        {
            get => interfloorHeightText;
            set => SetProperty(ref interfloorHeightText, value, nameof(InterfloorHeightText));
        }

        public string PopulationText
        {
            get => populationText;
            set => SetProperty(ref populationText, value, nameof(PopulationText));
        }

        public string EntranceBiasText
        {
            get => entranceBiasText;
            set => SetProperty(ref entranceBiasText, value, nameof(EntranceBiasText));
        }

        public bool EntranceFloor
        {
            get => entranceFloor;
            set => SetProperty(ref entranceFloor, value, nameof(EntranceFloor));
        }

        public string FloorNameLabel
        {
            get => floorNameLabel;
            set
            {
                SetProperty(ref floorNameLabel, value, nameof(FloorNameLabel));
                RaiseAutomationNames();
            }
        }

        public string InterfloorHeightLabel
        {
            get => interfloorHeightLabel;
            set
            {
                SetProperty(ref interfloorHeightLabel, value, nameof(InterfloorHeightLabel));
                RaiseAutomationNames();
            }
        }

        public string PopulationLabel
        {
            get => populationLabel;
            set
            {
                SetProperty(ref populationLabel, value, nameof(PopulationLabel));
                RaiseAutomationNames();
            }
        }

        public string EntranceBiasLabel
        {
            get => entranceBiasLabel;
            set
            {
                SetProperty(ref entranceBiasLabel, value, nameof(EntranceBiasLabel));
                RaiseAutomationNames();
            }
        }

        public string EntranceLabel
        {
            get => entranceLabel;
            set
            {
                SetProperty(ref entranceLabel, value, nameof(EntranceLabel));
                RaiseAutomationNames();
            }
        }

        public string RemoveFloorLabel
        {
            get => removeFloorLabel;
            set
            {
                SetProperty(ref removeFloorLabel, value, nameof(RemoveFloorLabel));
                RaiseAutomationNames();
            }
        }

        public string FloorNameAutomationName => $"{FloorNameLabel}: {FloorName}";

        public string InterfloorHeightAutomationName => $"{InterfloorHeightLabel}: {FloorName}";

        public string PopulationAutomationName => $"{PopulationLabel}: {FloorName}";

        public string EntranceBiasAutomationName => $"{EntranceBiasLabel}: {FloorName}";

        public string EntranceAutomationName => $"{EntranceLabel}: {FloorName}";

        public string RemoveFloorAutomationName => $"{RemoveFloorLabel}: {FloorName}";

        private void RaiseAutomationNames()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FloorNameAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InterfloorHeightAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PopulationAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntranceBiasAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntranceAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoveFloorAutomationName)));
        }

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class LiftCarRowViewModel : INotifyPropertyChanged
    {
        private string id = string.Empty;
        private string homeShaft = string.Empty;
        private string templateXml = string.Empty;
        private string title = string.Empty;
        private string capacityOption = string.Empty;
        private string cabWidthText = string.Empty;
        private string cabHeightText = string.Empty;
        private string speedOption = string.Empty;
        private int selectedDoorWidth;
        private DoorOpeningOption? selectedDoorOpening;
        private string homeFloor = string.Empty;
        private string capacityLabel = string.Empty;
        private string cabWidthLabel = string.Empty;
        private string cabHeightLabel = string.Empty;
        private string speedLabel = string.Empty;
        private string doorWidthLabel = string.Empty;
        private string doorOpeningLabel = string.Empty;
        private string homeFloorLabel = string.Empty;
        private string servedFloorsLabel = string.Empty;
        private string removeButtonLabel = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<string> CapacityOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> SpeedOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<int> DoorWidthOptions { get; } = new ObservableCollection<int>();

        public ObservableCollection<DoorOpeningOption> DoorOpeningOptions { get; } = new ObservableCollection<DoorOpeningOption>();

        public ObservableCollection<ServedFloorRowViewModel> ServedFloors { get; } = new ObservableCollection<ServedFloorRowViewModel>();

        public string Id
        {
            get => id;
            set => SetProperty(ref id, value, nameof(Id));
        }

        public string HomeShaft
        {
            get => homeShaft;
            set => SetProperty(ref homeShaft, value, nameof(HomeShaft));
        }

        public string TemplateXml
        {
            get => templateXml;
            set => SetProperty(ref templateXml, value, nameof(TemplateXml));
        }

        public string Title
        {
            get => title;
            set
            {
                SetProperty(ref title, value, nameof(Title));
                RaiseAutomationNames();
            }
        }

        public string CapacityOption
        {
            get => capacityOption;
            set => SetProperty(ref capacityOption, value, nameof(CapacityOption));
        }

        public string CabWidthText
        {
            get => cabWidthText;
            set => SetProperty(ref cabWidthText, value, nameof(CabWidthText));
        }

        public string CabHeightText
        {
            get => cabHeightText;
            set => SetProperty(ref cabHeightText, value, nameof(CabHeightText));
        }

        public string SpeedOption
        {
            get => speedOption;
            set => SetProperty(ref speedOption, value, nameof(SpeedOption));
        }

        public int SelectedDoorWidth
        {
            get => selectedDoorWidth;
            set => SetProperty(ref selectedDoorWidth, value, nameof(SelectedDoorWidth));
        }

        public DoorOpeningOption? SelectedDoorOpening
        {
            get => selectedDoorOpening;
            set => SetProperty(ref selectedDoorOpening, value, nameof(SelectedDoorOpening));
        }

        public string HomeFloor
        {
            get => homeFloor;
            set => SetProperty(ref homeFloor, value, nameof(HomeFloor));
        }

        public string CapacityLabel
        {
            get => capacityLabel;
            set
            {
                SetProperty(ref capacityLabel, value, nameof(CapacityLabel));
                RaiseAutomationNames();
            }
        }

        public string CabWidthLabel
        {
            get => cabWidthLabel;
            set
            {
                SetProperty(ref cabWidthLabel, value, nameof(CabWidthLabel));
                RaiseAutomationNames();
            }
        }

        public string CabHeightLabel
        {
            get => cabHeightLabel;
            set
            {
                SetProperty(ref cabHeightLabel, value, nameof(CabHeightLabel));
                RaiseAutomationNames();
            }
        }

        public string SpeedLabel
        {
            get => speedLabel;
            set
            {
                SetProperty(ref speedLabel, value, nameof(SpeedLabel));
                RaiseAutomationNames();
            }
        }

        public string DoorWidthLabel
        {
            get => doorWidthLabel;
            set
            {
                SetProperty(ref doorWidthLabel, value, nameof(DoorWidthLabel));
                RaiseAutomationNames();
            }
        }

        public string DoorOpeningLabel
        {
            get => doorOpeningLabel;
            set
            {
                SetProperty(ref doorOpeningLabel, value, nameof(DoorOpeningLabel));
                RaiseAutomationNames();
            }
        }

        public string HomeFloorLabel
        {
            get => homeFloorLabel;
            set
            {
                SetProperty(ref homeFloorLabel, value, nameof(HomeFloorLabel));
                RaiseAutomationNames();
            }
        }

        public string ServedFloorsLabel
        {
            get => servedFloorsLabel;
            set => SetProperty(ref servedFloorsLabel, value, nameof(ServedFloorsLabel));
        }

        public string RemoveButtonLabel
        {
            get => removeButtonLabel;
            set
            {
                SetProperty(ref removeButtonLabel, value, nameof(RemoveButtonLabel));
                RaiseAutomationNames();
            }
        }

        public string CapacityAutomationName => $"{Title}: {CapacityLabel}";

        public string CabWidthAutomationName => $"{Title}: {CabWidthLabel}";

        public string CabHeightAutomationName => $"{Title}: {CabHeightLabel}";

        public string SpeedAutomationName => $"{Title}: {SpeedLabel}";

        public string DoorWidthAutomationName => $"{Title}: {DoorWidthLabel}";

        public string DoorOpeningAutomationName => $"{Title}: {DoorOpeningLabel}";

        public string HomeFloorAutomationName => $"{Title}: {HomeFloorLabel}";

        public string RemoveAutomationName => $"{RemoveButtonLabel}: {Title}";

        private void RaiseAutomationNames()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CapacityAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CabWidthAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CabHeightAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoorWidthAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoorOpeningAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HomeFloorAutomationName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoveAutomationName)));
        }

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ServedFloorRowViewModel : INotifyPropertyChanged
    {
        private int floorIndex;
        private string floorName = string.Empty;
        private bool isServed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int FloorIndex
        {
            get => floorIndex;
            set => SetProperty(ref floorIndex, value, nameof(FloorIndex));
        }

        public string FloorName
        {
            get => floorName;
            set => SetProperty(ref floorName, value, nameof(FloorName));
        }

        public bool IsServed
        {
            get => isServed;
            set
            {
                if (isServed == value)
                {
                    return;
                }

                isServed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsServed)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ServiceText)));
            }
        }

        public string ServiceText
        {
            get => isServed ? "f" : "0";
            set
            {
                string normalized = value?.Trim() ?? string.Empty;
                IsServed = normalized.Equals("f", StringComparison.OrdinalIgnoreCase) ||
                           normalized.Equals("1", StringComparison.OrdinalIgnoreCase);
            }
        }

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class DispatcherOption
    {
        public DispatcherOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public string Value { get; set; }

        public string DisplayName { get; set; }
    }

    public sealed class DoorOpeningOption
    {
        public DoorOpeningOption(DoorOpeningKind kind, string displayName)
        {
            Kind = kind;
            DisplayName = displayName;
        }

        public DoorOpeningKind Kind { get; set; }

        public string DisplayName { get; set; }
    }

    internal enum EditorSection
    {
        Project,
        Analysis,
        Traffic,
        Building,
        LiftGroup,
    }

    internal enum FloorDisplayOrder
    {
        BottomFirst,
        TopFirst,
    }
}
