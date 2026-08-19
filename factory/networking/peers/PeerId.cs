using System;

namespace GameFactory.Networking.Peers;

public readonly record struct PeerId
{
    public const long ServerValue = 1;

    public static PeerId Server => new(ServerValue);

    public long Value { get; }

    public bool IsServer => Value == ServerValue;

    public PeerId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Peer IDs must be positive.");
        }

        Value = value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
