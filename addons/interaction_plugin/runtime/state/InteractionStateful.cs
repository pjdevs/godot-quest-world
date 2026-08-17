using System;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.State;

[GlobalClass]
public partial class InteractionStateful : Node
{
    public const int CurrentSaveVersion = 1;

    [Signal]
    public delegate void InteractionStateChangedEventHandler(int oldState, int newState);

    [Export]
    public InteractionState InitialState { get; set; } = InteractionState.Idle;

    [Export]
    public InteractionState ReplicatedState
    {
        get => _state;
        set => ApplyState(value);
    }

    [Export]
    public InteractiveComponent? Interactive
    {
        get => _interactive;
        set
        {
            if (_interactive == value)
            {
                return;
            }

            _interactive = value;
        }
    }

    private InteractionState _state;
    private InteractiveComponent? _interactive;
    private InteractionInteractor? _activeInteractor;

    public InteractionState State => _state;

    internal InteractionInteractor? ActiveInteractor => _activeInteractor;

    public override void _Ready()
    {
        _state = InitialState;
        if (Interactive is null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionStateful requires an explicitly assigned InteractiveComponent."
            );
        }
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

    public bool StartInteractionPhase(InteractionInteractor interactor)
    {
        if (interactor is null || ActiveInteractor is not null || State != InteractionState.Idle)
        {
            return false;
        }

        if (!Multiplayer.IsServer())
        {
            return false;
        }

        _activeInteractor = interactor;
        ApplyState(InteractionState.Activating);
        return true;
    }

    public bool EndInteractionPhase(InteractionState nextState)
    {
        if (ActiveInteractor is null)
        {
            return false;
        }

        if (!Multiplayer.IsServer())
        {
            return false;
        }

        ReleaseActiveInteractor(notifyInputEnded: false);
        return ApplyState(nextState);
    }

    public bool ReleaseInteractionInput(InteractionInteractor interactor)
    {
        if (ActiveInteractor != interactor)
        {
            return false;
        }

        ReleaseActiveInteractor(notifyInputEnded: true);
        return true;
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

        ApplyState(savedState.State, forceCallbacks: true);
    }

    public override void _ExitTree()
    {
        if (ActiveInteractor is not null && Multiplayer.IsServer())
        {
            ReleaseActiveInteractor(notifyInputEnded: false);
        }
    }

    private bool ApplyState(InteractionState state, bool forceCallbacks = false)
    {
        if (_state == state && !forceCallbacks)
        {
            return false;
        }

        InteractionState oldState = _state;
        _state = state;
        EmitSignal(SignalName.InteractionStateChanged, (int)oldState, (int)state);

        if (Interactive?.InteractionOwner is IInteractionStateHandler handler)
        {
            if (Multiplayer.IsServer())
            {
                handler.OnInteractionStateChangedAuthority(oldState, state);
            }

            if (!OS.HasFeature("dedicated_server"))
            {
                handler.OnInteractionStateChangedPresentation(oldState, state);
            }
        }

        Interactive?.NotifyStatusChanged();
        return true;
    }

    private void ReleaseActiveInteractor(bool notifyInputEnded)
    {
        InteractionInteractor? releasedInteractor = _activeInteractor;
        _activeInteractor = null;
        if (releasedInteractor is null || Interactive is null)
        {
            return;
        }

        if (notifyInputEnded && Interactive.InteractionOwner is IInteractionHandler handler)
        {
            InteractionContext context = new(
                releasedInteractor,
                Interactive,
                Interactive.InteractionOwner
            );
            handler.OnEndInteractionInput(context);
        }

        Interactive.NotifyStatusChanged();
    }
}
