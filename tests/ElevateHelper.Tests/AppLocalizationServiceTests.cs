using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class AppLocalizationServiceTests
{
    [Fact]
    public void SetLanguage_Russian_UsesRussianStrings()
    {
        AppLocalizationService service = new(persistSelection: false);

        service.SetLanguage(AppLanguage.Russian);

        Assert.Equal("\u042f\u0437\u044b\u043a", service.CurrentText.LanguageLabel);
        Assert.Equal("\u0421\u043a\u0440\u044b\u0432\u0430\u0442\u044c Elevate", service.CurrentText.ElevateHiddenModeLabel);
        Assert.Equal("\u0416\u0438\u043b\u044c\u0435", service.FormatBuildingType(BuildingType.Residence));
        Assert.Equal("\u0410\u043a\u0442\u0438\u0432\u043d\u044b\u0445 \u0437\u0430\u0434\u0430\u0447: 2", service.GetQueueSummary(2));
        Assert.Equal("\u041f\u0435\u0447\u0430\u0442\u044c \u043e\u0442\u0447\u0435\u0442\u043e\u0432", service.CurrentText.PrintReportsButton);
    }

    [Fact]
    public void FormatJobTitle_English_UsesLocalizedTemplate()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);

        string actual = service.FormatJobTitle(3, BuildingType.Office);

        Assert.Equal("Job 3 - Office", actual);
    }

    [Fact]
    public void TranslateRuntimeMessage_Russian_LocalizesKnownLauncherErrors()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.Russian);

        string actual = service.TranslateRuntimeMessage("Run Batch dialog did not open.");

        Assert.Equal("\u0414\u0438\u0430\u043b\u043e\u0433 Run Batch \u043d\u0435 \u043e\u0442\u043a\u0440\u044b\u043b\u0441\u044f.", actual);
    }

    [Fact]
    public void TranslateRuntimeMessage_Russian_LocalizesResultFileRecoveryFailure()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.Russian);

        string actual = service.TranslateRuntimeMessage(
            "Elevate could not open a results file after 4 attempts.");

        Assert.Equal(
            "Elevate \u043d\u0435 \u0441\u043c\u043e\u0433 \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0444\u0430\u0439\u043b \u0440\u0435\u0437\u0443\u043b\u044c\u0442\u0430\u0442\u043e\u0432 \u043f\u043e\u0441\u043b\u0435 \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u0445 \u043f\u0435\u0440\u0435\u0437\u0430\u043f\u0443\u0441\u043a\u043e\u0432 \u0440\u0430\u0441\u0447\u0435\u0442\u0430.",
            actual);
    }

    [Theory]
    [InlineData(
        AppLanguage.English,
        "Elevate cannot run because the installed copy has expired. Install or activate a current licensed version of Peters Research Elevate, then try again.")]
    [InlineData(
        AppLanguage.Russian,
        "Elevate не может выполнить расчет: срок действия установленной копии истек. Установите или активируйте актуальную лицензионную версию Peters Research Elevate и повторите попытку.")]
    public void TranslateRuntimeMessage_LocalizesExpiredLicenseAction(
        AppLanguage language,
        string expected)
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(language);

        string actual = service.TranslateRuntimeMessage(ElevateLauncherService.LicenseExpiredErrorMessage);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Russian)]
    public void FormatJobDetails_Residence_DoesNotMentionPeaks(AppLanguage language)
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(language);

        string actual = service.FormatJobDetails(@"C:\Temp\Project", BuildingType.Residence, includeLunchPeak: true);

        Assert.Equal(@"C:\Temp\Project", actual);
        Assert.DoesNotContain("morning", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lunch", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u0443\u0442\u0440\u0435\u043D", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u043E\u0431\u0435\u0434\u0435\u043D", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectBatchMorningOnlyLabels_AreLocalized()
    {
        AppLocalizationService service = new(persistSelection: false);

        service.SetLanguage(AppLanguage.English);
        Assert.Equal("Morning peak only", service.CurrentText.ProjectBatchMorningOnly);
        Assert.Equal("morning only", service.CurrentText.ProjectBatchPreviewMorningOnly);

        service.SetLanguage(AppLanguage.Russian);
        Assert.Equal("\u0422\u043E\u043B\u044C\u043A\u043E \u0443\u0442\u0440\u0435\u043D\u043D\u0438\u0439 \u043F\u0438\u043A", service.CurrentText.ProjectBatchMorningOnly);
        Assert.Equal("\u0442\u043E\u043B\u044C\u043A\u043E \u0443\u0442\u0440\u043E", service.CurrentText.ProjectBatchPreviewMorningOnly);
    }

    [Theory]
    [InlineData(AppLanguage.English, "en-US")]
    [InlineData(AppLanguage.Russian, "ru-RU")]
    public void CurrentCulture_FollowsSelectedLanguage(AppLanguage language, string expectedCulture)
    {
        AppLocalizationService service = new(persistSelection: false);

        service.SetLanguage(language);

        Assert.Equal(expectedCulture, service.CurrentCulture.Name);
    }

    [Theory]
    [InlineData(AppLanguage.English, "Group 'Office/G1' contains more than one source .elvx file and was skipped.")]
    [InlineData(AppLanguage.Russian, "Группа «Office/G1» содержит больше одного исходного .elvx-файла и пропущена.")]
    public void FormatProjectBatchWarning_UsesSelectedLanguage(AppLanguage language, string expected)
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(language);
        ProjectBatchWarning warning = new(
            @"C:\Project\Office\G1",
            ProjectBatchWarningKind.GroupContainsMultipleSourceFiles,
            "Office/G1");

        string actual = service.FormatProjectBatchWarning(warning);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RelocalizeCatalogMessage_TranslatesExactStatusInBothDirections()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.Russian);

        string russian = service.RelocalizeCatalogMessage(
            "The selected folder does not exist.",
            AppLanguage.English);

        Assert.Equal("Выбранная папка не существует.", russian);

        service.SetLanguage(AppLanguage.English);
        string english = service.RelocalizeCatalogMessage(russian, AppLanguage.Russian);

        Assert.Equal("The selected folder does not exist.", english);
    }

    [Fact]
    public void RelocalizeCatalogMessage_TranslatesFormattedStatusAndNestedLabel()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);
        string english = string.Format(
            service.CurrentCulture,
            service.CurrentText.SelectedBuildingTypeFormat,
            service.CurrentText.BuildingTypeOffice);
        service.SetLanguage(AppLanguage.Russian);

        string russian = service.RelocalizeCatalogMessage(english, AppLanguage.English);

        Assert.Equal("Выбран тип здания: Офис.", russian);
    }

    [Fact]
    public void RelocalizeCatalogMessage_UsesRuntimeTranslatorForKnownEnglishError()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.Russian);

        string actual = service.RelocalizeCatalogMessage(
            "Run Batch dialog did not open.",
            AppLanguage.English);

        Assert.Equal("Диалог Run Batch не открылся.", actual);
    }

    [Fact]
    public void RelocalizeCatalogMessage_DoesNotTranslateUserArgumentThatMatchesCatalogLabel()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);
        string english = string.Format(
            service.CurrentCulture,
            service.CurrentText.RunStartedFormat,
            "Office");
        service.SetLanguage(AppLanguage.Russian);

        string russian = service.RelocalizeCatalogMessage(english, AppLanguage.English);

        Assert.Equal("Office запущена.", russian);
    }

    [Fact]
    public void RelocalizeCatalogMessage_TranslatesRetainedBatchAndValidationStatuses()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);
        string batchStatus = service.CurrentText.ProjectBatchLaunchAlreadyPreparingMessage;
        string baseFloorStatus = string.Format(
            service.CurrentCulture,
            service.CurrentText.EditorBaseFloorLevelFormat,
            "Level -3");
        service.SetLanguage(AppLanguage.Russian);

        string russianBatch = service.RelocalizeCatalogMessage(batchStatus, AppLanguage.English);
        string russianBaseFloor = service.RelocalizeCatalogMessage(baseFloorStatus, AppLanguage.English);

        Assert.Equal("Подготовка пакетного запуска уже выполняется.", russianBatch);
        Assert.Equal("Отметка нижнего этажа «Level -3» должна быть 0 м.", russianBaseFloor);

        service.SetLanguage(AppLanguage.English);
        Assert.Equal(
            batchStatus,
            service.RelocalizeCatalogMessage(russianBatch, AppLanguage.Russian));
        Assert.Equal(
            baseFloorStatus,
            service.RelocalizeCatalogMessage(russianBaseFloor, AppLanguage.Russian));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelocalizeCatalogMessage_UsesJobModeContextForDuplicateLabels(bool includeLunchPeak)
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);
        string english = service.FormatJobDetails(@"C:\Temp\Project", BuildingType.Office, includeLunchPeak);
        service.SetLanguage(AppLanguage.Russian);
        string expectedRussian = service.FormatJobDetails(@"C:\Temp\Project", BuildingType.Office, includeLunchPeak);

        string russian = service.RelocalizeCatalogMessage(english, AppLanguage.English);

        Assert.Equal(expectedRussian, russian);
        service.SetLanguage(AppLanguage.English);
        Assert.Equal(english, service.RelocalizeCatalogMessage(russian, AppLanguage.Russian));
    }

    [Fact]
    public void RelocalizeCatalogMessage_TranslatesRetainedQueueAndEditorStatusesBothWays()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.English);
        string englishJobTitle = service.FormatJobTitle(1, BuildingType.Office);
        string[] english =
        [
            service.CurrentText.ProcessingModeSingleStatus,
            string.Format(service.CurrentCulture, service.CurrentText.JobStoppingFormat, "Office"),
            string.Format(service.CurrentCulture, service.CurrentText.JobDismissedFormat, "Office"),
            string.Format(service.CurrentCulture, service.CurrentText.JobStoppingFormat, englishJobTitle),
            string.Format(service.CurrentCulture, service.CurrentText.JobRestoredFormat, englishJobTitle),
            string.Format(service.CurrentCulture, service.CurrentText.RunStartedFormat, englishJobTitle),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.ScenarioRunStartedFormat,
                englishJobTitle,
                service.CurrentText.MorningScenario),
            service.CurrentText.EditorSimulationCountPositiveMessage,
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorPercentageRangeFormat,
                service.CurrentText.EditorIncomingHeader),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorBuildingTypeMismatchFormat,
                service.CurrentText.BuildingTypeOffice,
                service.CurrentText.BuildingTypeHotel),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.ProjectBatchStartedWithWarningsAndOfficeScenarioFormat,
                2,
                1,
                service.CurrentText.ProjectBatchPreviewMorningLunch),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorServedFloorRequiredFormat,
                string.Format(service.CurrentCulture, service.CurrentText.EditorLiftTitleFormat, 1)),
        ];

        service.SetLanguage(AppLanguage.Russian);
        string russianJobTitle = service.FormatJobTitle(1, BuildingType.Office);
        string[] russian =
        [
            service.CurrentText.ProcessingModeSingleStatus,
            string.Format(service.CurrentCulture, service.CurrentText.JobStoppingFormat, "Office"),
            string.Format(service.CurrentCulture, service.CurrentText.JobDismissedFormat, "Office"),
            string.Format(service.CurrentCulture, service.CurrentText.JobStoppingFormat, russianJobTitle),
            string.Format(service.CurrentCulture, service.CurrentText.JobRestoredFormat, russianJobTitle),
            string.Format(service.CurrentCulture, service.CurrentText.RunStartedFormat, russianJobTitle),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.ScenarioRunStartedFormat,
                russianJobTitle,
                service.CurrentText.MorningScenario),
            service.CurrentText.EditorSimulationCountPositiveMessage,
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorPercentageRangeFormat,
                service.CurrentText.EditorIncomingHeader),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorBuildingTypeMismatchFormat,
                service.CurrentText.BuildingTypeOffice,
                service.CurrentText.BuildingTypeHotel),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.ProjectBatchStartedWithWarningsAndOfficeScenarioFormat,
                2,
                1,
                service.CurrentText.ProjectBatchPreviewMorningLunch),
            string.Format(
                service.CurrentCulture,
                service.CurrentText.EditorServedFloorRequiredFormat,
                string.Format(service.CurrentCulture, service.CurrentText.EditorLiftTitleFormat, 1)),
        ];

        Assert.Equal(
            russian,
            english.Select(message => service.RelocalizeCatalogMessage(message, AppLanguage.English)));

        service.SetLanguage(AppLanguage.English);
        Assert.Equal(
            english,
            russian.Select(message => service.RelocalizeCatalogMessage(message, AppLanguage.Russian)));
    }

    [Fact]
    public void RelocalizeCatalogMessage_DoesNotTreatUnrelatedHyphenTextAsJobDetails()
    {
        AppLocalizationService service = new(persistSelection: false);
        service.SetLanguage(AppLanguage.Russian);

        string actual = service.RelocalizeCatalogMessage("Customer A - custom note", AppLanguage.English);

        Assert.Equal("Customer A - custom note", actual);
    }
}
