using System;

namespace GameFactory.Steam;

/// <summary>Launch intent is independent of debug/release and overlay capability.</summary>
public sealed record SteamLaunchOptions
{
    public const uint DevelopmentAppId = 480;
    public static SteamLaunchOptions Development { get; } = new(DevelopmentAppId, true);

    private SteamLaunchOptions(uint appId, bool developmentMode)
    {
        AppId = appId;
        DevelopmentMode = developmentMode;
    }

    public uint AppId { get; }
    public bool DevelopmentMode { get; }
    public bool EnforceSteamLaunch => !DevelopmentMode;
    public string Policy => DevelopmentMode ? "development_direct_allowed" : "production_steam_required";

    public static SteamLaunchOptions Production(uint appId)
    {
        if (appId is 0 or DevelopmentAppId)
            throw new ArgumentOutOfRangeException(nameof(appId), "Production requires the real GameFactory Steam App ID.");
        return new(appId, false);
    }

    /// <summary>
    /// Pure policy seam for an early native bootstrap. Do not call the vendor
    /// restart binding from an autoload: its singleton already registered callbacks.
    /// </summary>
    public SteamLaunchDecision DecideLaunch(Func<uint, bool> restartAppIfNecessary)
        => EnforceSteamLaunch && restartAppIfNecessary(AppId)
            ? SteamLaunchDecision.RelaunchRequested
            : SteamLaunchDecision.Continue;
}

public enum SteamLaunchDecision { Continue, RelaunchRequested }
