namespace GameFactory.Steam;

public enum SteamSessionState
{
    Offline,
    Initializing,
    Ready,
    CreatingLobby,
    Hosting,
    JoiningLobby,
    Connected,
    Leaving,
    Failed
}
