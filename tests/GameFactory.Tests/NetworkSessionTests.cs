using GameFactory.Core;
using GameFactory.Networking.Core;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Sessions;
using GameFactory.Networking.Transport;
using GameFactory.Tests.TestDoubles;

namespace GameFactory.Tests;

public sealed class NetworkSessionTests
{
    [Theory]
    [InlineData(HostMode.Listen, RuntimeMode.ListenServer, true)]
    [InlineData(HostMode.Dedicated, RuntimeMode.DedicatedServer, false)]
    public void Host_starts_requested_mode_and_registers_local_peer(
        HostMode hostMode,
        RuntimeMode runtimeMode,
        bool isClient)
    {
        SessionFixture fixture = CreateFixture(localPeerId: 1);
        var transitions = new List<(SessionState Previous, SessionState Next)>();
        fixture.Session.StateChanged += (previous, next) => transitions.Add((previous, next));

        SessionResult result = fixture.Session.Host(7000, 8, hostMode);

        Assert.True(result.Success);
        Assert.Equal((7000, 8), fixture.Transport.LastStartServerArguments);
        Assert.Equal(1, fixture.Transport.StartServerCallCount);
        Assert.True(fixture.Transport.IsRunning);
        Assert.Equal(SessionState.Running, fixture.Session.State);
        Assert.Equal(runtimeMode, fixture.Runtime.Mode);
        Assert.True(fixture.Runtime.IsServer);
        Assert.Equal(isClient, fixture.Runtime.IsClient);
        NetworkPeer peer = Assert.Single(fixture.Peers.Peers);
        Assert.Equal(PeerId.Server, peer.Id);
        Assert.True(peer.IsLocal);
        Assert.Equal(
            [
                (SessionState.Offline, SessionState.Starting),
                (SessionState.Starting, SessionState.Running)
            ],
            transitions);
    }

    [Fact]
    public void Host_initialization_failure_cleans_up_and_enters_failed()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Transport.StartServerResult = TransportResult.Fail("port unavailable");

        SessionResult result = fixture.Session.Host(7000, 8);

        Assert.False(result.Success);
        Assert.Equal("port unavailable", result.Error);
        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.HostStartFailed, fixture.Session.LastEndReason);
        Assert.Equal("port unavailable", fixture.Session.LastError);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Join_remains_connecting_until_transport_confirms_connection()
    {
        SessionFixture fixture = CreateFixture(localPeerId: 7);
        var transitions = new List<(SessionState Previous, SessionState Next)>();
        fixture.Session.StateChanged += (previous, next) => transitions.Add((previous, next));

        SessionResult result = fixture.Session.Join("127.0.0.1", 7000);

        Assert.True(result.Success);
        Assert.Equal(("127.0.0.1", 7000), fixture.Transport.LastConnectArguments);
        Assert.Equal(SessionState.Connecting, fixture.Session.State);
        Assert.Equal(RuntimeMode.Client, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);

        fixture.Transport.RaiseConnectedToServer();

        Assert.Equal(SessionState.Running, fixture.Session.State);
        NetworkPeer peer = Assert.Single(fixture.Peers.Peers);
        Assert.Equal(new PeerId(7), peer.Id);
        Assert.True(peer.IsLocal);
        Assert.Equal(
            [
                (SessionState.Offline, SessionState.Connecting),
                (SessionState.Connecting, SessionState.Running)
            ],
            transitions);
    }

    [Fact]
    public void Connection_confirmation_during_connect_is_accepted_for_client_session()
    {
        SessionFixture fixture = CreateFixture(localPeerId: 7);
        fixture.Transport.OnConnect = fixture.Transport.RaiseConnectedToServer;

        SessionResult result = fixture.Session.Join("127.0.0.1", 7000);

        Assert.True(result.Success);
        Assert.Equal(SessionState.Running, fixture.Session.State);
        Assert.Equal(RuntimeMode.Client, fixture.Runtime.Mode);
        Assert.Equal(new PeerId(7), Assert.Single(fixture.Peers.Peers).Id);
    }

    [Fact]
    public void Join_initialization_failure_uses_transport_error_and_cleans_up()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Transport.ConnectResult = TransportResult.Fail("bad address");

        SessionResult result = fixture.Session.Join("invalid", 7000);

        Assert.False(result.Success);
        Assert.Equal("bad address", result.Error);
        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.ConnectionFailed, fixture.Session.LastEndReason);
        Assert.Equal("bad address", fixture.Session.LastError);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Asynchronous_connection_failure_cleans_up_and_enters_failed()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Join("127.0.0.1", 7000);

        fixture.Transport.RaiseConnectionFailed();

        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.ConnectionFailed, fixture.Session.LastEndReason);
        Assert.Equal("Connection to server failed.", fixture.Session.LastError);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Peer_transport_events_add_and_remove_remote_peers()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8);
        var remoteId = new PeerId(12);

        fixture.Transport.RaisePeerConnected(remoteId);

        NetworkPeer remote = Assert.IsType<NetworkPeer>(fixture.Peers.Find(remoteId));
        Assert.False(remote.IsLocal);

        fixture.Transport.RaisePeerDisconnected(remoteId);

        Assert.Null(fixture.Peers.Find(remoteId));
        Assert.Equal(SessionState.Running, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_can_leave_while_connecting_or_running(bool confirmConnection)
    {
        SessionFixture fixture = CreateFixture(localPeerId: 7);
        fixture.Session.Join("127.0.0.1", 7000);
        if (confirmConnection)
            fixture.Transport.RaiseConnectedToServer();

        var transitions = new List<(SessionState Previous, SessionState Next)>();
        fixture.Session.StateChanged += (previous, next) => transitions.Add((previous, next));

        SessionResult result = fixture.Session.Leave();

        Assert.True(result.Success);
        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(SessionEndReason.LocalLeave, fixture.Session.LastEndReason);
        Assert.Null(fixture.Session.LastError);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
        SessionState startingState = confirmConnection ? SessionState.Running : SessionState.Connecting;
        Assert.Equal(
            [
                (startingState, SessionState.Stopping),
                (SessionState.Stopping, SessionState.Offline)
            ],
            transitions);
    }

    [Theory]
    [InlineData(HostMode.Listen)]
    [InlineData(HostMode.Dedicated)]
    public void Host_shutdown_cleans_up_and_returns_offline(HostMode hostMode)
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8, hostMode);
        fixture.Transport.RaisePeerConnected(new PeerId(12));

        SessionResult result = fixture.Session.ShutdownHost();

        Assert.True(result.Success);
        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(SessionEndReason.HostShutdown, fixture.Session.LastEndReason);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Server_disconnection_fails_client_and_clears_session_data()
    {
        SessionFixture fixture = CreateFixture(localPeerId: 7);
        fixture.Session.Join("127.0.0.1", 7000);
        fixture.Transport.RaiseConnectedToServer();
        fixture.Transport.RaisePeerConnected(PeerId.Server);

        fixture.Transport.RaiseServerDisconnected();

        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.ServerDisconnected, fixture.Session.LastEndReason);
        Assert.Equal("Server disconnected.", fixture.Session.LastError);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void ResetFailure_clears_failure_and_allows_another_start()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Transport.StartServerResult = TransportResult.Fail("first failure");
        fixture.Session.Host(7000, 8);

        SessionResult reset = fixture.Session.ResetFailure();

        Assert.True(reset.Success);
        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(SessionEndReason.None, fixture.Session.LastEndReason);
        Assert.Null(fixture.Session.LastError);

        fixture.Transport.StartServerResult = TransportResult.Ok();
        SessionResult retry = fixture.Session.Host(8000, 4);

        Assert.True(retry.Success);
        Assert.Equal(SessionState.Running, fixture.Session.State);
        Assert.Equal((8000, 4), fixture.Transport.LastStartServerArguments);
    }

    [Fact]
    public void Starting_another_session_while_running_is_rejected_without_transport_call()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8);

        SessionResult host = fixture.Session.Host(8000, 4);
        SessionResult join = fixture.Session.Join("127.0.0.1", 8000);

        Assert.False(host.Success);
        Assert.Contains("Cannot host", host.Error);
        Assert.False(join.Success);
        Assert.Contains("Cannot join", join.Error);
        Assert.Equal(1, fixture.Transport.StartServerCallCount);
        Assert.Equal(0, fixture.Transport.ConnectCallCount);
        Assert.Equal(SessionState.Running, fixture.Session.State);
    }

    [Fact]
    public void Invalid_end_and_reset_operations_leave_offline_session_unchanged()
    {
        SessionFixture fixture = CreateFixture();

        SessionResult leave = fixture.Session.Leave();
        SessionResult shutdown = fixture.Session.ShutdownHost();
        SessionResult reset = fixture.Session.ResetFailure();

        Assert.False(leave.Success);
        Assert.False(shutdown.Success);
        Assert.False(reset.Success);
        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(0, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Dispose_borrows_transport_cleans_active_session_and_unsubscribes_events()
    {
        SessionFixture fixture = CreateFixture(localPeerId: 7);
        fixture.Session.Join("127.0.0.1", 7000);
        fixture.Transport.RaiseConnectedToServer();

        fixture.Session.Dispose();
        fixture.Transport.RaiseConnectionFailed();
        fixture.Transport.RaiseServerDisconnected();
        fixture.Transport.RaisePeerConnected(new PeerId(12));

        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
        Assert.Equal(0, fixture.Transport.DisposeCallCount);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8);

        fixture.Session.Dispose();
        fixture.Session.Dispose();

        Assert.Equal(1, fixture.Transport.CloseCallCount);
        Assert.Equal(0, fixture.Transport.DisposeCallCount);
    }

    [Fact]
    public void Dispose_offline_session_does_not_close_or_dispose_borrowed_transport()
    {
        SessionFixture fixture = CreateFixture();

        fixture.Session.Dispose();

        Assert.Equal(0, fixture.Transport.CloseCallCount);
        Assert.Equal(0, fixture.Transport.DisposeCallCount);
    }

    [Fact]
    public void Public_lifecycle_operations_throw_after_dispose()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fixture.Session.Host(7000, 8));
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.Join("127.0.0.1", 7000));
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.Leave());
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.ShutdownHost());
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.ResetFailure());
    }

    [Fact]
    public void Leave_while_hosting_is_rejected_without_cleanup()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8);

        SessionResult result = fixture.Session.Leave();

        Assert.False(result.Success);
        Assert.Equal(SessionState.Running, fixture.Session.State);
        Assert.Equal(0, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Shutdown_host_while_client_is_rejected_without_cleanup()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Join("127.0.0.1", 7000);

        SessionResult result = fixture.Session.ShutdownHost();

        Assert.False(result.Success);
        Assert.Equal(SessionState.Connecting, fixture.Session.State);
        Assert.Equal(0, fixture.Transport.CloseCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Host_and_join_are_rejected_while_connecting_or_running(bool running)
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Join("127.0.0.1", 7000);
        if (running)
            fixture.Transport.RaiseConnectedToServer();

        Assert.False(fixture.Session.Host(8000, 4).Success);
        Assert.False(fixture.Session.Join("127.0.0.1", 8000).Success);
        Assert.Equal(1, fixture.Transport.ConnectCallCount);
        Assert.Equal(0, fixture.Transport.StartServerCallCount);
    }

    [Theory]
    [InlineData(SessionState.Offline)]
    [InlineData(SessionState.Connecting)]
    [InlineData(SessionState.Running)]
    public void Reset_failure_is_rejected_outside_failed(SessionState state)
    {
        SessionFixture fixture = CreateFixture();
        if (state is SessionState.Connecting or SessionState.Running)
        {
            fixture.Session.Join("127.0.0.1", 7000);
            if (state == SessionState.Running)
                fixture.Transport.RaiseConnectedToServer();
        }

        Assert.False(fixture.Session.ResetFailure().Success);
        Assert.Equal(state, fixture.Session.State);
    }

    [Fact]
    public void Inactive_or_irrelevant_transport_events_do_not_mutate_session()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Transport.RaisePeerConnected(new PeerId(12));
        fixture.Transport.RaiseConnectedToServer();
        fixture.Transport.RaiseConnectionFailed();
        fixture.Transport.RaiseServerDisconnected();

        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Empty(fixture.Peers.Peers);

        fixture.Session.Host(7000, 8);
        fixture.Transport.RaiseConnectedToServer();
        fixture.Transport.RaiseConnectionFailed();
        fixture.Transport.RaiseServerDisconnected();

        Assert.Equal(SessionState.Running, fixture.Session.State);
        Assert.Equal(RuntimeMode.ListenServer, fixture.Runtime.Mode);
    }

    [Fact]
    public void Cleanup_exception_still_clears_runtime_and_peers_and_enters_failed()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Host(7000, 8);
        fixture.Transport.CloseException = new InvalidOperationException("close exploded");

        SessionResult result = fixture.Session.ShutdownHost();

        Assert.False(result.Success);
        Assert.Contains("close exploded", result.Error);
        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.CleanupFailed, fixture.Session.LastEndReason);
        Assert.Equal(RuntimeMode.Offline, fixture.Runtime.Mode);
        Assert.Empty(fixture.Peers.Peers);
        Assert.Equal(1, fixture.Transport.CloseCallCount);
    }

    [Fact]
    public void Close_triggered_server_disconnection_does_not_fail_intentional_leave()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Join("127.0.0.1", 7000);
        fixture.Transport.RaiseConnectedToServer();
        fixture.Transport.OnClose = fixture.Transport.RaiseServerDisconnected;

        SessionResult result = fixture.Session.Leave();

        Assert.True(result.Success);
        Assert.Equal(SessionState.Offline, fixture.Session.State);
        Assert.Equal(SessionEndReason.LocalLeave, fixture.Session.LastEndReason);
        Assert.Null(fixture.Session.LastError);
    }

    [Fact]
    public void Failed_session_ignores_later_transport_events()
    {
        SessionFixture fixture = CreateFixture();
        fixture.Session.Join("127.0.0.1", 7000);
        fixture.Transport.RaiseConnectionFailed();

        fixture.Transport.RaiseConnectedToServer();
        fixture.Transport.RaisePeerConnected(new PeerId(12));
        fixture.Transport.RaiseServerDisconnected();

        Assert.Equal(SessionState.Failed, fixture.Session.State);
        Assert.Equal(SessionEndReason.ConnectionFailed, fixture.Session.LastEndReason);
        Assert.Empty(fixture.Peers.Peers);
    }

    private static SessionFixture CreateFixture(long localPeerId = PeerId.ServerValue)
    {
        var transport = new FakeNetworkTransport
        {
            LocalPeerId = new PeerId(localPeerId)
        };
        var runtime = new RuntimeContext();
        var peers = new PeerRegistry();
        var session = new NetworkSession(transport, runtime, peers);
        return new SessionFixture(transport, runtime, peers, session);
    }

    private sealed record SessionFixture(
        FakeNetworkTransport Transport,
        RuntimeContext Runtime,
        PeerRegistry Peers,
        NetworkSession Session);
}
