namespace StageManager4win.Core.Settings;

public sealed class SettingsStore
{
    public SettingsStore(string directory) => FilePath = Path.Combine(directory, "settings.json");

    public string FilePath { get; }

    public AppSettings Load() => AtomicJsonFile.Load(FilePath, () => new AppSettings()).Normalize();

    public void Save(AppSettings settings) => AtomicJsonFile.Save(FilePath, settings.Normalize());
}
