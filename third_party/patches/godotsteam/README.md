# GodotSteam re-host teardown patch

## Provenance

- Upstream: GodotSteam GDExtension `v4.22-gde`.
- Upstream source commit: `64e94003cadd48c891be3f8126506b0feed847f2`.
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

`0001-fix-steam-multiplayer-peer-close.patch` changes that guard. It is the
only functional GodotSteam source delta carried by GameFactory.

## Rebuild (Windows x86_64)

Prerequisites:

- Python with SCons (`python -m pip install --user scons`);
- Visual Studio 2022 C++ build tools;
- an official local Steamworks SDK 1.65 extraction; and
- a clean checkout of the upstream commit above, including its pinned
  `godot-cpp` submodule.

From the upstream source root, copy the official SDK's `sdk/public/` and
`sdk/redistributable_bin/` directories into the source root's `sdk/` directory,
then apply the patch file from this repository:

```powershell
git apply <gamefactory-root>/third_party/patches/godotsteam/0001-fix-steam-multiplayer-peer-close.patch
python -m SCons platform=windows target=template_debug arch=x86_64 -j 11
python -m SCons platform=windows target=template_release arch=x86_64 -j 11
```

The results are `bin/libgodotsteam.windows.template_debug.x86_64.dll` and
`bin/libgodotsteam.windows.template_release.x86_64.dll`. Replace the
same-named files under `addons/godotsteam/win64/`, retaining the upstream
release binaries outside version control only if a local rollback is needed.

Verify the rebuilt binary imports `steam_api64.dll` and run
`sandbox/steam/steam_native_rehost_probe.tscn` with Steam active. It must report
both host attempts as `OK` without an artificial delay. Then verify the
GameFactory flow `H -> L -> H -> L -> H` in `sandbox/steam/steam_probe.tscn`.

## Removal condition

When a future GodotSteam release includes this exact close-path correction,
remove this patch record and replace the patched binary with the corresponding
upstream binary after repeating the two manual smoke tests. Linux and macOS
builds are not supplied today; if GameFactory ships those targets before
upstream fixes the defect, apply this same patch and run the equivalent smoke
there.
