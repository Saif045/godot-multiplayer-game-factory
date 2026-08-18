# Terminology

Terms are marked **implemented** when represented in current source, **conceptual** when they guide architecture but lack a current type, and **planned** when they describe future work.

## Runtime and lifecycle

**Runtime mode** — **Implemented.** The role of the current process, represented by `RuntimeMode`: `Offline`, `Client`, `ListenServer`, or `DedicatedServer`.

**Listen server** — **Implemented role.** A process that is authoritative and also client-capable. This does not by itself provide a complete local-player system.

**Dedicated server** — **Implemented role.** An authoritative process with no implied local player. Headless packaging, hosting operations, and a dedicated application shell are not implemented.

**Session** — **Implemented.** The coordinated lifecycle spanning transport state, runtime role, and the local peer registry, currently owned by `NetworkSession`.

**Session state** — **Implemented.** One of `Offline`, `Starting`, `Connecting`, `Running`, `Stopping`, or `Failed`.

**Session end reason** — **Implemented.** A coarse reason retained after an intentional end or failure: local leave, host shutdown, host-start failure, connection failure, or server disconnection.

**Transport** — **Implemented boundary.** The mechanism that starts a server, connects a client, exposes the local peer ID, closes, and reports connection facts. `ENetTransport` is the only current adapter.

## Identity and authority

**Peer ID** — **Implemented.** A positive, transient transport identifier represented by `PeerId`. In the current Godot model, value `1` denotes the server.

**Peer** — **Implemented.** A process-visible connection represented by `NetworkPeer`, combining a peer ID with whether it is local.

**Local peer** — **Implemented property.** The peer representing the current process. Locality does not imply server status.

**Server peer** — **Implemented property.** The peer whose ID is `1`. Server status does not imply locality; it is remote to an ordinary client.

**Player ID** — **Conceptual, not implemented.** A persistent identity for a player across transient connections. It must not be substituted with `PeerId`.

**Runtime network-object identity** — **Conceptual, not implemented.** Stable identity for a replicated object instance. The current `NetworkObject` does not supply such an identifier.

**Multiplayer authority** — **Godot capability used today.** The peer permitted to act as authority for a node. `NetworkObject` reports the host node's Godot authority; it does not define a separate authority system.

**Server-authoritative state** — **Architectural direction with one probe example.** Shared state is intended to be changed by server authority by default. The `RawDoor` probe sends a request to peer `1` and changes its property on the authority. The factory does not yet enforce this policy generally.

## Objects and replication

**Network object** — **Experimental implementation.** A compositional child node that reflects annotated properties on its parent and configures a Godot `MultiplayerSynchronizer`. It is not a universal base type or a stable network identity.

**Host node** — **Implemented relation.** In `NetworkObject`, the direct parent gameplay node whose properties and multiplayer authority are used. This term does not mean network server.

**Replication** — The distribution of shared object state. The current implementation configures Godot replication for annotated exported properties.

**Spawn replication** — Godot synchronization of a property's initial value when an object is spawned, controlled per current annotation by `Spawn`. This is distinct from replicating the object's existence.

**Replicated existence** — **Demonstrated through raw Godot sandbox configuration, not generalized by the factory.** `MultiplayerSpawner` creates and removes the sample door across peers.

**Spawn definition** — **Planned concept.** A stable description used to create a known runtime object. No factory registry or stable ID exists yet.

**Network world** — **Planned concept.** A future coordination layer for spawn definitions, runtime creation/removal, joining peers, and cleanup. Its API is not defined.

## Project structure and evidence

**Factory** — Reusable infrastructure under `factory/`. It is intended for use by multiple games and must avoid game-specific policy.

**Sandbox** — Exploratory scenes and scripts under `sandbox/` used to exercise current behavior. A sandbox probe is not an automated test or a compatibility guarantee.

**Probe** — A manually launched executable example that logs events and permits direct interaction. Current probes do not make automated assertions.

**Unit test** — **Planned.** An automated, engine-independent check of a small contract. No test project currently exists.

**Integration test** — **Planned.** An automated check involving Godot or an adapter boundary.

**Multiprocess scenario** — **Planned.** An automated orchestration of multiple Godot roles with structured checkpoints and cleanup. The repository has no scenario runner today.

**Convention** — A safe, low-boilerplate default.

**Explicit configuration** — A visible choice for supported non-default behavior.

**Full replacement or raw access** — A way to substitute a factory layer or use the underlying Godot capability directly.
