# ADR 0004: Keep Peer, Player, and Network-Object Identities Separate

- Status: Accepted
- Date: 2026-08-18
- Owners: Project maintainers
- Related: [Project charter](../project-charter.md), [Terminology](../terminology.md), [Architecture](../architecture.md)

## Context

Multiplayer systems refer to several entities whose lifetimes differ. A transport peer identifies one live connection. A player may need continuity across reconnects or sessions. A runtime network object needs identity for creation, lookup, replication, and removal independent of either a connection or a player.

At acceptance, only `PeerId` was implemented. It validates a positive transport identifier and treats value `1` as the server in the current Godot model. `NetworkPeer` adds process-local connection information. The implementation update records the later runtime-object and dynamic-world work without treating it as a persistent player or prefab-definition contract.

## Decision

GameFactory will model transport peer identity, persistent player identity, and runtime network-object identity as separate concepts with separate lifetimes and contracts.

Code must not use `PeerId` as a durable player or object identifier. Associations between identities must be explicit at the subsystem that owns the relationship. Persistent player identity, stable prefab-definition identity, and persistence rules remain future design work. The current dynamic `NetworkObjectId` implementation is recorded below.

## Rationale

Keeping identities separate prevents transient connection details from leaking into persistence and world state. It supports reconnects, multiple players or controlled objects per process, ownership transfer, dedicated servers, and object lifetimes that outlast a particular peer without prematurely defining those systems.

## Alternatives considered

### Use peer ID as player ID

This is simple during one connection, but peer IDs are transport-assigned and transient. Reconnection or a changed transport can produce a different peer ID for the same player.

### Use peer ID as network-object ID

A peer can own many objects, and objects can be server-owned, shared, or transferred. Equating these identities cannot represent those relationships safely.

### Create one universal identifier type

A universal value shape might reduce type count but would permit accidental interchange and obscure different allocation, lifetime, and persistence rules.

### Define all future identifier APIs now

Predefining player and object identity contracts could appear comprehensive, but the repository lacks authentication, persistence, reconnect, and generalized spawning evidence needed to choose them responsibly.

## Consequences

### Positive

- Type and subsystem boundaries can prevent accidental identity substitution.
- Future reconnect, ownership, and spawning work can evolve independently.
- Logs and diagnostics can state which identity domain a value belongs to.

### Negative

- Systems that associate a peer, player, and controlled object require explicit mappings.
- More concepts and conversions must be documented and tested.
- Future persistence and allocation decisions remain necessary.

### Neutral or operational

- `PeerId` and the experimental dynamic `NetworkObjectId` are implemented identity types with distinct lifetimes.
- This decision does not promise reconnect support, persistent players, stable prefab definitions, or persistence behavior.

## Validation and evidence

Current source validates peer identity and the positive-value boundary for `PlayerId` and `NetworkObjectId`. Engine-independent tests cover `PeerId`, `NetworkPeer`, `PeerRegistry`, `PlayerId`, `PlayerRegistry`, `PlayerLifecycle`, and `NetworkObjectId`; persistent player or account identity has no implementation. Automated Godot and multiprocess evidence for dynamic object registration, spawning, player lifecycle, and late joining does not exist. Future identity types should be tested for domain separation, lifetime, equality, invalid values, mapping cleanup, and relevant reconnect or late-join scenarios.

## Compatibility and migration

There is no current player or network-object identity API to migrate. Existing code that uses raw or typed peer IDs should continue to treat them as transport-scoped. The project has no defined compatibility policy.

## Open questions

- Player identity authority, persistence, and authentication are undecided.
- Persistent or cross-world network-object identity and retention are undecided; the current dynamic world allocates positive IDs on the server.
- Spawn-definition identity is related to creation metadata but must not be assumed to be the same as runtime object identity.

## Follow-up work

- Preserve peer-only semantics while adding baseline tests for `PeerId`, `NetworkPeer`, and `PeerRegistry`.
- Design persistent player identity only with concrete reconnect or persistence requirements.
- Evolve runtime-object and spawn-definition identities only when concrete persistence, cross-world, or prefab-definition requirements appear.

## Supersession

None.

## Implementation update (2026-08-19)

`NetworkObjectId` now provides a positive, server-allocated runtime identity for dynamic objects in `NetworkWorld`. It remains separate from `PeerId` and does not establish persistent player identity, reconnect identity, or stable spawn-definition identity. Dynamic spawning uses Godot resource UIDs as the prefab identifier; that local-project mechanism is not an accepted stable cross-project prefab-definition design.

## Implementation update (2026-08-24)

`PlayerId` now provides positive session-scoped player identity. `PlayerLifecycle` associates it explicitly with a transient `PeerId` and a dynamic `NetworkObjectId` through `NetworkPlayer`; it does not establish account, Steam, reconnect, or persistence identity. `NetworkObject.OwnerPeerId` identifies the peer represented by a spawned object and remains separate from Godot multiplayer authority, which stays server-owned.
