namespace GameFactory.Runtime;

public sealed class RuntimeContext
{
    public RuntimeMode Mode { get; private set; } = RuntimeMode.Offline;

    public bool IsServer => Mode is RuntimeMode.ListenServer or RuntimeMode.DedicatedServer;

    public bool IsClient => Mode is RuntimeMode.Client or RuntimeMode.ListenServer;

    public bool IsOffline => Mode == RuntimeMode.Offline;

    internal void SetMode(RuntimeMode mode)
    {
        Mode = mode;
    }

    internal void Reset()
    {
        Mode = RuntimeMode.Offline;
    }
}
