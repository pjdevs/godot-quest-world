using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using QuestWorld.GameplayActions.Runtime.Bindings;

namespace QuestWorld.GameplayActions.Runtime.Runner;

internal sealed class GameplayActionGestureResolver(
    GameplayActionBindingStore bindings,
    Func<
        IReadOnlyList<GameplayActionBindingCandidate>,
        GameplayActionActivationMode?,
        bool
    > requestBest,
    Func<StringName, bool> cancelSustainedInput
)
{
    private readonly Dictionary<StringName, GameplayActionGesturePlan> _gestures = new();
    private readonly HashSet<StringName> _consumedInputs = new();

    public bool TryStart(StringName inputActionName)
    {
        if (
            inputActionName is null
            || inputActionName.IsEmpty
            || _consumedInputs.Contains(inputActionName)
        )
        {
            return false;
        }

        IReadOnlyList<GameplayActionBindingCandidate> candidates = bindings.GetInputCandidates(
            inputActionName
        );
        if (candidates.Count == 0)
        {
            return false;
        }

        _consumedInputs.Add(inputActionName);
        bool waitsForRelease = false;
        float longestHold = 0.0f;
        List<ulong> candidateIds = new(candidates.Count);
        foreach (GameplayActionBindingCandidate candidate in candidates)
        {
            candidateIds.Add(candidate.Binding.Id);
            waitsForRelease |=
                candidate.Binding.ActivationMode
                    is GameplayActionActivationMode.Hold
                        or GameplayActionActivationMode.Release;
            if (candidate.Binding.ActivationMode == GameplayActionActivationMode.Hold)
            {
                longestHold = Mathf.Max(longestHold, candidate.Binding.HoldDuration);
            }
        }

        if (waitsForRelease)
        {
            _gestures[inputActionName] = new GameplayActionGesturePlan(
                inputActionName,
                candidateIds,
                longestHold
            );
            return true;
        }

        bool requested = requestBest(candidates, GameplayActionActivationMode.Press);
        if (!requested)
        {
            _consumedInputs.Remove(inputActionName);
        }

        return requested;
    }

    public bool TryEnd(StringName inputActionName)
    {
        if (inputActionName is null || inputActionName.IsEmpty)
        {
            return false;
        }

        bool consumed = _consumedInputs.Remove(inputActionName);
        bool requested = false;
        if (_gestures.Remove(inputActionName, out GameplayActionGesturePlan? gesture))
        {
            requested = ResolveReleased(gesture);
        }

        bool cancelled = cancelSustainedInput(inputActionName);
        return consumed || requested || cancelled;
    }

    public void Advance(float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0.0f)
        {
            return;
        }

        Dictionary<StringName, float> reached = new();
        foreach (GameplayActionGesturePlan gesture in _gestures.Values)
        {
            gesture.Elapsed += delta;
            float longestRemainingHold = GetLongestRemainingHold(gesture);
            if (longestRemainingHold > 0.0f && gesture.Elapsed >= longestRemainingHold)
            {
                reached.Add(gesture.Input, longestRemainingHold);
            }
        }

        foreach (KeyValuePair<StringName, float> reachedGesture in reached)
        {
            if (_gestures.Remove(reachedGesture.Key, out GameplayActionGesturePlan? gesture))
            {
                RequestReachedHold(gesture, reachedGesture.Value);
            }
        }
    }

    public bool TryGetBindingHoldProgress(ulong bindingId, out float progress, out float elapsed)
    {
        foreach (GameplayActionGesturePlan gesture in _gestures.Values)
        {
            if (
                !gesture.CandidateIds.Contains(bindingId)
                || !bindings.TryGet(bindingId, out GameplayActionBinding? binding)
                || binding!.ActivationMode != GameplayActionActivationMode.Hold
                || binding.HoldDuration <= 0.0f
            )
            {
                continue;
            }

            elapsed = gesture.Elapsed;
            progress = Mathf.Clamp(elapsed / binding.HoldDuration, 0.0f, 1.0f);
            return true;
        }

        progress = 0.0f;
        elapsed = 0.0f;
        return false;
    }

    public IReadOnlyList<StringName> GetConsumedInputs() => new List<StringName>(_consumedInputs);

    private bool ResolveReleased(GameplayActionGesturePlan gesture)
    {
        float longestReached = 0.0f;
        foreach (ulong bindingId in gesture.CandidateIds)
        {
            if (
                bindings.TryGet(bindingId, out GameplayActionBinding? binding)
                && binding!.ActivationMode == GameplayActionActivationMode.Hold
                && binding.HoldDuration <= gesture.Elapsed
            )
            {
                longestReached = Mathf.Max(longestReached, binding.HoldDuration);
            }
        }

        return longestReached > 0.0f
            ? RequestReachedHold(gesture, longestReached)
            : RequestGestureEdge(gesture);
    }

    private bool RequestReachedHold(GameplayActionGesturePlan gesture, float threshold)
    {
        List<GameplayActionBindingCandidate> candidates = new();
        foreach (ulong bindingId in gesture.CandidateIds)
        {
            if (
                bindings.TryGet(bindingId, out GameplayActionBinding? binding)
                && binding!.ActivationMode == GameplayActionActivationMode.Hold
                && Mathf.IsEqualApprox(binding.HoldDuration, threshold)
            )
            {
                candidates.Add(
                    new GameplayActionBindingCandidate(binding, bindings.GetAvailability(bindingId))
                );
            }
        }

        return requestBest(candidates, GameplayActionActivationMode.Hold);
    }

    private bool RequestGestureEdge(GameplayActionGesturePlan gesture)
    {
        List<GameplayActionBindingCandidate> candidates = new();
        foreach (ulong bindingId in gesture.CandidateIds)
        {
            if (!bindings.TryGet(bindingId, out GameplayActionBinding? binding))
            {
                continue;
            }

            if (
                binding!.ActivationMode
                is GameplayActionActivationMode.Press
                    or GameplayActionActivationMode.Release
            )
            {
                candidates.Add(
                    new GameplayActionBindingCandidate(binding, bindings.GetAvailability(bindingId))
                );
            }
        }

        return requestBest(candidates, null);
    }

    private float GetLongestRemainingHold(GameplayActionGesturePlan gesture)
    {
        float longest = 0.0f;
        foreach (ulong bindingId in gesture.CandidateIds)
        {
            if (
                bindings.TryGet(bindingId, out GameplayActionBinding? binding)
                && binding!.ActivationMode == GameplayActionActivationMode.Hold
            )
            {
                longest = Mathf.Max(longest, binding.HoldDuration);
            }
        }

        return longest;
    }
}
