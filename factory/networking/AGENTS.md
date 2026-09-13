# Networking Scope

Read this guide for `NetworkObject`, `NetworkWorld`, registries, player
lifecycle, authority, spawning, or replicated-object work. For rollback-player
work, also read `netfox/AGENTS.md`; for Steam transport, route to
`../steam/AGENTS.md`.

- `NetworkWorld` server-allocates runtime object IDs, registers dynamic
  objects, and owns `MultiplayerSpawner` routing. Clients never authoritatively
  create players.
- `NetworkObject.OwnerPeerId` is represented-owner metadata. It does not grant
  Godot authority to the root player node or to the world.
- Keep root state and Simulation authority on server peer `1` for rollback
  players; only Input is owned by the represented peer.
- Resolve runtime targets through `NetworkWorld` by `NetworkObjectId`; do not
  make client-supplied NodePaths a gameplay authority boundary.
- Use `PeerId` only for the live Godot connection. Do not persist it as a
  player or Steam identity.
- `ReplicationComponent` owns ordinary `MultiplayerSynchronizer` setup from
  `[Replicated]` state. Use direct validated RPCs when that is the simpler
  boundary.

Before altering identity, authority, spawn/despawn, or replication composition,
read `docs/architecture.md` and the relevant module entry in
`docs/module-map.md`.
