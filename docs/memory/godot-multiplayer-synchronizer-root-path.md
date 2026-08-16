# Godot MultiplayerSynchronizer property paths

- `MultiplayerSynchronizer.root_path` defaults to `..`, so a synchronizer that is
  a child of the node being synchronized already uses its parent as the
  replication root.
- Paths stored in `SceneReplicationConfig` are relative to that replication
  root, not relative to the `MultiplayerSynchronizer` node itself.
- To synchronize a property on the root node, use `.:PropertyName`. Using
  `..:PropertyName` resolves the property on the root node's parent and causes
  `get_state: Property ... not found` to repeat on every synchronization tick.
- Exported C# property names keep their PascalCase spelling in a replication
  `NodePath`, for example `.:ReplicatedState`.
- A scene test must verify that each configured path resolves from `root_path`;
  checking only that `ReplicationConfig` is non-null does not validate the
  configuration.
