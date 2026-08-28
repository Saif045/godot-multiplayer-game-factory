using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;
using GameFactory.Steam.Models;

namespace GameFactory.Steam.Adapters.GodotSteam;

/// <summary>Typed C# facade over the vendor-specific GDScript bridge.</summary>
public sealed class GodotSteamAdapter : ISteamAdapter
{
    public const int DevelopmentAppId = 480;

    private readonly Node _bridge;
    private readonly Dictionary<string, string> _lobbyMetadata = [];
    private TaskCompletionSource<SteamLobby>? _pendingLobby;
    private CancellationTokenRegistration _pendingLobbyCancellation;
    private TaskCompletionSource<IReadOnlyList<SteamLobbyInfo>>? _pendingSearch;
    private MultiplayerPeer? _activePeer;
    private bool _disposed;

    public bool IsInitialized { get; private set; }
    public SteamUser LocalUser { get; private set; } = null!;
    public bool IsOverlayAvailable => IsInitialized && _bridge.Call("is_overlay_enabled").AsBool();
    public SteamLobby? CurrentLobby { get; private set; }
    public bool SupportsDedicatedServers => false;
    public bool IsLobbyOwner => CurrentLobby is { } lobby && lobby.OwnerId == LocalUser.Id;

    public event Action<SteamLobby>? LobbyCreated;
    public event Action<SteamLobby>? LobbyJoined;
    public event Action<SteamLobbyId>? LobbyLeft;
    public event Action<SteamLobby>? LobbyUpdated;
    public event Action<SteamLobbyId, SteamUser>? LobbyMemberJoined;
    public event Action<SteamLobbyId, SteamUserId>? LobbyMemberLeft;
    public event Action<SteamLobbyId, SteamUserId>? LobbyOwnerChanged;
    public event Action<SteamLobbyId, SteamUserId>? LobbyJoinRequested;
    public event Action<bool>? OverlayActivityChanged;
    public event Action<SteamAdapterError>? Error;

    public GodotSteamAdapter(Node bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        ConnectBridgeSignals();
    }

    public static GodotSteamAdapter Create(Node owner)
    {
        PackedScene scene = GD.Load<PackedScene>(
            "res://factory/steam/adapters/godot_steam/godot_steam_adapter.tscn");
        Node bridge = scene.Instantiate<Node>();
        owner.AddChild(bridge);
        return new GodotSteamAdapter(bridge);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (IsInitialized) return Task.CompletedTask;

        Godot.Collections.Dictionary result = _bridge.Call("initialize", DevelopmentAppId).AsGodotDictionary();
        bool success = result.TryGetValue("status", out Variant status) && status.AsInt64() == 0;
        if (!success)
        {
            string message = result.TryGetValue("verbal", out Variant verbal)
                ? verbal.AsString()
                : "Steam initialization failed.";
            throw Report("steam_initialize_failed", message);
        }

        Godot.Collections.Dictionary local = _bridge.Call("local_user").AsGodotDictionary();
        LocalUser = ToUser(local);
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        if (!IsInitialized) return Task.CompletedTask;
        _activePeer = null;
        CurrentLobby = null;
        _bridge.Call("shutdown");
        IsInitialized = false;
        return Task.CompletedTask;
    }

    public IReadOnlyList<SteamFriend> GetFriends()
    {
        EnsureInitialized();
        Godot.Collections.Array raw = _bridge.Call("get_friends").AsGodotArray();
        return raw.Select(value =>
        {
            SteamUser user = ToUser(value.AsGodotDictionary());
            return new SteamFriend(user, GetPresence(user.Id));
        }).ToArray();
    }

    public SteamPresence GetPresence(SteamUserId userId)
    {
        EnsureInitialized();
        Godot.Collections.Dictionary raw = _bridge.Call("get_presence", ToSteamInt(userId)).AsGodotDictionary();
        string state = raw["state"].AsString();
        string connect = raw["connect"].AsString();
        return new SteamPresence(state, string.IsNullOrEmpty(connect) ? null : connect);
    }

    public bool IsFriend(SteamUserId userId) => _bridge.Call("is_friend", ToSteamInt(userId)).AsBool();

    public Task<IReadOnlyList<SteamLobbyInfo>> FindLobbiesAsync(SteamLobbySearchOptions options, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (_pendingSearch is not null)
            throw new InvalidOperationException("A Steam lobby search is already in progress.");

        _pendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => _pendingSearch.TrySetCanceled(cancellationToken));
        Godot.Collections.Dictionary metadata = new();
        if (options.Metadata is not null)
            foreach ((string key, string value) in options.Metadata) metadata[key] = value;
        _bridge.Call("find_lobbies", metadata, options.MaxResults);
        return _pendingSearch.Task;
    }

    public Task<SteamLobbyInfo> GetLobbyInfoAsync(SteamLobbyId lobbyId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToLobbyInfo(lobbyId));
    }

    public async Task<SteamLobby> CreateLobbyAsync(SteamLobbyCreateOptions options, CancellationToken cancellationToken = default)
    {
        EnsureReadyForLobbyOperation();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLobbyOptions(options);
        _lobbyMetadata.Clear();
        if (options.Metadata is not null)
            foreach ((string key, string value) in options.Metadata) _lobbyMetadata[key] = value;
        _lobbyMetadata["joinable"] = options.IsJoinable ? "true" : "false";

        TaskCompletionSource<SteamLobby> pending = NewLobbyCompletion(cancellationToken);
        _bridge.Call("create_lobby", ToGodotLobbyType(options.Visibility), options.MaxMembers);
        SteamLobby lobby = await pending.Task;
        foreach ((string key, string value) in _lobbyMetadata) SetLobbyData(key, value);
        CurrentLobby = lobby with { IsJoinable = options.IsJoinable, MemberLimit = options.MaxMembers };
        return CurrentLobby;
    }

    public async Task<SteamLobby> JoinLobbyAsync(SteamLobbyId lobbyId, CancellationToken cancellationToken = default)
    {
        EnsureReadyForLobbyOperation();
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<SteamLobby> pending = NewLobbyCompletion(cancellationToken);
        _bridge.Call("join_lobby", ToSteamInt(lobbyId));
        return await pending.Task;
    }

    public Task LeaveLobbyAsync()
    {
        if (CurrentLobby is not { } lobby) return Task.CompletedTask;
        _bridge.Call("leave_lobby", ToSteamInt(lobby.Id));
        _activePeer = null;
        CurrentLobby = null;
        LobbyLeft?.Invoke(lobby.Id);
        return Task.CompletedTask;
    }

    public void SetLobbyJoinable(bool joinable)
    {
        SteamLobby lobby = RequireLobby();
        _bridge.Call("set_lobby_joinable", ToSteamInt(lobby.Id), joinable);
        _lobbyMetadata["joinable"] = joinable ? "true" : "false";
        CurrentLobby = lobby with { IsJoinable = joinable };
    }

    public void SetLobbyMemberLimit(int memberLimit)
    {
        if (memberLimit is < 2 or > 250) throw new ArgumentOutOfRangeException(nameof(memberLimit));
        SteamLobby lobby = RequireLobby();
        _bridge.Call("set_lobby_member_limit", ToSteamInt(lobby.Id), memberLimit);
        CurrentLobby = lobby with { MemberLimit = memberLimit };
    }

    public void SetLobbyData(string key, string value)
    {
        SteamLobby lobby = RequireLobby();
        ValidateMetadata(key, value);
        _bridge.Call("set_lobby_data", ToSteamInt(lobby.Id), key, value);
        _lobbyMetadata[key] = value;
    }

    public void SetLobbyMemberData(string key, string value)
    {
        SteamLobby lobby = RequireLobby();
        ValidateMetadata(key, value);
        _bridge.Call("set_lobby_member_data", ToSteamInt(lobby.Id), key, value);
    }

    public IReadOnlyList<SteamLobbyMember> GetLobbyMembers() => RequireLobby().Members;
    public SteamUserId GetLobbyOwner() => RequireLobby().OwnerId;
    public void OpenInviteOverlay()
    {
        SteamLobby lobby = RequireLobby();
        GameLog.Info("steam.adapter", "open_invite_overlay", $"lobby={lobby.Id}; enabled={IsOverlayAvailable}");
        _bridge.Call("activate_invite_overlay", ToSteamInt(lobby.Id));
    }
    public void OpenFriendsOverlay()
    {
        EnsureInitialized();
        GameLog.Info("steam.adapter", "open_friends_overlay", $"enabled={IsOverlayAvailable}");
        _bridge.Call("activate_friends_overlay");
    }
    public void OpenUserOverlay(SteamUserId userId) => _bridge.Call("activate_user_overlay", ToSteamInt(userId));
    public bool CanJoinUser(SteamUserId userId) => IsFriend(userId) && !string.IsNullOrEmpty(GetPresence(userId).ConnectString);
    public void SetRichPresence(string key, string value) { ValidateMetadata(key, value); _bridge.Call("set_rich_presence", key, value); }
    public void ClearRichPresence() => _bridge.Call("clear_rich_presence");

    public bool TryGetSteamUserForPeer(PeerId peerId, out SteamUserId userId)
    {
        userId = default;
        if (_activePeer is null) return false;
        long value = _bridge.Call("get_steam_id_for_peer", _activePeer, peerId.Value).AsInt64();
        if (value <= 0) return false;
        userId = new SteamUserId((ulong)value);
        return true;
    }

    public bool TryGetPeerForSteamUser(SteamUserId userId, out PeerId peerId)
    {
        peerId = default;
        if (_activePeer is null) return false;
        long value = _bridge.Call("get_peer_id_for_steam", _activePeer, ToSteamInt(userId)).AsInt64();
        if (value <= 0) return false;
        peerId = new PeerId(value);
        return true;
    }

    public Task<MultiplayerPeer> CreateListenServerPeerAsync(SteamListenServerOptions options, CancellationToken cancellationToken = default)
    {
        SteamLobby lobby = RequireLobby();
        if (!IsLobbyOwner) throw Report("not_lobby_owner", "Only the lobby owner can host the Steam listen server.");
        if (options.VirtualPort != 0) throw new NotSupportedException("GodotSteam's lobby helper currently creates its listen peer on virtual port 0.");
        _activePeer = ToPeer(_bridge.Call("create_host_peer", ToSteamInt(lobby.Id), options.VirtualPort));
        return Task.FromResult(_activePeer);
    }

    public Task<MultiplayerPeer> CreateLobbyClientPeerAsync(SteamLobbyId lobbyId, SteamClientOptions options, CancellationToken cancellationToken = default)
    {
        if (CurrentLobby?.Id != lobbyId) throw Report("not_in_lobby", "Join the lobby before creating its Steam peer.");
        if (options.VirtualPort != 0) throw new NotSupportedException("GodotSteam's lobby helper currently connects on virtual port 0.");
        _activePeer = ToPeer(_bridge.Call("create_client_peer", ToSteamInt(lobbyId), options.VirtualPort));
        return Task.FromResult(_activePeer);
    }

    public Task<SteamDedicatedServer> StartDedicatedServerAsync(SteamDedicatedServerOptions options, CancellationToken cancellationToken = default) => throw UnsupportedDedicatedServers();
    public Task StopDedicatedServerAsync() => throw UnsupportedDedicatedServers();
    public Task<MultiplayerPeer> CreateDedicatedServerPeerAsync(CancellationToken cancellationToken = default) => throw UnsupportedDedicatedServers();
    public Task<IReadOnlyList<SteamServerInfo>> FindDedicatedServersAsync(SteamServerSearchOptions options, CancellationToken cancellationToken = default) => throw UnsupportedDedicatedServers();
    public Task<SteamAuthTicket> CreateAuthTicketAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Steam authentication tickets are a declared seam, not implemented in the first adapter.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPendingLobby();
        _pendingSearch?.TrySetCanceled();
        if (IsInitialized) _ = ShutdownAsync();
        if (GodotObject.IsInstanceValid(_bridge)) _bridge.QueueFree();
    }

    private void ConnectBridgeSignals()
    {
        _bridge.Connect("lobby_created_result", Callable.From<long, long>(OnLobbyCreated));
        _bridge.Connect("lobby_joined_result", Callable.From<long, long>(OnLobbyJoined));
        _bridge.Connect("lobby_data_changed", Callable.From<long>(OnLobbyDataChanged));
        _bridge.Connect("lobby_member_changed", Callable.From<long, long, long, long>(OnLobbyMemberChanged));
        _bridge.Connect("lobby_invited", Callable.From<long, long>(OnLobbyInvited));
        _bridge.Connect("overlay_changed", Callable.From<bool>(active => OverlayActivityChanged?.Invoke(active)));
        _bridge.Connect("lobby_search_completed", Callable.From<Godot.Collections.Array>(OnLobbySearchCompleted));
    }

    private void OnLobbyCreated(long result, long rawLobbyId)
    {
        if (result != 1)
        {
            FailPendingLobby(Report("lobby_create_failed", $"Steam lobby creation failed with result {result}."));
            return;
        }
        CompleteLobby(new SteamLobbyId((ulong)rawLobbyId), created: true);
    }
    private void OnLobbyJoined(long rawLobbyId, long response)
    {
        if (response != 1)
        {
            FailPendingLobby(Report("lobby_join_failed", $"Steam lobby join failed with response {response}."));
            return;
        }
        CompleteLobby(new SteamLobbyId((ulong)rawLobbyId), created: false);
    }
    private void CompleteLobby(SteamLobbyId id, bool created)
    {
        TaskCompletionSource<SteamLobby>? pending = TakePendingLobby();
        if (pending is null) return;
        SteamLobby lobby = ToLobby(id);
        CurrentLobby = lobby;
        pending.TrySetResult(lobby);
        if (created) LobbyCreated?.Invoke(lobby); else LobbyJoined?.Invoke(lobby);
    }
    private void OnLobbyDataChanged(long rawLobbyId)
    {
        if (CurrentLobby?.Id.Value != (ulong)rawLobbyId) return;
        SteamUserId previousOwner = CurrentLobby.OwnerId;
        CurrentLobby = ToLobby(CurrentLobby.Id);
        LobbyUpdated?.Invoke(CurrentLobby);
        if (CurrentLobby.OwnerId != previousOwner)
            LobbyOwnerChanged?.Invoke(CurrentLobby.Id, CurrentLobby.OwnerId);
    }
    private void OnLobbyMemberChanged(long rawLobbyId, long changedId, long makingChangeId, long chatState)
    {
        if (CurrentLobby?.Id.Value != (ulong)rawLobbyId || changedId <= 0) return;
        SteamLobbyId lobbyId = CurrentLobby.Id;
        SteamUserId userId = new((ulong)changedId);
        if (chatState == 1) LobbyMemberJoined?.Invoke(lobbyId, new SteamUser(userId, string.Empty));
        else LobbyMemberLeft?.Invoke(lobbyId, userId);
    }
    private void OnLobbyInvited(long inviterId, long lobbyId)
    {
        if (inviterId > 0 && lobbyId > 0) LobbyJoinRequested?.Invoke(new SteamLobbyId((ulong)lobbyId), new SteamUserId((ulong)inviterId));
    }
    private void OnLobbySearchCompleted(Godot.Collections.Array rawLobbyIds)
    {
        try
        {
            IReadOnlyList<SteamLobbyInfo> lobbies = rawLobbyIds
                .Select(value => new SteamLobbyId((ulong)value.AsInt64()))
                .Where(id => id.Value != 0)
                .Select(ToLobbyInfo)
                .ToArray();
            _pendingSearch?.TrySetResult(lobbies);
        }
        catch (Exception exception)
        {
            _pendingSearch?.TrySetException(Report("lobby_search_failed", exception.Message));
        }
        finally { _pendingSearch = null; }
    }

    private SteamLobby ToLobby(SteamLobbyId id)
    {
        SteamLobbyInfo info = ToLobbyInfo(id);
        return new SteamLobby(id, info.OwnerId, SteamLobbyVisibility.FriendsOnly, info.Metadata.TryGetValue("joinable", out string? joinable) ? joinable != "false" : true, info.MemberLimit, info.Metadata, GetMembers(id));
    }
    private SteamLobbyInfo ToLobbyInfo(SteamLobbyId id)
    {
        Godot.Collections.Dictionary raw = _bridge.Call("get_lobby_summary", ToSteamInt(id)).AsGodotDictionary();
        SteamUserId owner = new((ulong)raw["owner_id"].AsInt64());
        int memberCount = (int)raw["member_count"].AsInt64();
        int memberLimit = (int)raw["member_limit"].AsInt64();
        return new SteamLobbyInfo(id, owner, memberCount, memberLimit, new Dictionary<string, string>(_lobbyMetadata));
    }
    private IReadOnlyList<SteamLobbyMember> GetMembers(SteamLobbyId id)
    {
        Godot.Collections.Array raw = _bridge.Call("get_lobby_members", ToSteamInt(id)).AsGodotArray();
        return raw.Select(value => new SteamLobbyMember(ToUser(value.AsGodotDictionary()), new Dictionary<string, string>())).ToArray();
    }
    private TaskCompletionSource<SteamLobby> NewLobbyCompletion(CancellationToken cancellationToken)
    {
        if (_pendingLobby is not null) throw new InvalidOperationException("A Steam lobby operation is already in progress.");
        TaskCompletionSource<SteamLobby> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLobby = completion;
        _pendingLobbyCancellation = cancellationToken.Register(() => CancelPendingLobby(completion, cancellationToken));
        return completion;
    }
    private void FailPendingLobby(Exception exception)
    {
        TaskCompletionSource<SteamLobby>? pending = TakePendingLobby();
        pending?.TrySetException(exception);
    }
    private void CancelPendingLobby() => CancelPendingLobby(_pendingLobby, default);
    private void CancelPendingLobby(TaskCompletionSource<SteamLobby>? expected, CancellationToken cancellationToken)
    {
        if (expected is null || !ReferenceEquals(_pendingLobby, expected)) return;
        TaskCompletionSource<SteamLobby>? pending = TakePendingLobby(disposeRegistration: false);
        pending?.TrySetCanceled(cancellationToken);
    }
    private TaskCompletionSource<SteamLobby>? TakePendingLobby(bool disposeRegistration = true)
    {
        TaskCompletionSource<SteamLobby>? pending = _pendingLobby;
        _pendingLobby = null;
        if (disposeRegistration) _pendingLobbyCancellation.Dispose();
        _pendingLobbyCancellation = default;
        return pending;
    }
    private SteamUser ToUser(Godot.Collections.Dictionary raw) => new(new SteamUserId((ulong)raw["id"].AsInt64()), raw["name"].AsString());
    private static MultiplayerPeer ToPeer(Variant result) => result.As<MultiplayerPeer>() ?? throw new SteamAdapterError("steam_peer_create_failed", "GodotSteam did not return a multiplayer peer.");
    private static long ToSteamInt(SteamUserId id) => checked((long)id.Value);
    private static long ToSteamInt(SteamLobbyId id) => checked((long)id.Value);
    private static int ToGodotLobbyType(SteamLobbyVisibility visibility) => visibility switch { SteamLobbyVisibility.Private => 0, SteamLobbyVisibility.FriendsOnly => 1, SteamLobbyVisibility.Public => 2, _ => throw new ArgumentOutOfRangeException(nameof(visibility)) };
    private static void ValidateMetadata(string key, string value) { if (string.IsNullOrWhiteSpace(key) || key.Length > 255 || value.Length > 255) throw new ArgumentException("Steam metadata keys and values must be nonempty and at most 255 characters."); }
    private static void ValidateLobbyOptions(SteamLobbyCreateOptions options) { if (options.MaxMembers is < 2 or > 250) throw new ArgumentOutOfRangeException(nameof(options.MaxMembers)); }
    private void EnsureReadyForLobbyOperation() { EnsureInitialized(); if (CurrentLobby is not null) throw new InvalidOperationException("Leave the current Steam lobby before starting another lobby operation."); }
    private void EnsureInitialized() { ThrowIfDisposed(); if (!IsInitialized) throw new InvalidOperationException("Steam is not initialized."); }
    private SteamLobby RequireLobby() => CurrentLobby ?? throw new InvalidOperationException("No Steam lobby is active.");
    private SteamAdapterError Report(string code, string message) { SteamAdapterError error = new(code, message); Error?.Invoke(error); return error; }
    private static NotSupportedException UnsupportedDedicatedServers() => new("GodotSteamAdapter currently supports Steam listen servers only. Dedicated-server support is reserved behind this interface.");
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
