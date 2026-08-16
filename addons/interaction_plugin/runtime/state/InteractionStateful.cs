using System;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.State;

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

    private InteractionState _state;
    private InteractiveComponent _interactive = null!;
    private InteractionInteractor _activeInteractor = null!;

    public InteractionState State => _state;

    public InteractionInteractor ActiveInteractor => _activeInteractor;

    public override void _Ready()
    {
        _state = InitialState;
        _interactive = FindAncestorOrSibling<InteractiveComponent>();
        if (_interactive == null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionStateful requires an InteractiveComponent on the same owner."
            );
        }
    }

    public bool SetState(InteractionState state)
    {
        if (IsInsideTree() && !Multiplayer.IsServer())
        {
            GD.PushWarning($"{GetPath()}: only the server may change InteractionStateful.State.");
            return false;
        }

        return ApplyState(state);
    }

    public bool StartInteractionPhase(InteractionInteractor interactor)
    {
        if (interactor == null || ActiveInteractor != null || State != InteractionState.Idle)
        {
            return false;
        }

        if (IsInsideTree() && !Multiplayer.IsServer())
        {
            return false;
        }

        _activeInteractor = interactor;
        ApplyState(InteractionState.Activating);
        return true;
    }

    public bool EndInteractionPhase(InteractionState nextState)
    {
        if (ActiveInteractor == null)
        {
            return false;
        }

        if (IsInsideTree() && !Multiplayer.IsServer())
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

        if (IsInsideTree() && !Multiplayer.IsServer())
        {
            throw new InvalidOperationException(
                $"{GetPath()}: state restoration requires authority."
            );
        }

        ApplyState(savedState.State, forceCallbacks: true);
    }

    public override void _ExitTree()
    {
        if (ActiveInteractor != null && (!IsInsideTree() || Multiplayer.IsServer()))
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

        Node owner = _interactive?.InteractionOwner!;
        if (IsAuthority())
        {
            (owner as IInteractionStateHandler)?.OnInteractionStateChangedAuthority(
                oldState,
                state
            );
        }

        if (!OS.HasFeature("dedicated_server"))
        {
            (owner as IInteractionStateHandler)?.OnInteractionStateChangedPresentation(
                oldState,
                state
            );
        }

        _interactive?.NotifyStatusChanged();
        return true;
    }

    private void ReleaseActiveInteractor(bool notifyInputEnded)
    {
        InteractionInteractor releasedInteractor = _activeInteractor;
        _activeInteractor = null!;
        if (releasedInteractor == null || _interactive == null)
        {
            return;
        }

        if (notifyInputEnded)
        {
            InteractionContext context = new(
                releasedInteractor,
                _interactive,
                _interactive.InteractionOwner
            );
            (_interactive.InteractionOwner as IInteractionHandler)?.OnEndInteractionInput(context);
        }

        _interactive.NotifyStatusChanged();
    }

    private T FindAncestorOrSibling<T>()
        where T : class
    {
        Node owner = GetParent();
        if (owner == null)
        {
            return null!;
        }

        foreach (Node child in owner.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }
        }

        return owner as T ?? null!;
    }

    private bool IsAuthority() => !IsInsideTree() || Multiplayer.IsServer();
}
