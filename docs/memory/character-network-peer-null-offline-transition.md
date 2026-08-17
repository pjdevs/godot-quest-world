# Character network peer null during offline transition

- `Multiplayer.MultiplayerPeer == null` is valid in offline mode and after a network session ends; it cannot by itself mean that Character processing must stop.
- Calling `IsMultiplayerAuthority()` while the peer is null emits Godot's "No multiplayer peer is assigned" error.
- The Character caches its last known local network authority. With no peer, it reuses that value so the local Character can keep moving after disconnection while remote proxies do not become authoritative.
