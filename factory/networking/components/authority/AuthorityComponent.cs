using GameFactory.Networking.Objects;

namespace GameFactory.Networking.Components.Authority;

public partial class AuthorityComponent
    : NetworkObjectComponent, INetworkAuthority
{
  public int AuthorityPeerId =>
      Host.GetMultiplayerAuthority();

  public bool HasAuthority =>
      Host.IsMultiplayerAuthority();
}