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
        bool hasRunTarget = OS.GetCmdlineArgs()
            .Concat(OS.GetCmdlineUserArgs())
            .Any(argument => argument.StartsWith("--run=", StringComparison.OrdinalIgnoreCase));

        GetTree().CallDeferred(
            SceneTree.MethodName.ChangeSceneToFile,
            hasRunTarget ? SandboxLauncherScenePath : MainMenuScenePath);
    }
}
