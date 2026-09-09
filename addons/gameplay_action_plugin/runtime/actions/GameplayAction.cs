using GameplayActionPlugin.Runtime.Rules;
using Godot;

namespace GameplayActionPlugin.Runtime.Actions;

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

    /// <summary>Gets or sets the provider ID authored for runner requests.</summary>
    [Export]
    public StringName ConfiguredAccessProviderId { get; set; } = new();

    /// <summary>Gets the named access provider required when a runner requests this action.</summary>
    /// <remarks>
    /// An empty value keeps the default owned-action access policy. A non-empty value always routes
    /// the request through the runner's provider with that ID, regardless of action ownership.
    /// </remarks>
    public virtual StringName AccessProviderId => ConfiguredAccessProviderId;

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
