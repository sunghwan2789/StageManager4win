using System.Text.Json;
using System.Text.Json.Serialization;

namespace StageManager4win.Core.Settings;

internal static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path))
        {
            return fallback();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (JsonException)
        {
            Backup(path);
            return fallback();
        }
        catch (IOException)
        {
            return fallback();
        }
        catch (UnauthorizedAccessException)
        {
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));

        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void Backup(string path)
    {
        try
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
