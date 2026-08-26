namespace GameFactory.Steam.Models;

public sealed record SteamAuthTicket(SteamUserId UserId, byte[] Bytes);
