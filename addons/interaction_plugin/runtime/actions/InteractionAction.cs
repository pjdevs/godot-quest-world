using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Interaction specialization of one generic gameplay action occurrence.
/// </summary>
[GlobalClass]
public partial class InteractionAction : InputGameplayAction
{
    public static readonly StringName InteractionAccessProviderId = new("interaction");
    public override StringName AccessProviderId => InteractionAccessProviderId;

    public InteractiveComponent? Interactive { get; internal set; }

    private InteractionTargetRulesAdapter? _targetRulesAdapter;

    internal void PrepareForInteractive(
        InteractiveComponent interactive,
        Godot.Collections.Array<InteractionRule> targetRules
    )
    {
        Interactive = interactive;
        if (Executor is InteractionActionExecutor executor)
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
}
