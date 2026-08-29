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
    /// <summary>Concurrency group used by an action that declares none.</summary>
    public static readonly StringName DefaultConcurrencyGroup = new("default");

    /// <summary>Gets or sets the required shared definition providing identity, label, and input.</summary>
    [Export]
    public InteractionActionDefinition? Definition { get; set; }

    /// <summary>Gets or sets the required single owner of the gameplay mutation of this action.</summary>
    /// <remarks>
    /// Add the executor node to the target scene, conventionally as a child of this action, and
    /// reference it here. An action without executor is a configuration error and stays blocked: the
    /// core never falls back to a signal that some subscriber might handle.
    /// </remarks>
    [Export]
    public InteractionActionExecutor? Executor { get; set; }

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

    /// <summary>Gets or sets which peers may observe this action while it executes.</summary>
    [Export]
    public InteractionExecutionVisibility ExecutionVisibility { get; set; } =
        InteractionExecutionVisibility.RequesterOnly;

    /// <summary>
    /// Gets or sets the group of executions this action is exclusive with on its own target.
    /// </summary>
    /// <remarks>
    /// Two active executions of the same target sharing one group cannot coexist. The default group
    /// makes every action of a target mutually exclusive, which is what a single interactable object
    /// almost always wants. Naming a distinct group is how a long action stops blocking an unrelated
    /// one, for example an inspection staying available during a hack. Exclusivity never crosses
    /// targets: this is not a lock manager.
    /// </remarks>
    [Export]
    public StringName ConcurrencyGroup { get; set; } = DefaultConcurrencyGroup;

    /// <summary>Gets or sets whether local focus requests this action without any player input.</summary>
    /// <remarks>
    /// An automatic action still goes through the authoritative command path and is still presented,
    /// but prompts omit it because no input is bound to it.
    /// </remarks>
    [Export]
    public bool Automatic { get; set; }

    /// <summary>Gets the group this action is exclusive with, falling back to the default group.</summary>
    /// <returns>The authored group, or <see cref="DefaultConcurrencyGroup"/> when none is set.</returns>
    public StringName GetConcurrencyGroup() =>
        ConcurrencyGroup is null || ConcurrencyGroup.IsEmpty
            ? DefaultConcurrencyGroup
            : ConcurrencyGroup;

    /// <summary>Godot callback that reports a missing definition or executor.</summary>
    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires a Definition.");
        }

        if (Executor is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires an Executor.");
        }
    }
}
