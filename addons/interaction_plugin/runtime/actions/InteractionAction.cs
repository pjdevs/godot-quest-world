using Godot;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Binds one reusable action definition to a single target and owns the choices of that occurrence.
/// </summary>
/// <remarks>
/// Add this node under the target and reference it explicitly from
/// <c>InteractiveComponent.Actions</c>. Availability is evaluated by the interactive component; this
/// node never mutates gameplay and never evaluates itself.
/// </remarks>
[GlobalClass]
public partial class InteractionAction : Node
{
    /// <summary>Gets or sets the required shared definition providing identity, label, and input.</summary>
    [Export]
    public InteractionActionDefinition? Definition { get; set; }

    /// <summary>
    /// Gets or sets the ordered gameplay conditions of this action. Evaluation stops at the first
    /// hidden or blocked result.
    /// </summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> Rules { get; set; } = new();

    /// <summary>Godot callback that reports a missing definition.</summary>
    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires a Definition.");
        }
    }
}
