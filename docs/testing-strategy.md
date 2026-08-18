# Testing Strategy

## Current baseline

The repository has a separate xUnit project under `tests/GameFactory.Tests/`. Its engine-independent tests cover current `PeerId`, `NetworkPeer`, `PeerRegistry`, and `NetworkSession` behavior through a deterministic fake implementation of `INetworkTransport`.

The baseline tests do not launch Godot, create a scene tree, open sockets, or depend on timing. They do not cover `ENetTransport`, Godot node integration, replication, or cross-process behavior. There is no CI workflow, Godot integration-test harness, or structured scenario runner.

The two sandbox probes remain exploratory aids rather than regression tests. Earlier conversation context reports manual connection and replication experiments, but those reports are not fresh automated verification.

## Evidence layers

### 1. Engine-independent unit tests

Use ordinary .NET tests for code whose contract does not require a running Godot scene tree. Current coverage includes:

- `PeerId` validation, equality, and server semantics;
- `NetworkPeer` locality and role semantics;
- `PeerRegistry` idempotence, conflict detection, lookup, removal, clear behavior, and event ordering;
- `RuntimeContext` mode projections as observed through session operations;
- `NetworkSession` lifecycle, results, state events, failure reasons, cleanup, and invalid operations.

`FakeNetworkTransport` exposes deterministic operation results, local identity, operation tracking, and explicit event injection. It models only the existing transport contract and does not simulate ENet or network timing.

Priority session cases include:

- successful listen and dedicated hosting;
- host initialization failure;
- client initialization followed by connection confirmation;
- connection initialization failure and asynchronous connection failure;
- intentional leave while connecting and running;
- host shutdown while starting and running where reachable;
- remote client disconnect without server-session failure;
- client server-loss failure;
- peer add/remove/clear invariants;
- reset after failure;
- repeated or invalid lifecycle calls;
- reset and another start after failure.

These baseline tests characterize current reviewed behavior. Disposal and event unsubscription, explicit transport ownership, exception-safe cleanup, validated transitions, and stale-event hardening remain lifecycle-hardening work; this baseline does not add failing tests that presume those future contracts.

### 2. Godot integration tests

Use a real Godot runtime for contracts that depend on engine behavior:

- ENet adapter creation, event translation, peer assignment, close, and disposal;
- node lifecycle and fixed hierarchy assumptions in `NetworkObject`;
- reflection-to-`SceneReplicationConfig` mapping;
- exported-property validation and unsupported-property diagnostics;
- authority reporting and synchronizer root targeting;
- future automatic versus manual replication configuration.

Keep these tests narrower than full networking scenarios. Failures should indicate whether factory policy or Godot integration broke.

### 3. Multiprocess scenarios

Network behavior involving independent peers requires multiple Godot processes. A planned scenario runner should:

- launch explicit server, dedicated-server, and client roles;
- select the intended scene without relying on an editor's current state;
- allocate or configure ports safely;
- collect structured checkpoints rather than scrape incidental prose where possible;
- impose startup and completion timeouts;
- assert role-specific outcomes;
- terminate every child process on success or failure;
- retain useful logs and report the process that failed.

Initial scenarios should cover host/client connection, multiple peers, intentional leave, unexpected server loss, spawn/despawn, state changes, and late joining. The runner and checkpoint format do not exist yet, so this section does not define an API.

### 4. Manual exploratory probes

Keep sandbox probes for debugging timing, inspecting Godot signals, and trying new concepts before their contract stabilizes. A useful manual record includes:

- exact revision and local changes;
- Godot/runtime versions;
- selected scene and process arguments;
- role count and timing;
- expected and observed checkpoints;
- platform and relevant network conditions.

Manual success can motivate an automated case but cannot replace it.

## Replication test matrix

The replication abstraction should eventually cover:

- no annotations and one or multiple annotations;
- `Never`, `Always`, and `OnChange` mapping;
- spawn enabled and disabled;
- missing `[Export]` and other invalid declarations;
- inherited properties and duplicate/path conflicts;
- automatic configuration, explicit manual configuration, and raw replacement;
- host hierarchy and replication-root validation;
- authority and non-authority mutation paths;
- initial state, delta state, spawn, despawn, and late join;
- Node, Node2D, Node3D, and other supported host shapes once support is defined;
- metadata-cache reuse and invalidation expectations;
- structured diagnostic contents once their contract is accepted.

These are planned cases, not evidence that the current component supports every row.

## Test qualities

Tests should be:

- deterministic about inputs, event order, and timeouts;
- isolated from public internet services;
- explicit about the authority and process role under test;
- safe to repeat without leaked peers, handlers, or child processes;
- diagnostic enough to locate a failed layer;
- independent of local editor paths and ignored configuration;
- honest about skipped or unsupported environments.

## Continuous verification

No CI system is configured. When automation is added, it should begin with restore/build and engine-independent tests, then add Godot integration and bounded multiprocess scenarios where the execution environment supports them. A green local run must not be described as CI evidence.

## Exit criteria for a subsystem

A networking subsystem is ready to be treated as a supported framework capability only when:

- its contract and ownership are documented;
- expected failures and invalid operations are tested;
- relevant engine integration is verified;
- cross-process behavior has an executable scenario where applicable;
- cleanup is repeatable and checked;
- diagnostics make failures actionable;
- convention, configuration, and replacement paths are covered.
