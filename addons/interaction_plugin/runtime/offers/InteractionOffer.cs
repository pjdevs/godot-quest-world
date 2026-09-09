using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using Godot;
using InteractionPlugin.Runtime.Rules;

namespace InteractionPlugin.Runtime.Offers;

/// <summary>Selects the action host from which an interaction offer resolves its action.</summary>
public enum InteractionOfferSource
{
    /// <summary>Resolve the action from the interactive target's action component.</summary>
    Target,

    /// <summary>Resolve the action from the requesting runner's owned action component.</summary>
    Instigator,
}

/// <summary>Authored invocation exposed by one interactive target.</summary>
[GlobalClass]
public partial class InteractionOffer : Resource
{
    /// <summary>Access-provider ID used by runners for interaction request validation.</summary>
    public static readonly StringName InteractionAccessProviderId = new("interaction");

    /// <summary>Gets or sets the action owner used to resolve this offer.</summary>
    [Export]
    public InteractionOfferSource ActionSource { get; set; } = InteractionOfferSource.Target;

    /// <summary>Gets or sets the stable action identifier resolved from the selected owner.</summary>
    [Export]
    public StringName ActionId { get; set; } = new();

    /// <summary>Gets or sets the contextual input binding created while this offer is focused.</summary>
    [Export]
    public GameplayActionBindingConfig? BindingConfig { get; set; }

    /// <summary>Gets or sets target-side rules specific to this offer.</summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> Rules { get; set; } = new();

    /// <summary>Gets or sets the optional target reservation group for this offer.</summary>
    [Export]
    public StringName TargetConcurrencyGroup { get; set; } = new();

    /// <summary>Gets or sets the presentation while this target is reserved by this requester.</summary>
    [Export]
    public GameplayActionUnavailableKind WhenReservedBySelf { get; set; } =
        GameplayActionUnavailableKind.Blocked;

    /// <summary>Gets or sets the presentation while this target is reserved by another requester.</summary>
    [Export]
    public GameplayActionUnavailableKind WhenReservedByOther { get; set; } =
        GameplayActionUnavailableKind.Blocked;
}

/// <summary>Resolved endpoint for one interaction offer.</summary>
public readonly record struct InteractionOfferResolution(
    GameplayActionComponent Component,
    GameplayAction Action
);
