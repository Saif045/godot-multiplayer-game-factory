namespace GameFactory.Networking.Sessions;

public readonly record struct SessionResult(
    bool Success,
    string? Error = null)
{
  public static SessionResult Ok()
  {
    return new SessionResult(true);
  }

  public static SessionResult Fail(string error)
  {
    return new SessionResult(
        false,
        error);
  }
}