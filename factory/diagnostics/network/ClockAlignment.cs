using System;

namespace GameFactory.Diagnostics.Network;

/// <summary>Creates a host timeline from a UTC anchor and source monotonic elapsed time.</summary>
public static class ClockAlignment
{
    public static DateTimeOffset NormalizeToHostUtc(
        DateTimeOffset hostUtcAnchor,
        long sourceElapsedAnchorMilliseconds,
        long sourceElapsedMilliseconds)
        => hostUtcAnchor.AddMilliseconds(sourceElapsedMilliseconds - sourceElapsedAnchorMilliseconds);
}
