using GameFactory.Networking.Objects;
using GameFactory.Networking.Components.Authority;
using Godot;

public partial class ComponentCommunicationProbe
    : NetworkObjectComponent
{
  protected override void OnNetworkInitialize()
  {
    INetworkAuthority authority =
        NetworkObject.GetComponent<INetworkAuthority>();

    GD.Print(
        $"[component-probe] authority = {authority.AuthorityPeerId}");
  }
}