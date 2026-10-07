# Netfox Scope

Read this guide and `docs/netfox-integration.md` before changing Netfox or a
rollback-player composition. Netfox is pinned to source commit
`38f59778b02bfd1a3dedc7dcc985945d7058858d` (reports 1.49.3); do not blindly update
upstream or introduce a parallel rollback/reconciliation system.

The upgrade and round reuse have accepted Steam A/B and focused guard/role
evidence. See `docs/session-reuse-acceptance.md` for the tested contract and
`docs/netfox-compatibility-spike.md` for provenance and local regression evidence.

- Steam is transport below Godot `MultiplayerAPI`; Netfox owns time sync,
  history, prediction, rollback/resimulation, and interpolation.
- Configure root and child authority before Netfox child initialization.
  `NetfoxRollbackPlayerComponent` owns only lifecycle/root split-authority
  glue; keep Netfox property lists visible on the prefab.
- Do not call `process_settings()` during normal player initialization.
- Every value affecting `_rollback_tick()` must be synchronized or derived
  deterministically from synchronized data. `is_fresh == false` is
  resimulation, not automatically a correction.
- Keep history at 64 and `enable_input_broadcast=false` unless evidence proves
  another requirement. Use `NetworkTime.physics_factor` only around
  `move_and_slide()`.
- Queue edge-triggered inputs such as jump. Keep cameras, cosmetic colors, and
  presentation outside rollback state.
- Use Netfox for continuous latency-sensitive simulation. Discrete commands
  and state (switches, doors, inventory) use reliable RPC, server validation,
  and ordinary replication instead.

For a runtime issue, first classify it against the transport/lifecycle layers;
do not diagnose Netfox from an attempt that never reached Godot connection.
