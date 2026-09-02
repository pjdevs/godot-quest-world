using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.Interaction.Runtime.Interactive;
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
public partial class InteractionAction : GameplayAction
{
    public static readonly StringName InteractionAccessProviderId = new("interaction");

    /// <summary>Concurrency group used by an action that declares none.</summary>
    public static readonly StringName DefaultConcurrencyGroup = DefaultHostConcurrencyGroup;

    public override StringName AccessProviderId => InteractionAccessProviderId;

    public new InteractionActionDefinition? Definition
    {
        get => base.Definition as InteractionActionDefinition;
        set => base.Definition = value;
    }

    public InteractionActionDefinition? InteractionDefinition => Definition;

    public new InteractionActionExecutor? Executor
    {
        get => base.Executor as InteractionActionExecutor;
        set => base.Executor = value;
    }

    public InteractionActionExecutor? InteractionExecutor => Executor;

    public new InteractionExecutionVisibility ExecutionVisibility
    {
        get => (InteractionExecutionVisibility)base.ExecutionVisibility;
        set => base.ExecutionVisibility = (GameplayActionExecutionVisibility)value;
    }

    public InteractiveComponent? Interactive { get; internal set; }

    private Godot.Collections.Array<GameplayActionRule>? _authoredRules;

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
    /// <summary>Gets or sets whether local focus requests this action without any player input.</summary>
    /// <remarks>
    /// An automatic action still goes through the authoritative command path and is still presented,
    /// but prompts omit it because no input is bound to it.
    /// </remarks>
    [Export]
    public bool Automatic { get; set; }

    /// <summary>Gets the group this action is exclusive with, falling back to the default group.</summary>
    /// <returns>The authored group, or <see cref="DefaultConcurrencyGroup"/> when none is set.</returns>
    public StringName GetConcurrencyGroup() => GetHostConcurrencyGroup();

    internal GameplayActionBindingConfig BuildBindingConfig()
    {
        InteractionActionDefinition? definition = InteractionDefinition;
        if (Automatic)
        {
            return new InteractionActionBindingConfig
            {
                ActivationMode = GameplayActionActivationMode.Automatic,
            };
        }

        float holdThreshold = definition?.HoldThreshold ?? 0.0f;
        return new InteractionActionBindingConfig
        {
            InputActionName = definition?.InputActionName ?? new StringName(),
            ActivationMode =
                holdThreshold > 0.0f
                    ? GameplayActionActivationMode.Hold
                    : GameplayActionActivationMode.Press,
            HoldDuration = holdThreshold,
            InputRequirement =
                definition?.CancelOnInputReleased == true
                    ? GameplayActionInputRequirement.Pressed
                    : GameplayActionInputRequirement.None,
            Priority = Priority,
        };
    }

    internal void PrepareForInteractive(
        InteractiveComponent interactive,
        Godot.Collections.Array<InteractionRule> targetRules
    )
    {
        Interactive = interactive;
        if (_authoredRules is null)
        {
            _authoredRules = new Godot.Collections.Array<GameplayActionRule>();
            foreach (GameplayActionRule rule in Rules)
            {
                _authoredRules.Add(rule);
            }
        }

        Godot.Collections.Array<GameplayActionRule> combined = new();
        foreach (InteractionRule rule in targetRules)
        {
            combined.Add(rule);
        }

        foreach (GameplayActionRule rule in _authoredRules)
        {
            combined.Add(rule);
        }

        Rules = combined;
    }

    /// <summary>Godot callback that reports a missing definition or executor.</summary>
    public override void _Ready()
    {
        base._Ready();

        if (Definition is not null && InteractionDefinition is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires an InteractionActionDefinition.");
        }

        if (Executor is not null && InteractionExecutor is null)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires an InteractionActionExecutor.");
        }
    }
}
