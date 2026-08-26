using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Steam;
using GameFactory.Steam.Adapters.GodotSteam;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Steam;

/// <summary>
/// Manual Steam sandbox. Run under Steam with App ID 480 and either --steam-host
/// or --steam-lobby=&lt;lobby-id&gt;. It does not duplicate game-world behavior.
/// </summary>
public partial class SteamProbe : Node
{
    private SteamSession? _session;
    private GodotSteamAdapter? _adapter;
    private SteamLobbyId? _requestedLobby;

    public override async void _Ready()
    {
        try
        {
            _adapter = GodotSteamAdapter.Create(this);
            GD.Print("_adapter:", _adapter);

            _session = new SteamSession(_adapter, Multiplayer);
            GD.Print("before_session:", _session);

            _session.StateChanged += (from, to) => GD.Print($"[steam] {from} -> {to}");
            _session.LobbyJoinRequested += OnLobbyJoinRequested;
            GD.Print("after_session:", _session);
            await _session.InitializeAsync();
            GD.Print($"[steam] local user: {_adapter.LocalUser.DisplayName} ({_adapter.LocalUser.Id})");

            string[] args = OS.GetCmdlineArgs()
                .Concat(OS.GetCmdlineUserArgs())
                .ToArray();
            if (args.Contains("--steam-host"))
            {
                await HostAsync();
                return;
            }

            string? joinArgument = args.FirstOrDefault(argument => argument.StartsWith("--steam-lobby=", StringComparison.Ordinal));
            if (joinArgument is not null && ulong.TryParse(joinArgument["--steam-lobby=".Length..], out ulong lobbyValue) && lobbyValue != 0)
            {
                await _session.JoinAsync(new SteamLobbyId(lobbyValue), new SteamClientOptions());
                GD.Print($"[steam] joined lobby {lobbyValue}; Godot MultiplayerPeer is assigned.");
                return;
            }

            GD.Print("[steam] ready. Use --steam-host or --steam-lobby=<id>. Keys: H host, I lobby invite, O friends overlay, J join requested lobby, L leave. App ID 480 is development-only.");
        }
        catch (Exception exception)
        {
            GD.PushError($"[steam] {exception.Message}");
        }
    }

    public override async void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key || _session is null)
            return;

        try
        {
            GD.Print($"[steam][probe] key={key.Keycode}; state={_session.State}; lobby={_adapter?.CurrentLobby?.Id}");
            switch (key.Keycode)
            {
                case Key.H when _session.State == SteamSessionState.Ready:
                    await HostAsync();
                    break;
                case Key.I when _adapter?.CurrentLobby is not null:
                    _adapter.OpenInviteOverlay();
                    break;
                case Key.O:
                    _adapter?.OpenFriendsOverlay();
                    break;
                case Key.J when _requestedLobby is SteamLobbyId lobby && _session.State == SteamSessionState.Ready:
                    await _session.JoinAsync(lobby, new SteamClientOptions());
                    _requestedLobby = null;
                    break;
                case Key.L:
                    await _session.LeaveAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[steam] {exception.Message}");
        }
    }

    public override void _ExitTree() => _session?.Dispose();

    private async Task HostAsync()
    {
        SteamLobby lobby = await _session!.HostAsync(
            new SteamLobbyCreateOptions(),
            new SteamListenServerOptions());
        GD.Print($"[steam] hosting friends-only lobby {lobby.Id}; press I for the invite overlay or use --steam-lobby={lobby.Id}.");

    }

    private void OnLobbyJoinRequested(SteamLobbyId lobby, SteamUserId inviter)
    {
        _requestedLobby = lobby;
        GD.Print($"[steam] join requested: lobby={lobby}, inviter={inviter}. Press J to join; no automatic active-session join.");
    }

}
