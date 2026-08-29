using Godot;
using GameFactory.Diagnostics;

namespace GameFactory.Shell;

/// <summary>Thin application-flow bridge; SteamSession remains owned by the active gameplay scene.</summary>
public partial class GameShell : Node
{
    public const string MainMenuScenePath = "res://factory/shell/main_menu.tscn";

    public override void _Ready()
    {
        GameLog.EnsureInitialized();
        GameLog.Info("shell", "ready");
    }

    public void GameStartRequested() => GameLog.Info("shell", "game_start_requested");
    public void GameEntered() => GameLog.Info("shell", "game_entered");
    public void PauseOpened() => GameLog.Info("shell", "pause_opened");
    public void PauseClosed() => GameLog.Info("shell", "pause_closed");

    public async void LeaveGame()
    {
        GameLog.Info("shell", "leave_requested");
        if (GetTree().CurrentScene is HostGameplayShell gameplay)
            await gameplay.LeaveGameAsync();

        GetNode("/root/SceneLoader").Call("load_scene", MainMenuScenePath);
        GameLog.Info("shell", "returned_to_menu");
    }
}
