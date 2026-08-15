# Networking Foundation

## Layering

Game
→ Factory gameplay networking
→ NetworkSession
→ INetworkTransport
→ Godot MultiplayerAPI
→ MultiplayerPeer
→ transport

Higher layers must not depend directly on ENet.

## Runtime

A process is one of:

- Offline
- Client
- ListenServer
- DedicatedServer

ListenServer is both server-capable and client-capable.

DedicatedServer has no implied local player.

Peer identity and player identity are separate concepts.

## Peer identity

Raw Godot peer IDs become PeerId at the transport boundary.

Peer 1 is the server.

PeerRegistry represents peers visible to this process.

IsLocal means "this peer is this process."

IsServer means "this peer is the server."

They are independent properties.

## Session lifecycle

Transport events describe networking facts.

NetworkSession converts them into lifecycle meaning.

A remote client disconnecting does not fail a server session.

A client losing its server ends that client's session.

Intentional leave and unexpected connection loss are different.

Local cleanup must not depend on remote disconnect events.

## Transport

NetworkSession does not know ENet.

ENetTransport is currently the transport adapter.

Future transports must be replaceable without rewriting session logic.

## Future gameplay networking

Do not assume all Nodes are networked.

Static authored content generally requires no networking.

Dynamic shared objects may require:

- replicated existence
- replicated state
- network events
- continuous state
- custom behavior

Default networking should require almost zero gameplay boilerplate.

Special networking must remain deeply customizable.

Networking mechanics should be hidden from normal gameplay code.

Networking behavior must remain observable through diagnostics.

Composition is preferred for networking capabilities:

Game Object
└── NetworkObject

Networking must not force game objects into a NetworkEntity inheritance hierarchy.
