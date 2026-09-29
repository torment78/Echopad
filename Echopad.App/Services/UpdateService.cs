using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echopad.App.Services;

public sealed record ReleaseVersion(Version Number, string Suffix) : IComparable<ReleaseVersion>
{
    public bool IsDevelopment => Suffix.Length > 0;
    public static ReleaseVersion? Parse(string? text)
    {
        // Also understand the original v1.0.0.0.latest tag.
        var match = Regex.Match(text ?? "", @"^[vV]?(\d+\.\d+\.\d+(?:\.\d+)?)(?:-([0-9A-Za-z.-]+)|\.latest)?(?:\+.*)?$");
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
        return new(new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)), match.Groups[2].Value);
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other == null) return 1;
        int result = Number.CompareTo(other.Number);
        if (result != 0) return result;
        if (!IsDevelopment || !other.IsDevelopment) return IsDevelopment == other.IsDevelopment ? 0 : IsDevelopment ? -1 : 1;
        var left = Suffix.Split('.'); var right = other.Suffix.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool ln = long.TryParse(left[i], out long l), rn = long.TryParse(right[i], out long r);
            result = ln && rn ? l.CompareTo(r) : ln != rn ? ln ? -1 : 1 : string.CompareOrdinal(left[i], right[i]);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }
}

public sealed record InstallerAsset(string Tag, string FileName, string DownloadUrl, string Sha256, long Size);
public sealed record UpdateResult(bool Available, string Message, string? ReleaseName = null, string? ReleaseUrl = null, InstallerAsset? Installer = null);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/torment78/Echopad";
    public const string FeedUrl = "https://api.github.com/repos/torment78/Echopad/releases?per_page=100";
    public static string InstalledVersion => typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _client;
    public UpdateService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<UpdateResult> CheckAsync(string installedVersion, CancellationToken cancellationToken = default)
    {
        var installed = ReleaseVersion.Parse(installedVersion);
        if (installed == null) return new(false, "This build has no comparable version number.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            request.Headers.UserAgent.ParseAdd("EchoPad-UpdateCheck/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new(false, "GitHub is limiting requests. Please try again later.");
            if (!response.IsSuccessStatusCode)
                return new(false, $"Could not read releases (HTTP {(int)response.StatusCode}). Please try again later.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return new(false, "The release feed returned an unexpected response.");
            ReleaseVersion? newest = null; string? name = null, url = null; InstallerAsset? installer = null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object || Flag(release, "draft")) continue;
                var version = ReleaseVersion.Parse(Text(release, "tag_name"));
                if (version == null || (!installed.IsDevelopment && (version.IsDevelopment || Flag(release, "prerelease")))) continue;
                string? releaseUrl = Text(release, "html_url");
                if (!IsReleaseUrl(releaseUrl) || (newest != null && version.CompareTo(newest) <= 0)) continue;
                newest = version; url = releaseUrl;
                name = $"{Text(release, "name") ?? "EchoPad"} ({Text(release, "tag_name")})";
                installer = FindInstaller(release);
            }
            if (newest == null) return new(false, "No versioned releases are available for this build yet.");
            return newest.CompareTo(installed) > 0
                ? new(true, installer != null ? "A newer EchoPad release is ready to download and install." :
                    "A newer release is available. No compatible verified installer is attached; open the release page for downloads.", name, url, installer)
                : new(false, "You have the newest available version for this release channel.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "The update check timed out. Please try again."); }
        catch (HttpRequestException) { return new(false, "Could not connect to GitHub. Check your connection and try again."); }
        catch (JsonException) { return new(false, "The release feed could not be read. Please try again later."); }
    }
    private static InstallerAsset? FindInstaller(JsonElement release)
    {
        string? tag = Text(release, "tag_name");
        if (tag == null || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || Text(asset, "state") != "uploaded" ||
                !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long bytes)) continue;
            string? name = Text(asset, "name"), url = Text(asset, "browser_download_url"), digest = Text(asset, "digest");
            if (name == null || url == null || digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) continue;
            var installer = new InstallerAsset(tag, name, url, digest[7..], bytes);
            if (IsInstallerAsset(installer)) return installer;
        }
        return null;
    }
    public static bool IsInstallerAsset(InstallerAsset asset)
    {
        if (!Regex.IsMatch(asset.Tag, @"\A[vV]?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\z") ||
            asset.Size <= 0 || asset.Size > 512L * 1024 * 1024 || !Regex.IsMatch(asset.Sha256, @"\A[0-9a-fA-F]{64}\z")) return false;
        string prefix = "EchoPad-" + asset.Tag.TrimStart('v', 'V');
        if (asset.FileName != prefix + "-Unsigned-Setup.exe" && asset.FileName != prefix + "-Setup.exe") return false;
        string expected = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(asset.Tag) + "/" + Uri.EscapeDataString(asset.FileName);
        return string.Equals(asset.DownloadUrl, expected, StringComparison.Ordinal);
    }
    private static bool Flag(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static bool IsReleaseUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/torment78/Echopad/releases/tag/", StringComparison.OrdinalIgnoreCase);
}
