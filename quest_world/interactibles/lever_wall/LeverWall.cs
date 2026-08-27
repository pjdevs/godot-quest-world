using Godot;
using QuestWorld.State;

/// <summary>Wall raised and lowered by a remote button, owning its own transition duration.</summary>
/// <remarks>
/// The interaction framework knows nothing about this object: a button carries the two actions and a
/// generic <c>SetStateInteractionExecutor</c> writes <c>raising</c> or <c>lowering</c> here. This
/// script owns what is genuinely specific to the wall, and only that: how long a transition lasts,
/// and what the geometry does meanwhile.
/// <para>
/// The animation is driven by <see cref="StatefulComponent.StateChanged"/> rather than by the
/// presentation signal, because the animated mesh carries collision: moving it is world simulation and
/// must also happen on a dedicated server.
/// </para>
/// </remarks>
public partial class LeverWall : Node3D
{
    private static readonly StringName LoweredState = new("lowered");
    private static readonly StringName RaisingState = new("raising");
    private static readonly StringName RaisedState = new("raised");
    private static readonly StringName LoweringState = new("lowering");

    /// <summary>Gets or sets the required component owning the authoritative wall state.</summary>
    [Export]
    public StatefulComponent? Stateful { get; set; } = null;

    /// <summary>Gets or sets the player animating the wall geometry and its collision.</summary>
    [Export]
    public AnimationPlayer? AnimationPlayer { get; set; } = null;

    /// <summary>Gets or sets the animation played forward while the wall is raising.</summary>
    [Export]
    public string RaiseAnimationName { get; set; } = "lever_wall_up";

    private StringName? _pendingState;
    private float _pendingElapsed;

    /// <summary>Godot callback that observes the wall state and shows the current one.</summary>
    public override void _Ready()
    {
        if (Stateful is null)
        {
            GD.PushError($"{GetPath()}: LeverWall requires a Stateful.");
            SetProcess(false);
            return;
        }

        Stateful.StateChanged += OnStateChanged;
        ApplyStateAnimation(Stateful.State);
    }

    /// <summary>Gets whether this peer runs the authoritative half of this wall.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have only pushes an error and answers no, which would make every
    /// authoritative path refuse itself outside a session.
    /// </remarks>
    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Godot callback that completes an authoritative transition once its animation is over.</summary>
    public override void _Process(double delta)
    {
        if (_pendingState is null || Stateful is null || !IsAuthoritative)
        {
            return;
        }

        _pendingElapsed += (float)delta;
        if (_pendingElapsed < TransitionDuration)
        {
            return;
        }

        StringName completedState = _pendingState;
        _pendingState = null;
        Stateful.SetState(completedState);
    }

    /// <summary>Godot callback that stops observing the wall state.</summary>
    public override void _ExitTree()
    {
        if (Stateful is not null && IsInstanceValid(Stateful))
        {
            Stateful.StateChanged -= OnStateChanged;
        }
    }

    private float TransitionDuration =>
        AnimationPlayer is not null && AnimationPlayer.HasAnimation(RaiseAnimationName)
            ? (float)AnimationPlayer.GetAnimation(RaiseAnimationName).Length
            : 0.0f;

    // The animation carries the collision, so it runs on a synchronization too: a player joining a
    // level whose wall is already raised must find it raised. The flag is ignored on purpose — this
    // wall plays no one-shot that would fire for an event its player never witnessed.
    private void OnStateChanged(StringName oldState, StringName newState, bool isSynchronization)
    {
        ApplyStateAnimation(newState);
        ScheduleTransition(newState);
    }

    private void ScheduleTransition(StringName state)
    {
        if (!IsAuthoritative)
        {
            return;
        }

        _pendingElapsed = 0.0f;

        if (state == RaisingState)
        {
            _pendingState = RaisedState;
        }
        else if (state == LoweringState)
        {
            _pendingState = LoweredState;
        }
        else
        {
            _pendingState = null;
        }
    }

    private void ApplyStateAnimation(StringName state)
    {
        if (AnimationPlayer is null || !AnimationPlayer.HasAnimation(RaiseAnimationName))
        {
            return;
        }

        AnimationPlayer.AssignedAnimation = RaiseAnimationName;

        if (state == RaisingState)
        {
            AnimationPlayer.Play(RaiseAnimationName);
        }
        else if (state == LoweringState)
        {
            AnimationPlayer.PlayBackwards(RaiseAnimationName);
        }
        else if (state == RaisedState)
        {
            AnimationPlayer.Seek(
                AnimationPlayer.GetAnimation(RaiseAnimationName).Length,
                update: true
            );
        }
        else
        {
            AnimationPlayer.Seek(0.0f, update: true);
        }
    }
}
