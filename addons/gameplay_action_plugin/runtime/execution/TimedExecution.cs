using System;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Execution;

public enum TimedExecutionStartResult
{
    Started,
    AlreadyActive,
    InvalidDuration,
    InvalidExecution,
    MissingSceneTree,
}

public sealed class TimedExecution : IDisposable
{
    private GameplayActionComponent? _component;
    private SceneTree? _sceneTree;
    private double _startedAt;
    private float _correctionInterval;
    private float _nextCorrection;

    public bool IsActive { get; private set; }

    public ulong ExecutionId { get; private set; }

    public float Duration { get; private set; }

    public TimedExecutionStartResult Start(
        GameplayActionComponent component,
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
            return TimedExecutionStartResult.AlreadyActive;
        }

        if (!float.IsFinite(duration) || duration <= 0.0f)
        {
            return TimedExecutionStartResult.InvalidDuration;
        }

        if (
            executionId == 0ul
            || !GodotObject.IsInstanceValid(component)
            || !component.IsExecutionActive(executionId)
        )
        {
            return TimedExecutionStartResult.InvalidExecution;
        }

        if (component.GetTree() is not SceneTree sceneTree)
        {
            return TimedExecutionStartResult.MissingSceneTree;
        }

        _component = component;
        _sceneTree = sceneTree;
        _startedAt = CurrentTimeSeconds();
        _correctionInterval = Mathf.Max(correctionInterval, 0.0f);
        _nextCorrection = _correctionInterval > 0.0f ? _correctionInterval : float.PositiveInfinity;
        Duration = duration;
        ExecutionId = executionId;
        IsActive = true;
        _sceneTree.ProcessFrame += OnProcessFrame;

        component.ReportExecutionLinearProgress(
            executionId,
            progressBase: 0.0f,
            progressPerSecond: 1.0f / duration
        );
        component.SetExecutionProgressSource(executionId, Callable.From(GetProgress));
        return TimedExecutionStartResult.Started;
    }

    public bool Stop(ulong executionId)
    {
        return ExecutionId == executionId && Stop();
    }

    public bool Stop()
    {
        if (!IsActive)
        {
            return false;
        }

        GameplayActionComponent? component = _component;
        ulong executionId = ExecutionId;
        if (GodotObject.IsInstanceValid(component))
        {
            component!.ClearExecutionProgressSource(executionId);
        }

        if (GodotObject.IsInstanceValid(_sceneTree))
        {
            _sceneTree!.ProcessFrame -= OnProcessFrame;
        }

        _component = null;
        _sceneTree = null;
        _startedAt = 0.0;
        _correctionInterval = 0.0f;
        _nextCorrection = 0.0f;
        Duration = 0.0f;
        ExecutionId = 0ul;
        IsActive = false;
        return true;
    }

    public float GetProgress()
    {
        return IsActive && Duration > 0.0f
            ? Mathf.Clamp((float)(CurrentTimeSeconds() - _startedAt) / Duration, 0.0f, 1.0f)
            : 0.0f;
    }

    public void Dispose()
    {
        Stop();
    }

    internal static GameplayActionProgressSample? BuildPredictionSample(float duration)
    {
        return float.IsFinite(duration) && duration > 0.0f
            ? new GameplayActionProgressSample(0.0f, 1.0f / duration, 0L)
            : null;
    }

    private void OnProcessFrame()
    {
        if (
            !IsActive
            || !GodotObject.IsInstanceValid(_component)
            || !_component!.IsExecutionActive(ExecutionId)
        )
        {
            Stop();
            return;
        }

        float elapsed = Mathf.Max((float)(CurrentTimeSeconds() - _startedAt), 0.0f);
        if (elapsed >= Duration)
        {
            GameplayActionComponent component = _component;
            ulong executionId = ExecutionId;
            component.CompleteExecution(executionId);
            Stop(executionId);
            return;
        }

        if (elapsed < _nextCorrection)
        {
            return;
        }

        _nextCorrection = elapsed + _correctionInterval;
        _component.ReportExecutionLinearProgress(ExecutionId, elapsed / Duration, 1.0f / Duration);
    }

    private static double CurrentTimeSeconds() => Time.GetTicksMsec() / 1000.0;
}
