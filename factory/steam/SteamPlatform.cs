using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Steam.Adapters.GodotSteam;

namespace GameFactory.Steam;

/// <summary>
/// Process-lifetime owner of the GodotSteam adapter. Gameplay sessions may come
/// and go with scenes, but Steam itself is initialized once per application.
/// </summary>
public partial class SteamPlatform : Node
{
    private GodotSteamAdapter? _adapter;
    private Task? _readyTask;
    private bool? _lastOverlayAvailable;
    private double _overlayPollSeconds;

    public GodotSteamAdapter Adapter => _adapter
        ?? throw new InvalidOperationException("SteamPlatform is not ready.");
    public Task ReadyTask => _readyTask ?? throw new InvalidOperationException("SteamPlatform has not entered the tree.");

    public override void _Ready()
    {
        SteamLaunchOptions options = SteamLaunchOptions.Development;
        GameLog.Info("steam.launch", "policy", fields: new Dictionary<string, string?>
        {
            ["configured_app_id"] = options.AppId.ToString(),
            ["launch_policy"] = options.Policy,
            ["development_mode"] = options.DevelopmentMode ? "true" : "false",
            ["enforce_steam_launch"] = options.EnforceSteamLaunch ? "true" : "false",
            ["steam_appid_file_present"] = File.Exists("steam_appid.txt") ? "true" : "false",
            ["steam_appid_beside_executable"] = File.Exists(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "steam_appid.txt")) ? "true" : "false"
        });
        _adapter = GodotSteamAdapter.Create(this);
        _readyTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Adapter.InitializeAsync();
            _lastOverlayAvailable = Adapter.IsOverlayAvailable;
            GameLog.Info("steam.platform", "ready", fields: new Dictionary<string, string?>
            {
                ["steam_initialized"] = "true",
                ["app_id"] = Adapter.InitializedAppId.ToString(),
                ["steam_user_id"] = Adapter.LocalUser.Id.ToString(),
                ["overlay_enabled"] = _lastOverlayAvailable.Value ? "true" : "false"
            });
        }
        catch (Exception exception)
        {
            GameLog.Error("steam.platform", "initialization_failed", exception.Message,
                new Dictionary<string, string?> { ["steam_initialized"] = "false" });
            throw;
        }
    }

    public override void _Process(double delta)
    {
        if (_adapter is not { IsInitialized: true }) return;
        _overlayPollSeconds += delta;
        if (_overlayPollSeconds < 1) return;
        _overlayPollSeconds = 0;
        bool available = _adapter.IsOverlayAvailable;
        if (_lastOverlayAvailable == available) return;
        _lastOverlayAvailable = available;
        GameLog.Info("steam.overlay", "availability_changed", fields: new Dictionary<string, string?>
        {
            ["overlay_enabled"] = available ? "true" : "false"
        });
    }

    public override void _ExitTree()
    {
        _adapter?.Dispose();
        _adapter = null;
    }
}
