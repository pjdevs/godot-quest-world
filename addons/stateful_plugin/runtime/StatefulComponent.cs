using System;
using Godot;

namespace QuestWorld.State;

/// <summary>
/// Owns one authoritative world-state value that can be replicated, persisted, and observed.
/// </summary>
/// <remarks>
/// The value is a free <see cref="StringName"/> such as <c>closed</c>, <c>open</c>, or <c>flooded</c>.
/// This node gives no universal meaning to any value and has no interaction dependency; gameplay and
/// interaction rules interpret the value themselves. Mutation and restoration are server-only, while the
/// replicated setter and presentation signals also run on clients.
/// </remarks>
[GlobalClass]
public partial class StatefulComponent : Node
{
    /// <summary>Current serialization version used by <see cref="StatefulSavedState"/>.</summary>
    public const int CurrentSaveVersion = 1;

    /// <summary>Emitted on server, host, dedicated server, and clients whenever local state is applied.</summary>
    /// <param name="oldState">Previous state value.</param>
    /// <param name="newState">Applied state value.</param>
    [Signal]
    public delegate void StateChangedEventHandler(StringName oldState, StringName newState);

    /// <summary>Emitted only with authority, including offline games, listen hosts, and dedicated servers.</summary>
    /// <param name="oldState">Previous state value.</param>
    /// <param name="newState">Applied state value.</param>
    [Signal]
    public delegate void StateChangedAuthorityEventHandler(
        StringName oldState,
        StringName newState
    );

    /// <summary>Emitted in offline games, clients, and listen hosts, but never on a dedicated server.</summary>
    /// <param name="oldState">Previous state value.</param>
    /// <param name="newState">Applied state value.</param>
    [Signal]
    public delegate void StateChangedPresentationEventHandler(
        StringName oldState,
        StringName newState
    );

    /// <summary>Gets or sets the optional schema declaring every accepted state value.</summary>
    /// <remarks>No schema means any value is accepted.</remarks>
    [Export]
    public StateSchema? Schema { get; set; }

    /// <summary>Gets or sets the local state applied when the node enters the scene tree.</summary>
    [Export]
    public StringName InitialState { get; set; } = new(string.Empty);

    [Export]
    private StringName ReplicatedState
    {
        get => _state;
        set => ApplyState(value);
    }

    private StringName _state = new(string.Empty);

    /// <summary>Gets the state currently applied on this peer.</summary>
    public StringName State => _state;

    /// <summary>Godot callback that initializes the local state without emitting change signals.</summary>
    public override void _Ready()
    {
        if (!IsStateDeclared(InitialState))
        {
            GD.PushError(
                $"{GetPath()}: InitialState '{InitialState}' is not declared by the assigned Schema."
            );
        }

        _state = InitialState;
    }

    /// <summary>Checks whether a value may be applied by <see cref="SetState"/>.</summary>
    /// <remarks>This query is synchronous, repeatable, and free of side effects.</remarks>
    /// <param name="state">State value to check.</param>
    /// <returns><see langword="true"/> when no schema is assigned or when the schema declares the value.</returns>
    public bool IsStateDeclared(StringName state) => Schema is null || Schema.Contains(state);

    /// <summary>Gets whether this peer runs the authoritative half of the world state.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have only pushes an error and answers no, which would make every
    /// authoritative path refuse itself outside a session.
    /// </remarks>
    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Applies an authoritative state change and dispatches the appropriate signals.</summary>
    /// <remarks>Call from server gameplay code; clients receive the result through replication.</remarks>
    /// <param name="state">New authoritative state.</param>
    /// <returns><see langword="true"/> when the server applied a different state.</returns>
    public bool SetState(StringName state)
    {
        if (!IsAuthoritative)
        {
            GD.PushWarning($"{GetPath()}: only the server may change StatefulComponent.State.");
            return false;
        }

        if (!IsStateDeclared(state))
        {
            GD.PushWarning($"{GetPath()}: state '{state}' is not declared by the assigned Schema.");
            return false;
        }

        return ApplyState(state);
    }

    /// <summary>Creates a versioned snapshot for storage by the project persistence system.</summary>
    /// <remarks>Read access is local; authoritative saves should collect the server copy.</remarks>
    /// <returns>A snapshot containing the current local state.</returns>
    public StatefulSavedState SaveState() => new(CurrentSaveVersion, State);

    /// <summary>Restores an authoritative snapshot and re-emits signals even when the state is unchanged.</summary>
    /// <remarks>Call only on the server, host, or dedicated server.</remarks>
    /// <param name="savedState">Versioned snapshot to restore.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The snapshot version is unsupported, or its state is not declared by the assigned schema.
    /// </exception>
    /// <exception cref="InvalidOperationException">The current peer is not the server.</exception>
    public void LoadState(StatefulSavedState savedState)
    {
        if (savedState.Version != CurrentSaveVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedState),
                savedState.Version,
                $"Unsupported stateful save version {savedState.Version}; expected {CurrentSaveVersion}."
            );
        }

        if (!IsAuthoritative)
        {
            throw new InvalidOperationException(
                $"{GetPath()}: state restoration requires authority."
            );
        }

        if (!IsStateDeclared(savedState.State))
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedState),
                savedState.State,
                $"Restored state '{savedState.State}' is not declared by the assigned Schema."
            );
        }

        ApplyState(savedState.State, forceSignals: true);
    }

    internal StateTransition? ApplyStateCore(StringName state, bool forceTransition = false)
    {
        if (_state == state && !forceTransition)
        {
            return null;
        }

        StringName oldState = _state;
        _state = state;

        return new StateTransition(oldState, state);
    }

    private bool ApplyState(StringName state, bool forceSignals = false)
    {
        StateTransition? transition = ApplyStateCore(state, forceSignals);
        if (transition is null)
        {
            return false;
        }

        DispatchStateTransition(transition.Value);
        return true;
    }

    internal void DispatchStateTransition(in StateTransition transition)
    {
        EmitSignal(SignalName.StateChanged, transition.OldState, transition.NewState);

        if (IsAuthoritative)
        {
            EmitSignal(SignalName.StateChangedAuthority, transition.OldState, transition.NewState);
        }

        if (!OS.HasFeature("dedicated_server"))
        {
            EmitSignal(
                SignalName.StateChangedPresentation,
                transition.OldState,
                transition.NewState
            );
        }
    }
}
