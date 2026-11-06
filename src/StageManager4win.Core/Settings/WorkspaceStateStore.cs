namespace StageManager4win.Core.Settings;

public sealed class WorkspaceStateStore
{
    public WorkspaceStateStore(string directory) => FilePath = Path.Combine(directory, "workspace.json");

    public string FilePath { get; }

    public WorkspaceState Load() =>
        AtomicJsonFile.Load(FilePath, () => new WorkspaceState(Array.Empty<MonitorWorkspace>()));

    public void Save(WorkspaceState state) => AtomicJsonFile.Save(FilePath, state);
}
