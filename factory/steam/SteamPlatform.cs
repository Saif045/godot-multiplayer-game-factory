using System;
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

    public GodotSteamAdapter Adapter => _adapter
        ?? throw new InvalidOperationException("SteamPlatform is not ready.");
    public Task ReadyTask => _readyTask ?? throw new InvalidOperationException("SteamPlatform has not entered the tree.");

    public override void _Ready()
    {
        _adapter = GodotSteamAdapter.Create(this);
        _readyTask = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Adapter.InitializeAsync();
            GameLog.Info("steam.platform", "ready");
        }
        catch (Exception exception)
        {
            GameLog.Error("steam.platform", "initialization_failed", exception.Message);
            throw;
        }
    }

    public override void _ExitTree()
    {
        _adapter?.Dispose();
        _adapter = null;
    }
}
