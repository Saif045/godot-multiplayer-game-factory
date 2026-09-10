using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Netfox;

/// <summary>
/// A spawned, interactive movement avatar. Its state remains server-owned;
/// only the Input child is delegated to the peer identified by OwnerPeerId.
/// </summary>
public partial class NetfoxGameplayPlayer : Node2D, INetworkSpawnInitializable
{
    private const float MarkerRadius = 22.0f;

    private long _playerId;
    private double _movementLogElapsed;
    private bool _inputActive;
    private bool _configured;

    public void ApplyNetworkSpawnData(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            throw new InvalidOperationException("Netfox player spawn data must be a Dictionary.");

        Godot.Collections.Dictionary values = data.AsGodotDictionary();
        if (!values.ContainsKey("player_id"))
            throw new InvalidOperationException("Netfox player spawn data is missing player_id.");

        _playerId = (long)values["player_id"];
        Vector2 startPosition = _playerId == 1 ? new Vector2(360, 324) : new Vector2(792, 324);
        Node2D simulation = GetNode<Node2D>("Simulation");
        simulation.Set("simulated_position", startPosition);
        simulation.Position = startPosition;
    }

    public override void _EnterTree()
    {
        GetNode<Node>("RollbackSynchronizer").Set("root", this);
        GetNode<Node>("TickInterpolator").Set("root", this);
    }

    public override void _Ready() => CallDeferred(nameof(ConfigureNetfoxAuthority));

    public override void _Process(double delta)
    {
        _movementLogElapsed += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        NetworkObject? networkObject = GetNodeOrNull<NetworkObject>("NetworkObject");
        Node2D? simulation = GetNodeOrNull<Node2D>("Simulation");
        if (networkObject is null || simulation is null)
            return;

        bool localOwner = networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        Color fill = networkObject.OwnerPeerId == PeerId.Server ? new Color("4ea8de") : new Color("f4a261");
        DrawCircle(simulation.Position, MarkerRadius, fill);
        DrawArc(simulation.Position, MarkerRadius + 4.0f, 0.0f, Mathf.Tau, 32,
            localOwner ? Colors.White : new Color(1.0f, 1.0f, 1.0f, 0.35f), localOwner ? 3.0f : 1.0f);
        DrawString(ThemeDB.FallbackFont, simulation.Position + new Vector2(-36, 48),
            localOwner ? "YOU" : networkObject.OwnerPeerId == PeerId.Server ? "HOST" : "CLIENT",
            HorizontalAlignment.Left, -1, 16, Colors.White);
    }

    /// <summary>Called by the input node when the locally owned input changes.</summary>
    public void ReportLocalInput(Vector2 movement)
    {
        bool active = movement != Vector2.Zero;
        if (_inputActive == active)
            return;

        _inputActive = active;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        GameLog.Info("netfox.movement", "local_input_active", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["active"] = active.ToString(),
            ["movement"] = movement.ToString()
        });
    }

    /// <summary>Called from rollback simulation; rate-limited for operator logs.</summary>
    public void ReportMovement(long tick, Vector2 position, bool _isFresh)
    {
        if (!_configured || _movementLogElapsed < 0.5)
            return;

        _movementLogElapsed = 0;
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        bool localOwner = networkObject.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        GameLog.Info("netfox.movement", localOwner ? "local_player_moved" : "remote_player_moved", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["network_tick"] = tick.ToString(),
            ["position"] = position.ToString()
        });
    }

    private void ConfigureNetfoxAuthority()
    {
        NetworkObject networkObject = GetNode<NetworkObject>("NetworkObject");
        Node input = GetNode<Node>("Input");
        Node simulation = GetNode<Node>("Simulation");
        Node rollbackSynchronizer = GetNode<Node>("RollbackSynchronizer");
        Node tickInterpolator = GetNode<Node>("TickInterpolator");

        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        simulation.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        input.SetMultiplayerAuthority((int)networkObject.OwnerPeerId.Value, recursive: false);
        rollbackSynchronizer.Call("process_settings");
        tickInterpolator.Call("process_settings");
        _configured = true;

        GameLog.Info("netfox.movement", "player_configured", fields: new Dictionary<string, string?>
        {
            ["player_id"] = _playerId.ToString(),
            ["network_object_id"] = networkObject.Id.ToString(),
            ["owner_peer_id"] = networkObject.OwnerPeerId.ToString(),
            ["root_multiplayer_authority"] = GetMultiplayerAuthority().ToString(),
            ["simulation_multiplayer_authority"] = simulation.GetMultiplayerAuthority().ToString(),
            ["input_multiplayer_authority"] = input.GetMultiplayerAuthority().ToString()
        });
    }
}
