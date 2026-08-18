namespace GameFactory.Networking.Sessions;

public enum SessionState
{
    Offline,

    // Server transport is being initialized.
    Starting,

    // Client transport exists, but connection
    // to the server is not confirmed yet.
    Connecting,

    // Session is operational.
    Running,

    // Intentional local shutdown is underway.
    Stopping,



    // Session could not continue.

    Failed
}
