# GameFactory GodotSteam source patches

## Provenance

- Upstream: GodotSteam GDExtension `v4.22-gde`.
- Upstream source commit: `64e94003cadd48c891be3f8126506b0feed847f2`.
- Pinned godot-cpp submodule: `7e18e40d7591429f915035a7de7cf79457d555cc`.
- Steamworks SDK: Valve Steamworks SDK 1.65.
- Godot used to build and exercise GameFactory: 4.7.1 .NET.
- Current targets: Windows x86_64 `template_debug` and `template_release`.
- Outputs: `addons/godotsteam/win64/libgodotsteam.windows.template_debug.x86_64.dll`
  and `addons/godotsteam/win64/libgodotsteam.windows.template_release.x86_64.dll`.

The Steamworks SDK is Valve-licensed local build input. Do not commit its
archive or extracted files to GameFactory.

## Bug and reproduction

Stock GodotSteam 4.22 fails this engine-level sequence in one Steam process:

```text
SteamMultiplayerPeer.create_host(0) -> OK
SteamMultiplayerPeer.close()
SteamMultiplayerPeer.create_host(0) -> ERR_CANT_CREATE
```

Raw Steam NetworkingSockets create/close/recreate succeeds, which isolates the
failure to `SteamMultiplayerPeer`. In `_close()`, the stock guard returns when
the `Steam` singleton exists. That skips the listener and poll-group teardown in
the normal runtime where it is required.

Apply these patches in numeric order to that upstream commit:

1. `0001-fix-steam-multiplayer-peer-close.patch`: correct the close singleton guard.
2. `0002-dedupe-pending-steam-peers.patch`: preserve the recovered pending-Steam-ID
   and existing-Steam-ID guard carried by the historical DLLs. This prevents
   repeated outgoing requests within one peer, not cross-instance callbacks.
3. `0003-isolate-peer-callback-lifecycle.patch`: closed peers reject connection
   and lobby callbacks; active peers accept only owned outgoing handles or
   incoming handles on their listener. Ownership starts at ConnectP2P and is
   cleared/closed even before the first callback. Reopening initializes fresh
   lifecycle state. Peer-level ping diagnostics include sending object, handle,
   and local peer ID.
4. `0004-steamworks-165-api-compatibility.patch`: recover the historical source
   adjustments for Flat API IP access, RemotePlay event data, and SteamInput
   event unions required by the local SDK headers.

The full series was applied to files extracted from the pinned upstream commit
in an isolated Git checkout and compared byte-for-byte (normalized line endings)
with the native build source. The Steamworks SDK and temporary source checkout
remain outside version control.

The lifecycle defect was captured in run `peer_lifecycle_20261003_narrow_02`,
attempt 003: a retained closed VM client consumed the new host's native handle
`1332833479` and sent stale peer ID `2074602627` alongside the current server
ID 1. The recipient admitted the stale ID and never reached ConnectedToServer.
The per-instance dedupe guard alone cannot prevent this failure.

## Rebuild (Windows x86_64)

Prerequisites:

- Python with SCons (`python -m pip install --user scons`);
- Visual Studio 2022 C++ build tools;
- an official local Steamworks SDK 1.65 extraction; and
- a clean checkout of the upstream commit above, including its pinned
  `godot-cpp` submodule.

From the upstream source root, copy the official SDK's `sdk/public/` and
`sdk/redistributable_bin/` directories into the source root's `sdk/` directory,
then apply the complete series from this repository:

```powershell
git apply <gamefactory-root>/third_party/patches/godotsteam/0001-fix-steam-multiplayer-peer-close.patch
git apply <gamefactory-root>/third_party/patches/godotsteam/0002-dedupe-pending-steam-peers.patch
git apply <gamefactory-root>/third_party/patches/godotsteam/0003-isolate-peer-callback-lifecycle.patch
git apply <gamefactory-root>/third_party/patches/godotsteam/0004-steamworks-165-api-compatibility.patch
python -m SCons platform=windows target=template_debug arch=x86_64 -j 11
python -m SCons platform=windows target=template_release arch=x86_64 -j 11
```

The results are `bin/libgodotsteam.windows.template_debug.x86_64.dll` and
`bin/libgodotsteam.windows.template_release.x86_64.dll`. Replace the
same-named files under `addons/godotsteam/win64/`, retaining the upstream
release binaries outside version control only if a local rollback is needed.

Current rebuilt SHA-256 (2026-10-03):

- Debug: `da20f384da6911b31cfb32951831206e7b6f93ff9c9b4d6472551d985f1e4d08`.
- Release: `2db139317c037b82f42c2e0d19f00cd6d009a9d2ad7f82cb4e80a393b9eb1176`.
- Steam API (unchanged): `8de54d32508e216c9135b8bf025749243d44e404c1c22a8e5fe35acecabe7a9c`.

Hashes identify these outputs, not guaranteed byte-identical results across
different MSVC installations. Verify the actual exported debug DLL against its
manifest and the deployed host/VM DLLs before acceptance.

Verify the rebuilt binary imports `steam_api64.dll` and run
`sandbox/steam/steam_native_rehost_probe.tscn` with Steam active. It must report
all three host attempts (including same-object reopen with a retained closed
peer) as `OK` and exit with its PASS marker, without an artificial delay. Then verify the
GameFactory flow `H -> L -> H -> L -> H` in `sandbox/steam/steam_probe.tscn`.

For two-account acceptance, use the supported shell with
`-Scenario shell_manual -SteamTransportTrace -SteamTransportRetainClosedPeer`.
The latter flag holds only the current and previous object (at most two), so
close isolation is tested without relying on garbage collection. Run repeated
fresh join, same-lobby client rejoin, new-lobby reuse, and role reversal, all
before Start. Require one successful ping/admission per endpoint, server ID 1,
ConnectedToServer, ready 2/2, no invalid packets or stale callback consumption,
and verified cleanup. Only afterward check Lobby → Start → gameplay once.
Tracing and retention are opt-in; normal gameplay retains no diagnostic peers.

Runtime lifecycle isolation is accepted for the tested paths on `d28f7ad`:
retained-object native probe; fresh join; same-lobby rejoin; new-lobby reuse;
role reversal; and a final gameplay/movement/jump/bidirectional switch check.
Run `native_lifecycle_acceptance_d28f7ad_20261003_02` attempt 001 passed all four
transport joins. Attempt 002 failed before admission with unrecovered native
certificate/rendezvous errors. A separately authorized Steam restart on both
machines preceded attempt 003, which passed fresh join, gameplay, and clean
Leave. All process cleanup was verified. BadCert remains unresolved; neither
its ownership nor permanent recovery is proven. The original second full
four-join cycle was not completed. See
[`docs/steam-peer-lifecycle-investigation.md`](../../../docs/steam-peer-lifecycle-investigation.md)
for preserved evidence and the failed stricter handle-count assertion.

## Removal condition

When a future GodotSteam release includes this exact close-path correction,
remove this patch record and replace the patched binary with the corresponding
upstream binary after repeating the two manual smoke tests. Linux and macOS
builds are not supplied today; if GameFactory ships those targets before
upstream fixes the defect, apply this same patch and run the equivalent smoke
there.
