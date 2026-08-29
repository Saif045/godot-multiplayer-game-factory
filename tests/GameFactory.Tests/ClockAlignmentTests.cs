using GameFactory.Diagnostics.Network;

namespace GameFactory.Tests;

public sealed class ClockAlignmentTests
{
    [Fact]
    public void Normal_progression_uses_elapsed_time_from_host_anchor()
    {
        DateTimeOffset hostAnchor = DateTimeOffset.Parse("2026-08-28T10:05:04Z");

        Assert.Equal(
            hostAnchor.AddMilliseconds(250),
            ClockAlignment.NormalizeToHostUtc(hostAnchor, 1_000, 1_250));
    }

    [Fact]
    public void Pre_anchor_event_extrapolates_backwards_from_elapsed_anchor()
    {
        DateTimeOffset hostAnchor = DateTimeOffset.Parse("2026-08-28T10:05:04Z");

        Assert.Equal(
            hostAnchor.AddMilliseconds(-250),
            ClockAlignment.NormalizeToHostUtc(hostAnchor, 1_000, 750));
    }

    [Fact]
    public void Source_wall_clock_changes_do_not_affect_normalized_timeline()
    {
        DateTimeOffset hostAnchor = DateTimeOffset.Parse("2026-08-28T10:05:04Z");
        DateTimeOffset sourceClockBeforeAdjustment = DateTimeOffset.Parse("2026-08-28T10:16:05Z");
        DateTimeOffset sourceClockAfterAdjustment = sourceClockBeforeAdjustment.AddMinutes(10);

        DateTimeOffset normalizedBefore = ClockAlignment.NormalizeToHostUtc(hostAnchor, 5_000, 5_200);
        DateTimeOffset normalizedAfter = ClockAlignment.NormalizeToHostUtc(hostAnchor, 5_000, 5_200);

        Assert.NotEqual(sourceClockBeforeAdjustment, sourceClockAfterAdjustment);
        Assert.Equal(normalizedBefore, normalizedAfter);
    }
}
