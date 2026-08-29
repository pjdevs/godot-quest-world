using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Shared composition for small Stateful transition actions.</summary>
public abstract partial class StatefulTransitionActionBase : InteractionAction
{
    private readonly StatefulStateInteractionRule _stateRule = new();
    private InteractionActionExecutor? _composedExecutor;

    /// <summary>Gets or sets the states in which this action is available.</summary>
    [ExportGroup("Stateful")]
    [Export]
    public Godot.Collections.Array<StringName> From { get; set; } = new();

    /// <summary>Gets or sets the optional Stateful component override.</summary>
    [ExportGroup("Overrides")]
    [Export]
    public StatefulComponent? Stateful { get; set; }

    /// <summary>Godot callback that caches the local Stateful before composing primitives.</summary>
    public override void _Ready()
    {
        Stateful ??= StatefulComposition.ResolveLocalFrom(this);
        base._Ready();
    }

    /// <inheritdoc />
    public override InteractionActionExecutor? ResolveExecutor()
    {
        InteractionActionExecutor? explicitExecutor = base.ResolveExecutor();
        if (explicitExecutor is not null)
        {
            return explicitExecutor;
        }

        _composedExecutor ??= CreateComposedExecutor();
        ConfigureComposedExecutor(_composedExecutor);
        return _composedExecutor;
    }

    /// <inheritdoc />
    public override IEnumerable<InteractionRule> ResolveRules()
    {
        ConfigureStateRule();
        yield return _stateRule;

        foreach (InteractionRule rule in base.ResolveRules())
        {
            yield return rule;
        }
    }

    /// <summary>Releases a generated executor that is not part of the authored scene tree.</summary>
    public override void _Notification(int what)
    {
        if (
            what == NotificationPredelete
            && _composedExecutor is not null
            && GodotObject.IsInstanceValid(_composedExecutor)
            && _composedExecutor.GetParent() is null
        )
        {
            _composedExecutor.Free();
        }
    }

    protected abstract InteractionActionExecutor CreateComposedExecutor();

    protected virtual void ConfigureComposedExecutor(InteractionActionExecutor executor) { }

    private void ConfigureStateRule()
    {
        _stateRule.StatefulOverride = Stateful;
        _stateRule.ExpectedStates.Clear();
        foreach (StringName state in From)
        {
            _stateRule.ExpectedStates.Add(state);
        }
    }
}
