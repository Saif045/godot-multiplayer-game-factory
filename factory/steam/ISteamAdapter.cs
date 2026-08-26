using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Steam.Models;

namespace GameFactory.Steam;

/// <summary>
/// Steam-specific boundary. Implementations may change, but GameFactory does not
/// generalize this into a cross-platform online abstraction.
/// </summary>
public interface ISteamAdapter : IDisposable
{
    bool IsInitialized { get; }
    SteamUser LocalUser { get; }
    bool IsOverlayAvailable { get; }
    SteamLobby? CurrentLobby { get; }
    bool SupportsDedicatedServers { get; }

    event Action<SteamLobby>? LobbyCreated;
    event Action<SteamLobby>? LobbyJoined;
    event Action<SteamLobbyId>? LobbyLeft;
    event Action<SteamLobby>? LobbyUpdated;
    event Action<SteamLobbyId, SteamUser>? LobbyMemberJoined;
    event Action<SteamLobbyId, SteamUserId>? LobbyMemberLeft;
    event Action<SteamLobbyId, SteamUserId>? LobbyOwnerChanged;
    event Action<SteamLobbyId, SteamUserId>? LobbyJoinRequested;
    event Action<bool>? OverlayActivityChanged;
    event Action<SteamAdapterError>? Error;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync();
    IReadOnlyList<SteamFriend> GetFriends();
    SteamPresence GetPresence(SteamUserId userId);
    bool IsFriend(SteamUserId userId);
    Task<IReadOnlyList<SteamLobbyInfo>> FindLobbiesAsync(SteamLobbySearchOptions options, CancellationToken cancellationToken = default);
    Task<SteamLobbyInfo> GetLobbyInfoAsync(SteamLobbyId lobbyId, CancellationToken cancellationToken = default);
    Task<SteamLobby> CreateLobbyAsync(SteamLobbyCreateOptions options, CancellationToken cancellationToken = default);
    Task<SteamLobby> JoinLobbyAsync(SteamLobbyId lobbyId, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync();
    void SetLobbyJoinable(bool joinable);
    void SetLobbyMemberLimit(int memberLimit);
    void SetLobbyData(string key, string value);
    void SetLobbyMemberData(string key, string value);
    IReadOnlyList<SteamLobbyMember> GetLobbyMembers();
    SteamUserId GetLobbyOwner();
    bool IsLobbyOwner { get; }
    void OpenInviteOverlay();
    void OpenFriendsOverlay();
    void OpenUserOverlay(SteamUserId userId);
    bool CanJoinUser(SteamUserId userId);
    void SetRichPresence(string key, string value);
    void ClearRichPresence();
    bool TryGetSteamUserForPeer(PeerId peerId, out SteamUserId userId);
    bool TryGetPeerForSteamUser(SteamUserId userId, out PeerId peerId);
    Task<MultiplayerPeer> CreateListenServerPeerAsync(SteamListenServerOptions options, CancellationToken cancellationToken = default);
    Task<MultiplayerPeer> CreateLobbyClientPeerAsync(SteamLobbyId lobbyId, SteamClientOptions options, CancellationToken cancellationToken = default);
    Task<SteamDedicatedServer> StartDedicatedServerAsync(SteamDedicatedServerOptions options, CancellationToken cancellationToken = default);
    Task StopDedicatedServerAsync();
    Task<MultiplayerPeer> CreateDedicatedServerPeerAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SteamServerInfo>> FindDedicatedServersAsync(SteamServerSearchOptions options, CancellationToken cancellationToken = default);
    Task<SteamAuthTicket> CreateAuthTicketAsync(CancellationToken cancellationToken = default);
}
