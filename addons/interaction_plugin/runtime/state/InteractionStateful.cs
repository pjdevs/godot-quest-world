using System;
using Godot;

namespace QuestWorld.Interaction.Runtime.State;

/// <summary>
/// Owns an interaction state that can be changed authoritatively, replicated, and persisted.
/// </summary>
/// <remarks>
/// This node is independent from <see cref="QuestWorld.Interaction.Runtime.Interactive.InteractiveComponent"/>
/// and may be used alone.
/// State mutation and restoration are server-only; replicated setters and presentation signals run on clients.
/// </remarks>
[GlobalClass]
public partial class InteractionStateful : Node
{
    /// <summary>Current serialization version used by <see cref="InteractionSavedState"/>.</summary>
    public const int CurrentSaveVersion = 1;

    /// <summary>Emitted on server, host, dedicated server, and clients whenever local state is applied.</summary>
    /// <param name="oldState">Previous <see cref="InteractionState"/> value.</param>
    /// <param name="newState">Applied <see cref="InteractionState"/> value.</param>
    [Signal]
    public delegate void InteractionStateChangedEventHandler(int oldState, int newState);

    /// <summary>Emitted only with authority, including offline games, listen hosts, and dedicated servers.</summary>
    /// <param name="oldState">Previous <see cref="InteractionState"/> value.</param>
    /// <param name="newState">Applied <see cref="InteractionState"/> value.</param>
    [Signal]
    public delegate void InteractionStateChangedAuthorityEventHandler(int oldState, int newState);

    /// <summary>Emitted in offline games, clients, and listen hosts, but never on a dedicated server.</summary>
    /// <param name="oldState">Previous <see cref="InteractionState"/> value.</param>
    /// <param name="newState">Applied <see cref="InteractionState"/> value.</param>
    [Signal]
    public delegate void InteractionStateChangedPresentationEventHandler(
        int oldState,
        int newState
    );

    /// <summary>Gets or sets the local state applied when the node enters the scene tree.</summary>
    [Export]
    public InteractionState InitialState { get; set; } = InteractionState.Idle;

    [Export]
    private InteractionState ReplicatedState
    {
        get => _state;
        set => ApplyState(value);
    }

    private InteractionState _state;

    /// <summary>Gets the state currently applied on this peer.</summary>
    public InteractionState State => _state;

    /// <summary>Godot callback that initializes the local state without emitting change signals.</summary>
    public override void _Ready()
    {
        _state = InitialState;
    }

    /// <summary>Applies an authoritative state change and dispatches the appropriate signals.</summary>
    /// <remarks>Call from server gameplay code; clients receive the result through replication.</remarks>
    /// <param name="state">New authoritative state.</param>
    /// <returns><see langword="true"/> when the server applied a different state.</returns>
    public bool SetState(InteractionState state)
    {
        if (!Multiplayer.IsServer())
        {
            GD.PushWarning($"{GetPath()}: only the server may change InteractionStateful.State.");
            return false;
        }

        return ApplyState(state);
    }

    /// <summary>Creates a versioned snapshot for storage by the project persistence system.</summary>
    /// <remarks>Read access is local; authoritative saves should collect the server copy.</remarks>
    /// <returns>A snapshot containing the current local state.</returns>
    public InteractionSavedState SaveState() => new(CurrentSaveVersion, State);

    /// <summary>Restores an authoritative snapshot and re-emits signals even when the state is unchanged.</summary>
    /// <remarks>Call only on the server, host, or dedicated server.</remarks>
    /// <param name="savedState">Versioned snapshot to restore.</param>
    /// <exception cref="ArgumentOutOfRangeException">The snapshot version is unsupported.</exception>
    /// <exception cref="InvalidOperationException">The current peer is not the server.</exception>
    public void LoadState(InteractionSavedState savedState)
    {
        if (savedState.Version != CurrentSaveVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedState),
                savedState.Version,
                $"Unsupported interaction save version {savedState.Version}; expected {CurrentSaveVersion}."
            );
        }

        if (!Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                $"{GetPath()}: state restoration requires authority."
            );
        }

        ApplyState(savedState.State, forceSignals: true);
    }

    private bool ApplyState(InteractionState state, bool forceSignals = false)
    {
        if (_state == state && !forceSignals)
        {
            return false;
        }

        InteractionState oldState = _state;
        _state = state;
        EmitSignal(SignalName.InteractionStateChanged, (int)oldState, (int)state);

        if (Multiplayer.IsServer())
        {
            EmitSignal(SignalName.InteractionStateChangedAuthority, (int)oldState, (int)state);
        }

        if (!OS.HasFeature("dedicated_server"))
        {
            EmitSignal(SignalName.InteractionStateChangedPresentation, (int)oldState, (int)state);
        }

        return true;
    }
}
