using System;
using System.Reflection;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Networking.Objects.Components.Replication;

public partial class ReplicationComponent
    : NetworkObjectComponent, INetworkReplication
{
  private MultiplayerSynchronizer _synchronizer = null!;

  public event Action? Synchronized;
  public event Action? DeltaSynchronized;

  protected override void OnNetworkInitialize()
  {
    _synchronizer =
        GetNode<MultiplayerSynchronizer>("MultiplayerSynchronizer");

    _synchronizer.RootPath =
        _synchronizer.GetPathTo(Host);

    _synchronizer.Synchronized += OnSynchronized;
    _synchronizer.DeltaSynchronized += OnDeltaSynchronized;

    ConfigureReplication();
  }

  protected override void OnNetworkShutdown()
  {
    _synchronizer.Synchronized -= OnSynchronized;
    _synchronizer.DeltaSynchronized -= OnDeltaSynchronized;
  }

  private void OnSynchronized()
  {
    Synchronized?.Invoke();
  }

  private void OnDeltaSynchronized()
  {
    DeltaSynchronized?.Invoke();
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
      ReplicatedAttribute? replicated =
          property.GetCustomAttribute<ReplicatedAttribute>();

      if (replicated is null)
        continue;

      NodePath propertyPath = new($".:{property.Name}");

      config.AddProperty(propertyPath);

      config.PropertySetSpawn(
          propertyPath,
          replicated.Spawn);

      config.PropertySetReplicationMode(
          propertyPath,
          ToGodotMode(replicated.Mode));
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
