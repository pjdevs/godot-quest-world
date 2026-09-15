extends SceneTree

const REPRO_SCENES := [
    "res://quest_world/character/Character.tscn",
    "res://quest_world/interactibles/lever_wall/LeverWall.tscn",
    "res://addons/interaction_plugin/integration/stateful/examples/LongActionExample.tscn",
]

func _initialize() -> void:
    var user_args := OS.get_cmdline_user_args()
    var repro_scenes := REPRO_SCENES
    if not user_args.is_empty():
        repro_scenes = user_args[0].split(",")

    for scene_path in repro_scenes:
        var scene := load(scene_path) as PackedScene
        print("loaded ", scene_path, ": ", scene != null)
        if scene == null:
            quit(2)
            return

    print("quitting after synchronous exact-scene load-only repro")
    quit()
