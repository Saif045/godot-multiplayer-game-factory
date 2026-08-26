using System;

namespace GameFactory.Steam.Models;

public sealed class SteamAdapterError : Exception
{
    public string Code { get; }

    public SteamAdapterError(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
