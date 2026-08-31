using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Netfox;

/// <summary>Configures the independent state-sync object as server-authoritative.</summary>
public partial class NetfoxServerStateProbe : Node2D
{
    private Node _networkTime = null!;
    private Node2D _state = null!;
    private bool _authoritativeProgressReported;
    private bool _remoteStateReported;
    private long _presentationTick = -1;
    private Vector2 _presentationPosition;
    public bool StateInterpolationConfirmed { get; private set; }
    public bool RemoteStateReceived => _remoteStateReported;

    public override void _Ready() => CallDeferred(nameof(ConfigureAuthority));

    public override void _Process(double _delta)
    {
        if (_networkTime is null || _state is null) return;
        long tick = _networkTime.Get("tick").AsInt64();
        Vector2 value = _state.Position;
        if (Multiplayer.IsServer() && !_authoritativeProgressReported && tick >= 10)
        {
            _authoritativeProgressReported = true;
            Log("authoritative_state_progress", tick, value);
        }
        else if (!Multiplayer.IsServer() && !_remoteStateReported && value != Vector2.Zero)
        {
            _remoteStateReported = true;
            Log("remote_state_received", tick, value);
        }
        if (!Multiplayer.IsServer() && _remoteStateReported && !StateInterpolationConfirmed)
        {
            if (_presentationTick == tick && value != _presentationPosition)
            {
                StateInterpolationConfirmed = true;
                GameLog.Info("netfox.interpolation", "state_probe_interpolation_confirmed", fields: Fields(tick, value));
            }
            _presentationTick = tick;
            _presentationPosition = value;
        }
    }

    private void ConfigureAuthority()
    {
        _networkTime = GetNode<Node>("/root/NetworkTime");
        _state = GetNode<Node2D>("State");
        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        _state.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        Node stateSynchronizer = GetNode<Node>("StateSynchronizer");
        stateSynchronizer.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        stateSynchronizer.Call("process_settings");
        GetNode<Node>("TickInterpolator").Call("process_settings");
    }

    private void Log(string eventName, long tick, Vector2 value)
        => GameLog.Info("netfox.state_sync", eventName, fields: Fields(tick, value));

    private Dictionary<string, string?> Fields(long tick, Vector2 value) => new()
    {
        ["role"] = Multiplayer.IsServer() ? "host" : "client",
        ["network_tick"] = tick.ToString(),
        ["x"] = value.X.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
        ["y"] = value.Y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
    };
}
