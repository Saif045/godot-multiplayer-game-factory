using System;

namespace GameFactory.Networking.Objects;

public readonly record struct NetworkObjectId
{
    public long Value { get; }

    public NetworkObjectId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value.ToString();
}
