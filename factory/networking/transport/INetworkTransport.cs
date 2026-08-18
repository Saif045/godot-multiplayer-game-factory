using System;
using GameFactory.Networking.Core;

namespace GameFactory.Networking.Transport;

public interface INetworkTransport : IDisposable
{
    bool IsRunning { get; }

    event Action<PeerId>? PeerConnected;
    event Action<PeerId>? PeerDisconnected;

    event Action? ConnectedToServer;
    event Action? ConnectionFailed;
    event Action? ServerDisconnected;

    TransportResult StartServer(
        int port,
        int maxClients);

    TransportResult Connect(
        string address,
        int port);

    PeerId GetLocalPeerId();

    void Close();
}
