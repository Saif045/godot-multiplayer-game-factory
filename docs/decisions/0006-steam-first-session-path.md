# ADR 0006: Use Steam/Godot as the First Online Session Path

- Status: Accepted
- Date: 2026-08-29
- Owners: Project maintainers
- Related: [Architecture](../architecture.md), [Steam integration](../steam-integration.md)

## Context

GameFactory had exploratory generic `NetworkSession`, `INetworkTransport`, and ENet adapter code alongside the real Steam/Godot listen-server path. The generic stack had no second concrete implementation and duplicated lifecycle ownership already required by Steam lobbies and Godot's `MultiplayerPeer`.

The Steam path has now been exercised by two real Steam accounts through hosting, joining, leaving, gameplay lifecycle, world replication, and diagnostics.

## Decision

Steam/Godot `MultiplayerPeer` is the accepted online session path. `SteamSession` owns lobby and peer lifecycle through `ISteamAdapter`; `GodotSteamAdapter` and its bridge isolate GodotSteam below gameplay.

Do not retain or introduce a generic transport or session-coordinator abstraction without a concrete second use that requires it. Gameplay remains on normal Godot multiplayer APIs plus `PeerRegistry`, `PlayerLifecycle`, `NetworkWorld`, and network-object components. Dedicated-server support remains future work justified by a real deployment need.

## Consequences

- The obsolete generic ENet transport and `NetworkSession` scaffolding are removed.
- Steam implementation details remain out of gameplay code.
- A future second online path must demonstrate why it cannot simply use the Godot gameplay layer and why a shared abstraction has real lifecycle semantics.
- `RuntimeMode.DedicatedServer` and existing player lifecycle policy remain; no dedicated Steam API is implied.

## Alternatives considered

### Preserve the generic transport stack

This would keep an unproven abstraction with one historical implementation and blur whether Steam or the generic session owns peer cleanup.

### Couple gameplay directly to GodotSteam

This would make platform replacement and isolated bridge maintenance harder, while providing no benefit to gameplay that only needs Godot multiplayer APIs.

## Validation and evidence

Manual two-account acceptance covered lobby and peer lifecycle, player lifecycle, dynamic spawn/despawn, late joining, authoritative door mutation, replicated revision acknowledgement, and distributed diagnostics. Unit tests continue to cover engine-independent policy and diagnostics. Automated Godot integration and multiprocess testing remain planned.

## Supersession

None.
