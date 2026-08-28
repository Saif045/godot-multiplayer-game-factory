using System;

namespace GameFactory.Diagnostics.Network;

/// <summary>Provides a deliberately lightweight host-clock offset estimate for debug timelines.</summary>
public static class ClockAlignment
{
    public static long EstimateHostOffsetMilliseconds(DateTimeOffset hostUtc, DateTimeOffset localUtc)
        => hostUtc.ToUnixTimeMilliseconds() - localUtc.ToUnixTimeMilliseconds();

    public static DateTimeOffset NormalizeToHostUtc(DateTimeOffset sourceUtc, long hostOffsetMilliseconds)
        => sourceUtc.AddMilliseconds(hostOffsetMilliseconds);
}
