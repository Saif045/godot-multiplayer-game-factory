using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameFactory.Diagnostics;

namespace GameFactory.Sandbox.Launcher;

/// <summary>
/// Development-only main scene that selects a packed sandbox by a stable command-line name.
/// Exported Godot binaries intentionally do not permit arbitrary --scene path overrides.
/// </summary>
public partial class SandboxLauncher : Node
{
    private const string DefaultTarget = "steam-gameplay";

    private static readonly IReadOnlyDictionary<string, string> ScenePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["steam"] = "res://sandbox/steam/steam_probe.tscn",
            ["steam-gameplay"] = "res://sandbox/steam/steam_gameplay_probe.tscn",
            ["netfox"] = "res://sandbox/netfox/netfox_time_probe.tscn",
            ["netfox-gameplay"] = "res://sandbox/netfox/netfox_gameplay_probe.tscn"
        };

    public override void _Ready()
    {
        GameLog.EnsureInitialized();
        BuildIdentity.LogCurrent();
        string target = ReadTarget(
            OS.GetCmdlineArgs()
                .Concat(OS.GetCmdlineUserArgs()));
        if (!ScenePaths.TryGetValue(target, out string? scenePath))
        {
            GD.PushError(
                $"[launcher] Unknown --run target '{target}'. " +
                $"Available targets: {string.Join(", ", ScenePaths.Keys)}.");
            GetTree().Quit(2);
            return;
        }

        GD.Print($"[launcher] --run={target} -> {scenePath}");
        GetTree().CallDeferred(
            SceneTree.MethodName.ChangeSceneToFile,
            scenePath);
    }

    private static string ReadTarget(IEnumerable<string> arguments)
    {
        const string prefix = "--run=";
        string? argument = arguments.FirstOrDefault(
            value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return argument is null
            ? DefaultTarget
            : argument[prefix.Length..];
    }
}
