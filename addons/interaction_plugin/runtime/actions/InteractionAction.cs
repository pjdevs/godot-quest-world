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

    /// <summary>
    /// Gets or sets the local weight used when several actions of this target share one input.
    /// </summary>
    /// <remarks>
    /// The resolver prefers an allowed action over a blocked one, then the highest priority. A
    /// remaining tie is broken by ascending action identifier so the choice stays deterministic.
    /// </remarks>
    [Export]
    public int Priority { get; set; }

    /// <summary>Gets or sets whether local focus requests this action without any player input.</summary>
    /// <remarks>
    /// An automatic action still goes through the authoritative command path and is still presented,
    /// but prompts omit it because no input is bound to it.
    /// </remarks>
    [Export]
    public bool Automatic { get; set; }

    /// <summary>Godot callback that reports a missing definition.</summary>
    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires a Definition.");
        }
    }
}
