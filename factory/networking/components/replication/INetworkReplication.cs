using System;

namespace GameFactory.Networking.Components.Replication;

public interface INetworkReplication
{
  event Action? Synchronized;

  event Action? DeltaSynchronized;
}