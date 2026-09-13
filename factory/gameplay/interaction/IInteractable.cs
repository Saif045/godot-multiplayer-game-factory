using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Interaction;

/// <summary>
/// The small server-side contract for an object a player can ask to use.
/// Implementations are invoked only after <see cref="PlayerInteractor"/>
/// has resolved and validated the request on the server.
/// </summary>
public interface IInteractable
{
    bool CanInteract(InteractionContext context);

    void Interact(InteractionContext context);
}

/// <summary>
/// Trusted data assembled by the server for one interaction request. It
/// deliberately contains no client-provided transform or target state.
/// </summary>
public readonly record struct InteractionContext(
    PeerId RequestingPeerId,
    NetworkObject Player,
    NetworkObject Target,
    Vector3 ServerPlayerPosition);
