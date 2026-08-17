using System;
using Godot;

namespace QuestWorld.Interaction.Runtime.State;

[GlobalClass]
public partial class InteractionStateful : Node
{
    public const int CurrentSaveVersion = 1;

    [Signal]
    public delegate void InteractionStateChangedEventHandler(int oldState, int newState);

    [Signal]
    public delegate void InteractionStateChangedAuthorityEventHandler(int oldState, int newState);

    [Signal]
    public delegate void InteractionStateChangedPresentationEventHandler(
        int oldState,
        int newState
    );

    [Export]
    public InteractionState InitialState { get; set; } = InteractionState.Idle;

    [Export]
    public InteractionState ReplicatedState
    {
        get => _state;
        set => ApplyState(value);
    }

    private InteractionState _state;

    public InteractionState State => _state;

    public override void _Ready()
    {
        _state = InitialState;
    }

    public bool SetState(InteractionState state)
    {
        if (!Multiplayer.IsServer())
        {
            GD.PushWarning($"{GetPath()}: only the server may change InteractionStateful.State.");
            return false;
        }

        return ApplyState(state);
    }

    public InteractionSavedState SaveState() => new(CurrentSaveVersion, State);

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
