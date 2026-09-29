using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Echopad.App.Services;

static partial class Program
{
    static InstallerAsset TestInstallerAsset(byte[] payload)
    {
        const string tag = "v1.1.0-dev.20260929.99";
        const string name = "EchoPad-1.1.0-dev.20260929.99-Unsigned-Setup.exe";
        return new(tag, name, UpdateService.RepositoryUrl + "/releases/download/" + tag + "/" + name,
            Convert.ToHexString(SHA256.HashData(payload)), payload.Length);
    }

    static async Task InstallerUpdateChecks(string root)
    {
        byte[] payload = Enumerable.Range(0, 180000).Select(i => (byte)(i % 251)).ToArray();
        var asset = TestInstallerAsset(payload);
        string Feed(InstallerAsset data, string? digest = null, string state = "uploaded") => JsonSerializer.Serialize(new[]
        {
            new { tag_name = data.Tag, name = "Unsigned Dev Release", prerelease = true,
                html_url = UpdateService.RepositoryUrl + "/releases/tag/" + data.Tag,
                assets = new[] { new { name = data.FileName, state, browser_download_url = data.DownloadUrl,
                    digest = digest ?? "sha256:" + data.Sha256, size = data.Size } } }
        });
        using var feedClient = new HttpClient(new FeedHandler(Feed(asset)));
        var release = await new UpdateService(feedClient).CheckAsync("1.1.0-dev.20260929.2");
        Check(release.Available && release.Installer == asset, "updater selects the matching installer with GitHub size and SHA-256");
        foreach (var invalid in new[] {
            asset with { DownloadUrl = "https://example.com/installer.exe" },
            asset with { DownloadUrl = asset.DownloadUrl.Replace("github.com/", "github.com.evil.example/") },
            asset with { DownloadUrl = asset.DownloadUrl.Replace("v1.1.0", "v2.1.0") },
            asset with { FileName = "../installer.exe" },
            asset with { Sha256 = "invalid" }, asset with { Size = 0 }, asset with { Size = 600L * 1024 * 1024 }
        }) Check(!UpdateService.IsInstallerAsset(invalid), "unsupported installer metadata rejected: " +
            (invalid.DownloadUrl != asset.DownloadUrl ? invalid.DownloadUrl : invalid.FileName != asset.FileName ? invalid.FileName : invalid.Sha256 != asset.Sha256 ? "hash" : "size " + invalid.Size));
        using var missingHash = new HttpClient(new FeedHandler(Feed(asset, "")));
        var manual = await new UpdateService(missingHash).CheckAsync("1.1.0-dev.20260929.2");
        Check(manual.Available && manual.Installer == null && manual.ReleaseUrl != null, "missing installer verification retains the manual release-page fallback");
        using var pendingAsset = new HttpClient(new FeedHandler(Feed(asset, state: "starter")));
        Check((await new UpdateService(pendingAsset).CheckAsync("1.1.0-dev.20260929.2")).Installer == null, "unfinished asset upload cannot be offered for installation");

        var order = new List<string>();
        var reports = new List<InstallerDownloadProgress>();
        using var downloadClient = new HttpClient(new BinaryHandler(payload));
        var downloader = new InstallerUpdateService(downloadClient);
        string goodRoot = Path.Combine(root, "good");
        await downloader.DownloadAndInstallAsync(asset, goodRoot,
            _ => { order.Add("saved"); return Task.CompletedTask; },
            path => { Check(File.ReadAllBytes(path).SequenceEqual(payload) && !path.EndsWith(".download"), "only a complete hash-verified installer reaches the launcher"); order.Add("launched"); },
            () => order.Add("closed"), new InlineProgress<InstallerDownloadProgress>(reports.Add));
        Check(order.SequenceEqual(new[] { "saved", "launched", "closed" }), "update handoff saves, launches successfully, then closes the app");
        Check(reports.First().Percent == 0 && reports.Last().Percent == 100 && reports.Last().Received == payload.Length,
            "installer download reports byte counts and progress through completion");

        async Task FailsSafely(string scenario, HttpClient client, InstallerAsset data, Type expected,
            Action<string>? launch = null, Func<CancellationToken, Task>? prepare = null,
            CancellationToken token = default, IProgress<InstallerDownloadProgress>? progress = null)
        {
            bool launched = false, closed = false; Exception? failure = null;
            string directory = Path.Combine(root, scenario);
            try
            {
                await new InstallerUpdateService(client).DownloadAndInstallAsync(data, directory,
                    prepare ?? (_ => Task.CompletedTask),
                    path => { launch?.Invoke(path); launched = true; }, () => closed = true, progress, token);
            }
            catch (Exception ex) { failure = ex; }
            Check(failure != null && expected.IsInstanceOfType(failure) && !launched && !closed &&
                (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*.download", SearchOption.AllDirectories).Any()),
                scenario + " leaves EchoPad open and removes partial downloads");
        }
        await FailsSafely("hash-mismatch", downloadClient, asset with { Sha256 = new string('0', 64) }, typeof(InvalidDataException));
        using var truncated = new HttpClient(new BinaryHandler(payload[..1000], unknownLength: true));
        await FailsSafely("truncated-transfer", truncated, asset, typeof(InvalidDataException));
        using var oversize = new HttpClient(new BinaryHandler(payload, unknownLength: true));
        await FailsSafely("oversized-transfer", oversize, asset with { Size = 500 }, typeof(InvalidDataException));
        using var wrongSize = new HttpClient(new BinaryHandler(payload));
        await FailsSafely("content-length-mismatch", wrongSize, asset with { Size = 500 }, typeof(InvalidDataException));
        using var unavailable = new HttpClient(new BinaryHandler(payload, status: HttpStatusCode.NotFound));
        await FailsSafely("http-failure", unavailable, asset, typeof(HttpRequestException));
        using var redirected = new HttpClient(new BinaryHandler(payload, finalUrl: "https://example.com/installer.exe"));
        await FailsSafely("foreign-redirect", redirected, asset, typeof(InvalidDataException));
        using var cancellation = new CancellationTokenSource();
        await FailsSafely("canceled-download", downloadClient, asset, typeof(OperationCanceledException), token: cancellation.Token,
            progress: new InlineProgress<InstallerDownloadProgress>(p => { if (p.Received > 0) cancellation.Cancel(); }));
        await FailsSafely("canceled-windows-prompt", downloadClient, asset, typeof(Win32Exception),
            launch: _ => throw new Win32Exception(1223));
        await FailsSafely("installer-launch-failure", downloadClient, asset, typeof(Win32Exception),
            launch: _ => throw new Win32Exception(2));
        await FailsSafely("settings-save-failure", downloadClient, asset, typeof(IOException),
            prepare: _ => throw new IOException("settings write blocked"));
    }

    sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }

    sealed class BinaryHandler(byte[] bytes, bool unknownLength = false, HttpStatusCode status = HttpStatusCode.OK,
        string? finalUrl = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpContent content = unknownLength ? new StreamContent(new NonSeekableBytes(bytes)) : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(status)
            { Content = content, RequestMessage = finalUrl == null ? request : new HttpRequestMessage(HttpMethod.Get, finalUrl) });
        }
    }
    sealed class NonSeekableBytes(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }
}
