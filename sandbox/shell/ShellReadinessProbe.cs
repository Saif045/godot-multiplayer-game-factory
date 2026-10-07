using System;
using System.Reflection;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell;

/// <summary>Opt-in sender of invalid readiness RPCs; never edits receiver state.</summary>
public partial class ShellReadinessProbe : Node
{
    private static readonly FieldInfo Revision = Field("_lobbyRevision");
    private static readonly FieldInfo ReadyRevision = Field("_clientReadyRevision");
    private static readonly FieldInfo Phase = Field("_phase");
    private ulong _shellId;
    private int _sentRevision = -1;
    private bool _sentGameplay;

    public override void _Process(double delta)
    {
        OnlineGameplayShell? shell = GetTree().Root.GetNodeOrNull<OnlineGameplayShell>("OnlineGameplayShell");
        if (shell is null || !shell.Multiplayer.HasMultiplayerPeer() || shell.Multiplayer.IsServer()) return;
        if (_shellId != shell.GetInstanceId())
        {
            _shellId = shell.GetInstanceId();
            _sentRevision = -1;
            _sentGameplay = false;
        }
        int revision = (int)Revision.GetValue(shell)!;
        string phase = Phase.GetValue(shell)!.ToString()!;
        if (phase == "Lobby" && revision > 0 && (int)ReadyRevision.GetValue(shell)! == revision && _sentRevision != revision)
        {
            Send(shell, revision - 1, "stale_revision", revision);
            _sentRevision = revision;
        }
        if (phase == "Gameplay" && !_sentGameplay)
        {
            Send(shell, revision, "gameplay_phase", revision);
            _sentGameplay = true;
        }
    }

    private static void Send(OnlineGameplayShell shell, int revision, string kind, int current)
    {
        Error error = shell.RpcId(1, "ClientReadyForPhaseRpc", revision);
        GameLog.Info("sandbox.readiness", "invalid_ready_sent", fields: new Dictionary<string, string?>()
        {
            ["kind"] = kind, ["sent_revision"] = revision.ToString(),
            ["current_revision"] = current.ToString(), ["rpc_result"] = error.ToString(),
            ["local_peer"] = shell.Multiplayer.GetUniqueId().ToString()
        });
        if (error != Error.Ok) GD.PushError($"READINESS_PROBE_FAIL: {error}");
    }

    private static FieldInfo Field(string name) => typeof(OnlineGameplayShell)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
