using System;

namespace GameFactory.Networking.Objects.Components.Replication;

public interface INetworkReplication
{
  event Action? Synchronized;

  event Action? DeltaSynchronized;
}
