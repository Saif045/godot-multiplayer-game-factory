# A/B Harness and Hyper-V Scope

Read this guide with `docs/testing-protocol.md` and
`docs/runtime-test-operator.md` before running or changing the host/VM A/B
harness. For a terminal-test investigation, also read
`docs/investigation-protocol.md`.

The path is host PC <-> SSH/SCP <-> GPU-P Hyper-V Windows guest. Immutable
releases go to `C:\GameFactoryBuilds\releases\<manifest-hash>`; the guest's
interactive scheduled task launches the game in its logged-in Steam/desktop
session. SSH/SCP is control and transfer only, not an interactive graphics or
Steam session. Do not rely on a shared-folder contract.

The universal interface is `run.ps1 -Mode Health|Launch|Verify|Retry|Stop`. It is
agent-independent: the same commands work from a human PowerShell terminal,
Codex, Qwen/Aider, or another future operator. Do not add agent-specific
behavior to this harness or this guide.

Before each attempt record build ID/manifest, host/guest parity, accounts and
session state, artifact path, and test-owned process IDs. During a frozen
attempt do not modify source/config, retry, restart Steam, or reconfigure the
VM. A run can contain multiple isolated attempts; each attempt gets its own
directory and logs.

Gameplay acceptance requires the stated feature assertions, human visual
observation, evidence interpretation, and cleanup. A window is only startup
evidence. Classify infrastructure failures bottom-up: export/dependencies,
Steam lobby, native peer, Godot connection, GameFactory lifecycle, then Netfox.

For manual A/B observation, use `-ShowHostConsole` to open a local terminal
that tails the artifact-owned Godot log. The commands are:

```powershell
# No-launch VM control preflight. Requires a verified remote PowerShell marker
# and an enabled GameFactoryClient scheduled task; it never starts the game.
.\tools\ab_test\run.ps1 -Mode Health

# New session. Exits only after AB_READY; both games stay open.
.\tools\ab_test\run.ps1 -Mode Launch -Scenario netfox_player_3d -ShowHostConsole

# Evidence only. Defaults to the latest attempt and never launches, rebuilds,
# or stops either game.
.\tools\ab_test\run.ps1 -Mode Verify -RunId <RunId> [-Attempt <number>]

# Stop a live session and verify cleanup. Safe to repeat when practical.
.\tools\ab_test\run.ps1 -Mode Stop -RunId <RunId>

# After Stop and any manual environment repair, create a fresh attempt using
# the run's captured immutable build identity.
.\tools\ab_test\run.ps1 -Mode Retry -RunId <RunId> -ShowHostConsole
```

`-RecoverVm` is an explicit preflight recovery option. It runs Health first;
only if that check fails, it makes one Hyper-V restart attempt, waits for SSH
and the existing interactive desktop/task contract, then reruns Health. It
does not modify VM networking or autologon. `-FreshTransport` is an explicit
pre-attempt option for `Launch` and `Retry`: after stale game cleanup it
restarts Steam on the host and in the VM's existing interactive session, waits
for Steam plus `steamwebhelper` in that session, verifies the VM task again,
then begins a new attempt. It never restarts Steam during an attempt.

For normal gameplay development, `-RecoverVm` is recommended and
`-FreshTransport` is useful when a clean Steam starting point is wanted:

```powershell
.\tools\ab_test\run.ps1 -Mode Launch -RecoverVm -FreshTransport
```

For Steam/native transport investigation, use plain `Launch`/`Retry` unless a
fresh-session experiment is explicitly requested. A `steam_peer` failure must
remain preserved; use `Stop`, then `Retry -FreshTransport` for a separate
attempt using the same immutable build.

Steam's exact online state has no robust local interface in this harness. The
readiness boundary is therefore an interactive-session Steam process plus one
or more `steamwebhelper` processes, not an assertion that Steam is online.
Recovery metadata is persisted under `infrastructure` in each attempt state.

`Launch` and `Retry` own export/reuse, parity, topology readiness, and state
persistence only. `Verify` emits compact generic evidence (`evidence.json`)
and raw-log paths; it never decides gameplay acceptance. `Stop` is the only
operation that tears processes down. Human visual observation plus a later
human/agent interpretation of generic evidence decides gameplay acceptance.

Artifact layout is stable across shells:

```text
artifacts/ab_tests/<RunId>/run_state.json
artifacts/ab_tests/<RunId>/attempt_001/{state.json,host,client,session,evidence.json}
artifacts/ab_tests/<RunId>/attempt_002/{...}
```

The launch paths are:

- unchanged clean checkout with matching export + verified VM marker: automatic
  fast reuse (clean, cheap identity/parity check, then launch);
- new manifest: export, stage, full SSH file-hash verification once, then
  launch through the interactive task;
- hardening: use `-ForceExport` and/or `-ForceFullVmParity` deliberately.

The parity marker is external metadata under `C:\GameFactoryBuilds\parity`,
not a modification of an immutable release. Verification runs through SSH; the
interactive scheduled task is reserved for the graphical Steam/Godot client.

The guest endpoint is local machine configuration, not repository state. Copy
`vm-endpoint.example.psd1` to gitignored `vm-endpoint.local.psd1` and edit its
`Target` when the Hyper-V guest address changes. An explicit `-VmAlias`
overrides that local setting for one command.
