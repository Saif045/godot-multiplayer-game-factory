namespace GameFactory.Networking.Transport;

public readonly record struct TransportResult(
    bool Success,
    string? Error = null)
{
    public static TransportResult Ok()
    {
        return new TransportResult(true);
    }

    public static TransportResult Fail(string error)
    {
        return new TransportResult(false, error);
    }
}
