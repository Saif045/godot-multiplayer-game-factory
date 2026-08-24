using System;

namespace GameFactory.Networking.Players;

public readonly record struct PlayerId
{
    public long Value { get; }

    public PlayerId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Player IDs must be positive.");
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
