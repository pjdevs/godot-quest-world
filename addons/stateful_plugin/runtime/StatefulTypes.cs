using Godot;

namespace QuestWorld.State;

/// <summary>Result of a local state mutation, dispatched after the mutation completed.</summary>
/// <param name="OldState">Value applied before the mutation.</param>
/// <param name="NewState">Value applied by the mutation.</param>
internal readonly record struct StateTransition(StringName OldState, StringName NewState);

/// <summary>Versioned state snapshot used by an external persistence system.</summary>
/// <param name="Version">Serialization contract version.</param>
/// <param name="State">State captured by the snapshot.</param>
public readonly record struct StatefulSavedState(int Version, StringName State);
