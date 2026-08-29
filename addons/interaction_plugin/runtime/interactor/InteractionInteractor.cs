using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
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

/// <summary>Pending requester command, keyed by its target and action.</summary>
/// <remarks>
/// The marker exists even when the action cannot provide a local progress sample. Prediction itself
/// lives on the target's local execution presentation slot.
/// </remarks>
/// <param name="Target">Target the action was requested on.</param>
/// <param name="ActionId">Identifier of the requested action.</param>
/// <remarks>
/// The pending marker is cleared when the authority acknowledges or refuses the request.
/// A confirmed execution is protected by its authoritative identifier.
/// </remarks>
internal readonly record struct InteractionRequestKey(
    InteractiveComponent Target,
    StringName ActionId
);

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
    private const string InteractorPeerLostReason = "The interactor's peer left the session.";

    /// <summary>Emitted locally when the best target changes.</summary>
    /// <param name="interactive">New focused interactive, or null when focus is cleared.</param>
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    /// <summary>Emitted locally when a visible target becomes worth looking at again.</summary>
    /// <remarks>
    /// The signal is a notification only. Availability is carried per action, so a consumer reads
    /// the fresh snapshot from <see cref="InteractiveComponent.GetPresentation"/> instead of relying
    /// on a target-wide summary.
    /// <para>
    /// It is an <b>event</b>, not a per-frame push: it fires when the focus moves, when a target
    /// enters detection, and when gameplay invalidates one. Nothing announces a rule that starts
    /// refusing on its own, because a snapshot is pulled and a consumer that needs continuous
    /// freshness reads it every frame — which is exactly what
    /// <see cref="Presentation.UI.InteractionPresenter"/> does. Pushing it every focused frame
    /// notified nothing new and cost one snapshot per presented target per frame to every subscriber.
    /// </para>
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

    /// <summary>Emitted on the requesting peer once the authority accepted and started its command.</summary>
    /// <remarks>
    /// This is the acknowledgement a requesting client had no generic way to obtain: a Godot signal is
    /// local, so the target-level notifications of <see cref="InteractiveComponent"/> never leave the
    /// authority. The requester therefore learns its own accepted lifecycle through this signal.
    /// <para>
    /// An acknowledgement is correlated by target and action identifier, not by a request number. That
    /// is sufficient <b>because</b> the requesting peer keeps one pending marker per target/action pair,
    /// so at most one request of that pair is ever in flight. The execution identifier then protects
    /// the confirmed slot from stale terminal acknowledgements.
    /// </para>
    /// <para>
    /// The acknowledgement is delivered exactly once to the owning peer, the listen host included, and
    /// is never broadcast. The other players observe the world through replicated state or through the
    /// downstream gameplay system: that is late-join safe where a transient acknowledgement is not, and
    /// it does not disclose an action hidden from them.
    /// </para>
    /// <para>
    /// An instant action is acknowledged as started and then completed, exactly as the authority
    /// notifies it, so a consumer only has one lifecycle to learn.
    /// </para>
    /// <para>
    /// A local window a non-blocking vendor or dialogue opens belongs here rather than on
    /// <see cref="InteractionRequested"/>, which is only an intention the authority may still refuse.
    /// </para>
    /// </remarks>
    /// <param name="interactive">Target that accepted the command, or null when it cannot be resolved locally.</param>
    /// <param name="actionId">Identifier of the accepted action, correlating this acknowledgement.</param>
    /// <param name="executionId">Identifier the authority allocated, addressable by a downstream system.</param>
    [Signal]
    public delegate void InteractionStartedEventHandler(
        Node interactive,
        StringName actionId,
        ulong executionId
    );

    /// <summary>Emitted on the requesting peer once its accepted action reached its end.</summary>
    /// <remarks>Always preceded by <see cref="InteractionStarted"/> for the same target and action.</remarks>
    /// <param name="interactive">Target whose execution completed, or null when it cannot be resolved locally.</param>
    /// <param name="actionId">Identifier of the completed action.</param>
    [Signal]
    public delegate void InteractionCompletedEventHandler(Node interactive, StringName actionId);

    /// <summary>Emitted on the requesting peer once its accepted action ended without completing.</summary>
    /// <remarks>
    /// This covers a released input, an interactor leaving range or the tree, and an explicit gameplay
    /// cancellation. Always preceded by <see cref="InteractionStarted"/>.
    /// <para>
    /// It does not own the local prediction: a release clears the prediction immediately so the bar
    /// disappears without a round trip, and this acknowledgement is what everything else — a vendor
    /// window, a local UI — closes on. Receiving it after the prediction is already gone is normal.
    /// </para>
    /// </remarks>
    /// <param name="interactive">Target whose execution ended, or null when it cannot be resolved locally.</param>
    /// <param name="actionId">Identifier of the cancelled action.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
    [Signal]
    public delegate void InteractionCancelledEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    /// <summary>Emitted on the requesting peer once its accepted action failed after acceptance.</summary>
    /// <remarks>
    /// A failure is not a refusal: the authority accepted the command, notified it as started and only
    /// then discovered a gameplay or technical error. The client mirrors that exactly — it receives
    /// <see cref="InteractionStarted"/> and then this — so a window opened on the acknowledgement has
    /// something to close on, and so <see cref="InteractionRejected"/> keeps meaning "never started".
    /// </remarks>
    /// <param name="interactive">Target whose execution failed, or null when it cannot be resolved locally.</param>
    /// <param name="actionId">Identifier of the failed action.</param>
    /// <param name="reason">Reason describing the failure.</param>
    [Signal]
    public delegate void InteractionFailedEventHandler(
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

    /// <summary>Gets or sets the required layer deciding what this interactor detects.</summary>
    /// <remarks>
    /// How a game picks the object a player may interact with is the game's decision, so nothing is
    /// guessed here: an interactor without detector detects nothing and says so, rather than falling
    /// back on a model nobody chose. See <see cref="InteractionDetector"/>.
    /// </remarks>
    [ExportGroup("Detection")]
    [Export]
    public InteractionDetector? Detector { get; set; }

    /// <summary>Gets or sets the server peer that receives reliable interaction RPCs.</summary>
    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId { get; set; } = 1;

    /// <summary>Gets or sets the peer allowed to control this interactor.</summary>
    [Export]
    public int OwnerPeerId { get; set; } = 1;

    private readonly HashSet<InteractiveComponent> _detectedInteractives = new();

    // A set on both sides of the reconcile, because both sides are membership questions: the pass
    // asks "have I already kept this candidate", then asks it once per tracked target and once per
    // detected one. With lists that is quadratic in the number of candidates, and the number of
    // candidates is the whole registry for a detector whose source is the registry.
    private readonly HashSet<InteractiveComponent> _detectionBuffer = new();
    private readonly List<InteractiveComponent> _detectionEntered = new();
    private readonly List<InteractiveComponent> _detectionExited = new();
    private readonly List<InteractorExecution> _ownedExecutions = new();
    private readonly HashSet<InteractionRequestKey> _pendingRequests = new();
    private readonly HashSet<StringName> _consumedInputs = new();
    private readonly HashSet<StringName> _sustainedInputs = new();
    private readonly List<StringName> _relevantInputs = new();
    private MultiplayerApi? _watchedMultiplayer;
    private bool _ownerPeerLost;
    private InteractiveComponent? _focusedInteractive;
    private InteractiveComponent? _automaticTarget;
    private StringName? _automaticActionId;
    private InteractiveComponent? _refusedAutomaticTarget;
    private StringName? _refusedAutomaticActionId;
    private InteractionGesture? _gesture;
    private bool _hasKnownLocalControl;
    private bool _lastKnownLocalControl;

    /// <summary>Gets the target currently selected by the owning peer.</summary>
    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

    /// <summary>Gets whether this peer runs the authoritative half of the interaction.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have would only push an error and answer no.
    /// </remarks>
    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

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
        if (Detector is null)
        {
            GD.PushError($"{GetPath()}: InteractionInteractor requires a Detector.");
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
        SubscribeToPeerDisconnected();
    }

    /// <summary>Godot callback running detection for the owner and continued validation for the server.</summary>
    /// <remarks>
    /// Two rhythms sharing one detector. The authoritative peer only re-tests the targets of the
    /// executions it holds for this interactor, so its cost is bounded by the number of sustained
    /// executions in flight and never by the number of candidates. Walking the candidates is the
    /// owning client's job alone, which is what lets a detection source exist only there.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (IsAuthoritative)
        {
            ValidateSustainedExecutions();
        }

        // The detector is told, and not asked to guess: a source that costs something must be able to
        // stop paying for it on the copies of this character that nobody controls.
        bool locallyControlled = IsLocallyControlled;
        Detector?.SetCandidateSourceActive(locallyControlled);
        if (!locallyControlled)
        {
            return;
        }

        RecalculateFocus();
        AdvanceGesture((float)delta);
    }

    /// <summary>Reads how far the hold in progress is towards selecting its action.</summary>
    /// <remarks>
    /// Local feedback for a gesture widget. This is the selection layer, not the action: an action
    /// the player must stay engaged in reports its progress through the target's
    /// <see cref="InteractiveComponent.TryGetExecutionPresentation"/> instead.
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

    /// <summary>Reads how long the input has been held, in seconds.</summary>
    /// <remarks>
    /// The raw duration, so a consumer can normalise it on the threshold of the action it draws.
    /// <see cref="TryGetGestureProgress"/> normalises on the longest threshold of the input, which is
    /// the one the gesture is heading for, and therefore never reaches one on a shorter action.
    /// </remarks>
    /// <param name="inputActionName">Input being held, or an empty name when none is.</param>
    /// <param name="seconds">Seconds the input has been held.</param>
    /// <returns><see langword="true"/> while an input is being held towards a threshold.</returns>
    public bool TryGetGestureElapsed(out StringName inputActionName, out float seconds)
    {
        inputActionName = _gesture?.Input ?? new StringName(string.Empty);
        seconds = _gesture?.Elapsed ?? 0.0f;
        return _gesture is not null;
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

    /// <summary>Forgets a target the framework is tearing down, on every peer.</summary>
    /// <remarks>
    /// An area never reports an overlap it loses by being freed, so the target itself says so. This
    /// reaches the detector too, because the reference it holds in its own source is the one that
    /// would outlive the node.
    /// </remarks>
    internal void NotifyInteractiveRemoved(InteractiveComponent interactive)
    {
        _pendingRequests.RemoveWhere(request => request.Target == interactive);
        Detector?.Forget(interactive);
        if (_detectedInteractives.Remove(interactive))
        {
            interactive.UnregisterInteractor(this);
            EmitSignal(SignalName.InteractiveIndicationRemoved, interactive);
        }

        if (_focusedInteractive == interactive)
        {
            _focusedInteractive = null;
        }

        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
    }

    internal bool RecalculateFocus()
    {
        FocusChangeResult? result = RecalculateFocusCore();
        if (result is null)
        {
            return false;
        }

        DispatchDetectionChanges();
        DispatchFocusChange(result.Value);
        return result.Value.Changed;
    }

    /// <summary>Runs the whole detection pipeline once and mutates focus, without dispatching.</summary>
    /// <remarks>
    /// One loop for every detection model: iterate the candidates the detector offers, ask it for the
    /// tier of each, and keep the best scored interactible. What entered and left detection since the
    /// last pass is recorded for <see cref="DispatchDetectionChanges"/> instead of being signalled
    /// here, because a core mutation never runs external code.
    /// </remarks>
    internal FocusChangeResult? RecalculateFocusCore()
    {
        if (Detector is null)
        {
            return null;
        }

        PurgeDetectedInteractives();
        InteractiveComponent? previous = _focusedInteractive;
        InteractiveComponent? best = null;
        float bestScore = float.MinValue;
        _detectionBuffer.Clear();
        foreach (InteractiveComponent candidate in Detector.GetCandidates())
        {
            if (!IsUsable(candidate) || _detectionBuffer.Contains(candidate))
            {
                continue;
            }

            InteractionDetectionKind kind = Detector.Detect(candidate);
            if (kind == InteractionDetectionKind.None)
            {
                continue;
            }

            _detectionBuffer.Add(candidate);
            if (kind != InteractionDetectionKind.Interactible || !candidate.HasVisibleAction(this))
            {
                continue;
            }

            float score = Detector.Score(candidate);
            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        ReconcileDetectedInteractives();

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
            if (result.Changed)
            {
                EmitStatusFor(result.Current);
            }

            // Retried on every focused frame, not only when focus moves: an automatic action that
            // becomes allowed while the player already looks at the target must start by itself.
            // The request is remembered so a still-allowed action is not re-sent every frame.
            TryStartAutomaticInteraction(result.Current);
        }
    }

    private void ReconcileDetectedInteractives()
    {
        _detectionEntered.Clear();
        _detectionExited.Clear();
        foreach (InteractiveComponent detected in _detectionBuffer)
        {
            if (!_detectedInteractives.Contains(detected))
            {
                _detectionEntered.Add(detected);
            }
        }

        foreach (InteractiveComponent tracked in _detectedInteractives)
        {
            if (!_detectionBuffer.Contains(tracked))
            {
                _detectionExited.Add(tracked);
            }
        }

        foreach (InteractiveComponent entered in _detectionEntered)
        {
            _detectedInteractives.Add(entered);
            entered.RegisterInteractor(this);
        }

        foreach (InteractiveComponent exited in _detectionExited)
        {
            _detectedInteractives.Remove(exited);
            if (IsUsable(exited))
            {
                exited.UnregisterInteractor(this);
            }
        }
    }

    /// <summary>Signals what entered and left detection during the last core pass.</summary>
    /// <remarks>
    /// Indication covers every detected target, focused one included: the tiers are cumulative, and
    /// hiding the indication of the target that carries the prompt is the presenter's call, not a
    /// detection decision.
    /// </remarks>
    internal void DispatchDetectionChanges()
    {
        foreach (InteractiveComponent entered in _detectionEntered)
        {
            if (!IsUsable(entered))
            {
                continue;
            }

            EmitSignal(SignalName.InteractiveIndicationAdded, entered);
            EmitStatusFor(entered);
        }

        _detectionEntered.Clear();

        foreach (InteractiveComponent exited in _detectionExited)
        {
            EmitSignal(SignalName.InteractiveIndicationRemoved, exited);
        }

        _detectionExited.Clear();
    }

    /// <summary>Lists the project inputs worth sampling for the owning player right now.</summary>
    /// <remarks>
    /// An input controller iterates this instead of hard-coding one action name, so adding an action
    /// bound to a different input to a scene needs no change in the character. The interactor only
    /// reports what is relevant; deciding that interaction wins over, say, an attack bound to the
    /// same key stays the game's call, because the interactor never samples input itself.
    /// <para>
    /// The list holds the inputs of the focused target's presentable actions, automatic ones
    /// excluded since no key requests them, plus every input this interactor has consumed or believes
    /// it is currently sustaining. That second half matters: without it, looking away from a target
    /// while holding its key would drop the release, leaving a consumed press latched or letting a
    /// sustained execution run until the player walked out of range.
    /// </para>
    /// <para>
    /// The returned list is reused between calls and is only valid until the next one. Copy it
    /// before storing it.
    /// </para>
    /// </remarks>
    /// <returns>Distinct project input action names, empty when this peer controls nothing.</returns>
    public IReadOnlyList<StringName> GetRelevantInputs()
    {
        _relevantInputs.Clear();
        if (!IsLocallyControlled)
        {
            return _relevantInputs;
        }

        if (_focusedInteractive is not null && IsUsable(_focusedInteractive))
        {
            foreach (InteractionAction action in _focusedInteractive.Actions)
            {
                if (
                    action?.Definition is null
                    || action.Automatic
                    || _focusedInteractive.EvaluateAvailability(this, action) is InteractionHidden
                )
                {
                    continue;
                }

                AddRelevantInput(action.Definition.InputActionName);
            }
        }

        foreach (StringName sustained in _sustainedInputs)
        {
            AddRelevantInput(sustained);
        }

        foreach (StringName consumed in _consumedInputs)
        {
            AddRelevantInput(consumed);
        }

        return _relevantInputs;
    }

    private void AddRelevantInput(StringName inputActionName)
    {
        if (
            inputActionName is not null
            && !inputActionName.IsEmpty
            && !_relevantInputs.Contains(inputActionName)
        )
        {
            _relevantInputs.Add(inputActionName);
        }
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
        if (
            inputActionName is null
            || inputActionName.IsEmpty
            || _consumedInputs.Contains(inputActionName)
        )
        {
            return false;
        }

        RecalculateFocus();
        InteractiveComponent? target = _focusedInteractive;
        if (target is null)
        {
            return false;
        }

        _consumedInputs.Add(inputActionName);

        // A threshold only exists to tell apart several actions sharing one input, so pressing an
        // input nobody asks to hold still selects immediately.
        float threshold = target.GetLongestHoldThreshold(this, inputActionName);
        if (threshold <= 0.0f)
        {
            bool requested = RequestResolvedAction(target, inputActionName, heldSeconds: 0.0f);
            if (!requested)
            {
                _consumedInputs.Remove(inputActionName);
            }

            return requested;
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
    /// <returns>
    /// Whether the interaction consumed this press, reported its release to the authoritative peer,
    /// or applied it on the host.
    /// </returns>
    public bool TryEndInteractionInput(StringName inputActionName)
    {
        if (inputActionName is null || inputActionName.IsEmpty)
        {
            return false;
        }

        bool consumed = _consumedInputs.Remove(inputActionName);

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
            return handled || consumed;
        }

        ClearPendingPredictionsForInput(inputActionName);

        if (!IsAuthoritative)
        {
            RpcId(ServerPeerId, nameof(ServerTryEndInteraction), inputActionName);
            return true;
        }

        return EndInteractionInputAuthoritatively(OwnerPeerId, inputActionName) > 0 || consumed;
    }

    private void TryStartAutomaticInteraction(InteractiveComponent target)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        InteractionAction? action = target.ResolveAutomaticAction(this);
        InteractionAvailability availability = action?.Definition is null
            ? new InteractionHidden()
            : target.EvaluateAvailability(this, action);

        if (availability is InteractionHidden)
        {
            // Only leaving the offered choices forgets the request. Blocked must not, because the
            // running execution of this very action blocks it: forgetting there would re-fire the
            // action the moment it completes, forever.
            ForgetAutomaticRequest();
            return;
        }

        // A blocked action is left alone rather than requested, so an action that stays unavailable
        // never floods the owner with refusals.
        if (
            availability is not InteractionAllowed
            || (_automaticTarget == target && _automaticActionId == action!.Definition!.Id)
            || (
                _refusedAutomaticTarget == target
                && _refusedAutomaticActionId == action!.Definition!.Id
            )
        )
        {
            return;
        }

        _automaticTarget = target;
        _automaticActionId = action!.Definition!.Id;
        TryRequestAction(target, action, inputActionName: null);
    }

    // Leaving the offered choices and moving the focus are both "the situation changed", which is
    // exactly when a refusal stops being a reason to stay silent: the pair is forgotten with the
    // request itself, so the next opportunity is a fresh one.
    private void ForgetAutomaticRequest()
    {
        _automaticTarget = null;
        _automaticActionId = null;
        ClearAutomaticRefusal();
    }

    private void ClearAutomaticRefusal()
    {
        _refusedAutomaticTarget = null;
        _refusedAutomaticActionId = null;
    }

    private bool TryRequestAction(
        InteractiveComponent target,
        InteractionAction action,
        StringName? inputActionName
    )
    {
        StringName actionId = action.Definition!.Id;
        InteractionRequestKey request = new(target, actionId);
        if (
            _pendingRequests.Contains(request)
            || (
                !IsAuthoritative
                && (
                    target.HasLocalExecution(action)
                    || target.HasLocalExecutionInGroup(action.GetConcurrencyGroup())
                    || HasPendingRequestInGroup(target, action.GetConcurrencyGroup())
                )
            )
        )
        {
            return false;
        }

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
        if (!IsAuthoritative)
        {
            _pendingRequests.Add(request);
            RpcId(
                ServerPeerId,
                nameof(ServerTryStartInteraction),
                GetNetworkPath(target),
                actionId
            );
            RememberSustainedInput(inputActionName, action);
            PredictExecution(target, action);
            return true;
        }

        InteractionExecutionResult result = TryStartInteractionAuthoritatively(
            target,
            actionId,
            OwnerPeerId
        );
        if (result is InteractionExecutionRejected rejected)
        {
            // The host is a requester like any other: an authoritative refusal reaches it through the
            // same acknowledgement a remote client receives, exactly once.
            RejectInteraction(OwnerPeerId, GetNetworkPath(target), actionId, rejected.Reason);
            return false;
        }

        if (result is InteractionExecutionFailed)
        {
            // Started and failed were already acknowledged by the target, and nothing is left running
            // to sustain or to predict.
            return false;
        }

        // Nothing to predict on this side: the acknowledgement of the authority is what armed the bar,
        // and here it already ran, synchronously, during the authoritative call above.
        RememberSustainedInput(inputActionName, action);
        return true;
    }

    // The bar starts now rather than on the acknowledgement by asking the executor for its generic
    // initial sample. The interactor does not know whether that sample came from a timed producer.
    private void PredictExecution(InteractiveComponent target, InteractionAction action)
    {
        InteractionProgressSample? sample = action.Executor?.GetPredictionSample(
            new InteractionContext(this, target, action)
        );
        if (sample is InteractionProgressSample initial)
        {
            target.AddPendingExecutionPresentation(action.Definition!.Id, initial);
        }
    }

    private bool HasPendingRequestInGroup(InteractiveComponent target, StringName group)
    {
        foreach (InteractionRequestKey request in _pendingRequests)
        {
            if (
                request.Target == target
                && request.Target.ResolveAction(request.ActionId)?.GetConcurrencyGroup() == group
            )
            {
                return true;
            }
        }

        return false;
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

    private void ClearPendingPredictionsForInput(StringName inputActionName)
    {
        foreach (InteractionRequestKey request in _pendingRequests)
        {
            InteractionActionDefinition? definition = request
                .Target.ResolveAction(request.ActionId)
                ?.Definition;
            if (definition?.InputActionName == inputActionName)
            {
                request.Target.RemovePendingExecution(request.ActionId);
            }
        }
    }

    internal void NotifyInteractiveStatusChanged(InteractiveComponent interactive)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        // Gameplay says this target is no longer what the refusal was decided against, so an
        // automatic action that was refused is allowed to try again.
        if (interactive == _refusedAutomaticTarget)
        {
            ClearAutomaticRefusal();
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
        UnsubscribeFromPeerDisconnected();
        if (IsAuthoritative)
        {
            CancelOwnedExecutions(interactive: null, inputActionName: null, InteractorLostReason);
        }

        foreach (InteractiveComponent interactive in _detectedInteractives)
        {
            if (IsUsable(interactive))
            {
                interactive.UnregisterInteractor(this);
            }
        }

        _detectedInteractives.Clear();
        _detectionBuffer.Clear();
        _detectionEntered.Clear();
        _detectionExited.Clear();
        _ownedExecutions.Clear();
        _consumedInputs.Clear();
        _sustainedInputs.Clear();
        _relevantInputs.Clear();
        foreach (InteractionRequestKey request in _pendingRequests)
        {
            if (IsUsable(request.Target))
            {
                request.Target.RemovePendingExecution(request.ActionId);
            }
        }
        _pendingRequests.Clear();
        _focusedInteractive = null;
        _gesture = null;
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
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
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

        // Only a refusal is reported here. A failure already reached the owner as started and then
        // failed, dispatched by the target itself, so reporting it again as a rejection would tell
        // that owner its command never ran.
        if (
            TryStartInteractionAuthoritatively(target, actionId, senderPeerId)
            is InteractionExecutionRejected rejected
        )
        {
            RejectInteraction(senderPeerId, targetPath, actionId, rejected.Reason);
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
        Node? target = ResolveAcknowledgedTarget(targetPath);
        ReconcileRefusedRequest(target as InteractiveComponent, actionId);
        EmitSignal(SignalName.InteractionRejected, target!, actionId, reason);
    }

    /// <summary>Reliable server-to-owner RPC acknowledging that the command started.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client, or directly on the listen host.</remarks>
    /// <param name="targetPath">Path of the target that accepted the command.</param>
    /// <param name="actionId">Identifier of the accepted action.</param>
    /// <param name="executionId">Identifier the authority allocated for the execution.</param>
    /// <param name="visibility">Presentation visibility authored by the authoritative action.</param>
    /// <param name="hasProgress">Whether the acknowledgement carries a progress sample.</param>
    /// <param name="progressBase">Progress value at sample receipt, when present.</param>
    /// <param name="progressPerSecond">Local extrapolation rate, when present.</param>
    /// <param name="revision">Monotonic presentation sample revision.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionStarted(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        InteractionExecutionVisibility visibility,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        Node? target = ResolveAcknowledgedTarget(targetPath);

        if (target is InteractiveComponent interactive)
        {
            _pendingRequests.Remove(new InteractionRequestKey(interactive, actionId));
            if (visibility != InteractionExecutionVisibility.AuthorityOnly)
            {
                interactive.ConfirmRequesterExecution(
                    actionId,
                    executionId,
                    hasProgress,
                    new InteractionProgressSample(progressBase, progressPerSecond, revision)
                );
            }
            else
            {
                interactive.RemovePendingExecution(actionId);
            }
        }

        // The acknowledgement confirms the requester slot with the authority's latest presentation
        // sample. Reconciliation preserves the value already shown and applies only newer revisions;
        // actions hidden from the requester discard their pending marker without creating a slot.
        EmitSignal(SignalName.InteractionStarted, target!, actionId, executionId);
    }

    /// <summary>Reliable server-to-owner RPC carrying a published or timed progress correction.</summary>
    /// <param name="targetPath">Path of the target carrying the execution.</param>
    /// <param name="actionId">Identifier of the action carrying the execution.</param>
    /// <param name="executionId">Identifier of the confirmed execution.</param>
    /// <param name="hasProgress">Whether the update carries a progress sample.</param>
    /// <param name="progressBase">Progress value at sample receipt, when present.</param>
    /// <param name="progressPerSecond">Local extrapolation rate, when present.</param>
    /// <param name="revision">Monotonic presentation sample revision.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionProgress(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        Node? target = ResolveAcknowledgedTarget(targetPath);
        if (target is InteractiveComponent interactive)
        {
            interactive.ApplyRequesterProgress(
                actionId,
                executionId,
                hasProgress,
                new InteractionProgressSample(progressBase, progressPerSecond, revision)
            );
        }
    }

    /// <summary>Reliable server-to-owner RPC acknowledging that the accepted action completed.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client, or directly on the listen host.</remarks>
    /// <param name="targetPath">Path of the target whose execution completed.</param>
    /// <param name="actionId">Identifier of the completed action.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionCompleted(
        NodePath targetPath,
        StringName actionId,
        ulong executionId
    )
    {
        Node? target = ResolveAcknowledgedTarget(targetPath);
        if (target is InteractiveComponent interactive)
        {
            interactive.RemoveRequesterExecution(actionId, executionId);
        }
        EmitSignal(SignalName.InteractionCompleted, target!, actionId);
    }

    /// <summary>Reliable server-to-owner RPC acknowledging that the accepted action was interrupted.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client, or directly on the listen host.</remarks>
    /// <param name="targetPath">Path of the target whose execution ended.</param>
    /// <param name="actionId">Identifier of the cancelled action.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionCancelled(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        string reason
    )
    {
        Node? target = ResolveAcknowledgedTarget(targetPath);
        if (target is InteractiveComponent interactive)
        {
            interactive.RemoveRequesterExecution(actionId, executionId);
        }
        EmitSignal(SignalName.InteractionCancelled, target!, actionId, reason);
    }

    /// <summary>Reliable server-to-owner RPC acknowledging that the accepted action failed.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client, or directly on the listen host.</remarks>
    /// <param name="targetPath">Path of the target whose execution failed.</param>
    /// <param name="actionId">Identifier of the failed action.</param>
    /// <param name="reason">Reason describing the failure.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionFailed(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        string reason
    )
    {
        Node? target = ResolveAcknowledgedTarget(targetPath);
        if (target is InteractiveComponent interactive)
        {
            interactive.RemoveRequesterExecution(actionId, executionId);
        }
        EmitSignal(SignalName.InteractionFailed, target!, actionId, reason);
    }

    /// <summary>Acknowledges to the owning peer that the authority started its command.</summary>
    /// <remarks>
    /// Called directly by the authoritative target, never broadcast, exactly like the notification the
    /// executor owning the mutation receives: an interactor learns about its own executions without
    /// subscribing to a target-level signal and without filtering out the executions of others.
    /// </remarks>
    /// <param name="interactive">Target that accepted the command.</param>
    /// <param name="action">Action that was accepted.</param>
    /// <param name="executionId">Identifier allocated for the execution.</param>
    internal void NotifyExecutionStarted(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        if (!TryBuildAcknowledgement(interactive, action, out NodePath path, out StringName id))
        {
            return;
        }

        bool hasSlot = interactive.TryGetProgressSample(
            executionId,
            out bool hasProgress,
            out InteractionProgressSample sample
        );
        if (!hasSlot || !hasProgress)
        {
            hasProgress = false;
            sample = default;
        }

        if (IsLocallyControlled)
        {
            ClientInteractionStarted(
                path,
                id,
                executionId,
                action.ExecutionVisibility,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
        else if (CanSendToOwner)
        {
            RpcId(
                OwnerPeerId,
                nameof(ClientInteractionStarted),
                path,
                id,
                executionId,
                (int)action.ExecutionVisibility,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
    }

    internal void NotifyExecutionProgress(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        bool hasProgress,
        InteractionProgressSample sample
    )
    {
        if (!TryBuildAcknowledgement(interactive, action, out NodePath path, out StringName id))
        {
            return;
        }

        if (IsLocallyControlled)
        {
            ClientInteractionProgress(
                path,
                id,
                executionId,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
        else if (CanSendToOwner)
        {
            RpcId(
                OwnerPeerId,
                nameof(ClientInteractionProgress),
                path,
                id,
                executionId,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
    }

    /// <summary>Acknowledges to the owning peer that its accepted action completed.</summary>
    /// <param name="interactive">Target whose execution completed.</param>
    /// <param name="action">Action that completed.</param>
    internal void NotifyExecutionCompleted(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        if (!TryBuildAcknowledgement(interactive, action, out NodePath path, out StringName id))
        {
            return;
        }

        if (IsLocallyControlled)
        {
            ClientInteractionCompleted(path, id, executionId);
        }
        else if (CanSendToOwner)
        {
            RpcId(OwnerPeerId, nameof(ClientInteractionCompleted), path, id, executionId);
        }
    }

    /// <summary>Acknowledges to the owning peer that its accepted action ended without completing.</summary>
    /// <param name="interactive">Target whose execution ended.</param>
    /// <param name="action">Action that was cancelled.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
    internal void NotifyExecutionCancelled(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        string reason
    )
    {
        if (!TryBuildAcknowledgement(interactive, action, out NodePath path, out StringName id))
        {
            return;
        }

        if (IsLocallyControlled)
        {
            ClientInteractionCancelled(path, id, executionId, reason);
        }
        else if (CanSendToOwner)
        {
            RpcId(OwnerPeerId, nameof(ClientInteractionCancelled), path, id, executionId, reason);
        }
    }

    /// <summary>Acknowledges to the owning peer that its accepted action failed after acceptance.</summary>
    /// <remarks>
    /// Reported as its own case rather than as a refusal: the authority already acknowledged the start,
    /// so reporting a rejection here would tell the owner that nothing ever ran.
    /// </remarks>
    /// <param name="interactive">Target whose execution failed.</param>
    /// <param name="action">Action that failed.</param>
    /// <param name="reason">Reason describing the failure.</param>
    internal void NotifyExecutionFailed(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        string reason
    )
    {
        if (!TryBuildAcknowledgement(interactive, action, out NodePath path, out StringName id))
        {
            return;
        }

        if (IsLocallyControlled)
        {
            ClientInteractionFailed(path, id, executionId, reason);
        }
        else if (CanSendToOwner)
        {
            RpcId(OwnerPeerId, nameof(ClientInteractionFailed), path, id, executionId, reason);
        }
    }

    // An acknowledgement names its target by path so the owner resolves it in its own scene, and the
    // owner may have no scene at all: an interactor outside the tree has a null tree, which is also
    // how an offline test reaches this path. The acknowledgement is still reported, without a target.
    private Node? ResolveAcknowledgedTarget(NodePath targetPath) => ResolveNetworkPath(targetPath);

    /// <summary>Gets the node every path crossing the network is named relative to.</summary>
    /// <remarks>
    /// Godot routes an RPC by the path of its node relative to the multiplayer root, so a payload
    /// naming a target must use the same origin or the two peers do not talk about the same object.
    /// In a normal game that root is the scene root and nothing changes; the two differ when several
    /// peers share one process, each owning its own subtree, which is what makes a real test with a
    /// server and two clients possible at all.
    /// </remarks>
    private Node? GetNetworkRoot()
    {
        SceneTree? tree = GetTree();
        if (tree is null)
        {
            return null;
        }

        return Multiplayer is SceneMultiplayer scene && !scene.RootPath.IsEmpty
            ? tree.Root.GetNodeOrNull(scene.RootPath)
            : tree.Root;
    }

    // Falls back to the absolute path rather than dropping the payload: a node outside any tree has
    // no network root, and naming it absolutely is exactly what a single-rooted game would have done.
    private NodePath GetNetworkPath(Node node)
    {
        Node? root = GetNetworkRoot();
        return root is null ? node.GetPath() : root.GetPathTo(node);
    }

    private Node? ResolveNetworkPath(NodePath targetPath)
    {
        Node? root = GetNetworkRoot();
        return root is null || targetPath is null || targetPath.IsEmpty
            ? null
            : root.GetNodeOrNull(targetPath);
    }

    // A reservation must not outlive the session that asked for it. Ending an execution when the
    // interactor leaves the tree covers the project despawning a departed player, but that is a
    // contract the plugin does not own: a peer that simply drops while its node stays would keep the
    // target locked for everybody else. Listening to the session itself is what makes the plugin
    // correct on its own, and it stays correct when the project despawns too — the execution is
    // already over and an identifier is never reused.
    private void SubscribeToPeerDisconnected()
    {
        if (Multiplayer is null || _watchedMultiplayer is not null)
        {
            return;
        }

        _watchedMultiplayer = Multiplayer;
        _watchedMultiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    private void UnsubscribeFromPeerDisconnected()
    {
        if (_watchedMultiplayer is null)
        {
            return;
        }

        _watchedMultiplayer.PeerDisconnected -= OnPeerDisconnected;
        _watchedMultiplayer = null;
    }

    // Only the peer this interactor belongs to matters: every other departure is somebody else's
    // interactor. The flag is one-way because a peer identifier is never handed out twice.
    private void OnPeerDisconnected(long peerId)
    {
        if ((int)peerId != OwnerPeerId)
        {
            return;
        }

        _ownerPeerLost = true;
        if (IsAuthoritative)
        {
            CancelOwnedExecutions(
                interactive: null,
                inputActionName: null,
                InteractorPeerLostReason
            );
        }
    }

    // An acknowledgement leaving the process needs a session to leave through. The authority cancels
    // the executions of a departing interactor from its _ExitTree, which is exactly when the node may
    // already have lost its multiplayer API: there is nobody left to tell, and that is not an error.
    // A peer that dropped is the same situation seen from the other side: its cancellation is still
    // worth applying to the world, but naming it as an RPC target would only raise an unknown peer.
    private bool CanSendToOwner =>
        OwnerPeerId > 0
        && !_ownerPeerLost
        && Multiplayer is not null
        && Multiplayer.MultiplayerPeer is not null;

    // An acknowledgement only exists on the authority and only for a target and action that can still
    // be named: a peer that is not the authority has nothing to acknowledge, and an execution whose
    // action lost its definition has no identifier the owner could correlate.
    private bool TryBuildAcknowledgement(
        InteractiveComponent interactive,
        InteractionAction action,
        out NodePath targetPath,
        out StringName actionId
    )
    {
        targetPath = new NodePath();
        actionId = new StringName(string.Empty);
        if (!IsAuthoritative || !IsUsable(interactive) || action?.Definition is null)
        {
            return false;
        }

        targetPath = GetNetworkPath(interactive);
        actionId = action.Definition.Id;
        return true;
    }

    // A terminal acknowledgement removes the matching presentation slot immediately. Matching on the
    // pair prevents a stale acknowledgement from erasing the slot of a newer execution.
    // A request the authority refused leaves nothing running, so the bar it drew optimistically and the
    // sustained input it created must go with it. Only an unacknowledged prediction is dropped: pressing
    // again on the very action one is already running is also a refusal, and it must not erase the bar
    // of the execution that is still running. An automatic request is forgotten too, but the refused
    // pair is remembered: forgetting it alone would re-send the very same request on the very next frame
    // and turn one refusal into a flood. The pair is released as soon as the situation changes — focus
    // moving, the action leaving the offered choices, or gameplay invalidating the target.
    private void ReconcileRefusedRequest(InteractiveComponent? target, StringName actionId)
    {
        if (target is InteractiveComponent interactive)
        {
            interactive.RemovePendingExecution(actionId);
            _pendingRequests.Remove(new InteractionRequestKey(interactive, actionId));
        }

        if (!IsUsable(target) || actionId is null || actionId.IsEmpty)
        {
            return;
        }

        if (_automaticTarget == target && _automaticActionId == actionId)
        {
            ForgetAutomaticRequest();
            _refusedAutomaticTarget = target;
            _refusedAutomaticActionId = actionId;
        }

        InteractionActionDefinition? definition = target!.ResolveAction(actionId)?.Definition;
        if (definition is not null && definition.CancelOnInputReleased)
        {
            _sustainedInputs.Remove(definition.InputActionName);
        }
    }

    /// <summary>Validates one requested action on the authority and runs it, reporting its outcome.</summary>
    /// <remarks>
    /// The outcome is returned rather than collapsed into a boolean because the four cases are not
    /// interchangeable to the caller. A rejection never ran, so the owner must be told it was refused;
    /// a failure did run and was already acknowledged as started, so telling the owner it was refused
    /// would contradict the acknowledgement it just received.
    /// </remarks>
    /// <param name="target">Target resolved on the authority, never the one the client holds.</param>
    /// <param name="actionId">Identifier of the action the requester believes it can start.</param>
    /// <param name="senderPeerId">Peer the request came from, validated against the owner.</param>
    /// <returns>The executor outcome, or the refusal that stopped the command before it ran.</returns>
    private InteractionExecutionResult TryStartInteractionAuthoritatively(
        InteractiveComponent target,
        StringName actionId,
        int senderPeerId
    )
    {
        if (!ValidateSender(senderPeerId, out string reason))
        {
            return new InteractionExecutionRejected(reason);
        }

        if (Detector is null || Detector.Detect(target) != InteractionDetectionKind.Interactible)
        {
            return new InteractionExecutionRejected("The interaction target is out of range.");
        }

        InteractionAction? action = target.ResolveAction(actionId);
        if (action is null)
        {
            return new InteractionExecutionRejected(
                InteractionAvailabilityExtensions.UnavailableReason
            );
        }

        InteractionAvailability availability = target.EvaluateAvailability(this, action);
        if (availability is not InteractionAllowed)
        {
            return new InteractionExecutionRejected(availability.DescribeRefusal());
        }

        InteractionExecutionResult result = target.ExecuteAction(
            this,
            action,
            out ulong executionId
        );
        if (result is InteractionExecutionRunning)
        {
            // Only a running execution keeps a reservation the interactor may later release.
            RememberOwnedExecution(target, action, executionId);
        }

        return result;
    }

    private int EndInteractionInputAuthoritatively(int senderPeerId, StringName inputActionName)
    {
        if (!ValidateSender(senderPeerId, out _))
        {
            return 0;
        }

        return CancelOwnedExecutions(interactive: null, inputActionName, ReleasedReason);
    }

    /// <summary>Tracks an execution this interactor stays answerable for while it runs.</summary>
    /// <remarks>
    /// An execution the world owns is deliberately never tracked: it survives the interactor by
    /// definition, so nothing here should be able to end it — not a lost window, not a released input,
    /// not this node leaving the tree. Not tracking it is what makes it world-owned from its start.
    /// </remarks>
    private void RememberOwnedExecution(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        PruneOwnedExecutions();
        if (!RequiresInteractorPresence(action))
        {
            return;
        }

        _ownedExecutions.Add(new InteractorExecution(interactive, action, executionId));
    }

    // Holding the key that started an action is a way of being present, so a definition that cancels
    // on release keeps the execution bound to the interactor whatever its executor claims.
    private static bool RequiresInteractorPresence(InteractionAction action) =>
        action.Definition?.CancelOnInputReleased == true
        || action.Executor?.RequiresInteractorPresence != false;

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

    /// <summary>Ends the executions whose target stopped being interactible for this interactor.</summary>
    /// <remarks>
    /// This is the "sustained by presence" axis, and it is validated with the very same window the
    /// command was accepted with: walking away or turning away from a channel ends it. An execution
    /// the world owns is not tracked here at all, so it survives on its own.
    /// </remarks>
    private void ValidateSustainedExecutions()
    {
        for (int index = _ownedExecutions.Count - 1; index >= 0; index--)
        {
            InteractorExecution owned = _ownedExecutions[index];
            if (!IsUsable(owned.Interactive) || !owned.Interactive.IsExecutionActive(owned.Id))
            {
                _ownedExecutions.RemoveAt(index);
                continue;
            }

            if (
                Detector is not null
                && Detector.Detect(owned.Interactive) == InteractionDetectionKind.Interactible
            )
            {
                continue;
            }

            _ownedExecutions.RemoveAt(index);
            owned.Interactive.CancelExecution(owned.Id, InteractorLostReason);
        }
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

    private void PurgeDetectedInteractives()
    {
        // The guard stays even though a target leaving the tree announces itself: a game may write a
        // detector whose source the target knows nothing about — the registry and the cast are two —
        // and then no teardown reaches this set, while the reconcile below would unregister from a
        // freed instance. The count check is what keeps it free when idle: the predicate closes over
        // this, so it would allocate a delegate every frame for an empty set.
        if (_detectedInteractives.Count > 0)
        {
            _detectedInteractives.RemoveWhere(interactive => !IsUsable(interactive));
        }
        if (_focusedInteractive is not null && !IsUsable(_focusedInteractive))
        {
            _focusedInteractive = null;
        }
    }
}
