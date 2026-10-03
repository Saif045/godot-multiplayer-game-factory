using System;

namespace GameFactory.Steam;

public static class SteamInviteFeedback
{
    public const string OverlayUnavailable = "Steam Overlay is unavailable. Your friend can still join from Join Game.";

    /// <summary>Query capability at click time; overlay attachment can lag initialization.</summary>
    public static string? Request(bool overlayAvailable, Action openInvite)
    {
        if (!overlayAvailable) return OverlayUnavailable;
        openInvite();
        return null;
    }
}
