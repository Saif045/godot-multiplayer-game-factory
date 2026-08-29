using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Network;

/// <summary>Small navigation index for the participants that contributed to a diagnostics session.</summary>
public sealed class SessionManifestWriter
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private Participant? _host;
    private readonly Dictionary<long, Participant> _clients = [];

    public SessionManifestWriter(string filePath, string sessionId)
    {
        _filePath = filePath;
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public void Record(string role, PeerId peerId, string runId, IReadOnlyDictionary<string, string?>? metadata)
    {
        var participant = new Participant(runId, peerId.Value, metadata?.GetValueOrDefault("steam_id"));
        lock (_gate)
        {
            if (role == "host")
            {
                if (_host == participant) return;
                _host = participant;
            }
            else
            {
                if (_clients.TryGetValue(peerId.Value, out Participant? existing) && existing == participant) return;
                _clients[peerId.Value] = participant;
            }
            Write();
        }
    }

    private void Write()
    {
        var document = new { session_id = SessionId, host = _host, clients = _clients.Values.OrderBy(client => client.PeerId).ToArray() };
        string temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _filePath, overwrite: true);
    }

    private sealed record Participant(string RunId, long PeerId, string? SteamId);
}
