# Steam Integration

## Status

GameFactory has a build-validated Steam listen-server foundation. It is not yet accepted as runtime evidence: a manual two-account Steam test must prove host, invite or explicit lobby join, peer connection/disconnection, and existing replication/player lifecycle behavior before ENet is reconsidered.

## Pinned dependency

- **GodotSteam GDExtension 4.22**, Steamworks SDK 1.65.
- Source and release: <https://codeberg.org/godotsteam/godotsteam/releases/tag/v4.22-gde>.
- The release states support for Godot 4.4 and up, including this project's Godot .NET SDK 4.7.1.
- Files live in `addons/godotsteam/`, including the upstream `license.md` (MIT).
- Development uses Steam App ID **480** only. It is not a production App ID or release configuration.

## Boundary and flow

`SteamSession` calls the Steam-specific `ISteamAdapter`. The current adapter calls a small GDScript bridge, which is the sole location that knows GodotSteam's `Steam` singleton and `SteamMultiplayerPeer`. The bridge returns the peer to C#, and `SteamSession` assigns it to Godot's `MultiplayerApi`; existing Godot RPC, spawners, synchronizers, `NetworkWorld`, and player lifecycle remain above that point unchanged.

The initial flow is listen-server only:

1. Initialize Steam and read local identity.
2. Create a friends-only, four-member lobby by default.
3. Create a `SteamMultiplayerPeer` for that lobby and assign it to Godot.
4. Invite through the Steam overlay, or explicitly join a requested/supplied lobby.
5. Leave explicitly closes the `SteamMultiplayerPeer`, clears it from Godot's
   `MultiplayerApi`, then leaves the lobby. This releases the listen socket so
   the same process can host another lobby.

Incoming invites are surfaced to the caller. They never auto-join an active session. Peer IDs and Steam IDs remain distinct and are mapped through the active Steam peer.

## Manual acceptance procedure

Run two Steam accounts with the Steam client active, using development App ID 480. On one, run `steam_probe.tscn` with `--steam-host`; it prints the lobby ID. On the other, run it with `--steam-lobby=<id>` or accept an invite. The exported project uses `sandbox_launcher.tscn` as its main scene and accepts `--run=steam`, `--run=connection`, or `--run=replication`. The launcher selects only these packed, registered sandbox scenes; it replaces unsupported arbitrary `--scene` path overrides. The interactive probe also supports `H` (host), `I` (invite overlay), `J` (explicitly accept the latest requested lobby), and `L` (leave). Record Godot peer-connected/disconnected behavior and run the existing replication probe against the assigned peer before calling Steam transport replacement proven.

The probe is intentionally not a second world implementation. It establishes the platform/peer boundary; existing network-world, object, replication, and player slices must be exercised above it.

## Deferred seams

`ISteamAdapter` declares dedicated-server discovery/peer/lifecycle and authentication-ticket seams. `GodotSteamAdapter` deliberately returns unsupported for them today. Host migration, production App ID setup, shipping/export configuration, and an application/menu shell are out of this slice.
