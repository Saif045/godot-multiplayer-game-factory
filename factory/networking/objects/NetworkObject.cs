using System;
using System.Reflection;
using Godot;

namespace GameFactory.Networking.Objects;

public partial class NetworkObject : Node
{
    private MultiplayerSynchronizer _synchronizer = null!;

    public Node Host { get; private set; } = null!;

    public int AuthorityPeerId => Host.GetMultiplayerAuthority();

    public bool HasAuthority => Host.IsMultiplayerAuthority();

    public MultiplayerSynchronizer Synchronizer => _synchronizer;

    public override void _EnterTree()
    {
        Host = GetParent()
            ?? throw new InvalidOperationException(
                "NetworkObject must be a child of a host node.");

        _synchronizer = GetNode<MultiplayerSynchronizer>("MultiplayerSynchronizer");

        ConfigureReplication();
    }

    private void ConfigureReplication()
    {
        SceneReplicationConfig config = new();

        PropertyInfo[] properties = Host.GetType().GetProperties(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);

        foreach (PropertyInfo property in properties)
        {
            ReplicatedAttribute? replicated = property.GetCustomAttribute<ReplicatedAttribute>();

            if (replicated is null)
                continue;

            if (property.GetCustomAttribute<ExportAttribute>() is null)
            {
                throw new InvalidOperationException(
                    $"{Host.GetType().Name}.{property.Name} " +
                    "uses [Replicated] but is missing [Export].");
            }

            NodePath propertyPath = new($".:{property.Name}");

            config.AddProperty(propertyPath);

            config.PropertySetSpawn(
                propertyPath,
                replicated.Spawn);

            config.PropertySetReplicationMode(
                propertyPath,
                ToGodotMode(replicated.Mode));

            GD.Print(
                $"[network][replication] " +
                $"{Host.Name}.{property.Name}: " +
                $"mode={replicated.Mode}, " +
                $"spawn={replicated.Spawn}");
        }

        _synchronizer.ReplicationConfig = config;
    }

    private static SceneReplicationConfig.ReplicationMode ToGodotMode(
        ReplicationMode mode)
    {
        return mode switch
        {
            ReplicationMode.Never =>
                SceneReplicationConfig.ReplicationMode.Never,

            ReplicationMode.Always =>
                SceneReplicationConfig.ReplicationMode.Always,

            ReplicationMode.OnChange =>
                SceneReplicationConfig.ReplicationMode.OnChange,

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                null)
        };
    }
}
