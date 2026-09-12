using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Networking.Netfox;

/// <summary>
/// Establishes the authority and root topology required by a Netfox rollback
/// player. Gameplay-specific input, simulation, interpolation, and property
/// configuration remain on the host scene.
/// </summary>
public partial class NetfoxRollbackPlayerComponent : NetworkObjectComponent
{
    [Export]
    public NodePath InputPath { get; set; } = new("Input");

    [Export]
    public NodePath SimulationPath { get; set; } = new("Simulation");

    [Export]
    public NodePath RollbackSynchronizerPath { get; set; } = new("RollbackSynchronizer");

    [Export]
    public NodePath TickInterpolatorPath { get; set; } = new("TickInterpolator");

    public override void _EnterTree()
    {
        base._EnterTree();

        // NetworkObject is bound before its host enters the scene tree. This
        // component enters before the host's Netfox children, so their normal
        // initialization observes the final authority topology.
        Node input = Host.GetNode<Node>(InputPath);
        Node simulation = Host.GetNode<Node>(SimulationPath);
        Node rollbackSynchronizer = Host.GetNode<Node>(RollbackSynchronizerPath);
        Node tickInterpolator = Host.GetNode<Node>(TickInterpolatorPath);

        rollbackSynchronizer.Set("root", Host);
        tickInterpolator.Set("root", Host);
        Host.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        simulation.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        input.SetMultiplayerAuthority((int)NetworkObject.OwnerPeerId.Value, recursive: false);
    }
}
