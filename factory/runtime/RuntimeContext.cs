namespace GameFactory.Runtime;

public sealed class RuntimeContext
{
    public RuntimeMode Mode { get; private set; }

    public bool IsServer => Mode is RuntimeMode.ListenServer or RuntimeMode.DedicatedServer;

    public bool IsClient => Mode is RuntimeMode.Client or RuntimeMode.ListenServer;

    public bool IsOffline => Mode == RuntimeMode.Offline;

    public RuntimeContext(RuntimeMode mode = RuntimeMode.Offline)
    {
        Mode = mode;
    }

    internal void SetMode(RuntimeMode mode)
    {
        Mode = mode;
    }

    internal void Reset()
    {
        Mode = RuntimeMode.Offline;
    }
}
