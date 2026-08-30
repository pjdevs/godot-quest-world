@tool
extends EditorScript

# One-shot migration for quest_world/levels/facility.tscn.
#
# Goal:
# - one CSGCombiner3D named Architecture per major room;
# - one top-level CSGCombiner3D for all connector architecture, with nested
#   combiners preserving each connector transform;
# - gameplay props, labels and lighting keep their existing paths;
# - after running this script, the eight Architecture roots can each be baked
#   to one MeshInstance3D for LightmapGI/culling tests.
#
# Run with facility.tscn as the active edited scene, then inspect and save.
# The script is intentionally idempotent so running it twice is harmless.

const EXPECTED_SCENE_SUFFIX := "/quest_world/levels/facility.tscn"

const ROOM_NAMES: Array[StringName] = [
	&"Entrance",
	&"CentralHub",
	&"SecurityWing",
	&"ArchivesWing",
	&"WaterSystems",
	&"Cooling",
	&"PrototypeLab",
]

# Room architecture is deliberately conservative: only the structural gray
# shell and ceiling material are folded into the room mesh. Technical,
# interactive, movable, objective and hazard blockout props remain separate.
const ROOM_ARCHITECTURE_MATERIALS := {
	"BLOCKOUT_Static_Gray": true,
	"BLOCKOUT_Ceiling_DarkGray": true,
}

# Connector trims are part of the static corridor shell, but actual gameplay
# blockers must remain separate so the playable-slice scene can replace them.
const CONNECTOR_ARCHITECTURE_MATERIALS := {
	"BLOCKOUT_Static_Gray": true,
	"BLOCKOUT_Ceiling_DarkGray": true,
	"BLOCKOUT_Transition_Yellow": true,
}

const CONNECTOR_DYNAMIC_NAMES := {
	"MaintenanceHatch": true,
	"ArchiveBulkhead": true,
	"BlastDoor_Left": true,
	"BlastDoor_Right": true,
}


func _run() -> void:
	var scene_root := EditorInterface.get_edited_scene_root()
	if scene_root == null:
		push_error("Facility CSG restructure: no edited scene.")
		return

	var scene_path := scene_root.scene_file_path
	if not scene_path.ends_with(EXPECTED_SCENE_SUFFIX):
		push_error(
			"Facility CSG restructure: open quest_world/levels/facility.tscn first. "
			+ "Current scene: " + scene_path
		)
		return

	var facility := scene_root.get_node_or_null("Level/FacilityBlockout")
	if facility == null:
		push_error("Facility CSG restructure: Level/FacilityBlockout was not found.")
		return

	var moved_room_shapes := 0
	for room_name in ROOM_NAMES:
		var room := facility.get_node_or_null(NodePath(String(room_name))) as Node3D
		if room == null:
			push_warning("Facility CSG restructure: room not found: " + String(room_name))
			continue
		moved_room_shapes += _restructure_room(room, scene_root)

	var connectors := facility.get_node_or_null("Connectors") as Node3D
	var moved_connector_shapes := 0
	if connectors == null:
		push_warning("Facility CSG restructure: Connectors node was not found.")
	else:
		moved_connector_shapes = _restructure_connectors(connectors, scene_root)

	EditorInterface.mark_scene_as_unsaved()
	print(
		"Facility CSG restructure complete: ",
		moved_room_shapes,
		" room shapes + ",
		moved_connector_shapes,
		" connector shapes moved into 8 bake roots. Inspect scene tree, then save."
	)


func _restructure_room(room: Node3D, scene_root: Node) -> int:
	var architecture := _ensure_combiner(room, &"Architecture", scene_root)
	room.move_child(architecture, 0)

	var candidates: Array[Node] = []
	for child in room.get_children():
		if child == architecture:
			continue
		if _has_material_category(child, ROOM_ARCHITECTURE_MATERIALS):
			candidates.append(child)

	for child in candidates:
		# Architecture is identity under the same room, so preserving the global
		# transform also preserves the authored blockout placement exactly.
		child.reparent(architecture, true)

	return candidates.size()


func _restructure_connectors(connectors: Node3D, scene_root: Node) -> int:
	var architecture := _ensure_combiner(connectors, &"Architecture", scene_root)
	connectors.move_child(architecture, 0)

	var moved := 0
	var connector_nodes: Array[Node] = []
	for child in connectors.get_children():
		if child != architecture:
			connector_nodes.append(child)

	for connector_node in connector_nodes:
		if not connector_node is Node3D:
			continue

		var connector := connector_node as Node3D
		var candidates: Array[Node] = []
		for child in connector.get_children():
			if CONNECTOR_DYNAMIC_NAMES.has(String(child.name)):
				continue
			if _has_material_category(child, CONNECTOR_ARCHITECTURE_MATERIALS):
				candidates.append(child)

		if candidates.is_empty():
			continue

		var nested_name := StringName(String(connector.name) + "_Architecture")
		var nested := architecture.get_node_or_null(NodePath(String(nested_name))) as CSGCombiner3D
		if nested == null:
			nested = CSGCombiner3D.new()
			nested.name = nested_name
			architecture.add_child(nested)
			nested.owner = scene_root

		# The original connector Node3D keeps its Lighting child/path. Mirroring
		# its transform on the nested combiner lets the CSG geometry keep the same
		# authored local transforms while becoming part of one connector CSG root.
		nested.transform = connector.transform

		for child in candidates:
			child.reparent(nested, true)
			moved += 1

	return moved


func _ensure_combiner(parent: Node, combiner_name: StringName, scene_root: Node) -> CSGCombiner3D:
	var existing := parent.get_node_or_null(NodePath(String(combiner_name))) as CSGCombiner3D
	if existing != null:
		# Once CSG primitives become children of a combiner, their own
		# use_collision flags no longer define the collision of the final CSG
		# result. The root combiner must own the generated static collision.
		existing.use_collision = true
		return existing

	var combiner := CSGCombiner3D.new()
	combiner.name = combiner_name
	combiner.use_collision = true
	parent.add_child(combiner)
	combiner.owner = scene_root
	return combiner


func _has_material_category(node: Node, allowed_materials: Dictionary) -> bool:
	if not node is CSGPrimitive3D:
		return false

	var primitive := node as CSGPrimitive3D
	if primitive.material == null:
		return false

	return allowed_materials.has(primitive.material.resource_name)
