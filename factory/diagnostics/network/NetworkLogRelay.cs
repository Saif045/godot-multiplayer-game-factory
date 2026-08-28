using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Network;

/// <summary>Forwards bounded local diagnostic batches to the authoritative host.</summary>
public partial class NetworkLogRelay : Node
{
    private const int DiagnosticsChannel = 7;
    private const int BatchLimit = 32;
    private const double FlushIntervalSeconds = 0.1;

    private readonly RelayBacklog _backlog = new();
    private readonly Dictionary<string, long> _highestReceived = [];
    private StreamWriter? _masterWriter;
    private DiagnosticsSessionId? _hostSession;
    private double _secondsUntilFlush;

    public Func<PeerId, IReadOnlyDictionary<string, string?>?>? SourceMetadataResolver { get; set; }
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
        _highestReceived.Clear();
        _backlog.BeginSession(sessionId);
        foreach (LogEntry entry in _backlog.Entries)
            WriteMaster(entry with { DiagnosticsSessionId = sessionId.ToString() }, PeerId.Server, "host", entry.Utc);
        GameLog.Info("diagnostics.session", "host_started", $"session={sessionId}");
    }

    public void EndSession()
    {
        _masterWriter?.Dispose();
        _masterWriter = null;
        _hostSession = null;
        SessionId = null;
        _highestReceived.Clear();
        _backlog.EndSession();
        GameLog.ClearSession();
    }

    public override void _Process(double delta)
    {
        _secondsUntilFlush -= delta;
        if (_secondsUntilFlush > 0 || Multiplayer.IsServer())
            return;

        FlushClientBatch();
        _secondsUntilFlush = FlushIntervalSeconds;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = DiagnosticsChannel)]
    private void AssignSessionRpc(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out Guid raw)) return;
        DiagnosticsSessionId assigned = new(raw);
        if (SessionId is not null && SessionId.Value == assigned) return;
        SessionId = assigned;
        _backlog.BeginSession(assigned);
        GameLog.AssociateSession(assigned);
        GameLog.Info("diagnostics.session", "assigned", $"session={SessionId}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = DiagnosticsChannel)]
    private void ReceiveBatchRpc(string payload)
    {
        if (!Multiplayer.IsServer() || _hostSession is null) return;
        long sender = Multiplayer.GetRemoteSenderId();
        LogBatch? batch;
        try { batch = JsonSerializer.Deserialize<LogBatch>(payload); }
        catch (JsonException) { return; }
        if (batch is null || batch.Entries.Count == 0 || batch.Entries.Count > BatchLimit || batch.DiagnosticsSessionId != _hostSession.Value.ToString()) return;

        string runId = batch.RunId;
        if (batch.Entries.Any(entry => entry.RunId != runId || entry.DiagnosticsSessionId != batch.DiagnosticsSessionId)) return;
        bool hasAcceptedEntry = _highestReceived.TryGetValue(runId, out long highest);
        if (batch.DroppedThroughSequence > highest)
        {
            WriteGap(runId, new PeerId(sender), highest + 1, batch.DroppedThroughSequence);
            highest = batch.DroppedThroughSequence;
            hasAcceptedEntry = true;
        }
        foreach (LogEntry entry in batch.Entries.OrderBy(entry => entry.Sequence))
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
        _backlog.Acknowledge(highestSequence);
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

        if (entry.Category == "diagnostics.relay") return;
        _backlog.Record(entry);
    }

    private void FlushClientBatch()
    {
        LogBatch? batch = _backlog.CreateBatch(GameLog.RunId, BatchLimit);
        if (batch is null) return;
        RpcId(PeerId.Server.Value, MethodName.ReceiveBatchRpc, JsonSerializer.Serialize(batch));
    }

    private void WriteMaster(LogEntry entry, PeerId peerId, string role, DateTimeOffset receivedUtc)
    {
        IReadOnlyDictionary<string, string?>? sourceMetadata = SourceMetadataResolver?.Invoke(peerId);
        var master = new
        {
            source_role = role,
            source_peer_id = peerId.Value,
            source_metadata = sourceMetadata,
            host_received_utc = receivedUtc,
            entry
        };
        _masterWriter?.WriteLine(JsonSerializer.Serialize(master));
        _masterWriter?.Flush();
    }

    private void WriteGap(string runId, PeerId peerId, long firstMissing, long droppedThrough)
    {
        var gap = new
        {
            source_role = "client",
            source_peer_id = peerId.Value,
            source_metadata = SourceMetadataResolver?.Invoke(peerId),
            host_received_utc = DateTimeOffset.UtcNow,
            diagnostics_gap = new { run_id = runId, missing_from_sequence = firstMissing, missing_through_sequence = droppedThrough }
        };
        _masterWriter?.WriteLine(JsonSerializer.Serialize(gap));
        _masterWriter?.Flush();
    }
}
