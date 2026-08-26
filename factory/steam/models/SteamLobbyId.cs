using System;

namespace GameFactory.Steam.Models;

public readonly record struct SteamLobbyId
{
    public ulong Value { get; }

    public SteamLobbyId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Steam lobby IDs cannot be zero.");

        Value = value;
    }

    public override string ToString() => Value.ToString();
}
