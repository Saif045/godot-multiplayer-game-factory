using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Networking.Players;

public sealed class NetworkPlayer
{
    public PlayerId Id { get; }

    public PeerId PeerId { get; }

    public NetworkObjectId ObjectId { get; }

    public NetworkPlayer(
        PlayerId id,
        PeerId peerId,
        NetworkObjectId objectId)
    {
        Id = id;
        PeerId = peerId;
        ObjectId = objectId;
    }
}
