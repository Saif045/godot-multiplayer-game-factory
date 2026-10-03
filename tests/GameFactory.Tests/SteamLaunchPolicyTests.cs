using GameFactory.Steam;

namespace GameFactory.Tests;

public sealed class SteamLaunchPolicyTests
{
    [Fact]
    public void Development_never_calls_restart()
    {
        SteamLaunchOptions options = SteamLaunchOptions.Development;
        Assert.Equal(480u, options.AppId);
        Assert.False(options.EnforceSteamLaunch);
        Assert.Equal(SteamLaunchDecision.Continue, options.DecideLaunch(_ => throw new Exception("Must never launch Spacewar")));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(480u)]
    public void Production_requires_a_non_development_app_id(uint appId)
        => Assert.Throws<ArgumentOutOfRangeException>(() => SteamLaunchOptions.Production(appId));

    [Theory]
    [InlineData(true, SteamLaunchDecision.RelaunchRequested)]
    [InlineData(false, SteamLaunchDecision.Continue)]
    public void Production_reports_the_early_restart_decision(bool relaunch, SteamLaunchDecision expected)
    {
        // Test-only sentinel, never configured in GameFactory or sent to Steam.
        SteamLaunchOptions options = SteamLaunchOptions.Production(123u);
        uint? requested = null;
        Assert.Equal(expected, options.DecideLaunch(appId => { requested = appId; return relaunch; }));
        Assert.Equal(options.AppId, requested);
        Assert.True(options.EnforceSteamLaunch);
    }

    [Fact]
    public void Missing_overlay_returns_feedback_without_invoking_overlay()
    {
        Assert.Equal(SteamInviteFeedback.OverlayUnavailable,
            SteamInviteFeedback.Request(false, () => throw new Exception("Overlay must not open")));
    }

    [Fact]
    public void Available_overlay_opens_invite_and_clears_feedback()
    {
        int calls = 0;
        Assert.Null(SteamInviteFeedback.Request(true, () => calls++));
        Assert.Equal(1, calls);
    }
}
