using GameFactory.Networking.Core;
using GameFactory.Networking.Transport;

namespace GameFactory.Tests.TestDoubles;

internal sealed class FakeNetworkTransport : INetworkTransport
{
    public bool IsRunning { get; private set; }

    public TransportResult StartServerResult { get; set; } = TransportResult.Ok();
    public TransportResult ConnectResult { get; set; } = TransportResult.Ok();
    public PeerId LocalPeerId { get; set; } = PeerId.Server;

    public int StartServerCallCount { get; private set; }
    public int ConnectCallCount { get; private set; }
    public int CloseCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public (int Port, int MaxClients)? LastStartServerArguments { get; private set; }
    public (string Address, int Port)? LastConnectArguments { get; private set; }

    public event Action<PeerId>? PeerConnected;
    public event Action<PeerId>? PeerDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action? ServerDisconnected;

    public TransportResult StartServer(int port, int maxClients)
    {
        StartServerCallCount++;
        LastStartServerArguments = (port, maxClients);
        IsRunning = StartServerResult.Success;
        return StartServerResult;
    }

    public TransportResult Connect(string address, int port)
    {
        ConnectCallCount++;
        LastConnectArguments = (address, port);
        IsRunning = ConnectResult.Success;
        return ConnectResult;
    }

    public PeerId GetLocalPeerId()
    {
        return LocalPeerId;
    }

    public void Close()
    {
        CloseCallCount++;
        IsRunning = false;
    }

    public void Dispose()
    {
        DisposeCallCount++;
        IsRunning = false;
    }

    public void RaisePeerConnected(PeerId peerId) => PeerConnected?.Invoke(peerId);
    public void RaisePeerDisconnected(PeerId peerId) => PeerDisconnected?.Invoke(peerId);
    public void RaiseConnectedToServer() => ConnectedToServer?.Invoke();
    public void RaiseConnectionFailed() => ConnectionFailed?.Invoke();
    public void RaiseServerDisconnected() => ServerDisconnected?.Invoke();
}
