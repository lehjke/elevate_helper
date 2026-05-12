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
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ElevateHelperWinUI.Views
{
    public sealed partial class ElevateProjectEditorWindow : Window
    {
        private const int EditorWindowWidth = 1120;
        private const int EditorWindowHeight = 840;

        private readonly AppLocalizationService localizationService = AppLocalizationService.Instance;
        private readonly IElevateProjectEditorService projectEditorService = new ElevateProjectEditorService();
        private readonly LiftGroupRulesService liftGroupRulesService = new LiftGroupRulesService();
        private readonly ObservableCollection<BuildingFloorRowViewModel> floorRows = new ObservableCollection<BuildingFloorRowViewModel>();
        private readonly ObservableCollection<BuildingFloorRowViewModel> displayedFloorRows = new ObservableCollection<BuildingFloorRowViewModel>();
        private readonly ObservableCollection<LiftCarRowViewModel> liftRows = new ObservableCollection<LiftCarRowViewModel>();
        private readonly ObservableCollection<DispatcherOption> dispatcherOptions = new ObservableCollection<DispatcherOption>();
        private readonly string workingFolder;
        private readonly BuildingType buildingType;

        private ElevateProjectEditorDocument? loadedDocument;
        private EditorSection currentSection = EditorSection.Project;
        private FloorDisplayOrder currentFloorDisplayOrder = FloorDisplayOrder.BottomFirst;
        private string preservedTrafficMode = string.Empty;
        private int preservedLearningRuns;
        private int preservedRandomSeed;
        private double preservedHandlingCapacity;
        private double preservedLoadingTime;
        private double preservedUnloadingTime;
        private string preservedLogoFile = string.Empty;

        public ElevateProjectEditorWindow(string workingFolder, BuildingType buildingType)
        {
            this.workingFolder = Path.GetFullPath(workingFolder);
            this.buildingType = buildingType;

            InitializeComponent();

            FloorsItemsControl.ItemsSource = displayedFloorRows;
            LiftItemsControl.ItemsSource = liftRows;
            DispatcherComboBox.ItemsSource = dispatcherOptions;

            localizationService.LanguageChanged += OnLanguageChanged;
            Closed += OnClosed;

            ConfigureWindow();
            ApplyStaticContext();
            ApplyLocalizedText();
            ResetEditorState();
            ShowSection(EditorSection.Project);
        }

        private AppLocalizationService.AppTextCatalog Text
        {
            get { return localizationService.CurrentText; }
        }

        private void ConfigureWindow()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }

            AppWindow.Resize(new SizeInt32(EditorWindowWidth, EditorWindowHeight));
            DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            RectInt32 workArea = displayArea.WorkArea;
            int x = workArea.X + Math.Max(0, (workArea.Width - EditorWindowWidth) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - EditorWindowHeight) / 2);
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
            Title = Text.EditorTitle;
            HeroTitleTextBlock.Text = Text.EditorTitle;
            WorkingFolderLabelTextBlock.Text = Text.EditorWorkingFolderLabel;
            BuildingTypeLabelTextBlock.Text = Text.EditorBuildingTypeLabel;
            SourceLabelTextBlock.Text = Text.EditorSourceLabel;
            OutputLabelTextBlock.Text = Text.EditorOutputLabel;

            LoadExistingButton.Content = Text.LoadEditorButton;
            LoadTemplateButton.Content = Text.LoadEditorTemplateButton;
            SaveButton.Content = Text.SaveEditorButton;
            CloseButton.Content = Text.EditorCloseButton;

            ProjectTabButton.Content = Text.EditorProjectTabTitle;
            AnalysisTabButton.Content = Text.EditorAnalysisTabTitle;
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

            FloorNameColumnTextBlock.Text = Text.EditorFloorNameColumn;
            InterfloorHeightColumnTextBlock.Text = Text.EditorInterfloorHeightColumn;
            PopulationColumnTextBlock.Text = Text.EditorPopulationColumn;
            EntranceColumnTextBlock.Text = Text.EditorEntranceColumn;
            AddFloorAboveButton.Content = Text.EditorAddFloorAboveButton;
            AddFloorBelowButton.Content = Text.EditorAddFloorBelowButton;
            SortTopFirstButton.Content = Text.EditorSortTopFirstButton;
            SortBottomFirstButton.Content = Text.EditorSortBottomFirstButton;

            AddLiftButton.Content = Text.EditorAddLiftButton;
            StatusInfoBar.Title = Text.StatusTitle;
            if (!StatusInfoBar.IsOpen)
            {
                StatusInfoBar.Message = Text.Ready;
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
            }

            RebuildDispatcherOptions();
            ApplyLocalizationToLiftRows();
            RefreshLiftCountSummary();
            ApplyFloorSortButtonStyles();
            ApplySectionVisuals();
        }

        private void ResetEditorState()
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
            SimulationCountTextBox.Text = "10";
            AbsenteeismTextBox.Text = buildingType == BuildingType.Office ? "20" : string.Empty;

            List<ElevateProjectEditorFloor> fallbackFloors = BuildFallbackFloors();
            ApplyBuildingRows(fallbackFloors);
            liftRows.Clear();
            AddDefaultLiftRow(fallbackFloors);
            RefreshLiftCountSummary();
        }

        private async void OnLoadExistingButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string filePath = ResolveExistingElvxPath();
                ElevateProjectEditorDocument document = await projectEditorService.LoadFile(filePath);
                ApplyLoadedDocument(document);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(filePath)), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
        }

        private async void OnLoadTemplateButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ElevateProjectEditorDocument document = await projectEditorService.LoadTemplate(buildingType);
                ApplyLoadedDocument(document);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorLoadSuccessFormat, Path.GetFileName(document.TemplatePath ?? string.Empty)), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
            }
        }

        private async void OnSaveButtonClick(object sender, RoutedEventArgs e)
        {
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

            try
            {
                string outputPath = ResolveOutputPath(document);
                ProcessingResult result = await projectEditorService.SaveAsync(document, outputPath);
                if (!result.Success)
                {
                    SetStatus(result.Message, InfoBarSeverity.Error);
                    return;
                }

                ElevateProjectEditorDocument refreshedDocument = await projectEditorService.LoadFile(outputPath);
                ApplyLoadedDocument(refreshedDocument);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorSaveSuccessFormat, Path.GetFileName(outputPath)), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                SetStatus(BuildExceptionMessage(ex), InfoBarSeverity.Error);
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
            BuildingPanel.Visibility = section == EditorSection.Building ? Visibility.Visible : Visibility.Collapsed;
            LiftGroupPanel.Visibility = section == EditorSection.LiftGroup ? Visibility.Visible : Visibility.Collapsed;
            ApplySectionVisuals();
        }

        private void ApplySectionVisuals()
        {
            SetTabButtonStyle(ProjectTabButton, currentSection == EditorSection.Project);
            SetTabButtonStyle(AnalysisTabButton, currentSection == EditorSection.Analysis);
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
        }

        private void ApplyFloorSortButtonStyles()
        {
            SetTabButtonStyle(SortTopFirstButton, currentFloorDisplayOrder == FloorDisplayOrder.TopFirst);
            SetTabButtonStyle(SortBottomFirstButton, currentFloorDisplayOrder == FloorDisplayOrder.BottomFirst);
        }

        private void OnAddLiftButtonClick(object sender, RoutedEventArgs e)
        {
            AddDefaultLiftRow(BuildFloorDraft());
        }

        private void OnRemoveLiftCardButtonClick(object sender, RoutedEventArgs e)
        {
            if (liftRows.Count <= 1)
            {
                return;
            }

            Button? button = sender as Button;
            LiftCarRowViewModel? row = button?.Tag as LiftCarRowViewModel;
            if (row == null)
            {
                return;
            }

            liftRows.Remove(row);
            RefreshLiftTitles();
            RefreshLiftCountSummary();
        }

        private void OnOutputRelevantFieldChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOutputPreview();
        }
        private void ApplyLoadedDocument(ElevateProjectEditorDocument document)
        {
            loadedDocument = document;
            preservedTrafficMode = document.Analysis.TrafficMode;
            preservedLearningRuns = document.Analysis.LearningRuns;
            preservedRandomSeed = document.Analysis.RandomSeed;
            preservedHandlingCapacity = document.Traffic.HandlingCapacity;
            preservedLoadingTime = document.Traffic.LoadingTimeSeconds;
            preservedUnloadingTime = document.Traffic.UnloadingTimeSeconds;
            preservedLogoFile = document.Job.LogoFile;

            SourceValueTextBlock.Text = document.SourcePath ?? document.TemplatePath ?? "-";
            ProjectTitleTextBox.Text = document.Job.Title;
            ProjectNumberTextBox.Text = document.Job.Number;
            ProjectCalculationTitleTextBox.Text = document.Job.CalculationTitle;
            ProjectMadeByTextBox.Text = document.Job.MadeBy;
            ProjectCheckedByTextBox.Text = document.Job.CheckedBy;
            ProjectCompanyTextBox.Text = document.Job.Company;
            SimulationCountTextBox.Text = document.Analysis.SimulationsPerConfiguration.ToString(CultureInfo.InvariantCulture);
            AbsenteeismTextBox.Text = FormatEditableNumber(document.Building.AbsenteeismPercent);

            ApplyDispatcherSelection(document.Analysis.DispatcherAlgorithmName);
            ApplyBuildingRows(document.Floors);
            ApplyLiftRows(document);
            UpdateOutputPreview();
        }

        private void ApplyDispatcherSelection(string algorithmName)
        {
            DispatcherOption? option = dispatcherOptions.FirstOrDefault(candidate => string.Equals(candidate.Value, algorithmName, StringComparison.OrdinalIgnoreCase));
            DispatcherComboBox.SelectedItem = option ?? dispatcherOptions.FirstOrDefault();
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
                    EntranceFloor = floor.EntranceFloor,
                };
                row.PropertyChanged += OnBuildingFloorRowPropertyChanged;
                floorRows.Add(row);
            }

            RebuildDisplayedFloorRows();
            SyncLiftRowsWithFloors();
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

            BuildingFloorRowViewModel newRow = new BuildingFloorRowViewModel
            {
                SourceFloorName = string.Empty,
                FloorName = SuggestFloorName(isTopFloor),
                InterfloorHeightText = ResolveSuggestedInterfloorHeightText(seed.InterfloorHeightText),
                PopulationText = isTopFloor ? seed.PopulationText : "0",
                EntranceFloor = false,
            };
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
                floorRows.Insert(0, newRow);
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
        }

        private void OnBuildingFloorRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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

                liftRow.ServedFloors.Clear();
                for (int floorIndex = 0; floorIndex < floors.Count; floorIndex++)
                {
                    ElevateProjectEditorFloor floor = floors[floorIndex];
                    liftRow.ServedFloors.Add(new ServedFloorRowViewModel
                    {
                        FloorIndex = floorIndex + 1,
                        FloorName = floor.FloorName,
                        IsServed = servedFloorIndexes.Count == 0 || servedFloorIndexes.Contains(floorIndex + 1),
                    });
                }

                int homeFloor = ParseIntOrDefault(liftRow.HomeFloor, ParseIntOrDefault(ResolveFallbackHomeFloor(floors), 1));
                if (homeFloor < 1 || homeFloor > floors.Count)
                {
                    liftRow.HomeFloor = ResolveFallbackHomeFloor(floors);
                }
            }
        }

        private BuildingFloorRowViewModel CreateDefaultFloorRow()
        {
            return new BuildingFloorRowViewModel
            {
                SourceFloorName = string.Empty,
                FloorName = "Level 1",
                InterfloorHeightText = "3,9",
                PopulationText = "150",
                EntranceFloor = true,
            };
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
                return $"Level {nextValue}";
            }

            return isTopFloor
                ? $"Level {floorRows.Count + 1}"
                : $"Level {Math.Min(0, 1 - floorRows.Count)}";
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
            liftRows.Clear();
            foreach (ElevateProjectEditorCar car in document.Cars)
            {
                liftRows.Add(CreateLiftRow(car, document.Floors, null));
            }

            if (liftRows.Count == 0)
            {
                AddDefaultLiftRow(document.Floors);
            }
            else
            {
                RefreshLiftTitles();
                RefreshLiftCountSummary();
            }
        }

        private LiftCarRowViewModel CreateLiftRow(ElevateProjectEditorCar car, IReadOnlyList<ElevateProjectEditorFloor> floors, LiftCarRowViewModel? source)
        {
            Tuple<int, int> cabinDimensions = source == null
                ? EstimateCabinDimensions(car.FloorAreaM2)
                : Tuple.Create(ParseIntOrDefault(source.CabWidthText, 1600), ParseIntOrDefault(source.CabHeightText, 2100));

            DoorOpeningKind openingKind;
            int doorWidth;
            if (source == null)
            {
                (string Width, string Type) doorInfo = ElevateReportService.ResolveDoorInfo(
                    ParseFlexibleDouble(car.DoorOpenTime),
                    ParseFlexibleDouble(car.DoorCloseTime),
                    ParseFlexibleDouble(car.DoorPreOpening));
                openingKind = liftGroupRulesService.ResolveDoorOpeningKind(doorInfo.Type);
                doorWidth = ParseIntOrDefault(doorInfo.Width, liftGroupRulesService.GetDoorWidthOptions().First());
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
                HomeShaft = string.IsNullOrWhiteSpace(car.HomeShaft) ? (liftRows.Count + 1).ToString(CultureInfo.InvariantCulture) : car.HomeShaft,
                TemplateXml = car.TemplateXml,
                CapacityOption = NormalizeCapacity(car.CapacityKg),
                CabWidthText = cabinDimensions.Item1.ToString(CultureInfo.InvariantCulture),
                CabHeightText = cabinDimensions.Item2.ToString(CultureInfo.InvariantCulture),
                SpeedOption = NormalizeSpeed(car.Speed),
                SelectedDoorWidth = doorWidth,
                HomeFloor = ResolveHomeFloor(car.HomeFloor, floors),
            };

            foreach (string option in liftGroupRulesService.GetCapacityOptions())
            {
                row.CapacityOptions.Add(option);
            }

            foreach (string option in liftGroupRulesService.GetSpeedOptions())
            {
                row.SpeedOptions.Add(option);
            }

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
                    CapacityKg = "1050.000000",
                    FloorAreaM2 = liftGroupRulesService.ResolveCarAreaSquareMeters(1600, 2100).ToString("0.000000", CultureInfo.InvariantCulture),
                    Speed = "2.500000",
                    Acceleration = "0.900000",
                    Jerk = "1.000000",
                    DoorPreOpening = "0.500000",
                    DoorOpenTime = "1.800000",
                    DoorCloseTime = "2.900000",
                    HomeFloor = ResolveFallbackHomeFloor(effectiveFloors),
                    ServedFloorIndexes = Enumerable.Range(1, effectiveFloors.Count).ToList(),
                };

                row = CreateLiftRow(baselineCar, effectiveFloors, null);
            }

            liftRows.Add(row);
            RefreshLiftTitles();
            RefreshLiftCountSummary();
        }

        private LiftCarRowViewModel CloneLiftRow(LiftCarRowViewModel source)
        {
            LiftCarRowViewModel clone = new LiftCarRowViewModel
            {
                Id = (liftRows.Count + 1).ToString(CultureInfo.InvariantCulture),
                HomeShaft = (liftRows.Count + 1).ToString(CultureInfo.InvariantCulture),
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
            row.ServedFloorsLabel = localizationService.CurrentLanguage == AppLanguage.Russian ? "Обслуживаемые этажи" : "Served floors";
            row.RemoveButtonLabel = Text.EditorRemoveLiftButton;

            row.DoorOpeningOptions.Clear();
            row.DoorOpeningOptions.Add(new DoorOpeningOption(DoorOpeningKind.Central, Text.EditorDoorOpeningCentral));
            row.DoorOpeningOptions.Add(new DoorOpeningOption(DoorOpeningKind.Telescopic, Text.EditorDoorOpeningTelescopic));
            row.SelectedDoorOpening = row.DoorOpeningOptions.FirstOrDefault(option => option.Kind == selectedKind) ?? row.DoorOpeningOptions.FirstOrDefault();
        }
        private bool TryBuildDocument(out ElevateProjectEditorDocument? document)
        {
            document = null;
            if (loadedDocument == null)
            {
                SetStatus(Text.EditorNotLoadedMessage, InfoBarSeverity.Warning);
                return false;
            }

            if (!TryParseDouble(AbsenteeismTextBox.Text, Text.EditorAbsenteeismHeader, out double absenteeism, buildingType != BuildingType.Office) ||
                !TryParseInt(SimulationCountTextBox.Text, Text.EditorSimulationsHeader, out int simulationCount) ||
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
                    DispatcherAlgorithmName = (DispatcherComboBox.SelectedItem as DispatcherOption)?.Value ?? "Group Collective",
                    TrafficMode = preservedTrafficMode,
                    SimulationsPerConfiguration = simulationCount,
                    LearningRuns = preservedLearningRuns,
                    RandomSeed = preservedRandomSeed,
                },
                Building = new ElevateProjectEditorBuildingSection
                {
                    BuildingType = buildingType,
                    AbsenteeismPercent = buildingType == BuildingType.Office ? absenteeism : loadedDocument.Building.AbsenteeismPercent,
                    NumberOfFloors = floors.Count,
                },
                Traffic = new ElevateProjectEditorTrafficSection
                {
                    IncomingPercent = loadedDocument.Traffic.IncomingPercent,
                    OutgoingPercent = loadedDocument.Traffic.OutgoingPercent,
                    InterfloorPercent = loadedDocument.Traffic.InterfloorPercent,
                    HandlingCapacity = preservedHandlingCapacity,
                    LoadingTimeSeconds = preservedLoadingTime,
                    UnloadingTimeSeconds = preservedUnloadingTime,
                },
                Floors = floors,
                Cars = cars,
            };

            return true;
        }

        private bool TryBuildFloors(out List<ElevateProjectEditorFloor> floors)
        {
            floors = new List<ElevateProjectEditorFloor>();
            double currentLevel = 0d;

            foreach (BuildingFloorRowViewModel row in floorRows)
            {
                if (!TryParseDouble(row.InterfloorHeightText, row.FloorName, out double interfloorHeight, false) ||
                    !TryParseDouble(row.PopulationText, row.FloorName, out double population, false))
                {
                    return false;
                }

                currentLevel += interfloorHeight;
                floors.Add(new ElevateProjectEditorFloor
                {
                    FloorIndex = floors.Count + 1,
                    SourceFloorName = row.SourceFloorName,
                    FloorName = row.FloorName,
                    InterfloorHeight = interfloorHeight,
                    FloorLevel = currentLevel,
                    Population = population,
                    EntranceFloor = row.EntranceFloor,
                });
            }

            if (floors.Count == 0)
            {
                SetStatus(localizationService.CurrentLanguage == AppLanguage.Russian ? "Таблица здания пуста." : "Building table is empty.", InfoBarSeverity.Warning);
                return false;
            }

            return true;
        }

        private bool TryBuildCars(IReadOnlyList<ElevateProjectEditorFloor> floors, out List<ElevateProjectEditorCar> cars)
        {
            cars = new List<ElevateProjectEditorCar>();
            if (liftRows.Count == 0)
            {
                SetStatus(localizationService.CurrentLanguage == AppLanguage.Russian ? "Нужно добавить хотя бы один лифт." : "Add at least one lift.", InfoBarSeverity.Warning);
                return false;
            }

            foreach (LiftCarRowViewModel row in liftRows)
            {
                int cabinWidth = ParseIntOrDefault(row.CabWidthText, 0);
                int cabinHeight = ParseIntOrDefault(row.CabHeightText, 0);
                if (cabinWidth <= 0)
                {
                    SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorInvalidNumberFormat, row.CabWidthLabel), InfoBarSeverity.Warning);
                    return false;
                }

                if (cabinHeight <= 0)
                {
                    SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorInvalidNumberFormat, row.CabHeightLabel), InfoBarSeverity.Warning);
                    return false;
                }

                int homeFloor = ParseIntOrDefault(row.HomeFloor, ParseIntOrDefault(ResolveFallbackHomeFloor(floors), 1));
                if (homeFloor < 1 || homeFloor > floors.Count)
                {
                    homeFloor = ParseIntOrDefault(ResolveFallbackHomeFloor(floors), 1);
                }

                List<int> servedFloorIndexes = row.ServedFloors.Where(floor => floor.IsServed).Select(floor => floor.FloorIndex).Distinct().OrderBy(floorIndex => floorIndex).ToList();
                if (servedFloorIndexes.Count == 0)
                {
                    SetStatus(
                        localizationService.CurrentLanguage == AppLanguage.Russian
                            ? string.Format(CultureInfo.CurrentCulture, "Для {0} нужно выбрать хотя бы один обслуживаемый этаж.", row.Title)
                            : string.Format(CultureInfo.CurrentCulture, "{0} must serve at least one floor.", row.Title),
                        InfoBarSeverity.Warning);
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
                    CapacityKg = ParseFlexibleDouble(row.CapacityOption).ToString("0.000000", CultureInfo.InvariantCulture),
                    FloorAreaM2 = liftGroupRulesService.ResolveCarAreaSquareMeters(cabinWidth, cabinHeight).ToString("0.000000", CultureInfo.InvariantCulture),
                    Speed = ParseFlexibleDouble(row.SpeedOption).ToString("0.000000", CultureInfo.InvariantCulture),
                    Acceleration = motionProfile.Acceleration,
                    Jerk = motionProfile.Jerk,
                    DoorPreOpening = doorProfile.DoorPreOpening,
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
                    EntranceFloor = row.EntranceFloor,
                });
            }

            return floors;
        }

        private List<ElevateProjectEditorFloor> BuildFallbackFloors()
        {
            return new List<ElevateProjectEditorFloor>
            {
                new ElevateProjectEditorFloor { FloorIndex = 1, SourceFloorName = string.Empty, FloorName = "Level 1", InterfloorHeight = 0d, FloorLevel = 0d, Population = 0d, EntranceFloor = true },
                new ElevateProjectEditorFloor { FloorIndex = 2, SourceFloorName = string.Empty, FloorName = "Level 2", InterfloorHeight = 3.9d, FloorLevel = 3.9d, Population = 150d, EntranceFloor = false },
                new ElevateProjectEditorFloor { FloorIndex = 3, SourceFloorName = string.Empty, FloorName = "Level 3", InterfloorHeight = 3.9d, FloorLevel = 7.8d, Population = 150d, EntranceFloor = false },
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

        private string ResolveExistingElvxPath()
        {
            string[] topLevelFiles = Directory.GetFiles(workingFolder, "*.elvx", SearchOption.TopDirectoryOnly);
            string? existingElvxPath = SelectPreferredElvxPath(topLevelFiles);
            if (!string.IsNullOrEmpty(existingElvxPath))
            {
                return existingElvxPath;
            }

            string[] recursiveFiles = Directory.GetFiles(workingFolder, "*.elvx", SearchOption.AllDirectories).Where(path => !IsKnownBatchFolder(Path.GetDirectoryName(path))).ToArray();
            existingElvxPath = SelectPreferredElvxPath(recursiveFiles);
            if (!string.IsNullOrEmpty(existingElvxPath))
            {
                return existingElvxPath;
            }

            throw new InvalidOperationException(Text.EditorExistingFileMissingMessage);
        }

        private static string? SelectPreferredElvxPath(IEnumerable<string> files)
        {
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(path => path.EndsWith("01.elvx", StringComparison.OrdinalIgnoreCase))
                ?? files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
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

                row.Title = localizationService.CurrentLanguage == AppLanguage.Russian ? $"Лифт {index + 1}" : $"Lift {index + 1}";
            }
        }

        private void RefreshLiftCountSummary()
        {
            LiftCountSummaryTextBlock.Text = string.Format(CultureInfo.CurrentCulture, "{0}: {1}", Text.EditorLiftCountLabel, liftRows.Count);
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
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
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

            ApplyStaticContext();
            ApplyLocalizedText();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            localizationService.LanguageChanged -= OnLanguageChanged;
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
            return value <= 0 ? "1050" : ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeSpeed(string numericText)
        {
            double value = ParseFlexibleDouble(numericText);
            if (value <= 0)
            {
                value = 2.5d;
            }

            return value.ToString("0.##", CultureInfo.InvariantCulture);
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

        private bool TryParseDouble(string? text, string fieldName, out double value, bool allowEmpty)
        {
            if (allowEmpty && string.IsNullOrWhiteSpace(text))
            {
                value = 0d;
                return true;
            }

            if (!TryParseFlexibleDoubleInternal(text, out value) || !double.IsFinite(value))
            {
                SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName), InfoBarSeverity.Warning);
                return false;
            }

            return true;
        }

        private bool TryParseInt(string? text, string fieldName, out int value)
        {
            value = 0;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out value))
            {
                return true;
            }

            SetStatus(string.Format(CultureInfo.CurrentCulture, Text.EditorInvalidNumberFormat, fieldName), InfoBarSeverity.Warning);
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

        private static string FormatEditableNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
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
        private bool entranceFloor;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SourceFloorName
        {
            get => sourceFloorName;
            set => SetProperty(ref sourceFloorName, value, nameof(SourceFloorName));
        }

        public string FloorName
        {
            get => floorName;
            set => SetProperty(ref floorName, value, nameof(FloorName));
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

        public bool EntranceFloor
        {
            get => entranceFloor;
            set => SetProperty(ref entranceFloor, value, nameof(EntranceFloor));
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
            set => SetProperty(ref title, value, nameof(Title));
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
            set => SetProperty(ref capacityLabel, value, nameof(CapacityLabel));
        }

        public string CabWidthLabel
        {
            get => cabWidthLabel;
            set => SetProperty(ref cabWidthLabel, value, nameof(CabWidthLabel));
        }

        public string CabHeightLabel
        {
            get => cabHeightLabel;
            set => SetProperty(ref cabHeightLabel, value, nameof(CabHeightLabel));
        }

        public string SpeedLabel
        {
            get => speedLabel;
            set => SetProperty(ref speedLabel, value, nameof(SpeedLabel));
        }

        public string DoorWidthLabel
        {
            get => doorWidthLabel;
            set => SetProperty(ref doorWidthLabel, value, nameof(DoorWidthLabel));
        }

        public string DoorOpeningLabel
        {
            get => doorOpeningLabel;
            set => SetProperty(ref doorOpeningLabel, value, nameof(DoorOpeningLabel));
        }

        public string HomeFloorLabel
        {
            get => homeFloorLabel;
            set => SetProperty(ref homeFloorLabel, value, nameof(HomeFloorLabel));
        }

        public string ServedFloorsLabel
        {
            get => servedFloorsLabel;
            set => SetProperty(ref servedFloorsLabel, value, nameof(ServedFloorsLabel));
        }

        public string RemoveButtonLabel
        {
            get => removeButtonLabel;
            set => SetProperty(ref removeButtonLabel, value, nameof(RemoveButtonLabel));
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
            set => SetProperty(ref isServed, value, nameof(IsServed));
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
        Building,
        LiftGroup,
    }

    internal enum FloorDisplayOrder
    {
        BottomFirst,
        TopFirst,
    }
}
