# GameFactory

GameFactory is a reusable Godot C# foundation for rapidly building small-session online co-op games. It is intended to make the recurring production work behind games in the general shape of *PEAK* cheaper to start and safer to evolve: player-hosted/listen-server and dedicated-server sessions, networked world lifecycle, and eventually the surrounding game shell.

It is not merely a networking framework, and it is not a custom game engine. GameFactory supplies reusable infrastructure and primitives while each game keeps its distinctive mechanics, rules, content, progression, balance, and art direction. Server-authoritative shared gameplay is the default direction; specialized behavior remains explicit and raw Godot APIs remain available.

The repository is an early foundation, not a production-ready framework. It has no platform-support, compatibility, release, or licensing guarantee.

## What exists today

- process roles, typed transient `PeerId`, peer registry, session lifecycle, and an ENet transport adapter;
- engine-independent xUnit coverage for the peer and session foundation through a deterministic test-only fake transport;
- a compositional `NetworkObject` host with replaceable authority and replication component scenes;
- `[Replicated]` metadata that configures a component-owned `MultiplayerSynchronizer` for host properties; and
- server-allocated `NetworkObjectId`, owner-peer metadata, and automatic `NetworkWorld` spawn routing;
- session-scoped `PlayerId`, player registry, and server-side player lifecycle orchestration; and
- manual connection and replication sandbox probes.

`NetworkSession` borrows its injected transport: it can close active session use, but its caller owns transport disposal. `NetworkObject` is not a universal gameplay base class. `NetworkObjectId` is runtime object identity, while `PlayerId` is session-scoped player identity rather than persistent account or Steam identity. `OwnerPeerId` identifies the peer a spawned object represents; it does not transfer Godot multiplayer authority from the server. `NetworkWorld` currently covers dynamic runtime spawning, including pre-tree shared `Variant` spawn data for opted-in gameplay roots and server-only off-tree configuration through `Spawn<T>`. Persistent/account identity, authored/static network objects, Steam integration, menus, settings, loading, Godot integration tests, multiprocess scenarios, CI, packaging, and an application shell remain planned.

The replication sandbox has been manually exercised for normal state change and late joining. That is exploratory evidence, not automated Godot test coverage.

## Current configuration

The project uses Godot .NET SDK 4.7.1 and .NET 8, with conditional .NET 9 for Android. The configured main scene is `res://sandbox/connection/network_probe.tscn`. Both probes recognize `--server`, `--dedicated-server`, and `--client`; the replication probe is not the configured main scene and no checked-in multiprocess runner prescribes a command line. These probes are foundation evidence, not a complete co-op game flow.

## Design philosophy

1. **Convention** for common, low-boilerplate behavior.
2. **Explicit configuration** for recognized variations.
3. **Replacement or raw Godot access** for advanced behavior.

The project prefers composition over a required gameplay hierarchy, keeps peer/player/object identities separate, treats server-authoritative shared state as the default direction, and keeps game-specific mechanics outside the factory.

For recurring, already-solved problems, the project investigates existing Godot libraries, plugins, templates, and open-source projects before building a new subsystem. The outcome should be a deliberate choice to use, adapt/learn from, or build. Dependencies are worthwhile when they save substantial work, are compatible and maintained, fit the architecture, and preserve a reasonable replacement path; they are not introduced merely to avoid trivial code. Steam is a planned first-class online/platform target and will follow this evaluation process rather than reimplementing Steam APIs.

Development is driven by coherent, playable co-op slices rather than speculative framework layers. For example, player lifecycle should be proved as a complete join, spawn, ownership, disconnect, and cleanup flow across listen and dedicated hosting before adjacent abstractions are generalized.

## Repository map

- `factory/runtime/` — process role and runtime context.
- `factory/networking/peers/` — peer value and peer registry.
- `factory/networking/sessions/` — session lifecycle policy.
- `factory/networking/transport/` — transport contract and ENet adapter.
- `factory/networking/objects/` — generic component host and default object components.
- `factory/networking/world/` — runtime object registry and automatic spawn routing.
- `sandbox/` — manual connection and replication probes.
- `tests/GameFactory.Tests/` — engine-independent xUnit tests and fake transport.
- `docs/` — architecture and engineering records.

Directories, scenes, resources, and assets use lowercase or snake_case. C# namespaces, types, and filenames use PascalCase.

## Documentation

- [Project charter](docs/project-charter.md)
- [Architecture](docs/architecture.md)
- [Terminology](docs/terminology.md)
- [Module map](docs/module-map.md)
- [Coding standards](docs/coding-standards.md)
- [Testing strategy](docs/testing-strategy.md)
- [Architecture decision records](docs/decisions/README.md)
- [Networking foundation](docs/networking-foundation.md)
- [Agent working guidance](AGENTS.md)
