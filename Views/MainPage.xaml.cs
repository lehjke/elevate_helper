using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelperWinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly IElevateIntegrationService integrationService = new ElevateIntegrationService();
    private readonly IElevateProcessingService processingService = new ElevateProcessingService();
    private readonly IElevateReportService reportService = new ElevateReportService();
    private bool isBusy;

    public MainPage()
    {
        this.InitializeComponent();
        OfficeRadioButton.IsChecked = true;
        UpdateModeButtons(BuildingType.Office);
        RefreshIntegrationStatus(showStatusMessage: true);
    }

    private async void OnRunButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        bool includeLunchPeak = true;
        await ExecuteBusyActionAsync(
            "Processing Elevate files...",
            async () =>
            {
                ProcessingResult result = await processingService.RunAsync(
                    path,
                    buildingType,
                    includeLunchPeak);
                HandleResult(result, "Run completed successfully.");
            });
    }

    private async void OnRunMorningOnlyButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        if (!TryEnsureIntegrationForLaunch())
        {
            return;
        }

        if (buildingType != BuildingType.Office)
        {
            SetStatus("Run Morning Only is available only for Office.", InfoBarSeverity.Warning);
            return;
        }

        await ExecuteBusyActionAsync(
            "Processing morning scenario...",
            async () =>
            {
                ProcessingResult result = await processingService.RunAsync(
                    path,
                    buildingType,
                    includeLunchPeak: false);
                HandleResult(result, "Morning scenario completed successfully.");
            });
    }

    private async void OnReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteBusyActionAsync(
            "Generating report...",
            async () =>
            {
                ProcessingResult result = await reportService.PrintReportAsync(path, buildingType);
                HandleResult(result, "Report generated successfully.");
            });
    }

    private async void OnMorningReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteBusyActionAsync(
            "Generating morning report...",
            async () =>
            {
                string morningPath = Path.Combine(path, "morning");
                ProcessingResult result = await reportService.PrintReportAsync(morningPath, buildingType);
                HandleResult(result, "Morning report generated successfully.");
            });
    }

    private async void OnLunchReportButtonClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetInputs(out string path, out BuildingType buildingType))
        {
            return;
        }

        await ExecuteBusyActionAsync(
            "Generating lunch report...",
            async () =>
            {
                string lunchPath = Path.Combine(path, "lunch");
                ProcessingResult result = await reportService.PrintReportAsync(lunchPath, buildingType);
                HandleResult(result, "Lunch report generated successfully.");
            });
    }

    private void OnExitButtonClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private void OnBuildingTypeRadioButtonChecked(object sender, RoutedEventArgs e)
    {
        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            return;
        }

        UpdateModeButtons(selectedType.Value);
        SetStatus($"Selected building type: {selectedType.Value}.", InfoBarSeverity.Informational);
    }

    private bool TryGetInputs(out string path, out BuildingType buildingType)
    {
        path = PathTextBox.Text?.Trim() ?? string.Empty;
        buildingType = BuildingType.Office;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Enter the path to the Elevate folder.", InfoBarSeverity.Warning);
            return false;
        }

        if (!Directory.Exists(path))
        {
            SetStatus("The specified folder does not exist.", InfoBarSeverity.Error);
            return false;
        }

        BuildingType? selectedType = GetSelectedBuildingType();
        if (!selectedType.HasValue)
        {
            SetStatus("Select a building type.", InfoBarSeverity.Warning);
            return false;
        }

        buildingType = selectedType.Value;
        return true;
    }

    private async Task ExecuteBusyActionAsync(string busyText, Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        try
        {
            SetBusy(true, busyText);
            await action();
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            if (ex.InnerException is not null)
            {
                message = $"{message} | {ex.InnerException.Message}";
            }

            SetStatus(message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false, "Ready");
        }
    }

    private void HandleResult(ProcessingResult result, string successMessage)
    {
        if (result.Success)
        {
            SetStatus(successMessage, InfoBarSeverity.Success);
            return;
        }

        string message = result.Message;
        if (result.Exception is not null)
        {
            message = $"{message} | {result.Exception.Message}";
        }

        SetStatus(message, InfoBarSeverity.Error);
    }

    private void SetBusy(bool value, string message)
    {
        isBusy = value;
        BusyRing.IsActive = value;
        BusyTextBlock.Text = message;

        PathTextBox.IsEnabled = !value;
        OfficeRadioButton.IsEnabled = !value;
        ResidenceRadioButton.IsEnabled = !value;
        HotelRadioButton.IsEnabled = !value;

        RunButton.IsEnabled = !value;
        RunMorningOnlyButton.IsEnabled = !value;
        ExitButton.IsEnabled = !value;

        ReportButton.IsEnabled = !value;
        MorningReportButton.IsEnabled = !value;
        LunchReportButton.IsEnabled = !value;
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private void UpdateModeButtons(BuildingType buildingType)
    {
        bool isOffice = buildingType == BuildingType.Office;

        RunMorningOnlyButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        MorningReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        LunchReportButton.Visibility = isOffice ? Visibility.Visible : Visibility.Collapsed;
        ReportButton.Visibility = isOffice ? Visibility.Collapsed : Visibility.Visible;
    }

    private BuildingType? GetSelectedBuildingType()
    {
        if (OfficeRadioButton.IsChecked == true)
        {
            return BuildingType.Office;
        }

        if (ResidenceRadioButton.IsChecked == true)
        {
            return BuildingType.Residence;
        }

        if (HotelRadioButton.IsChecked == true)
        {
            return BuildingType.Hotel;
        }

        return null;
    }

    private bool TryEnsureIntegrationForLaunch()
    {
        ElevateIntegrationInfo info = integrationService.GetIntegrationInfo();
        if (info.IsDetected)
        {
            return true;
        }

        SetStatus(
            "Peters Research Elevate is not detected. Install Elevate or set ELEVATE_EXE_PATH.",
            InfoBarSeverity.Error);
        return false;
    }

    private void RefreshIntegrationStatus(bool showStatusMessage)
    {
        ElevateIntegrationInfo info = integrationService.GetIntegrationInfo();

        if (!showStatusMessage)
        {
            return;
        }

        if (info.IsDetected)
        {
            string versionPart = string.IsNullOrWhiteSpace(info.ProductVersion)
                ? string.Empty
                : $" Version: {info.ProductVersion}.";
            SetStatus(
                $"Elevate found.{versionPart} Path: {info.ExecutablePath}",
                InfoBarSeverity.Success);
            return;
        }

        SetStatus(
            "Elevate was not found. Check installation or define ELEVATE_EXE_PATH.",
            InfoBarSeverity.Warning);
    }
}
