namespace GameFactory.Networking.Objects.Components.Authority;

public interface INetworkAuthority
{
    int AuthorityPeerId { get; }

    bool HasAuthority { get; }
}
