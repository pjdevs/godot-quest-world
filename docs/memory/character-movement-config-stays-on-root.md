# Character movement configuration stays on the root

- `Character` remains the single serialized owner of all character configuration, including movement and landing tuning.
- `CharacterMovement` is a plain C# runtime class, not a Godot `Node` and not a `Resource`.
- The root passes a `CharacterMovementSettings` value snapshot to the motor for each simulation tick. This avoids duplicate exported properties while keeping the motor independent from the inspector and scene serialization.
- If alternative movement modes are added later, they should vary the runtime motor/mode implementation without introducing a second serialized configuration hierarchy unless the entire Character configuration moves together.
