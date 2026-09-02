using Godot;
using QuestWorld.GameplayActions.Runtime.Rules;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class GameplayAction : Node
{
    public static readonly StringName DefaultHostConcurrencyGroup = new("default");

    [Export]
    public GameplayActionDefinition? Definition { get; set; }

    [Export]
    public GameplayActionExecutor? Executor { get; set; }

    [Export]
    public Godot.Collections.Array<GameplayActionRule> Rules { get; set; } = new();

    [Export]
    public StringName HostConcurrencyGroup { get; set; } = DefaultHostConcurrencyGroup;

    [Export]
    public GameplayActionExecutionVisibility ExecutionVisibility { get; set; } =
        GameplayActionExecutionVisibility.RequesterOnly;

    public GameplayActionComponent? Component { get; internal set; }

    public StringName GetHostConcurrencyGroup() =>
        HostConcurrencyGroup is null || HostConcurrencyGroup.IsEmpty
            ? DefaultHostConcurrencyGroup
            : HostConcurrencyGroup;

    public override void _Ready()
    {
        if (Definition is null)
        {
            GD.PushError($"{GetPath()}: GameplayAction requires a Definition.");
        }

        if (Executor is null)
        {
            GD.PushError($"{GetPath()}: GameplayAction requires an Executor.");
        }
    }
}
