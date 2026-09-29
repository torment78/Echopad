using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Echopad.App.Services;

public sealed record InstallerDownloadProgress(long Received, long Total)
{
    public double Percent => Math.Clamp(100d * Received / Total, 0, 100);
}

public sealed class InstallerUpdateService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient _client;
    public InstallerUpdateService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<string> DownloadAsync(InstallerAsset asset, string cacheDirectory,
        IProgress<InstallerDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!UpdateService.IsInstallerAsset(asset)) throw new InvalidDataException("The release does not contain a supported EchoPad installer.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        // A fresh directory prevents an earlier partial download from being mistaken for an installer.
        string directory = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string final = Path.Combine(directory, asset.FileName);
        string partial = final + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("EchoPad-UpdateDownload/1.0");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Uri? downloadUri = response.RequestMessage?.RequestUri;
            if (downloadUri != null && (downloadUri.Scheme != "https" || !downloadUri.IsDefaultPort || downloadUri.UserInfo.Length != 0 ||
                !(downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                  downloadUri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                  downloadUri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("The installer download was redirected away from GitHub.");
            if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                throw new InvalidDataException("The installer download size does not match the release.");
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            progress?.Report(new(0, asset.Size));
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > asset.Size) throw new InvalidDataException("The installer download exceeds its expected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress?.Report(new(received, asset.Size));
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            if (received != asset.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Sha256)))
                throw new InvalidDataException("The installer could not be verified. Please download it again.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, final);
            return final;
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    public async Task DownloadAndInstallAsync(InstallerAsset asset, string cacheDirectory,
        Func<CancellationToken, Task> prepareToClose, Action<string> launchInstaller, Action closeApplication,
        IProgress<InstallerDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string installer = await DownloadAsync(asset, cacheDirectory, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await prepareToClose(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // ShellExecute returns after the Windows elevation prompt. Cancellation/failure must leave
        // EchoPad running; only a successful launch is allowed to close it.
        launchInstaller(installer);
        closeApplication();
    }

    public static void LaunchInstaller(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path)
        { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
        if (process == null) throw new IOException("Windows did not start the installer.");
    }
}
