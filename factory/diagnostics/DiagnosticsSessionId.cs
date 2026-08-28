using System;

namespace GameFactory.Diagnostics;

public readonly record struct DiagnosticsSessionId(Guid Value)
{
    public static DiagnosticsSessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
