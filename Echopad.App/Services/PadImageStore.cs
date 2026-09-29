using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Echopad.App.Services;

public static class PadImageStore
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Import(string source, string dataDirectory)
    {
        var info = new FileInfo(source);
        if (!info.Exists || info.Length > 25 * 1024 * 1024)
            throw new InvalidDataException("Choose a PNG smaller than 25 MB.");
        byte[] bytes = File.ReadAllBytes(source);
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException("Please choose a valid PNG image.");
        // Check the PNG header before allocating its decoded bitmap.
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width == 0 || height == 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("PNG dimensions must be between 1 and 8192 pixels.");
        _ = Decode(bytes);
        string directory = Path.Combine(dataDirectory, "PadImages");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)) + ".png");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }

    public static BitmapSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var image)) return image;
            try
            {
                image = Decode(File.ReadAllBytes(path));
                // Bound retained thumbnails when switching through large profile libraries.
                if (Cache.Count >= 128) Cache.Clear();
                Cache[path] = image;
                return image;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
            { return null; }
        }
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
            throw new InvalidDataException("The image is not a PNG.");
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width == 0 || height == 0 || width > 8192 || height > 8192)
            throw new InvalidDataException("Unsupported PNG dimensions.");
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (width >= height) bitmap.DecodePixelWidth = (int)Math.Min(width, 512);
        else bitmap.DecodePixelHeight = (int)Math.Min(height, 512);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
