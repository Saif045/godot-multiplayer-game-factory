# Testing Strategy

## Current automated baseline

`tests/GameFactory.Tests/` is a separate xUnit project. Its engine-independent tests cover peer/player value types and registries, `PlayerLifecycle` role policy, `NetworkObjectId`, spawn-group values, diagnostics records and writers, replication-confirmation tracking, and distributed-log batching/sequence handling. They do not launch Godot, create a scene tree, open a Steam connection, or validate GodotSteam.

There is no CI workflow or generic Godot integration-test framework. `tools/ab_test/run.ps1` is a concrete two-account Steam scenario runner. It creates one clean, configurable export; records every runtime file in `build_manifest.json`; verifies the configured VirtualBox share and every manifest hash from the VM's interactive scheduled-task session before Steam starts; then hosts on the PC, discovers the current lobby ID from structured diagnostics, and launches the VM client. It is deliberately a laboratory harness for the existing Steam gameplay slice, not a generic multiplayer test framework or a substitute for CI.

The Maaack shell has a headless Godot startup smoke for addon/script/autoload/scene-load failures. Visual UX remains manual acceptance: menu focus/navigation, settings persistence, keyboard/mouse/controller remapping and reset persistence, loading transition, pause/resume, first-click leave-to-menu, and host/leave/re-host without a Steam reinitialization must be checked in a normal exported run. Maaack's physical-key display support requires a normal display server, so loading its Controls scene headlessly can emit expected display-server warnings while input labels are formatted.

## Evidence layers

1. **Engine-independent unit tests** cover deterministic values, policy, and diagnostic logic.
2. **Manual Steam dependency smoke** verifies vendored `SteamMultiplayerPeer` can host, close, and host again in one process.
3. **Automated two-account Steam A/B acceptance** runs the existing `steam_basic` scenario across a host PC and a VM: lobby creation/join, Godot connection, player/world lifecycle, server-authoritative door mutation, replicated revision acknowledgement, diagnostics relay, and cleanup. It requires real logged-in Steam accounts and the documented VM control prerequisites.
4. **Manual two-account Steam gameplay acceptance** remains useful for invite overlays, UX, arbitrary join/leave exploration, and conditions not represented by a deterministic scenario.
5. **Future Godot integration and multiprocess scenarios** should add focused coverage when another repeatable contract justifies it; the A/B harness is the starting execution path, not a claim that all engine behavior is covered.

Diagnostics writes `game.jsonl` and `engine.log` per run. An authoritative session additionally materializes `session.log`, `master.jsonl`, and `manifest.json`; focused unit tests cover their pure/file-layout behavior. `NetworkLogRelay` is bounded best-effort telemetry, not durable offline upload.

The A/B harness creates a unique `--test-run-id`, stores harness artifacts under `artifacts/ab_tests/<test-run-id>/`, and writes `result.json` with build identity, timing, cleanup evidence, `result`, `layer`, `stage`, and `reason`. It gives both Godot processes an explicit artifact-owned `--log-file`, avoiding the engine's default user-data log path during automated execution. Its explicit checkpoints are lobby creation, lobby membership, peer creation, assignment to Godot's `MultiplayerApi`, native peer handshake, Godot connection signals, GameFactory lifecycle, and replication. Per-process `game.jsonl` and `engine.log`, host and VM Godot logs, the host-collected session, the build manifest, VM parity result, and a focused native-term extract are retained for each attempt. Its test-only `steam_basic` scenario is activated only by `--test-scenario=steam_basic`; normal and manual probe runs do not execute it.

`tools/ab_test/run_suite.ps1` is the repeated-reliability mode. It exports once (unless `-SkipExport` is explicitly requested), records one immutable manifest, and runs independent clean attempts beneath `artifacts/ab_suites/<suite-id>/attempts/`. The first attempt proves VM parity. Later attempts reuse that proof only after verifying the same host manifest hash, while retaining normal per-attempt preflight, runtime assertions, artifact capture, teardown, and cleanup verification. `summary.json` records the build identity, pass/fail totals, failed-stage distribution, connection-time samples, and every attempt artifact path. A suite never retries a failed attempt in place.

New coverage should follow coherent playable slices rather than speculative helpers. Tests must be deterministic, explicit about authority and runtime role, and honest about external environment requirements.
