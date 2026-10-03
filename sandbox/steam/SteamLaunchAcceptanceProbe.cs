using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Shell;
using GameFactory.Steam;

namespace GameFactory.Sandbox.Steam;

/// <summary>Bounded single-account check of the actual menu/lobby UI contract.</summary>
public partial class SteamLaunchAcceptanceProbe : Node
{
    private int _overlayToggles;

    public override void _Ready() => CallDeferred(nameof(Run));

    public async void Run()
    {
        // Keep this test driver as a root sibling while exercising normal scenes.
        GetTree().CurrentScene = null;
        SteamPlatform platform = GetNode<SteamPlatform>("/root/SteamPlatform");
        platform.Adapter.OverlayActivityChanged += CountOverlayToggle;
        OnlineGameplayShell? online = null;
        bool passed = false;
        try
        {
            await platform.ReadyTask;
            // Sample after the documented overlay attachment delay, and stay
            // alive beyond the scheduled host runner's three-second startup check.
            await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
            GameShell shell = GetNode<GameShell>("/root/GameShell");
            GetTree().ChangeSceneToFile(GameShell.MainMenuScenePath);
            await WaitUntil(() => Button("Join Game") is not null);
            Button("Join Game")!.EmitSignal(Godot.Button.SignalName.Pressed);
            await WaitUntil(() => GetTree().CurrentScene?.SceneFilePath == GameShell.JoinMenuScenePath);
            // Allow friend discovery callbacks; no second account/join is claimed.
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            GetTree().ChangeSceneToFile(GameShell.MainMenuScenePath);
            await WaitUntil(() => Button("Host Game") is not null);
            Button("Host Game")!.EmitSignal(Godot.Button.SignalName.Pressed);
            await WaitUntil(() => Button("Invite Friends") is not null);
            online = GetTree().CurrentScene as OnlineGameplayShell
                ?? throw new InvalidOperationException("Normal online shell was not loaded.");
            if (!platform.Adapter.IsInitialized || platform.Adapter.CurrentLobby is null ||
                !platform.Adapter.IsLobbyOwner || GetTree().GetMultiplayer().MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
                throw new InvalidOperationException("Steam initialization/lobby/host-peer assertion failed.");
            bool overlay = platform.Adapter.IsOverlayAvailable;
            if (!overlay)
            {
                Button("Invite Friends")!.EmitSignal(Godot.Button.SignalName.Pressed);
                if (!Descendants(GetTree().CurrentScene).OfType<Label>().Any(label => label.Text == SteamInviteFeedback.OverlayUnavailable))
                    throw new InvalidOperationException("Invite fallback label was not displayed.");
                if (platform.Adapter.CurrentLobby is null || Button("Leave") is null)
                    throw new InvalidOperationException("Invite fallback left the Lobby.");
            }
            GameLog.Info("steam.launch.acceptance", "observed", fields: new Dictionary<string, string?>
            {
                ["configured_app_id"] = platform.Adapter.LaunchOptions.AppId.ToString(),
                ["steam_initialized"] = platform.Adapter.IsInitialized ? "true" : "false",
                ["steam_user_id"] = platform.Adapter.LocalUser.Id.ToString(),
                ["overlay_enabled"] = overlay ? "true" : "false",
                ["overlay_toggled_callbacks"] = _overlayToggles.ToString(),
                ["main_menu"] = "passed", ["join_menu"] = "passed", ["host_lobby"] = "passed",
                ["invite_fallback"] = overlay ? "not_applicable" : "passed"
            });
            await online.LeaveGameAsync();
            online = null;
            if (platform.Adapter.CurrentLobby is not null)
                throw new InvalidOperationException("Leave did not clear lobby state.");
            passed = true;
        }
        catch (Exception exception)
        {
            GameLog.Error("steam.launch.acceptance", "failed", exception.Message);
        }
        finally
        {
            if (online is not null)
            {
                try { await online.LeaveGameAsync(); }
                catch (Exception exception) { GameLog.Error("steam.launch.acceptance", "cleanup_failed", exception.Message); passed = false; }
            }
            platform.Adapter.OverlayActivityChanged -= CountOverlayToggle;
            GameLog.Info("steam.launch.acceptance", passed ? "passed" : "failed_terminal");
            GetTree().Quit(passed ? 0 : 1);
        }
    }

    private void CountOverlayToggle(bool active) => _overlayToggles++;

    private Button? Button(string text) => Descendants(GetTree().CurrentScene).OfType<Button>().FirstOrDefault(button => button.Text == text);

    private static IEnumerable<Node> Descendants(Node? parent)
    {
        if (parent is null) yield break;
        foreach (Node child in parent.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        ulong deadline = Time.GetTicksMsec() + 15000;
        while (!condition())
        {
            if (Time.GetTicksMsec() >= deadline) throw new TimeoutException("Menu/lobby readiness exceeded 15 seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
