using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Interactor;

internal readonly record struct FocusChangeResult(
    InteractiveComponent? Previous,
    InteractiveComponent? Current,
    bool Changed
);

/// <summary>Execution this interactor reserved on a target, tracked on the authoritative peer.</summary>
/// <remarks>
/// The interactor keeps the identifier so it can end its own executions without re-resolving
/// anything. It never ends an execution it does not own, because it only knows about its own.
/// </remarks>
/// <param name="Interactive">Target holding the reservation.</param>
/// <param name="Action">Action that reserved it.</param>
/// <param name="Id">Identifier allocated by the target.</param>
internal readonly record struct InteractorExecution(
    InteractiveComponent Interactive,
    InteractionAction Action,
    ulong Id
);

/// <summary>Hold in progress on one input, resolved entirely on the requesting client.</summary>
/// <param name="Target">Target focused when the input was pressed.</param>
/// <param name="Input">Project input action being held.</param>
/// <param name="Threshold">Longest hold the target asks for on that input.</param>
/// <param name="Elapsed">Seconds the input has been held.</param>
internal readonly record struct InteractionGesture(
    InteractiveComponent Target,
    StringName Input,
    float Threshold,
    float Elapsed
)
{
    /// <summary>Gets how far the hold has progressed towards its threshold.</summary>
    public float Progress => Threshold > 0.0f ? Mathf.Clamp(Elapsed / Threshold, 0.0f, 1.0f) : 1.0f;
}

/// <summary>Local prediction of the running execution the owning player requested.</summary>
/// <remarks>
/// Built from the duration authored in the scene the client already has, so a progress bar needs no
/// replication and no acknowledgement. It is feedback only: what actually runs is the authoritative
/// execution, and a prediction that drifts is corrected by the world state it is waiting for.
/// </remarks>
/// <param name="Target">Target the action was requested on.</param>
/// <param name="ActionId">Identifier of the requested action.</param>
/// <param name="Duration">Duration authored for that action.</param>
/// <param name="Elapsed">Seconds since the request was sent.</param>
internal readonly record struct PredictedExecution(
    InteractiveComponent Target,
    StringName ActionId,
    float Duration,
    float Elapsed
)
{
    /// <summary>Gets how far the predicted execution has progressed.</summary>
    public float Progress => Duration > 0.0f ? Mathf.Clamp(Elapsed / Duration, 0.0f, 1.0f) : 0.0f;
}

/// <summary>
/// Detects interaction targets, selects local focus, and routes input intentions to the server.
/// </summary>
/// <remarks>
/// Add one instance to each interacting character. Focus and presentation run only for
/// <see cref="OwnerPeerId"/>, while authoritative validation and gameplay dispatch run on the server.
/// </remarks>
[GlobalClass]
public partial class InteractionInteractor : Node
{
    private const string ReleasedReason = "The interaction input was released.";
    private const string InteractorLostReason = "The interactor left the interaction.";

    /// <summary>Emitted locally when the best target changes.</summary>
    /// <param name="interactive">New focused interactive, or null when focus is cleared.</param>
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    /// <summary>Emitted locally when the presentation of a visible target may have changed.</summary>
    /// <remarks>
    /// The signal is a notification only. Availability is carried per action, so a consumer reads
    /// the fresh snapshot from <see cref="InteractiveComponent.GetPresentation"/> instead of relying
    /// on a target-wide summary.
    /// </remarks>
    /// <param name="interactive">Interactive whose presentation may have changed.</param>
    [Signal]
    public delegate void InteractionStatusChangedEventHandler(Node interactive);

    /// <summary>Emitted locally after prevalidation and before any client RPC or host dispatch.</summary>
    /// <param name="interactive">Target requested by the owning player.</param>
    /// <param name="actionId">Identifier of the action resolved from the local input.</param>
    [Signal]
    public delegate void InteractionRequestedEventHandler(Node interactive, StringName actionId);

    /// <summary>Emitted locally for a prevalidation failure or a rejection returned by the server.</summary>
    /// <remarks>
    /// The refusal carries the action so presentation can attach it to the right prompt instead of
    /// to the whole target. The identifier is empty when no action could be resolved at all.
    /// </remarks>
    /// <param name="interactive">Rejected target, or null when no target can be resolved.</param>
    /// <param name="actionId">Identifier of the rejected action, or an empty name.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Signal]
    public delegate void InteractionRejectedEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    /// <summary>Emitted when an interactive enters the optional indication area.</summary>
    /// <param name="interactive">Interactive available for indication presentation.</param>
    [Signal]
    public delegate void InteractiveIndicationAddedEventHandler(Node interactive);

    /// <summary>Emitted when an interactive leaves the optional indication area.</summary>
    /// <param name="interactive">Interactive removed from indication presentation.</param>
    [Signal]
    public delegate void InteractiveIndicationRemovedEventHandler(Node interactive);

    /// <summary>Gets or sets the required view transform used for angle and alignment scoring.</summary>
    [ExportGroup("Detection")]
    [Export]
    public Node3D? ViewOrigin
    {
        get => _viewOrigin;
        set
        {
            if (_viewOrigin == value)
            {
                return;
            }

            _viewOrigin = value;
        }
    }

    /// <summary>
    /// Gets or sets the optional physical origin used for distance checks. Defaults to the Node3D parent.
    /// </summary>
    [Export]
    public Node3D? InteractionOrigin { get; set; }

    /// <summary>Gets or sets the maximum authoritative interaction distance in world units.</summary>
    [Export]
    public float MaxInteractionDistance { get; set; } = 10.0f;

    /// <summary>Gets or sets the maximum view angle accepted for focus and server validation.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxInteractionAngleDegrees { get; set; } = 30.0f;

    /// <summary>Gets or sets how strongly distance reduces the focus score relative to alignment.</summary>
    [Export]
    public float DistanceScoreCoefficient { get; set; } = 0.5f;

    /// <summary>Gets or sets the server peer that receives reliable interaction RPCs.</summary>
    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId { get; set; } = 1;

    /// <summary>Gets or sets the peer allowed to control this interactor.</summary>
    [Export]
    public int OwnerPeerId { get; set; } = 1;

    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();
    private readonly HashSet<InteractiveComponent> _interactiveCandidates = new();
    private readonly List<InteractorExecution> _ownedExecutions = new();
    private readonly HashSet<StringName> _sustainedInputs = new();
    private Node3D? _viewOrigin;
    private Node3D? _resolvedInteractionOrigin;
    private InteractiveComponent? _focusedInteractive;
    private InteractiveComponent? _automaticTarget;
    private StringName? _automaticActionId;
    private InteractionGesture? _gesture;
    private PredictedExecution? _prediction;
    private bool _hasKnownLocalControl;
    private bool _lastKnownLocalControl;

    /// <summary>Gets the target currently selected by the owning peer.</summary>
    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

    /// <summary>Gets whether this peer owns focus calculation, input requests, and presentation.</summary>
    public bool IsLocallyControlled
    {
        get
        {
            if (Multiplayer is null || Multiplayer.MultiplayerPeer is null)
            {
                return _hasKnownLocalControl ? _lastKnownLocalControl : OwnerPeerId == 1;
            }

            _lastKnownLocalControl = OwnerPeerId == (int)Multiplayer.GetUniqueId();
            _hasKnownLocalControl = true;
            return _lastKnownLocalControl;
        }
    }

    /// <summary>Godot callback that resolves origins and keeps node authority on the server.</summary>
    public override void _Ready()
    {
        _resolvedInteractionOrigin = InteractionOrigin ?? GetParent() as Node3D;
        if (ViewOrigin is null)
        {
            GD.PushError($"{GetPath()}: InteractionInteractor requires a ViewOrigin.");
            SetProcess(false);
            return;
        }

        if (_resolvedInteractionOrigin is null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionInteractor requires an InteractionOrigin or a Node3D parent."
            );
            SetProcess(false);
            return;
        }

        if (OwnerPeerId <= 0)
        {
            OwnerPeerId =
                Multiplayer is null || Multiplayer.MultiplayerPeer is null
                    ? 1
                    : (int)Multiplayer.GetUniqueId();
        }

        SetMultiplayerAuthority(ServerPeerId);
    }

    /// <summary>Godot callback that recalculates focus for the owning peer each frame.</summary>
    public override void _Process(double delta)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        RecalculateFocus();
        AdvanceGesture((float)delta);
        AdvancePrediction((float)delta);
    }

    /// <summary>Reads how far the hold in progress is towards selecting its action.</summary>
    /// <remarks>
    /// Local feedback for a gesture widget. This is the selection layer, not the action: an action
    /// the player must stay engaged in reports its progress through
    /// <see cref="TryGetExecutionProgress"/> instead.
    /// </remarks>
    /// <param name="inputActionName">Input being held, or an empty name when none is.</param>
    /// <param name="progress">Progress between zero and one.</param>
    /// <returns><see langword="true"/> while an input is being held towards a threshold.</returns>
    public bool TryGetGestureProgress(out StringName inputActionName, out float progress)
    {
        inputActionName = _gesture?.Input ?? new StringName(string.Empty);
        progress = _gesture?.Progress ?? 0.0f;
        return _gesture is not null;
    }

    /// <summary>Reads how far the action requested by the owning player has progressed.</summary>
    /// <remarks>
    /// Predicted from the duration authored in the scene, so it works the same on a client and on a
    /// host. The authoritative clock stays on the target: this only decides what the bar draws.
    /// </remarks>
    /// <param name="actionId">Running action, or an empty name when none is.</param>
    /// <param name="progress">Progress between zero and one.</param>
    /// <returns><see langword="true"/> while a timed action requested by this peer is running.</returns>
    public bool TryGetExecutionProgress(out StringName actionId, out float progress)
    {
        actionId = _prediction?.ActionId ?? new StringName(string.Empty);
        progress = _prediction?.Progress ?? 0.0f;
        return _prediction is not null;
    }

    private void AdvanceGesture(float delta)
    {
        if (_gesture is not InteractionGesture gesture)
        {
            return;
        }

        if (!IsUsable(gesture.Target) || _focusedInteractive != gesture.Target)
        {
            // Looking away abandons the hold: the action it was selecting is no longer the one the
            // player is pointing at.
            _gesture = null;
            return;
        }

        float elapsed = gesture.Elapsed + delta;
        if (elapsed < gesture.Threshold)
        {
            _gesture = gesture with { Elapsed = elapsed };
            return;
        }

        // A sustained action is held down by the very key that selected it, so it has to start at
        // the threshold: starting on release would end it the instant it began.
        _gesture = null;
        RequestResolvedAction(gesture.Target, gesture.Input, elapsed);
    }

    private void AdvancePrediction(float delta)
    {
        if (_prediction is not PredictedExecution prediction)
        {
            return;
        }

        float elapsed = prediction.Elapsed + delta;
        _prediction =
            elapsed >= prediction.Duration || !IsUsable(prediction.Target)
                ? null
                : prediction with
                {
                    Elapsed = elapsed,
                };
    }

    internal void AddInteractiveIndication(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive) || !_indicatedInteractives.Add(interactive))
        {
            return;
        }

        interactive.RegisterInteractor(this);
        EmitSignal(SignalName.InteractiveIndicationAdded, interactive);
        if (IsLocallyControlled)
        {
            EmitStatusFor(interactive);
        }
    }

    internal void RemoveInteractiveIndication(InteractiveComponent interactive)
    {
        if (!_indicatedInteractives.Remove(interactive))
        {
            return;
        }

        EmitSignal(SignalName.InteractiveIndicationRemoved, interactive);
        if (!_interactiveCandidates.Contains(interactive))
        {
            interactive.UnregisterInteractor(this);
        }
    }

    internal void AddInteractive(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive) || !_interactiveCandidates.Add(interactive))
        {
            return;
        }

        interactive.RegisterInteractor(this);
        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
    }

    internal void RemoveInteractive(InteractiveComponent interactive)
    {
        if (!_interactiveCandidates.Remove(interactive))
        {
            return;
        }

        if (Multiplayer.IsServer())
        {
            CancelOwnedExecutions(interactive, inputActionName: null, InteractorLostReason);
        }

        if (!_indicatedInteractives.Contains(interactive))
        {
            interactive.UnregisterInteractor(this);
        }

        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
        else if (_focusedInteractive == interactive)
        {
            _focusedInteractive = null;
        }
    }

    internal bool RecalculateFocus()
    {
        FocusChangeResult? result = RecalculateFocusCore();
        if (result is null)
        {
            return false;
        }

        DispatchFocusChange(result.Value);
        return result.Value.Changed;
    }

    internal FocusChangeResult? RecalculateFocusCore()
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return null;
        }

        PurgeInvalidCandidates();
        InteractiveComponent? previous = _focusedInteractive;
        InteractiveComponent? best = null;
        float bestScore = float.MinValue;
        foreach (InteractiveComponent candidate in _interactiveCandidates)
        {
            if (!IsWithinInteractionRange(candidate) || !candidate.HasVisibleAction(this))
            {
                continue;
            }

            float score = CalculateInteractionScore(candidate);
            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (_focusedInteractive == best)
        {
            return new FocusChangeResult(previous, best, Changed: false);
        }

        _focusedInteractive = best;
        return new FocusChangeResult(previous, best, Changed: true);
    }

    internal void DispatchFocusChange(in FocusChangeResult result)
    {
        if (result.Changed)
        {
            Variant focusedInteractive = result.Current is null ? new Variant() : result.Current;
            EmitSignal(SignalName.FocusedInteractiveChanged, focusedInteractive);
            ForgetAutomaticRequest();
        }

        if (result.Current is not null)
        {
            EmitStatusFor(result.Current);

            // Retried on every focused frame, not only when focus moves: an automatic action that
            // becomes allowed while the player already looks at the target must start by itself.
            // The request is remembered so a still-allowed action is not re-sent every frame.
            TryStartAutomaticInteraction(result.Current);
        }
    }

    internal float CalculateInteractionScore(InteractiveComponent interactive)
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return float.MinValue;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(_resolvedInteractionOrigin.GlobalPosition);
        if (distance <= Mathf.Epsilon)
        {
            return 1.0f;
        }

        float alignment = Mathf.Max(0.0f, (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized()));
        return alignment / (1.0f + distance * Mathf.Max(DistanceScoreCoefficient, 0.0f));
    }

    internal bool IsWithinInteractionRange(InteractiveComponent interactive)
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return false;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(_resolvedInteractionOrigin.GlobalPosition);
        if (distance > Mathf.Max(MaxInteractionDistance, 0.0f))
        {
            return false;
        }

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        float alignment = (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized());
        float minimumAlignment = Mathf.Cos(
            Mathf.DegToRad(Mathf.Clamp(MaxInteractionAngleDegrees, 0.0f, 180.0f))
        );
        return alignment >= minimumAlignment;
    }

    /// <summary>Builds a fresh prompt snapshot for the current focused target.</summary>
    /// <returns>The focused presentation, or null when no target is focused.</returns>
    public InteractionTargetPresentation? GetInteractionPresentation()
    {
        return _focusedInteractive?.GetPresentation(this, true);
    }

    /// <summary>Resolves one input into an action of the focused target and requests its start.</summary>
    /// <remarks>
    /// Call from the local player's input code. The resolved action is only a local intention: the
    /// authoritative peer re-resolves the identifier against its own scene and re-evaluates it. On a
    /// client, true means the reliable request was sent; final acceptance is reported by gameplay
    /// state or <see cref="InteractionRejected"/>.
    /// </remarks>
    /// <param name="inputActionName">Project input action pressed by the player.</param>
    /// <returns>Whether a locally valid request was dispatched, or a hold towards one started.</returns>
    public bool TryStartInteractionInput(StringName inputActionName)
    {
        RecalculateFocus();
        InteractiveComponent? target = _focusedInteractive;
        if (target is null || inputActionName is null || inputActionName.IsEmpty)
        {
            return false;
        }

        // A threshold only exists to tell apart several actions sharing one input, so pressing an
        // input nobody asks to hold still selects immediately.
        float threshold = target.GetLongestHoldThreshold(this, inputActionName);
        if (threshold <= 0.0f)
        {
            return RequestResolvedAction(target, inputActionName, heldSeconds: 0.0f);
        }

        _gesture = new InteractionGesture(target, inputActionName, threshold, 0.0f);
        return true;
    }

    private bool RequestResolvedAction(
        InteractiveComponent target,
        StringName inputActionName,
        float heldSeconds
    )
    {
        InteractionAction? action = target.ResolveActionForInput(
            this,
            inputActionName,
            heldSeconds
        );
        if (action?.Definition is null)
        {
            return false;
        }

        return TryRequestAction(target, action, inputActionName);
    }

    /// <summary>Reports that the player released one interaction input.</summary>
    /// <remarks>
    /// Call from the local player's input-release code. Nothing else is said: the client states that
    /// an input ended, and the authoritative peer decides which of the executions this interactor
    /// owns that concerns. Only an action declared <see cref="InteractionActionDefinition.CancelOnInputReleased"/>
    /// is ended this way, so an instant or self-sustaining action ignores the release entirely.
    /// </remarks>
    /// <param name="inputActionName">Project input action released by the player.</param>
    /// <returns>Whether a release was reported to the authoritative peer or applied by the host.</returns>
    public bool TryEndInteractionInput(StringName inputActionName)
    {
        if (inputActionName is null || inputActionName.IsEmpty)
        {
            return false;
        }

        // Releasing before the threshold selects the action that asked for no hold, which is how
        // "tap to open, hold to force" resolves without the two ever competing.
        bool handled = false;
        if (_gesture is InteractionGesture gesture && gesture.Input == inputActionName)
        {
            _gesture = null;
            handled =
                IsUsable(gesture.Target)
                && _focusedInteractive == gesture.Target
                && RequestResolvedAction(gesture.Target, inputActionName, gesture.Elapsed);
        }

        if (!_sustainedInputs.Remove(inputActionName))
        {
            return handled;
        }

        _prediction = null;

        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryEndInteraction), inputActionName);
            return true;
        }

        return EndInteractionInputAuthoritatively(OwnerPeerId, inputActionName) > 0;
    }

    private void TryStartAutomaticInteraction(InteractiveComponent target)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        InteractionAction? action = target.ResolveAutomaticAction(this);
        if (
            action?.Definition is null
            || target.EvaluateAvailability(this, action) is not InteractionAllowed
        )
        {
            // Forgotten while unavailable so the action fires again as soon as it becomes allowed,
            // without the player having to look away and back.
            ForgetAutomaticRequest();
            return;
        }

        if (_automaticTarget == target && _automaticActionId == action.Definition.Id)
        {
            return;
        }

        _automaticTarget = target;
        _automaticActionId = action.Definition.Id;
        TryRequestAction(target, action, inputActionName: null);
    }

    private void ForgetAutomaticRequest()
    {
        _automaticTarget = null;
        _automaticActionId = null;
    }

    private bool TryRequestAction(
        InteractiveComponent target,
        InteractionAction action,
        StringName? inputActionName
    )
    {
        StringName actionId = action.Definition!.Id;
        InteractionAvailability localAvailability = target.EvaluateAvailability(this, action);
        if (localAvailability is not InteractionAllowed)
        {
            EmitSignal(
                SignalName.InteractionRejected,
                target,
                actionId,
                localAvailability.DescribeRefusal()
            );
            return false;
        }

        EmitSignal(SignalName.InteractionRequested, target, actionId);
        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryStartInteraction), target.GetPath(), actionId);
            RememberSustainedInput(inputActionName, action);
            PredictExecution(target, action);
            return true;
        }

        if (!TryStartInteractionAuthoritatively(target, actionId, OwnerPeerId, out _))
        {
            return false;
        }

        RememberSustainedInput(inputActionName, action);
        PredictExecution(target, action);
        return true;
    }

    // Purely local: the client predicts that it is sustaining this input so a release it never
    // started stays silent. What the release actually ends is decided authoritatively.
    private void RememberSustainedInput(StringName? inputActionName, InteractionAction action)
    {
        if (
            inputActionName is not null
            && !inputActionName.IsEmpty
            && action.Definition?.CancelOnInputReleased == true
        )
        {
            _sustainedInputs.Add(inputActionName);
        }
    }

    private void PredictExecution(InteractiveComponent target, InteractionAction action)
    {
        // Read straight from the scene the client already has, so a bar needs no replication and no
        // acknowledgement. An executor that declares no duration simply has nothing to draw.
        float duration = action.Executor?.ExpectedDuration ?? 0.0f;
        _prediction =
            duration > 0.0f
                ? new PredictedExecution(target, action.Definition!.Id, duration, 0.0f)
                : null;
    }

    internal void NotifyInteractiveStatusChanged(InteractiveComponent interactive)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        if (interactive == _focusedInteractive)
        {
            EmitStatusFor(interactive);
        }

        RecalculateFocus();
    }

    /// <summary>Godot callback that releases server reservations and unregisters detected targets.</summary>
    public override void _ExitTree()
    {
        if (Multiplayer.IsServer())
        {
            CancelOwnedExecutions(interactive: null, inputActionName: null, InteractorLostReason);
        }

        HashSet<InteractiveComponent> registered = new(_interactiveCandidates);
        registered.UnionWith(_indicatedInteractives);
        foreach (InteractiveComponent interactive in registered)
        {
            if (IsUsable(interactive))
            {
                interactive.UnregisterInteractor(this);
            }
        }

        _interactiveCandidates.Clear();
        _indicatedInteractives.Clear();
        _ownedExecutions.Clear();
        _sustainedInputs.Clear();
        _focusedInteractive = null;
        _gesture = null;
        _prediction = null;
        ForgetAutomaticRequest();
    }

    /// <summary>Reliable client-to-server RPC that validates and executes one action.</summary>
    /// <remarks>Called by Godot RPC dispatch; input code should call <see cref="TryStartInteractionInput"/>.</remarks>
    /// <param name="targetPath">Scene-tree path of the client-selected interactive.</param>
    /// <param name="actionId">Identifier of the action the client believes it can request.</param>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartInteraction(NodePath targetPath, StringName actionId)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        InteractiveComponent? target = GetTree()
            .Root.GetNodeOrNull<InteractiveComponent>(targetPath);
        if (target is null)
        {
            RejectInteraction(
                senderPeerId,
                targetPath,
                actionId,
                "The interaction target no longer exists."
            );
            return;
        }

        if (!TryStartInteractionAuthoritatively(target, actionId, senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, targetPath, actionId, reason);
        }
    }

    /// <summary>Reliable client-to-server RPC reporting that one interaction input was released.</summary>
    /// <remarks>
    /// Called by Godot RPC dispatch; input code should call <see cref="TryEndInteractionInput"/>. The
    /// client names an input, never an execution: the server owns the executions this interactor
    /// reserved and decides which of them a release ends.
    /// </remarks>
    /// <param name="inputActionName">Project input action the client released.</param>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryEndInteraction(StringName inputActionName)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        if (!ValidateSender(senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, new NodePath(), new StringName(string.Empty), reason);
            return;
        }

        EndInteractionInputAuthoritatively(senderPeerId, inputActionName);
    }

    /// <summary>Reliable server-to-owner RPC that reports an authoritative rejection.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client or directly on an offline host.</remarks>
    /// <param name="targetPath">Rejected target path, which may be empty.</param>
    /// <param name="actionId">Identifier of the rejected action, which may be empty.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionRejected(NodePath targetPath, StringName actionId, string reason)
    {
        Node? target = GetTree().Root.GetNodeOrNull(targetPath);
        EmitSignal(SignalName.InteractionRejected, target, actionId, reason);
    }

    private bool TryStartInteractionAuthoritatively(
        InteractiveComponent target,
        StringName actionId,
        int senderPeerId,
        out string reason
    )
    {
        reason = string.Empty;
        if (!ValidateSender(senderPeerId, out reason))
        {
            return false;
        }

        if (!_interactiveCandidates.Contains(target) || !IsWithinInteractionRange(target))
        {
            reason = "The interaction target is out of range.";
            return false;
        }

        InteractionAction? action = target.ResolveAction(actionId);
        if (action is null)
        {
            reason = InteractionAvailabilityExtensions.UnavailableReason;
            return false;
        }

        InteractionAvailability availability = target.EvaluateAvailability(this, action);
        if (availability is not InteractionAllowed)
        {
            reason = availability.DescribeRefusal();
            return false;
        }

        InteractionExecutionResult result = target.ExecuteAction(
            this,
            action,
            out ulong executionId
        );
        switch (result)
        {
            case InteractionExecutionRunning:
                // Only a running execution keeps a reservation the interactor may later release.
                RememberOwnedExecution(target, action, executionId);
                return true;

            case InteractionExecutionCompleted:
                return true;

            case InteractionExecutionRejected rejected:
                reason = rejected.Reason;
                return false;

            case InteractionExecutionFailed failed:
                reason = failed.Reason;
                return false;
        }

        reason = InteractionAvailabilityExtensions.UnavailableReason;
        return false;
    }

    private int EndInteractionInputAuthoritatively(int senderPeerId, StringName inputActionName)
    {
        if (!ValidateSender(senderPeerId, out _))
        {
            return 0;
        }

        return CancelOwnedExecutions(interactive: null, inputActionName, ReleasedReason);
    }

    private void RememberOwnedExecution(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        PruneOwnedExecutions();
        _ownedExecutions.Add(new InteractorExecution(interactive, action, executionId));
    }

    /// <summary>Ends the executions this interactor owns that match an optional target and input.</summary>
    /// <remarks>
    /// An entry whose execution already ended is simply dropped: identifiers are never reused, so a
    /// stale one can never cancel somebody else's execution.
    /// </remarks>
    private int CancelOwnedExecutions(
        InteractiveComponent? interactive,
        StringName? inputActionName,
        string reason
    )
    {
        int cancelled = 0;
        for (int index = _ownedExecutions.Count - 1; index >= 0; index--)
        {
            InteractorExecution owned = _ownedExecutions[index];
            if (!IsUsable(owned.Interactive) || !owned.Interactive.IsExecutionActive(owned.Id))
            {
                _ownedExecutions.RemoveAt(index);
                continue;
            }

            if (interactive is not null && owned.Interactive != interactive)
            {
                continue;
            }

            if (inputActionName is not null && !IsEndedByInput(owned.Action, inputActionName))
            {
                continue;
            }

            _ownedExecutions.RemoveAt(index);
            if (owned.Interactive.CancelExecution(owned.Id, reason))
            {
                cancelled++;
            }
        }

        return cancelled;
    }

    private void PruneOwnedExecutions()
    {
        _ownedExecutions.RemoveAll(owned =>
            !IsUsable(owned.Interactive) || !owned.Interactive.IsExecutionActive(owned.Id)
        );
    }

    private static bool IsEndedByInput(InteractionAction action, StringName inputActionName)
    {
        InteractionActionDefinition? definition = action?.Definition;
        return definition is not null
            && definition.CancelOnInputReleased
            && definition.InputActionName == inputActionName;
    }

    private void EmitStatusFor(InteractiveComponent interactive)
    {
        EmitSignal(SignalName.InteractionStatusChanged, interactive);
    }

    private bool ValidateSender(int senderPeerId, out string reason)
    {
        if (senderPeerId != OwnerPeerId)
        {
            reason = "The interaction owner is invalid.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void RejectInteraction(
        int senderPeerId,
        NodePath targetPath,
        StringName actionId,
        string reason
    )
    {
        GD.PushWarning($"{GetPath()}: rejected interaction from peer {senderPeerId}: {reason}");
        if (senderPeerId == OwnerPeerId && IsLocallyControlled)
        {
            ClientInteractionRejected(targetPath, actionId, reason);
        }
        else if (senderPeerId > 0)
        {
            RpcId(senderPeerId, nameof(ClientInteractionRejected), targetPath, actionId, reason);
        }
    }

    private bool IsUsable(InteractiveComponent? interactive) =>
        interactive is not null && IsInstanceValid(interactive);

    private int GetRemoteSenderOrOwner()
    {
        int senderPeerId = (int)Multiplayer.GetRemoteSenderId();
        return senderPeerId == 0 ? OwnerPeerId : senderPeerId;
    }

    private void PurgeInvalidCandidates()
    {
        _indicatedInteractives.RemoveWhere(interactive => !IsUsable(interactive));
        _interactiveCandidates.RemoveWhere(interactive => !IsUsable(interactive));
        if (_focusedInteractive is not null && !IsUsable(_focusedInteractive))
        {
            _focusedInteractive = null;
        }
    }
}
