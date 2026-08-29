using System.Threading.Tasks;
using Godot;
using GameFactory.Sandbox.Steam;

namespace GameFactory.Shell;

/// <summary>Temporary application-shell host flow around the retained Steam gameplay acceptance scene.</summary>
public partial class HostGameplayShell : Node
{
    private SteamGameplayProbe _probe = null!;

    public override async void _Ready()
    {
        _probe = GetNode<SteamGameplayProbe>("SteamGameplayProbe");
        GetNode<GameShell>("/root/GameShell").GameEntered();
        await _probe.HostGameAsync();
    }

    public Task LeaveGameAsync() => _probe.LeaveGameAsync();
}
