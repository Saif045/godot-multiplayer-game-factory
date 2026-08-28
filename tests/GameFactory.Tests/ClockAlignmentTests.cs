using GameFactory.Diagnostics.Network;

namespace GameFactory.Tests;

public sealed class ClockAlignmentTests
{
    [Fact]
    public void Estimate_offset_normalizes_source_time_to_host_time()
    {
        DateTimeOffset hostUtc = DateTimeOffset.Parse("2026-08-28T10:05:04Z");
        DateTimeOffset sourceUtc = DateTimeOffset.Parse("2026-08-28T10:16:05Z");

        long offset = ClockAlignment.EstimateHostOffsetMilliseconds(hostUtc, sourceUtc);

        Assert.Equal(hostUtc, ClockAlignment.NormalizeToHostUtc(sourceUtc, offset));
    }

    [Fact]
    public void Zero_offset_preserves_source_time()
    {
        DateTimeOffset sourceUtc = DateTimeOffset.Parse("2026-08-28T10:05:04Z");

        Assert.Equal(sourceUtc, ClockAlignment.NormalizeToHostUtc(sourceUtc, 0));
    }
}
