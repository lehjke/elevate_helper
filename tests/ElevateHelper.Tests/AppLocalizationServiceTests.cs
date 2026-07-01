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
}
