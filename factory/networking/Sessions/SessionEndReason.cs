namespace GameFactory.Networking.Sessions;

public enum SessionEndReason
{
  None,

  LocalLeave,

  HostShutdown,

  HostStartFailed,

  ConnectionFailed,

  ServerDisconnected
}