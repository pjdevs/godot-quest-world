using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Interaction specialization of one generic gameplay action occurrence.
/// </summary>
[GlobalClass]
public partial class InteractionAction : GameplayAction
{
    public static readonly StringName InteractionAccessProviderId = new("interaction");
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

    private InteractionTargetRulesAdapter? _targetRulesAdapter;

    [Export]
    public int Priority { get; set; }

    [Export]
    public bool Automatic { get; set; }

    /// <summary>
    /// Preserves the Interaction authoring property while the final scene migration is deferred to
    /// tranche 5.
    /// </summary>
    [Export]
    public StringName ConcurrencyGroup
    {
        get => HostConcurrencyGroup;
        set => HostConcurrencyGroup = value;
    }

    public StringName GetConcurrencyGroup() => GetHostConcurrencyGroup();

    internal GameplayActionBindingConfig BuildBindingConfig()
    {
        InteractionActionDefinition? definition = InteractionDefinition;
        if (Automatic)
        {
            return new InteractionActionBindingConfig
            {
                ActivationMode = GameplayActionActivationMode.Automatic,
                Priority = Priority,
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
        if (InteractionExecutor is { } executor)
        {
            executor.InteractionAction = this;
        }

        _targetRulesAdapter ??= new InteractionTargetRulesAdapter();
        _targetRulesAdapter.Interactive = interactive;

        // Target rules are an Interaction concern but must participate in the generic authoritative
        // rule pass. Keep one dynamic adapter first instead of copying the target and action arrays:
        // authored action rules remain the real mutable Rules collection.
        Rules.Remove(_targetRulesAdapter);
        Rules.Insert(0, _targetRulesAdapter);
    }

    public override void _Ready()
    {
        base._Ready();

        if (base.Definition is not null && base.Definition is not InteractionActionDefinition)
        {
            GD.PushError(
                $"{GetPath()}: InteractionAction requires an InteractionActionDefinition."
            );
        }

        if (base.Executor is not null && base.Executor is not InteractionActionExecutor)
        {
            GD.PushError($"{GetPath()}: InteractionAction requires an InteractionActionExecutor.");
        }
    }
}
