namespace GameFactory.Networking.Components.Authority;

public interface INetworkAuthority
{
    int AuthorityPeerId { get; }

    bool HasAuthority { get; }
}