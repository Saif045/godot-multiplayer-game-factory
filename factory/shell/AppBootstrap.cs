using System;
using System.Linq;
using Godot;

namespace GameFactory.Shell;

/// <summary>Selects the normal application shell or an explicitly requested development probe.</summary>
public partial class AppBootstrap : Node
{
    private const string MainMenuScenePath = "res://factory/shell/main_menu.tscn";
    private const string SandboxLauncherScenePath = "res://sandbox/launcher/sandbox_launcher.tscn";

    public override void _Ready()
    {
        string[] arguments = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
        bool hasRunTarget = arguments.Any(argument => argument.StartsWith("--run=", StringComparison.OrdinalIgnoreCase));

        int connectIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "+connect_lobby", StringComparison.OrdinalIgnoreCase));
        if (!hasRunTarget && connectIndex >= 0 && connectIndex + 1 < arguments.Length)
        {
            string lobbyId = arguments[connectIndex + 1];
            GetNode<GameShell>("/root/GameShell").CallDeferred(nameof(GameShell.JoinLobby), lobbyId);
            return;
        }

        GetTree().CallDeferred(
            SceneTree.MethodName.ChangeSceneToFile,
            hasRunTarget ? SandboxLauncherScenePath : MainMenuScenePath);
    }
}
