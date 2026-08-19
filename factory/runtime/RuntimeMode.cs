namespace GameFactory.Runtime;

public enum RuntimeMode
{
    Offline,

    // One process is both the authoritative server
    // and a local player.
    ListenServer,

    // Connected to a remote authority.
    Client,

    // Authoritative server with no local player.
    DedicatedServer
}
