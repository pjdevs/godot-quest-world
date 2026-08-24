extends SceneTree


func _initialize() -> void:
	_run()


func _run() -> void:
	if not ClassDB.class_exists("NativeInteractionStateful"):
		_fail("NativeInteractionStateful is not registered.")
		return

	var stateful := ClassDB.instantiate("NativeInteractionStateful") as Node
	if stateful == null:
		_fail("NativeInteractionStateful could not be instantiated.")
		return

	stateful.set("initial_state", 1)
	root.add_child(stateful)
	await process_frame
	if stateful.get("state") != 1:
		_fail("initial_state was not applied when the node became ready.")
		return

	var changes: Array[Array] = []
	stateful.connect(
		"interaction_state_changed",
		func(old_state: int, new_state: int) -> void: changes.append([old_state, new_state])
	)

	if stateful.call("set_state", 2) != true:
		_fail("set_state did not report a state change.")
		return

	if stateful.get("state") != 2 or changes != [[1, 2]]:
		_fail("set_state did not update state and emit the expected signal.")
		return

	if stateful.call("set_state", 2) != false or changes.size() != 1:
		_fail("setting the current state should be a no-op.")
		return

	stateful.queue_free()
	print("NativeInteractionStateful smoke test passed.")
	quit()


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
