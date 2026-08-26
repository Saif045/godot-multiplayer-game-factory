using System;

namespace GameFactory.Steam.Models;

public sealed record SteamClientOptions(int VirtualPort = 0, TimeSpan? ConnectionTimeout = null)
{
    public TimeSpan EffectiveConnectionTimeout => ConnectionTimeout ?? TimeSpan.FromSeconds(20);
}
