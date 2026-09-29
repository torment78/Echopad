using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Echopad.Core;

namespace Echopad.App.Services;

/// <summary>Copies owned legacy data once; never removes originals or replaces newer settings.</summary>
public static class LegacyDataMigration
{
    public const string MarkerName = "migration-elka-v1.json";
    public const string InstallHintName = "legacy-install-paths.txt";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    public static void MigrateDefault(string destination)
    {
        if (File.Exists(Path.Combine(destination, MarkerName))) return;
        // The installer retains the previous custom install location before updating its registry entry.
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echopad")
        };
        string hint = Path.Combine(AppContext.BaseDirectory, InstallHintName);
        if (File.Exists(hint)) roots.AddRange(File.ReadAllLines(hint).Where(Path.IsPathFullyQualified));
        // ZIP builds can also discover an older custom installation without an installer hint.
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            try
            {
                using var registry = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A2F2F07E-7A2F-4CE9-9D53-9E4F6B6F2F11}_is1");
                if (key?.GetValue("InstallLocation") is string path && Path.IsPathFullyQualified(path)) roots.Add(path);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            { /* Optional discovery; known folders and installer hints remain available. */ }
        }
        roots.Add(AppContext.BaseDirectory);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EchoPad"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EchoPad"));
        Migrate(destination, roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Echopad", "Captures"));
    }

    public static void Migrate(string destination, IEnumerable<string> legacyRoots, string legacyCaptures)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        // Prevent simultaneous first launches from importing/saving over each other.
        using var migrationLock = new FileStream(Path.Combine(destination, "migration.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string marker = Path.Combine(destination, MarkerName);
        if (File.Exists(marker)) return;

        var roots = legacyRoots.Select(Path.GetFullPath)
            .Where(p => !SamePath(p, destination)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var documents = new List<(string Source, string Destination, JsonObject Json)>();
        // Parse all selected documents before committing any of them. Broken data must not silently
        // become empty defaults, and an existing destination document always takes precedence.
        foreach (string name in new[] { "echopad.settings.json", "profiles.json" })
        {
            string target = Path.Combine(destination, name);
            if (File.Exists(target)) continue;
            string? source = roots.SelectMany(root => name == "echopad.settings.json"
                ? new[] { Path.Combine(root, name), Path.Combine(root, "settings.json") }
                : new[] { Path.Combine(root, name) }).FirstOrDefault(File.Exists);
            if (source == null) continue;
            string text = File.ReadAllText(source);
            try
            {
                var json = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
                { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                    ?? throw new JsonException("Expected a JSON object.");
                // Check the known schema while retaining unknown properties in the JSON tree.
                if (name == "profiles.json") JsonSerializer.Deserialize<ProfileStore>(text, JsonOptions);
                else JsonSerializer.Deserialize<GlobalSettings>(text, JsonOptions);
                documents.Add((source, target, json));
            }
            catch (JsonException ex) { throw new InvalidDataException($"Cannot read saved data: {source}", ex); }
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Copy only owned media directories from install folders, never the application binaries.
        var mediaRoots = roots.SelectMany(root => new[]
        {
            (Source: Path.Combine(root, "PadImages"), Target: Path.Combine(destination, "PadImages")),
            (Source: Path.Combine(root, "Captures"), Target: Path.Combine(destination, "Captures"))
        }).Append((Source: Path.GetFullPath(legacyCaptures), Target: Path.Combine(destination, "Captures"))).ToArray();
        foreach (var media in mediaRoots)
            if (!SamePath(media.Source, media.Target)) CopyTree(media.Source, media.Target, files);

        foreach (var doc in documents)
        {
            RewriteMediaPaths(doc.Json, Path.GetDirectoryName(doc.Source)!, roots, destination, files);
            WriteNew(doc.Destination, stream => JsonSerializer.Serialize(stream, doc.Json, JsonOptions));
        }
        WriteNew(marker, stream => JsonSerializer.Serialize(stream, new
        {
            CompletedUtc = DateTimeOffset.UtcNow, Sources = roots,
            ImportedDocuments = documents.Select(d => d.Source).ToArray(), MediaFiles = files.Count
        }, JsonOptions));
    }

    private static void RewriteMediaPaths(JsonNode node, string sourceDirectory, string[] roots,
        string destination, Dictionary<string, string> files)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                bool media = pair.Key.Equals("ClipPath", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Equals("StoppedImage", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Equals("PlayingImage", StringComparison.OrdinalIgnoreCase);
                if (media && pair.Value is JsonValue value && value.TryGetValue<string>(out string? path) &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    string full;
                    try { full = Path.GetFullPath(path, sourceDirectory); }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
                    if (files.TryGetValue(full, out string? copied)) obj[pair.Key] = copied;
                    else if (File.Exists(full) && roots.Any(r => IsWithin(full, r)))
                        // Very old builds may reference media directly beside the executable.
                        obj[pair.Key] = CopyMedia(full, Path.Combine(destination, "LegacyMedia", Path.GetFileName(full)), files);
                    else if (!Path.IsPathFullyQualified(path)) obj[pair.Key] = full;
                }
                else if (pair.Value != null) RewriteMediaPaths(pair.Value, sourceDirectory, roots, destination, files);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array) if (child != null) RewriteMediaPaths(child, sourceDirectory, roots, destination, files);
    }

    private static void CopyTree(string source, string destination, Dictionary<string, string> files)
    {
        if (!Directory.Exists(source)) return;
        if (IsWithin(destination, source)) throw new IOException($"Migration destination is inside its source: {source}");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"The legacy media folder is a link; copy its files into a normal folder before retrying: {source}");
        foreach (string file in Directory.EnumerateFiles(source)) CopyMedia(file, Path.Combine(destination, Path.GetFileName(file)), files);
        foreach (string directory in Directory.EnumerateDirectories(source)) CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)), files);
    }

    private static string CopyMedia(string source, string destination, Dictionary<string, string> files)
    {
        if (files.TryGetValue(source, out string? existing)) return existing;
        if (File.Exists(destination))
        {
            string hash = Hash(source);
            if (Hash(destination) != hash)
                destination = Path.Combine(Path.GetDirectoryName(destination)!,
                    Path.GetFileNameWithoutExtension(destination) + "-migrated-" + hash + Path.GetExtension(destination));
            if (File.Exists(destination) && Hash(destination) != hash)
                throw new IOException($"A migrated media file conflicts with an existing file: {destination}");
        }
        if (!File.Exists(destination)) WriteNew(destination, stream =>
        {
            using var input = File.OpenRead(source);
            input.CopyTo(stream);
        });
        files[source] = destination;
        return destination;
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static void WriteNew(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { write(file); file.Flush(true); }
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private static bool IsWithin(string path, string root) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
