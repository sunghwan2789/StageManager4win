using System.Text;

namespace StageManager4win.Core.Diagnostics;

public sealed class FileLogger
{
    public static FileLogger Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StageManager4win",
        "logs",
        "app.log"));

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;

    public FileLogger(string path, long maxBytes = 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public void Write(string category, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {category} {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new FileInfo(_path);
                if (file.Exists && file.Length + Encoding.UTF8.GetByteCount(line) > _maxBytes)
                {
                    var backup = _path + ".1";
                    File.Delete(backup);
                    File.Move(_path, backup);
                }

                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Logging is best effort; a failed write must not stop the app.
            }
        }
    }
}
