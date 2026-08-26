using System;

namespace GameFactory.Steam.Models;

public readonly record struct SteamUserId
{
    public ulong Value { get; }

    public SteamUserId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Steam user IDs cannot be zero.");

        Value = value;
    }

    public override string ToString() => Value.ToString();
}
