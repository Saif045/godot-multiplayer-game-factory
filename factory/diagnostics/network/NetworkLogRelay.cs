using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Steam.Models;

namespace GameFactory.Diagnostics.Network;

/// <summary>Forwards bounded local diagnostic batches to the authoritative host.</summary>
public partial class NetworkLogRelay : Node
{
    private const int DiagnosticsChannel = 7;
    private const int BatchLimit = 32;
    private const int BacklogLimit = 512;
    private const double FlushIntervalSeconds = 0.1;

    private readonly List<LogEntry> _backlog = [];
    private readonly Dictionary<string, long> _highestReceived = [];
    private StreamWriter? _masterWriter;
    private DiagnosticsSessionId? _hostSession;
    private double _secondsUntilFlush;

    public Func<PeerId, SteamUserId?>? SteamUserResolver { get; set; }
    public DiagnosticsSessionId? SessionId { get; private set; }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        GameLog.EntryWritten += OnLocalEntry;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        GameLog.EntryWritten -= OnLocalEntry;
        _masterWriter?.Dispose();
        _masterWriter = null;
    }

    public void StartHostSession()
    {
        if (!Multiplayer.IsServer())
            throw new InvalidOperationException("Only the authoritative server can start diagnostics collection.");

        DiagnosticsSessionId sessionId = DiagnosticsSessionId.New();
        SessionId = sessionId;
        _hostSession = sessionId;
        GameLog.AssociateSession(sessionId);
        string directory = ProjectSettings.GlobalizePath($"user://logs/sessions/{sessionId}");
        Directory.CreateDirectory(directory);
        _masterWriter?.Dispose();
        _masterWriter = new StreamWriter(new FileStream(Path.Combine(directory, "master.jsonl"), FileMode.Append, System.IO.FileAccess.Write, FileShare.ReadWrite));
        GameLog.Info("diagnostics.session", "host_started", $"session={sessionId}");
    }

    public override void _Process(double delta)
    {
        _secondsUntilFlush -= delta;
        if (_secondsUntilFlush > 0 || _backlog.Count == 0 || Multiplayer.IsServer() || SessionId is null)
            return;

        FlushClientBatch();
        _secondsUntilFlush = FlushIntervalSeconds;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = DiagnosticsChannel)]
    private void AssignSessionRpc(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out Guid raw)) return;
        SessionId = new DiagnosticsSessionId(raw);
        GameLog.AssociateSession(SessionId.Value);
        GameLog.Info("diagnostics.session", "assigned", $"session={SessionId}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = DiagnosticsChannel)]
    private void ReceiveBatchRpc(string payload)
    {
        if (!Multiplayer.IsServer() || _hostSession is null) return;
        long sender = Multiplayer.GetRemoteSenderId();
        List<LogEntry>? entries;
        try { entries = JsonSerializer.Deserialize<List<LogEntry>>(payload); }
        catch (JsonException) { return; }
        if (entries is null || entries.Count == 0 || entries.Count > BatchLimit) return;

        string runId = entries[0].RunId;
        if (entries.Any(entry => entry.RunId != runId || entry.DiagnosticsSessionId != _hostSession.Value.ToString())) return;
        bool hasAcceptedEntry = _highestReceived.TryGetValue(runId, out long highest);
        foreach (LogEntry entry in entries.OrderBy(entry => entry.Sequence))
        {
            if (entry.Sequence <= highest) continue;
            if (hasAcceptedEntry && entry.Sequence != highest + 1) break;
            highest = entry.Sequence;
            hasAcceptedEntry = true;
            WriteMaster(entry, new PeerId(sender), role: "client", DateTimeOffset.UtcNow);
        }
        _highestReceived[runId] = highest;
        RpcId(sender, MethodName.AcknowledgeBatchRpc, runId, highest);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = DiagnosticsChannel)]
    private void AcknowledgeBatchRpc(string runId, long highestSequence)
    {
        if (runId != GameLog.RunId) return;
        _backlog.RemoveAll(entry => entry.Sequence <= highestSequence);
    }

    private void OnPeerConnected(long peerId)
    {
        GameLog.Info("network.peer", "connected", fields: new Dictionary<string, string?> { ["peer_id"] = peerId.ToString() });
        if (Multiplayer.IsServer() && _hostSession is not null)
            RpcId(peerId, MethodName.AssignSessionRpc, _hostSession.Value.ToString());
    }

    private void OnPeerDisconnected(long peerId) => GameLog.Info("network.peer", "disconnected", fields: new Dictionary<string, string?> { ["peer_id"] = peerId.ToString() });
    private void OnConnectedToServer() => GameLog.Info("network.connection", "connected_to_server");
    private void OnConnectionFailed() => GameLog.Warning("network.connection", "connection_failed");
    private void OnServerDisconnected() => GameLog.Warning("network.connection", "server_disconnected");

    private void OnLocalEntry(LogEntry entry)
    {
        if (_hostSession is not null && entry.DiagnosticsSessionId == _hostSession.Value.ToString())
            WriteMaster(entry, PeerId.Server, "host", entry.Utc);

        if (Multiplayer.IsServer() || SessionId is null || entry.Category == "diagnostics.relay") return;
        _backlog.Add(entry);
        if (_backlog.Count > BacklogLimit) _backlog.RemoveAt(0);
    }

    private void FlushClientBatch()
    {
        List<LogEntry> batch = _backlog.Take(BatchLimit).ToList();
        RpcId(PeerId.Server.Value, MethodName.ReceiveBatchRpc, JsonSerializer.Serialize(batch));
    }

    private void WriteMaster(LogEntry entry, PeerId peerId, string role, DateTimeOffset receivedUtc)
    {
        SteamUserId? steamId = SteamUserResolver?.Invoke(peerId);
        var master = new
        {
            source_role = role,
            source_peer_id = peerId.Value,
            source_steam_id = steamId?.Value,
            host_received_utc = receivedUtc,
            entry
        };
        _masterWriter?.WriteLine(JsonSerializer.Serialize(master));
        _masterWriter?.Flush();
    }
}
