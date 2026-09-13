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

Before each attempt record build ID/manifest, host/guest parity, accounts and
session state, assertions/timeouts, artifact path, and test-owned process IDs.
During a frozen attempt do not modify source/config, retry, restart Steam, or
reconfigure the VM. At the first terminal condition preserve logs, clean both
participants, verify cleanup, and report the deepest completed checkpoint.

`PASS` requires every stated assertion and cleanup. A window is only startup
evidence. Classify failures bottom-up: export/dependencies, Steam lobby, native
peer, Godot connection, GameFactory lifecycle, Netfox, then gameplay. A harness
assertion can be observability-only; inspect its supporting structured events.

For manual A/B observation, use `-ShowHostConsole` to open a local terminal
that tails the artifact-owned host console log. The harness has three paths:

- unchanged clean checkout with matching export + verified VM marker: automatic
  fast reuse (clean, cheap identity/parity check, then launch);
- new manifest: export, stage, full SSH file-hash verification once, then
  launch through the interactive task;
- hardening: use `-ForceExport` and/or `-ForceFullVmParity` deliberately.

The parity marker is external metadata under `C:\GameFactoryBuilds\parity`,
not a modification of an immutable release. Verification runs through SSH; the
interactive scheduled task is reserved for the graphical Steam/Godot client.
