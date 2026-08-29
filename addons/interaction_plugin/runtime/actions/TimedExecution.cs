using System;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>Composable timing policy for one generic interaction execution.</summary>
/// <remarks>
/// This helper is not another interaction execution. It owns the authoritative time anchor, sparse
/// linear synchronization samples, locally derived progress, and automatic completion for the
/// generic execution identified by <see cref="ExecutionId"/>. The interaction core continues to see
/// only a payload-free <see cref="InteractionExecutionRunning"/> result.
/// </remarks>
public sealed class TimedExecution : IDisposable
{
    private InteractiveComponent? _interactive;
    private SceneTree? _sceneTree;
    private float _elapsed;
    private float _correctionInterval;
    private float _nextCorrection;

    /// <summary>Gets whether this helper currently owns a positive-duration clock.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the generic execution occurrence this clock belongs to.</summary>
    public ulong ExecutionId { get; private set; }

    /// <summary>Gets the current positive duration, or zero while inactive.</summary>
    public float Duration { get; private set; }

    /// <summary>Starts timing one already-reserved generic execution.</summary>
    /// <param name="interactive">Authoritative target owning the execution.</param>
    /// <param name="executionId">Identifier allocated by the interaction core.</param>
    /// <param name="duration">Positive duration in seconds.</param>
    /// <param name="correctionInterval">Interval between sparse authority samples.</param>
    /// <returns>False when no positive clock could be started.</returns>
    public bool Start(
        InteractiveComponent interactive,
        ulong executionId,
        float duration,
        float correctionInterval = 0.5f
    )
    {
        if (IsActive)
        {
            GD.PushWarning(
                $"TimedExecution already owns execution '{ExecutionId}' and cannot start '{executionId}'."
            );
            return false;
        }

        if (
            executionId == 0ul
            || !GodotObject.IsInstanceValid(interactive)
            || !interactive.IsExecutionActive(executionId)
            || interactive.GetTree() is not SceneTree sceneTree
        )
        {
            return false;
        }

        if (!float.IsFinite(duration))
        {
            GD.PushWarning(
                $"{interactive.GetPath()}: timed execution duration must be finite; the execution will wait for gameplay."
            );
            duration = 0.0f;
        }
        else
        {
            duration = Mathf.Max(duration, 0.0f);
        }
        if (duration <= 0.0f)
        {
            return false;
        }

        _interactive = interactive;
        _sceneTree = sceneTree;
        _elapsed = 0.0f;
        _correctionInterval = Mathf.Max(correctionInterval, 0.0f);
        _nextCorrection = _correctionInterval > 0.0f ? _correctionInterval : float.PositiveInfinity;
        Duration = duration;
        ExecutionId = executionId;
        IsActive = true;
        _sceneTree.ProcessFrame += OnProcessFrame;

        interactive.ReportExecutionLinearProgress(
            executionId,
            progressBase: 0.0f,
            progressPerSecond: 1.0f / duration
        );
        interactive.SetExecutionProgressSource(executionId, Callable.From(GetProgress));
        return true;
    }

    /// <summary>Stops this helper when it still belongs to <paramref name="executionId"/>.</summary>
    /// <returns>True when an active clock was stopped.</returns>
    public bool Stop(ulong executionId)
    {
        return ExecutionId == executionId && Stop();
    }

    /// <summary>Stops whichever clock this helper currently owns.</summary>
    /// <returns>True when an active clock was stopped.</returns>
    public bool Stop()
    {
        if (!IsActive)
        {
            return false;
        }

        InteractiveComponent? interactive = _interactive;
        ulong executionId = ExecutionId;
        if (GodotObject.IsInstanceValid(interactive))
        {
            interactive!.ClearExecutionProgressSource(executionId);
        }

        if (GodotObject.IsInstanceValid(_sceneTree))
        {
            _sceneTree!.ProcessFrame -= OnProcessFrame;
        }

        _interactive = null;
        _sceneTree = null;
        _elapsed = 0.0f;
        _correctionInterval = 0.0f;
        _nextCorrection = 0.0f;
        Duration = 0.0f;
        ExecutionId = 0ul;
        IsActive = false;
        return true;
    }

    /// <summary>Derives normalized progress locally from the synchronized time anchor.</summary>
    public float GetProgress()
    {
        return IsActive && Duration > 0.0f ? Mathf.Clamp(_elapsed / Duration, 0.0f, 1.0f) : 0.0f;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    internal static InteractionProgressSample? BuildPredictionSample(float duration)
    {
        duration = float.IsFinite(duration) ? Mathf.Max(duration, 0.0f) : 0.0f;
        return duration > 0.0f ? new InteractionProgressSample(0.0f, 1.0f / duration, 0L) : null;
    }

    private void OnProcessFrame()
    {
        if (
            !IsActive
            || !GodotObject.IsInstanceValid(_interactive)
            || !_interactive!.IsExecutionActive(ExecutionId)
        )
        {
            Stop();
            return;
        }

        if (!_interactive.CanProcess())
        {
            return;
        }

        _elapsed += Mathf.Max((float)_interactive.GetProcessDeltaTime(), 0.0f);
        if (_elapsed >= Duration)
        {
            InteractiveComponent interactive = _interactive;
            ulong executionId = ExecutionId;
            interactive.CompleteExecution(executionId);
            Stop(executionId);
            return;
        }

        if (_elapsed < _nextCorrection)
        {
            return;
        }

        _nextCorrection = _elapsed + _correctionInterval;
        _interactive.ReportExecutionLinearProgress(
            ExecutionId,
            _elapsed / Duration,
            1.0f / Duration
        );
    }
}
