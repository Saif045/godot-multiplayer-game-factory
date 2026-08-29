using System;
using Godot;
using GameFactory.Steam.Adapters.GodotSteam;

namespace GameFactory.Steam;

/// <summary>
/// Process-lifetime owner of the GodotSteam adapter. Gameplay sessions may come
/// and go with scenes, but Steam itself is initialized once per application.
/// </summary>
public partial class SteamPlatform : Node
{
    private GodotSteamAdapter? _adapter;

    public GodotSteamAdapter Adapter => _adapter
        ?? throw new InvalidOperationException("SteamPlatform is not ready.");

    public override void _Ready()
    {
        _adapter = GodotSteamAdapter.Create(this);
    }

    public override void _ExitTree()
    {
        _adapter?.Dispose();
        _adapter = null;
    }
}
