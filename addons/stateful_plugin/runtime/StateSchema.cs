using Godot;

namespace QuestWorld.State;

/// <summary>
/// Optional list of the state values a <see cref="StatefulComponent"/> is allowed to hold.
/// </summary>
/// <remarks>
/// The schema documents and validates values; it is not a state machine and declares no transition,
/// guard, or effect. A component without schema accepts any value.
/// </remarks>
[GlobalClass]
public partial class StateSchema : Resource
{
    /// <summary>Gets or sets the declared state values, for example <c>closed</c> and <c>open</c>.</summary>
    [Export]
    public Godot.Collections.Array<StringName> States { get; set; } = new();

    /// <summary>Checks whether a value is declared by this schema.</summary>
    /// <remarks>This query is synchronous, repeatable, and free of side effects.</remarks>
    /// <param name="state">State value to look for.</param>
    /// <returns><see langword="true"/> when the value is declared.</returns>
    public bool Contains(StringName state) => States.Contains(state);
}
