# Gameplay Networking Scope

Read this guide for gameplay code layered on the networking foundation. Keep a
feature's rules and state ownership explicit; do not introduce a generic
ability, command, event-bus, or progression framework for one slice.

Use this default choice:

| Need | Mechanism |
|---|---|
| Continuous latency-sensitive player simulation | Netfox rollback |
| Discrete request to server | Reliable RPC plus server validation |
| Ordinary authoritative state | `ReplicationComponent` / `MultiplayerSynchronizer` |
| Spawn/despawn | `NetworkWorld` / `MultiplayerSpawner` |

For interaction, a client chooses a target locally but sends only a
`NetworkObjectId`; the server resolves it through `NetworkWorld` and validates
the sender, represented player, target, range, and current interactability.
The client never authoritatively changes a target's state. Do not put switches,
doors, pickups, or inventory into player rollback history without concrete
evidence.

Presentation is deterministic/local where possible. Owner-distinguishing
colors derive from owner metadata, are consistent on all peers, and are never
replicated or rolled back.

Read `docs/architecture.md` for a cross-module ownership change and
`docs/current-state.md` before claiming a gameplay slice acceptance-proven.
