using System;

namespace GameFactory.Networking.Objects;

public enum ReplicationMode
{
  Never,
  Always,
  OnChange
}

[AttributeUsage(
    AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = true)]
public sealed class ReplicatedAttribute : Attribute
{
  public ReplicationMode Mode { get; }

  public bool Spawn { get; set; } = true;

  public ReplicatedAttribute(
      ReplicationMode mode = ReplicationMode.OnChange)
  {
    Mode = mode;
  }
}