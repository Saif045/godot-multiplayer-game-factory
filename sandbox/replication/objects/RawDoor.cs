using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Sandbox.Replication;

using GameFactory.Networking.Components.Authority;
using GameFactory.Networking.Components.Replication;


public partial class RawDoor : Node3D
{

    [Replicated(
        ReplicationMode.OnChange,
        Spawn = true)]
    public bool IsOpen { get; set; }

    private NetworkObject _network = null!;
    private INetworkReplication _replication = null!;
    private INetworkAuthority _authority = null!;

    public override void _UnhandledInput(
        InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey key)
            return;

        if (!key.Pressed || key.Echo)
            return;

        if (key.Keycode != Key.E)
            return;

        if (!Multiplayer.HasMultiplayerPeer())
        {
            GD.Print(
                "[door] no multiplayer peer available");

            return;
        }

        // For B1 we only test:
        //
        // client -> server
        //
        // The host-side player case comes later.
        if (_authority.HasAuthority)
        {
            GD.Print(
                "[door][server] E pressed locally; " +
                "B1 only tests remote client requests.");

            return;
        }

        GD.Print(
            $"[door][client] requesting open; " +
            $"local IsOpen = {IsOpen}");

        RpcId(
            1,
            MethodName.RequestOpen);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode =
            MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestOpen()
    {
        int senderId = Multiplayer.GetRemoteSenderId();

        GD.Print(
            $"[door][server] RequestOpen received " +
            $"from peer {senderId}");

        if (!_authority.HasAuthority)
        {
            GD.PushWarning(
                "[door] RequestOpen executed " +
                "on a non-server peer");

            return;
        }

        if (IsOpen)
        {
            GD.Print(
                "[door][server] door already open");

            return;
        }

        Open();
    }

    private void Open()
    {
        IsOpen = true;

        GD.Print(
            $"[door][server] door opened; " +
            $"IsOpen = {IsOpen}");
    }

    public override void _Ready()
    {
        _network = GetNode<NetworkObject>("NetworkObject");

        _replication =
            _network.GetComponent<INetworkReplication>();

        _authority =
            _network.GetComponent<INetworkAuthority>();



        GD.Print(
            $"[door][network] authority peer = {_authority.AuthorityPeerId}, " +
            $"has authority = {_authority.HasAuthority}");

        _replication.Synchronized += () =>
        {
            GD.Print(
                $"[door][sync] full sync received; " +
                $"IsOpen = {IsOpen}");
        };

        _replication.DeltaSynchronized += () =>
        {
            GD.Print(
                $"[door][sync] delta received; " +
                $"IsOpen = {IsOpen}");
        };

        GD.Print(
            $"[door] created on peer " +
            $"{Multiplayer.GetUniqueId()}");
    }
}
