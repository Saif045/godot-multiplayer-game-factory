using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameFactory.Sandbox.Launcher;

/// <summary>
/// Development-only main scene that selects a packed sandbox by a stable command-line name.
/// Exported Godot binaries intentionally do not permit arbitrary --scene path overrides.
/// </summary>
public partial class SandboxLauncher : Node
{
    private const string DefaultTarget = "connection";

    private static readonly IReadOnlyDictionary<string, string> ScenePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["connection"] = "res://sandbox/connection/network_probe.tscn",
            ["replication"] = "res://sandbox/replication/replication_probe.tscn",
            ["steam"] = "res://sandbox/steam/steam_probe.tscn"
        };

    public override void _Ready()
    {
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
