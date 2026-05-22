using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class AppUpdateServiceTests
{
    private const string TestDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("2.0.4", "v2.0.5", true)]
    [InlineData("2.0.4+abc123", "v2.0.4", false)]
    [InlineData("2.0.5", "v2.0.4", false)]
    [InlineData("2.0.5-preview.1", "v2.0.5", true)]
    [InlineData("2.0.5", "v2.0.5-preview.1", false)]
    [InlineData("2.0.5-preview.1", "v2.0.5-preview.2", true)]
    [InlineData("2.0.5-preview.10", "v2.0.5-preview.2", false)]
    [InlineData("", "v2.0.4", false)]
    public void IsUpdateAvailable_ComparesNormalizedReleaseVersions(
        string currentVersion,
        string latestTag,
        bool expected)
    {
        bool actual = AppUpdateService.IsUpdateAvailable(currentVersion, latestTag);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("v2.0.4", "2.0.4")]
    [InlineData("2.0.4+branch.sha", "2.0.4")]
    [InlineData("v2.0.4-preview.1", "2.0.4-preview.1")]
    public void NormalizeVersion_RemovesTagPrefixAndMetadata(string input, string expected)
    {
        string actual = AppUpdateService.NormalizeVersion(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectSetupAsset_PrefersSetupInstaller()
    {
        AppUpdateAsset? actual = AppUpdateService.SelectSetupAsset(
        [
            new("ElevateHelper-win-x64-v2.0.5.zip", "https://example.test/app.zip", "sha256:zip"),
            new("ElevateHelper-win-x64-v2.0.5-setup.exe", "https://example.test/app-setup.exe", "sha256:setup"),
            new("ElevateHelper-win-x86-v2.0.5.exe", "https://example.test/app-x86.exe", "sha256:x86"),
        ]);

        Assert.NotNull(actual);
        Assert.Equal("ElevateHelper-win-x64-v2.0.5-setup.exe", actual.Name);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReadsReleaseAssetDigest()
    {
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "tag_name": "v999.0.0",
                  "html_url": "https://example.test/releases/v999.0.0",
                  "assets": [
                    {
                      "name": "ElevateHelper-win-x64-v999.0.0-setup.exe",
                      "browser_download_url": "https://example.test/setup.exe",
                      "digest": "{{TestDigest}}"
                    }
                  ]
                }
                """),
        }));
        AppUpdateService service = new(httpClient);

        AppUpdateInfo? update = await service.CheckForUpdateAsync();

        Assert.NotNull(update);
        Assert.Equal(TestDigest, update.SetupAsset.Digest);
    }

    [Fact]
    public async Task DownloadAndStartUpdateAsync_RejectsDigestMismatchBeforeStartingInstaller()
    {
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("not the expected installer"u8.ToArray()),
        }));
        AppUpdateService service = new(httpClient);
        string latestTag = $"v2.0.5-test-{Guid.NewGuid():N}";
        AppUpdateInfo update = new(
            CurrentVersion: "2.0.4",
            LatestVersion: "2.0.5",
            LatestTag: latestTag,
            ReleaseUrl: "https://example.test/releases/v2.0.5",
            SetupAsset: new AppUpdateAsset("ElevateHelper-test-setup.exe", "https://example.test/setup.exe", TestDigest));

        try
        {
            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadAndStartUpdateAsync(update));

            Assert.Contains("SHA-256", actual.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            string updateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElevateHelper",
                "updates",
                latestTag);
            if (Directory.Exists(updateDirectory))
            {
                Directory.Delete(updateDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DownloadAndStartUpdateAsync_ReportsDownloadProgressBeforeVerification()
    {
        byte[] installerBytes = "not the expected installer"u8.ToArray();
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes),
        }));
        AppUpdateService service = new(httpClient);
        List<AppUpdateProgress> updates = [];
        InlineProgress<AppUpdateProgress> progress = new(updates.Add);
        string latestTag = $"v2.0.5-progress-{Guid.NewGuid():N}";
        AppUpdateInfo update = new(
            CurrentVersion: "2.0.4",
            LatestVersion: "2.0.5",
            LatestTag: latestTag,
            ReleaseUrl: "https://example.test/releases/v2.0.5",
            SetupAsset: new AppUpdateAsset("ElevateHelper-test-setup.exe", "https://example.test/setup.exe", TestDigest));

        try
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DownloadAndStartUpdateAsync(update, progress));

            Assert.Contains(updates, item => item.Stage == AppUpdateProgressStage.Preparing);
            AppUpdateProgress downloadProgress = Assert.Single(
                updates,
                item => item.Stage == AppUpdateProgressStage.Downloading);
            Assert.Equal(installerBytes.Length, downloadProgress.BytesReceived);
            Assert.Equal(installerBytes.Length, downloadProgress.TotalBytes);
            Assert.Equal(100d, downloadProgress.Percentage.GetValueOrDefault());
            Assert.Contains(updates, item => item.Stage == AppUpdateProgressStage.Verifying);
            Assert.DoesNotContain(updates, item => item.Stage == AppUpdateProgressStage.StartingInstaller);
        }
        finally
        {
            string updateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElevateHelper",
                "updates",
                latestTag);
            if (Directory.Exists(updateDirectory))
            {
                Directory.Delete(updateDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildSilentInstallerArguments_LeavesRestartToInstallerRunEntry()
    {
        string actual = AppUpdateService.BuildSilentInstallerArguments();

        Assert.Contains("/CLOSEAPPLICATIONS", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/RESTARTAPPLICATIONS", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsSha256DigestMatch_VerifiesDownloadedAssetDigest()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"elevate-helper-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllText(filePath, "update");
            string digest = "sha256:" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();

            Assert.True(AppUpdateService.IsSha256DigestMatch(filePath, digest));
            Assert.False(AppUpdateService.IsSha256DigestMatch(filePath, "sha256:" + new string('0', 64)));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> handler;

        public InlineProgress(Action<T> handler)
        {
            this.handler = handler;
        }

        public void Report(T value)
        {
            handler(value);
        }
    }
}
