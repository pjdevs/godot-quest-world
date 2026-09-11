using System.Collections.Generic;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Access;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Rules;
using Godot;
using InteractionPlugin.Runtime.Detection;
using InteractionPlugin.Runtime.Interactor;
using InteractionPlugin.Runtime.Offers;

namespace InteractionPlugin.Runtime.Interactive;

/// <summary>
/// Defines an interactable target, evaluates its rules, and projects authored offers onto gameplay actions.
/// </summary>
/// <remarks>
/// Add this node beside its gameplay node and assign explicit scene references in the Inspector.
/// Availability evaluation is pure and runs anywhere; reserving, executing, completing, and
/// cancelling run on the server or offline host.
/// </remarks>
[GlobalClass]
public partial class InteractiveComponent : Node
{
    /// <summary>
    /// Emitted on any peer whose visible interaction status may have changed.
    /// </summary>
    [Signal]
    public delegate void InteractiveStatusChangedEventHandler();

    /// <summary>Emitted when this peer's visible execution presentation changes.</summary>
    /// <remarks>
    /// It covers slot creation/removal and published or synchronized progress changes. Derived local
    /// progress is pulled by consumers and does not emit once per frame.
    /// </remarks>
    /// <param name="actionId">Action whose execution presentation changed.</param>
    [Signal]
    public delegate void ExecutionPresentationChangedEventHandler(StringName actionId);

    /// <summary>Emitted when the transient target-side reservation snapshot changes.</summary>
    [Signal]
    public delegate void TargetReservationChangedEventHandler();

    /// <summary>Gets or sets the required area that registers interactors in interaction range.</summary>
    [ExportGroup("Interaction")]
    [Export]
    public Area3D? InteractionArea
    {
        get => _interactionArea;
        set
        {
            if (_interactionArea == value)
            {
                return;
            }

            _interactionArea = value;
        }
    }

    /// <summary>Gets or sets the optional wider area used to show interaction indications.</summary>
    [Export]
    public Area3D? IndicationArea { get; set; }

    /// <summary>Gets or sets the required world-space point used for range, focus, and projection.</summary>
    [Export]
    public Node3D? InteractionAnchor { get; set; }

    /// <summary>Gets or sets the gameplay node receiving actions offered by this component.</summary>
    [Export]
    public Node? InvocationTarget { get; set; }

    /// <summary>Resolves the explicit gameplay target, falling back to this component.</summary>
    public Node ResolveInvocationTarget() => InvocationTarget ?? this;

    /// <summary>Gets or sets the distance at which this target may be interacted with, or zero.</summary>
    /// <remarks>
    /// Only a detector that decides range per target reads this — the proximity one. Zero means "use
    /// the detector's default", so an object that has no opinion authors nothing. A target that wants a
    /// <b>shape</b> rather than a radius does not fiddle with this: it uses the area detector, which is
    /// made for that. The choice is made per scene and even per interactor.
    /// </remarks>
    [Export]
    public float InteractionRadius { get; set; }

    /// <summary>Gets or sets the distance at which this target is worth indicating, or zero.</summary>
    /// <remarks>Same contract as <see cref="InteractionRadius"/>, for the wider tier.</remarks>
    [Export]
    public float IndicationRadius { get; set; }

    /// <summary>Gets or sets the player-facing name used by presentation widgets.</summary>
    [Export]
    public string DisplayName { get; set; } = "Interact";

    /// <summary>Gets or sets optional descriptive text included in presentation snapshots.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional prompt scene instantiated once per presented action.</summary>
    /// <remarks>
    /// The focused target shows one prompt widget per presented action. Stacking and the
    /// target-level frame around them belong to <c>InteractionPresenter</c>.
    /// </remarks>
    [Export]
    public PackedScene? ActionPromptScene { get; set; }

    /// <summary>Gets or sets the optional indication scene shown when this target is allowed.</summary>
    [Export]
    public PackedScene? IndicationScene { get; set; }

    /// <summary>
    /// Gets or sets the optional host owning target-sourced actions, assigned before tree entry.
    /// </summary>
    /// <remarks>
    /// Author it beside this node so the relative paths its rules and executors spell out keep the
    /// depth they were written at, and assign it in the Inspector: this node subscribes to its host
    /// when it enters the tree, so a host attached afterwards is never wired.
    /// </remarks>
    [ExportGroup("Actions")]
    [Export]
    public GameplayActionComponent? ActionComponent { get; set; }

    /// <summary>Gets or sets the optional code-owned synchronizer for target reservation state.</summary>
    [Export]
    public InteractionTargetReservationSynchronizer? TargetReservationSynchronizer { get; set; }

    /// <summary>
    /// Gets or sets the ordered invocations this target offers to an interactor.
    /// </summary>
    /// <remarks>
    /// An offer is not an action occurrence. It resolves either the target's host or the requesting
    /// runner's owned host and supplies the invocation target to the generic action pipeline.
    /// </remarks>
    [Export]
    public Godot.Collections.Array<InteractionOffer> Offers { get; set; } = new();

    /// <summary>
    /// Gets or sets the ordered gameplay conditions shared by every action of this target.
    /// Evaluation stops at the first hidden or blocked result, before the action rules run.
    /// </summary>
    [Export]
    public Godot.Collections.Array<GameplayActionRule> TargetRules { get; set; } = new();

    private const string NotConfiguredReason = "Interaction is not configured.";
    private const string AlreadyRunningReason = "This is already in use.";
    private const string SomeoneElseReason = "Someone else is using this.";
    private const string TargetAlreadyReservedReason = "This target is already in use.";
    private const string TargetReservedByOtherReason = "Someone else is using this target.";

    // Every target currently in the tree, for the detectors whose source is not an overlap event. A
    // plain list rather than a Godot group: GetNodesInGroup allocates on every call, and a detector
    // walks this once per frame. It stays retranscriptible in GDExtension, which a static of the
    // plugin trivially is.
    private static readonly List<InteractiveComponent> _registered = new();

    // The reverse of the two area properties, because a physics query answers with a collider and a
    // cast detector resolves every hit it reports, every frame. Keyed by instance id rather than by
    // the object so a freed area still resolves to a removable entry, and filled at registration
    // like the signals the target connects to those very areas: swapping one at runtime is already
    // outside the contract.
    private static readonly Dictionary<ulong, InteractiveComponent> _areaOwners = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private readonly HashSet<InteractionInteractor> _interactionOverlaps = new();
    private readonly HashSet<InteractionInteractor> _indicationOverlaps = new();
    private readonly List<InteractionInteractor> _overlapBuffer = new();
    private readonly Dictionary<StringName, TargetReservation> _targetReservations = new();
    private Area3D? _interactionArea;

    private readonly Dictionary<
        StringName,
        TargetReservationSnapshot
    > _replicatedTargetReservations = new();

    internal bool HasActiveExecution =>
        ActionComponent?.TryGetFirstActiveExecution(out _, out _, out _) == true;

    internal InteractionInteractor? ActiveInteractor
    {
        get
        {
            if (
                ActionComponent?.TryGetFirstActiveExecution(
                    out _,
                    out Node? instigator,
                    out Node? requester
                ) != true
            )
            {
                return null;
            }

            return InteractionInteractor.ResolveForInstigator(instigator);
        }
    }

    /// <summary>Gets whether this peer runs the authoritative half of an interaction.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have only pushes an error and answers no, which would make every
    /// authoritative path refuse itself outside a session.
    /// </remarks>
    /// <summary>Gets every target currently in the tree, in registration order.</summary>
    internal static IReadOnlyList<InteractiveComponent> Registered => _registered;

    /// <summary>Finds the target owning one of the areas a physics query returned.</summary>
    /// <param name="area">Collider reported by a cast or an overlap query.</param>
    /// <returns>The owning target, or null when the area belongs to something else.</returns>
    internal static InteractiveComponent? FindByArea(GodotObject? area)
    {
        return
            area is not null
            && _areaOwners.TryGetValue(area.GetInstanceId(), out InteractiveComponent? owner)
            && IsInstanceValid(owner)
            ? owner
            : null;
    }

    /// <summary>Godot callback that joins the registry the sourceless detectors read.</summary>
    public override void _EnterTree()
    {
        if (!_registered.Contains(this))
        {
            _registered.Add(this);
            IndexArea(InteractionArea);
            IndexArea(IndicationArea);
        }
    }

    /// <summary>Godot callback that validates configuration and connects area and state signals.</summary>
    public override void _Ready()
    {
        if (InteractionArea is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionArea.");
        }

        if (InteractionAnchor is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionAnchor.");
        }

        if (ActionComponent is null && RequiresTargetActionComponent())
        {
            GD.PushError(
                $"{GetPath()}: InteractiveComponent requires a GameplayActionComponent for target offers."
            );
        }

        ConnectActionComponent();

        if (InteractionArea is not null)
        {
            InteractionArea.BodyEntered += OnInteractionAreaBodyEntered;
            InteractionArea.BodyExited += OnInteractionAreaBodyExited;
        }

        if (IndicationArea is not null)
        {
            IndicationArea.BodyEntered += OnIndicationAreaBodyEntered;
            IndicationArea.BodyExited += OnIndicationAreaBodyExited;
        }
    }

    private void ConnectActionComponent()
    {
        if (ActionComponent is null)
        {
            return;
        }

        ActionComponent.ExecutionPresentationChanged += OnExecutionPresentationChanged;
    }

    /// <summary>Resolves one authored offer to the action endpoint it invokes.</summary>
    /// <param name="interactor">Interactor supplying the instigator-owned action host.</param>
    /// <param name="offer">Offer to resolve.</param>
    /// <param name="resolution">Resolved component and action when the offer is valid.</param>
    /// <returns>
    /// Whether the offer names a live, configured action endpoint that is unambiguous for this
    /// interactor.
    /// </returns>
    public bool TryResolveOffer(
        InteractionInteractor interactor,
        InteractionOffer? offer,
        out InteractionOfferResolution resolution
    )
    {
        resolution = default;
        if (!TryResolveOfferEndpoint(interactor, offer, out resolution))
        {
            return false;
        }

        foreach (InteractionOffer candidate in Offers)
        {
            if (
                ReferenceEquals(candidate, offer)
                || !TryResolveOfferEndpoint(
                    interactor,
                    candidate,
                    out InteractionOfferResolution candidateResolution
                )
            )
            {
                continue;
            }

            if (
                candidateResolution.Component == resolution.Component
                && candidateResolution.Action == resolution.Action
            )
            {
                resolution = default;
                return false;
            }
        }

        return true;
    }

    private bool TryResolveOfferEndpoint(
        InteractionInteractor interactor,
        InteractionOffer? offer,
        out InteractionOfferResolution resolution
    )
    {
        resolution = default;
        if (interactor is null || offer is null || offer.ActionId is null || offer.ActionId.IsEmpty)
        {
            return false;
        }

        GameplayActionComponent? component = offer.ActionSource switch
        {
            InteractionOfferSource.Target => ActionComponent,
            InteractionOfferSource.Instigator => interactor.Runner?.OwnedActionComponent,
            _ => null,
        };
        GameplayAction? action = component?.ResolveAction(offer.ActionId);
        if (component is null || action is null)
        {
            return false;
        }

        resolution = new InteractionOfferResolution(component, action);
        return true;
    }

    /// <summary>
    /// Acquires the optional target-side reservation declared by one resolved offer.
    /// </summary>
    /// <remarks>
    /// The lease is intentionally acquired by the Gameplay Action request pipeline, after access
    /// validation and immediately before the action owner starts execution. Empty groups do not
    /// claim the target and return a successful null lease.
    /// </remarks>
    internal bool TryAcquireTargetReservation(
        InteractionInteractor interactor,
        InteractionOffer offer,
        GameplayActionComponent component,
        GameplayAction action,
        out IGameplayActionRequestReservation? reservation
    )
    {
        reservation = null;
        StringName group = offer.TargetConcurrencyGroup;
        if (group is null || group.IsEmpty)
        {
            return true;
        }

        if (!IsAuthoritative)
        {
            return false;
        }

        if (_targetReservations.ContainsKey(group))
        {
            return false;
        }

        TargetReservation claim = new(
            this,
            group,
            component,
            action,
            interactor,
            interactor.Runner?.OwnerPeerId ?? 1
        );
        _targetReservations.Add(group, claim);
        reservation = claim.Lease;
        NotifyTargetReservationChanged();
        return true;
    }

    internal GameplayActionAvailability EvaluateTargetReservation(
        InteractionInteractor interactor,
        InteractionOffer offer
    )
    {
        StringName group = offer.TargetConcurrencyGroup;
        if (group is null || group.IsEmpty)
        {
            return new GameplayActionAllowed();
        }

        TargetReservationRelation relation = GetTargetReservationRelation(interactor, group);
        return relation switch
        {
            TargetReservationRelation.ReservedBySelf => offer.WhenReservedBySelf.ToAvailability(
                TargetAlreadyReservedReason
            ),
            TargetReservationRelation.ReservedByOther => offer.WhenReservedByOther.ToAvailability(
                TargetReservedByOtherReason
            ),
            _ => new GameplayActionAllowed(),
        };
    }

    internal Godot.Collections.Array<Godot.Collections.Dictionary<
        string,
        Variant
    >> BuildTargetReservationEntries()
    {
        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries = new();
        foreach (TargetReservation claim in _targetReservations.Values)
        {
            entries.Add(claim.ToEntry());
        }

        return entries;
    }

    internal void ApplyTargetReservationEntries(
        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries
    )
    {
        _replicatedTargetReservations.Clear();
        foreach (Godot.Collections.Dictionary<string, Variant> entry in entries)
        {
            if (!TargetReservationSnapshot.TryRead(entry, out TargetReservationSnapshot snapshot))
            {
                continue;
            }

            _replicatedTargetReservations[snapshot.Group] = snapshot;
        }

        NotifyStatusChanged();
        EmitSignal(SignalName.TargetReservationChanged);
    }

    private TargetReservationRelation GetTargetReservationRelation(
        InteractionInteractor interactor,
        StringName group
    )
    {
        if (IsAuthoritative)
        {
            return _targetReservations.TryGetValue(group, out TargetReservation? claim)
                ? claim.IsOwnedBy(interactor)
                    ? TargetReservationRelation.ReservedBySelf
                    : TargetReservationRelation.ReservedByOther
                : TargetReservationRelation.Unclaimed;
        }

        return _replicatedTargetReservations.TryGetValue(
            group,
            out TargetReservationSnapshot snapshot
        )
            ? snapshot.IsOwnedBy(interactor)
                ? TargetReservationRelation.ReservedBySelf
                : TargetReservationRelation.ReservedByOther
            : TargetReservationRelation.Unclaimed;
    }

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    private void ReleaseTargetReservation(TargetReservation claim)
    {
        if (claim.Released)
        {
            return;
        }

        claim.Released = true;
        if (
            _targetReservations.TryGetValue(claim.Group, out TargetReservation? current)
            && ReferenceEquals(current, claim)
        )
        {
            _targetReservations.Remove(claim.Group);
            NotifyTargetReservationChanged();
        }
    }

    private void NotifyTargetReservationChanged()
    {
        EmitSignal(SignalName.TargetReservationChanged);
        NotifyStatusChanged();
    }

    /// <summary>Finds the authored offer that resolves to one requested action endpoint.</summary>
    internal bool TryResolveOfferForEndpoint(
        InteractionInteractor interactor,
        GameplayActionComponent component,
        GameplayAction action,
        out InteractionOffer? offer
    )
    {
        foreach (InteractionOffer candidate in Offers)
        {
            if (
                candidate is not null
                && TryResolveOffer(interactor, candidate, out InteractionOfferResolution resolution)
                && resolution.Component == component
                && resolution.Action == action
            )
            {
                offer = candidate;
                return true;
            }
        }

        offer = null;
        return false;
    }

    /// <summary>Evaluates one authored offer without mutating the resolved action's rules.</summary>
    public GameplayActionAvailability EvaluateAvailability(
        InteractionInteractor interactor,
        InteractionOffer offer
    )
    {
        if (
            interactor is null
            || InteractionArea is null
            || InteractionAnchor is null
            || offer is null
            || !Offers.Contains(offer)
            || !TryResolveOffer(interactor, offer, out InteractionOfferResolution resolution)
            || resolution.Action.Definition is null
            || resolution.Action.Executor is null
        )
        {
            return new GameplayActionBlocked(NotConfiguredReason);
        }

        Node interactionInstigator = interactor.Runner?.ResolveInstigator() ?? interactor;
        GameplayActionContext context = resolution.Component.CreateContext(
            0ul,
            interactionInstigator,
            interactor.Runner,
            resolution.Action,
            ResolveInvocationTarget()
        );
        GameplayActionAvailability targetAvailability = EvaluateRules(TargetRules, context);
        if (targetAvailability is not GameplayActionAllowed)
        {
            return targetAvailability;
        }

        GameplayActionAvailability offerAvailability = EvaluateRules(offer.Rules, context);
        if (offerAvailability is not GameplayActionAllowed)
        {
            return offerAvailability;
        }

        GameplayActionAvailability targetReservationAvailability = EvaluateTargetReservation(
            interactor,
            offer
        );
        if (targetReservationAvailability is not GameplayActionAllowed)
        {
            return targetReservationAvailability;
        }

        GameplayActionAvailability actionAvailability = resolution.Component.EvaluateAction(
            resolution.Action.Definition.Id,
            interactionInstigator,
            interactor.Runner,
            ResolveInvocationTarget()
        );
        if (actionAvailability is not GameplayActionAllowed)
        {
            return actionAvailability;
        }

        StringName concurrencyGroup = resolution.Action.GetHostConcurrencyGroup();
        bool concurrencyActive;
        bool startedByInteractor;
        if (resolution.Component.IsAuthoritative)
        {
            concurrencyActive =
                resolution.Component.IsActionExecuting(resolution.Action.Definition.Id)
                || resolution.Component.IsConcurrencyGroupExecuting(concurrencyGroup);
            startedByInteractor =
                concurrencyActive
                && resolution.Component.IsConcurrencyGroupExecutingFor(
                    concurrencyGroup,
                    interactionInstigator
                );
        }
        else
        {
            concurrencyActive = resolution.Component.TryGetExecutionPresentationInGroup(
                concurrencyGroup,
                out GameplayActionExecutionPresentation execution
            );
            startedByInteractor =
                concurrencyActive
                && execution.Relation == GameplayActionExecutionRelation.RequestedLocally;
        }

        if (!concurrencyActive)
        {
            return new GameplayActionAllowed();
        }

        GameplayActionUnavailableKind kind = startedByInteractor
            ? resolution.Action.WhenExecutingBySelf
            : resolution.Action.WhenExecutingByOther;
        return kind.ToAvailability(startedByInteractor ? AlreadyRunningReason : SomeoneElseReason);
    }

    /// <summary>Re-validates an already-running offer without treating its own reservations as lost access.</summary>
    /// <remarks>
    /// Sustained access re-runs spatial, target, and offer rules. The action rules already admitted
    /// the execution and are not re-evaluated here: an action commonly changes the very state that
    /// made it available while it is running. The target and action-owner busy checks are also skipped
    /// because both reservations belong to the request currently being validated.
    /// </remarks>
    internal GameplayActionAvailability EvaluateAccess(
        InteractionInteractor interactor,
        InteractionOffer offer,
        bool sustained
    )
    {
        if (!sustained)
        {
            return EvaluateAvailability(interactor, offer);
        }

        if (
            interactor is null
            || InteractionArea is null
            || InteractionAnchor is null
            || offer is null
            || !Offers.Contains(offer)
            || !TryResolveOffer(interactor, offer, out InteractionOfferResolution resolution)
            || resolution.Action.Definition is null
            || resolution.Action.Executor is null
        )
        {
            return new GameplayActionBlocked(NotConfiguredReason);
        }

        Node interactionInstigator = interactor.Runner?.ResolveInstigator() ?? interactor;
        GameplayActionContext context = resolution.Component.CreateContext(
            0ul,
            interactionInstigator,
            interactor.Runner,
            resolution.Action,
            ResolveInvocationTarget()
        );
        GameplayActionAvailability targetAvailability = EvaluateRules(TargetRules, context);
        if (targetAvailability is not GameplayActionAllowed)
        {
            return targetAvailability;
        }

        GameplayActionAvailability offerAvailability = EvaluateRules(offer.Rules, context);
        if (offerAvailability is not GameplayActionAllowed)
        {
            return offerAvailability;
        }

        StringName group = offer.TargetConcurrencyGroup;
        if (
            group is not null
            && !group.IsEmpty
            && GetTargetReservationRelation(interactor, group)
                != TargetReservationRelation.ReservedBySelf
        )
        {
            return new GameplayActionBlocked(TargetReservedByOtherReason);
        }

        return new GameplayActionAllowed();
    }

    private bool RequiresTargetActionComponent()
    {
        foreach (InteractionOffer offer in Offers)
        {
            if (offer?.ActionSource == InteractionOfferSource.Target)
            {
                return true;
            }
        }

        return false;
    }

    private void OnExecutionPresentationChanged(StringName actionId) =>
        EmitSignal(SignalName.ExecutionPresentationChanged, actionId);

    /// <summary>Aggregates the availability of every offered action into one target-level result.</summary>
    /// <remarks>
    /// Allowed wins over blocked, and blocked over hidden, so a target is presentable as long as one
    /// offer is requestable or explained. A target without any offer is hidden.
    /// The evaluation and every rule are side-effect free; world state influences the result only
    /// through an explicit rule.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <returns>Allowed when one action is allowed, the first blocked result, or hidden.</returns>
    public GameplayActionAvailability EvaluateAvailability(InteractionInteractor interactor)
    {
        if (Offers.Count > 0)
        {
            GameplayActionAvailability offerAggregate = new GameplayActionHidden();
            foreach (InteractionOffer offer in Offers)
            {
                if (offer is null)
                {
                    continue;
                }

                GameplayActionAvailability availability = EvaluateAvailability(interactor, offer);
                if (availability is GameplayActionAllowed)
                {
                    return availability;
                }

                if (availability is GameplayActionBlocked && offerAggregate is GameplayActionHidden)
                {
                    offerAggregate = availability;
                }
            }

            return offerAggregate;
        }

        return new GameplayActionHidden();
    }

    private static GameplayActionAvailability EvaluateRules(
        Godot.Collections.Array<GameplayActionRule> rules,
        in GameplayActionContext context
    )
    {
        foreach (GameplayActionRule rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            GameplayActionAvailability availability = rule.Evaluate(context);
            if (availability is not GameplayActionAllowed)
            {
                return availability;
            }
        }

        return new GameplayActionAllowed();
    }

    /// <summary>Builds the local presentation snapshot for prompt or indication widgets.</summary>
    /// <remarks>
    /// One entry is produced per presentable action, in declaration order. Hidden actions are
    /// omitted; blocked ones are kept so a prompt can explain them.
    /// </remarks>
    /// <param name="interactor">Interactor viewing this target.</param>
    /// <param name="isFocused">Whether this target currently owns focus.</param>
    /// <returns>A fresh snapshot including the currently evaluated actions.</returns>
    public InteractionTargetPresentation GetPresentation(
        InteractionInteractor interactor,
        bool isFocused
    ) => GetOfferPresentation(interactor, isFocused);

    /// <summary>Gets whether this target currently offers at least one presentable action.</summary>
    /// <remarks>
    /// Focus and indication use this instead of a target-wide availability: a target whose actions
    /// are all hidden is ignored entirely rather than presented as unavailable.
    /// </remarks>
    /// <param name="interactor">Interactor viewing this target.</param>
    /// <returns><see langword="true"/> when one action is allowed or blocked.</returns>
    public bool HasVisibleAction(InteractionInteractor interactor)
    {
        foreach (InteractionOffer offer in Offers)
        {
            if (offer is not null && TryGetOfferPresentation(interactor, offer, out _))
            {
                return true;
            }
        }

        return false;
    }

    private InteractionTargetPresentation GetOfferPresentation(
        InteractionInteractor interactor,
        bool isFocused
    )
    {
        List<GameplayActionPresentation> presentedActions = new();
        List<InteractionOffer?> presentedOffers = new();
        foreach (InteractionOffer offer in Offers)
        {
            if (
                offer is not null
                && TryGetOfferPresentation(interactor, offer, out var presentation)
            )
            {
                presentedActions.Add(presentation);
                presentedOffers.Add(offer);
            }
        }

        return new InteractionTargetPresentation(
            this,
            DisplayName,
            Description,
            presentedActions,
            isFocused,
            interactor.Detector?.GetInteractionDistance(this) ?? 0.0f,
            presentedOffers
        );
    }

    private bool TryGetOfferPresentation(
        InteractionInteractor interactor,
        InteractionOffer offer,
        out GameplayActionPresentation presentation
    )
    {
        presentation = default;
        if (
            offer.BindingConfig is not GameplayActionBindingConfig config
            || !TryResolveOffer(interactor, offer, out InteractionOfferResolution resolution)
            || resolution.Action.Definition is null
        )
        {
            return false;
        }

        GameplayActionAvailability availability = EvaluateAvailability(interactor, offer);
        if (availability is GameplayActionHidden)
        {
            return false;
        }

        float? holdProgress = null;
        float? holdElapsed = null;
        if (
            interactor.Runner is not null
            && interactor.Runner.TryGetBinding(
                resolution.Component,
                resolution.Action.Definition.Id,
                this,
                out GameplayActionBinding? binding
            )
            && binding is not null
            && interactor.Runner.TryGetBindingHoldProgress(
                binding.Id,
                out float currentProgress,
                out float currentElapsed
            )
        )
        {
            holdProgress = currentProgress;
            holdElapsed = currentElapsed;
        }

        presentation = new GameplayActionPresentation(
            resolution.Action.Definition.Id,
            resolution.Action.Definition.Label,
            resolution.Action.Definition.Description,
            config.InputActionName,
            availability,
            config.ActivationMode,
            holdProgress,
            holdElapsed
        );
        return true;
    }

    /// <summary>Gets the execution presentations visible on this peer.</summary>
    /// <remarks>
    /// The returned snapshot is ordered by the action owner's declarations, not by execution start time. Progress
    /// is resolved lazily from a local source, a linear transport sample, or a published value.
    /// </remarks>
    /// <returns>A fresh action-ordered snapshot of the visible active executions.</returns>
    public IReadOnlyList<GameplayActionExecutionPresentation> GetExecutionPresentations()
    {
        return ActionComponent?.GetExecutionPresentations()
            ?? System.Array.Empty<GameplayActionExecutionPresentation>();
    }

    /// <summary>Looks up the visible execution presentation for one action identifier.</summary>
    /// <param name="actionId">Stable identifier of the action to look up.</param>
    /// <param name="presentation">Visible execution snapshot when one exists.</param>
    /// <returns><see langword="true"/> when this target has a matching visible execution.</returns>
    public bool TryGetExecutionPresentation(
        StringName actionId,
        out GameplayActionExecutionPresentation presentation
    )
    {
        presentation = default;
        if (
            ActionComponent is null
            || !ActionComponent.TryGetExecutionPresentation(
                actionId,
                out GameplayActionExecutionPresentation current
            )
        )
        {
            return false;
        }

        presentation = current;
        return true;
    }

    /// <summary>Looks up an execution presentation through the action owner of an authored offer.</summary>
    public bool TryGetExecutionPresentation(
        InteractionInteractor interactor,
        InteractionOffer offer,
        out GameplayActionExecutionPresentation presentation
    )
    {
        presentation = default;
        return TryResolveOffer(interactor, offer, out InteractionOfferResolution resolution)
            && resolution.Component.TryGetExecutionPresentation(
                resolution.Action.Definition?.Id ?? new StringName(),
                out presentation
            )
            && presentation.Target == this;
    }

    internal void NotifyStatusChanged()
    {
        EmitSignal(SignalName.InteractiveStatusChanged);
        PurgeInvalidInteractors();
        foreach (InteractionInteractor interactor in _presentInteractors)
        {
            interactor.NotifyInteractiveStatusChanged(this);
        }
    }

    internal void RegisterInteractor(InteractionInteractor interactor)
    {
        _presentInteractors.Add(interactor);
    }

    internal void UnregisterInteractor(InteractionInteractor interactor)
    {
        _presentInteractors.Remove(interactor);
    }

    /// <summary>Gets the world-space point used by focus scoring, range validation, and UI projection.</summary>
    /// <returns>The configured anchor position, or <see cref="Vector3.Zero"/> before configuration.</returns>
    public Vector3 GetInteractionPosition()
    {
        return InteractionAnchor?.GlobalPosition ?? Vector3.Zero;
    }

    /// <summary>Tells whether one collider reported by a query belongs to this target itself.</summary>
    /// <remarks>
    /// This is what "excluding the target's colliders" means for the line of sight ray: a target does
    /// not occlude itself, and an anchor authored inside the mesh that carries it must still be
    /// visible. The whole scene of the target counts, because the geometry and the anchor are siblings
    /// under it.
    /// </remarks>
    /// <param name="collider">Collider a physics query reported.</param>
    /// <returns>Whether the collider is part of this target.</returns>
    internal bool OwnsCollider(GodotObject? collider)
    {
        if (collider is not Node node)
        {
            return false;
        }

        Node root = GetParent() ?? this;
        return root == node || root.IsAncestorOf(node);
    }

    // Two targets sharing one area is a configuration error rather than a model, so the first
    // registration wins, exactly like the walk this replaces did.
    private void IndexArea(Area3D? area)
    {
        if (area is not null)
        {
            _areaOwners.TryAdd(area.GetInstanceId(), this);
        }
    }

    private void ForgetArea(Area3D? area)
    {
        if (
            area is not null
            && _areaOwners.TryGetValue(area.GetInstanceId(), out InteractiveComponent? owner)
            && owner == this
        )
        {
            _areaOwners.Remove(area.GetInstanceId());
        }
    }

    /// <summary>Godot callback that disconnects state and interactor registrations.</summary>
    public override void _ExitTree()
    {
        if (ActionComponent is not null && IsInstanceValid(ActionComponent))
        {
            ActionComponent.ExecutionPresentationChanged -= OnExecutionPresentationChanged;
        }

        _registered.Remove(this);
        ForgetArea(InteractionArea);
        ForgetArea(IndicationArea);
        PurgeInvalidInteractors();

        // An area cannot report the overlap it loses by being freed, so every interactor that holds
        // this target — through its detector or through a registration — is told explicitly.
        HashSet<InteractionInteractor> holders = new(_presentInteractors);
        holders.UnionWith(_interactionOverlaps);
        holders.UnionWith(_indicationOverlaps);
        foreach (InteractionInteractor interactor in holders)
        {
            interactor.NotifyInteractiveRemoved(this);
        }

        _presentInteractors.Clear();
        _interactionOverlaps.Clear();
        _indicationOverlaps.Clear();
        _targetReservations.Clear();
        _replicatedTargetReservations.Clear();
        if (IsAuthoritative)
        {
            EmitSignal(SignalName.TargetReservationChanged);
        }
    }

    private void OnInteractionAreaBodyEntered(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Interactible, entered: true);

    private void OnInteractionAreaBodyExited(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Interactible, entered: false);

    private void OnIndicationAreaBodyEntered(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Indicated, entered: true);

    private void OnIndicationAreaBodyExited(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Indicated, entered: false);

    /// <summary>Pushes one overlap change of an area this target owns to the interactors involved.</summary>
    /// <remarks>
    /// The areas belong to the target, so the target is the only one able to report them, and it does
    /// so on every peer: an authoritative validation reads the very same overlap the owning client
    /// detected with. A detector that has a source of its own simply ignores the call.
    /// </remarks>
    private void NotifyOverlapChanged(Node3D body, InteractionDetectionKind kind, bool entered)
    {
        HashSet<InteractionInteractor> overlaps =
            kind == InteractionDetectionKind.Interactible
                ? _interactionOverlaps
                : _indicationOverlaps;

        _overlapBuffer.Clear();
        CollectInteractors(body, _overlapBuffer);
        foreach (InteractionInteractor interactor in _overlapBuffer)
        {
            if (entered ? !overlaps.Add(interactor) : !overlaps.Remove(interactor))
            {
                continue;
            }

            if (interactor.Detector is not InteractionDetector detector)
            {
                continue;
            }

            if (entered)
            {
                detector.OnEnteredTargetArea(this, kind);
            }
            else
            {
                detector.OnExitedTargetArea(this, kind);
            }
        }
    }

    private static void CollectInteractors(Node node, List<InteractionInteractor> interactors)
    {
        if (node is InteractionInteractor interactor)
        {
            interactors.Add(interactor);
        }

        foreach (Node child in node.GetChildren())
        {
            CollectInteractors(child, interactors);
        }
    }

    private void PurgeInvalidInteractors()
    {
        _presentInteractors.RemoveWhere(interactor => !IsInstanceValid(interactor));
        _interactionOverlaps.RemoveWhere(interactor => !IsInstanceValid(interactor));
        _indicationOverlaps.RemoveWhere(interactor => !IsInstanceValid(interactor));
    }

    private enum TargetReservationRelation
    {
        Unclaimed,
        ReservedBySelf,
        ReservedByOther,
    }

    private sealed class TargetReservation
    {
        public TargetReservation(
            InteractiveComponent owner,
            StringName group,
            GameplayActionComponent component,
            GameplayAction action,
            InteractionInteractor interactor,
            int requesterPeerId
        )
        {
            Owner = owner;
            Group = group;
            Component = component;
            Action = action;
            Interactor = interactor;
            RequesterPeerId = requesterPeerId;
            Lease = new TargetReservationLease(owner);
            Lease.Attach(this);
        }

        public InteractiveComponent Owner { get; }
        public StringName Group { get; }
        public GameplayActionComponent Component { get; }
        public GameplayAction Action { get; }
        public InteractionInteractor Interactor { get; }
        public int RequesterPeerId { get; }
        public ulong ExecutionId { get; private set; }
        public bool Released { get; set; }
        public TargetReservationLease Lease { get; }

        public bool IsOwnedBy(InteractionInteractor interactor)
        {
            return Owner.IsAuthoritative
                ? Interactor == interactor
                : interactor.Runner?.OwnerPeerId == RequesterPeerId;
        }

        public Godot.Collections.Dictionary<string, Variant> ToEntry() =>
            new()
            {
                ["group"] = Group,
                ["action_id"] = Action.Definition?.Id ?? new StringName(),
                ["execution_id"] = checked((long)ExecutionId),
                ["requester_peer_id"] = RequesterPeerId,
            };

        public void BindExecution(ulong executionId) => ExecutionId = executionId;
    }

    private sealed class TargetReservationLease(InteractiveComponent owner)
        : IGameplayActionRequestReservation
    {
        private readonly InteractiveComponent _owner = owner;
        private TargetReservation? _claim;

        public void Attach(TargetReservation claim) => _claim = claim;

        public void BindExecution(ulong executionId)
        {
            if (_claim is not null && !_claim.Released)
            {
                _claim.BindExecution(executionId);
            }
        }

        public void Release()
        {
            if (_claim is not null)
            {
                _owner.ReleaseTargetReservation(_claim);
            }
        }
    }

    private readonly record struct TargetReservationSnapshot(
        StringName Group,
        StringName ActionId,
        ulong ExecutionId,
        int RequesterPeerId
    )
    {
        public bool IsOwnedBy(InteractionInteractor interactor) =>
            interactor.Runner?.OwnerPeerId == RequesterPeerId;

        public static bool TryRead(
            Godot.Collections.Dictionary<string, Variant> entry,
            out TargetReservationSnapshot snapshot
        )
        {
            snapshot = default;
            if (
                !entry.TryGetValue("group", out Variant groupValue)
                || !entry.TryGetValue("action_id", out Variant actionValue)
                || !entry.TryGetValue("execution_id", out Variant executionValue)
                || !entry.TryGetValue("requester_peer_id", out Variant requesterValue)
                || groupValue.VariantType != Variant.Type.StringName
                || actionValue.VariantType != Variant.Type.StringName
                || executionValue.VariantType != Variant.Type.Int
                || requesterValue.VariantType != Variant.Type.Int
            )
            {
                return false;
            }

            long executionId = executionValue.AsInt64();
            long requesterPeerId = requesterValue.AsInt64();
            if (executionId < 0 || requesterPeerId <= 0)
            {
                return false;
            }

            snapshot = new TargetReservationSnapshot(
                groupValue.AsStringName(),
                actionValue.AsStringName(),
                (ulong)executionId,
                checked((int)requesterPeerId)
            );
            return !snapshot.Group.IsEmpty;
        }
    }
}
