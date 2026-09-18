using AnimatedInteractionPlugin.Runtime;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Execution;
using Godot;

/// <summary>
/// Plug-and-play Gameplay Action executor for one animated interaction profile.
/// </summary>
[GlobalClass]
public partial class AnimatedInteractionExecutor : GameplayActionExecutor
{
    [Export]
    public AnimatedInteractionProfile? Profile { get; set; }

    /// <summary>
    /// Optional gameplay work duration. When positive, it starts on the controlled phase.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.05,or_greater")]
    public float WorkDuration { get; set; }

    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    private readonly TimedExecution _workTimer = new();
    private AnimatedInteractionOperation? _operation;
    private GameplayActionContext? _executionContext;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        if (_operation is not null)
        {
            return new GameplayActionExecutionFailed(
                "The animated interaction executor is already active."
            );
        }

        if (!TryValidateConfiguration(out string reason))
        {
            return new GameplayActionExecutionFailed(reason);
        }

        IAnimatedInteractor? interactor =
            context.GetInstigator<IAnimatedInteractor>() ?? context.GetHost<IAnimatedInteractor>();
        AnimatedInteractionOperation? operation =
            interactor?.AnimatedInteractionComponent?.TryStart(Profile!);
        if (operation is null)
        {
            return new GameplayActionExecutionFailed(
                "The animated interaction could not start on this actor."
            );
        }

        _executionContext = context;
        _operation = operation;
        operation.PhaseEntered += OnPhaseEntered;
        operation.CommitReached += OnCommitReached;
        operation.Finished += OnOperationFinished;
        operation.Cancelled += OnOperationCancelled;
        return new GameplayActionExecutionRunning();
    }

    /// <summary>
    /// Performs the irreversible domain mutation at the choreography commit boundary.
    /// The spike implementation succeeds without mutation so profiles can be tried directly.
    /// </summary>
    protected virtual bool TryCommit(in GameplayActionContext context) => true;

    protected internal override void OnExecutionCompleted(in GameplayActionContext context)
    {
        StopActiveOperation();
        base.OnExecutionCompleted(context);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        StopActiveOperation();
        base.OnExecutionCancelled(context, reason);
    }

    protected internal override void OnExecutionFailed(
        in GameplayActionContext context,
        string reason
    )
    {
        StopActiveOperation();
        base.OnExecutionFailed(context, reason);
    }

    private void OnPhaseEntered(AnimatedInteractionPhase phase)
    {
        if (
            phase.Transition != AnimatedInteractionTransition.Controlled
            || WorkDuration <= 0.0f
            || _executionContext is not GameplayActionContext context
        )
        {
            return;
        }

        TimedExecutionStartResult result = _workTimer.Start(
            context.Component,
            context.ExecutionId,
            WorkDuration,
            CorrectionInterval
        );
        if (result != TimedExecutionStartResult.Started)
        {
            FailActiveExecution(WorkTimerFailureReason(result));
            return;
        }

        _workTimer.Expired += OnWorkExpired;
    }

    private void OnWorkExpired()
    {
        _workTimer.Expired -= OnWorkExpired;
        if (_operation?.ReachCommit() != true)
        {
            FailActiveExecution("The animated interaction could not reach its commit boundary.");
        }
    }

    private void OnCommitReached()
    {
        if (
            _operation is not AnimatedInteractionOperation operation
            || _executionContext is not GameplayActionContext context
        )
        {
            return;
        }

        _workTimer.Expired -= OnWorkExpired;
        _workTimer.Stop();
        if (!TryCommit(context))
        {
            FailActiveExecution("The animated interaction commit failed.");
            return;
        }

        context.ReleaseRequesterDependency();
        if (operation.CurrentPhase?.Transition == AnimatedInteractionTransition.Controlled)
        {
            operation.Advance();
        }
    }

    private void OnOperationFinished()
    {
        GameplayActionContext? context = _executionContext;
        Cleanup();
        context?.CompleteExecution();
    }

    private void OnOperationCancelled()
    {
        GameplayActionContext? context = _executionContext;
        Cleanup();
        if (context is GameplayActionContext activeContext)
        {
            activeContext.Component.CancelExecution(
                activeContext.ExecutionId,
                "Animated interaction cancelled."
            );
        }
    }

    private void FailActiveExecution(string reason)
    {
        AnimatedInteractionOperation? operation = _operation;
        GameplayActionContext? context = _executionContext;
        Cleanup();
        operation?.Cancel();
        context?.FailExecution(reason);
    }

    private void StopActiveOperation()
    {
        AnimatedInteractionOperation? operation = _operation;
        Cleanup();
        operation?.Cancel();
    }

    private void Cleanup()
    {
        if (_operation is AnimatedInteractionOperation operation)
        {
            operation.PhaseEntered -= OnPhaseEntered;
            operation.CommitReached -= OnCommitReached;
            operation.Finished -= OnOperationFinished;
            operation.Cancelled -= OnOperationCancelled;
        }

        _workTimer.Expired -= OnWorkExpired;
        _workTimer.Stop();
        _operation = null;
        _executionContext = null;
    }

    private bool TryValidateConfiguration(out string reason)
    {
        reason = string.Empty;
        if (Profile is null || Profile.Phases.Count == 0)
        {
            reason = "The animated interaction executor requires a profile with phases.";
            return false;
        }

        if (!float.IsFinite(WorkDuration) || WorkDuration < 0.0f)
        {
            reason = "Animated interaction work duration must be finite and non-negative.";
            return false;
        }

        AnimatedInteractionPhase? controlledPhase = null;
        int authoredCommitCount = 0;
        foreach (AnimatedInteractionPhase? phase in Profile.Phases)
        {
            if (phase is null)
            {
                continue;
            }

            if (phase.HasCommitPoint)
            {
                authoredCommitCount++;
            }

            if (phase.Transition != AnimatedInteractionTransition.Controlled)
            {
                continue;
            }

            if (controlledPhase is not null)
            {
                reason = "The plug-and-play executor supports only one controlled phase.";
                return false;
            }

            controlledPhase = phase;
        }

        if (WorkDuration > 0.0f && controlledPhase is null)
        {
            reason = "A positive work duration requires one controlled phase.";
            return false;
        }

        if (WorkDuration > 0.0f && authoredCommitCount > 0)
        {
            reason = "Choose either work-duration commit or an authored commit point, not both.";
            return false;
        }

        if (WorkDuration <= 0.0f && controlledPhase is not null && !controlledPhase.HasCommitPoint)
        {
            reason = "A controlled phase requires work duration or an authored commit point.";
            return false;
        }

        return true;
    }

    private static string WorkTimerFailureReason(TimedExecutionStartResult result) =>
        result switch
        {
            TimedExecutionStartResult.AlreadyActive =>
                "The animated interaction work timer is already active.",
            TimedExecutionStartResult.InvalidDuration =>
                "Animated interaction work duration must be finite and greater than zero.",
            TimedExecutionStartResult.InvalidExecution =>
                "The Gameplay Action execution is no longer active.",
            TimedExecutionStartResult.MissingSceneTree =>
                "Animated interaction work requires an active scene tree.",
            _ => "The animated interaction work timer could not start.",
        };
}
