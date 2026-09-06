using Godot;
using QuestWorld.GameplayActions.Runtime.Rules;

namespace QuestWorld.GameplayActions.Runtime.Actions;

/// <summary>One host-owned gameplay action occurrence with rules, executor and execution policy.</summary>
[GlobalClass]
public partial class GameplayAction : Node
{
    /// <summary>Default host-local concurrency group used when none is authored.</summary>
    public static readonly StringName DefaultHostConcurrencyGroup = new("default");

    /// <summary>Gets or sets the reusable identity and presentation metadata for this occurrence.</summary>
    [Export]
    public GameplayActionDefinition? Definition { get; set; }

    /// <summary>Gets or sets the single executor that owns this action's gameplay command.</summary>
    [Export]
    public GameplayActionExecutor? Executor { get; set; }

    /// <summary>Gets or sets the ordered availability rules evaluated before reservation.</summary>
    [Export]
    public Godot.Collections.Array<GameplayActionRule> Rules { get; set; } = new();

    /// <summary>Gets or sets the host-local group whose active actions exclude this occurrence.</summary>
    [Export]
    public StringName HostConcurrencyGroup { get; set; } = DefaultHostConcurrencyGroup;

    /// <summary>Gets or sets how transient execution presentation is exposed to remote peers.</summary>
    [Export]
    public GameplayActionExecutionVisibility ExecutionVisibility { get; set; } =
        GameplayActionExecutionVisibility.RequesterOnly;

    /// <summary>Gets the named access provider required when this action is bound externally.</summary>
    public virtual StringName AccessProviderId => new();

    /// <summary>Gets the component currently owning this occurrence, or null before registration.</summary>
    public GameplayActionComponent? Component { get; internal set; }

    /// <summary>Returns the authored concurrency group, falling back to the default for an empty value.</summary>
    public StringName GetHostConcurrencyGroup() =>
        HostConcurrencyGroup is null || HostConcurrencyGroup.IsEmpty
            ? DefaultHostConcurrencyGroup
            : HostConcurrencyGroup;

    /// <summary>Validates the minimum action configuration when the node enters the tree.</summary>
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
