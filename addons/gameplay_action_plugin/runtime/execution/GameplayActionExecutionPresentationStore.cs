using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Execution;

internal readonly record struct GameplayActionExecutionPresentationSource(
    ulong ExecutionId,
    GameplayAction Action
);

internal sealed class GameplayActionExecutionPresentationStore(
    GameplayActionComponent owner,
    Func<StringName, GameplayAction?> resolveAction,
    Action<StringName> notifyChanged
)
{
    private readonly Dictionary<StringName, GameplayActionExecutionPresentationSlot> _visible =
        new();
    private readonly Dictionary<ulong, GameplayActionExecutionPresentationSlot> _pending = new();
    private readonly HashSet<StringName> _warnedUnknownReplicatedActions = new();

    public IReadOnlyList<GameplayActionExecutionPresentation> GetPresentations(
        IEnumerable<GameplayAction> actions
    )
    {
        List<GameplayActionExecutionPresentation> presentations = new();
        foreach (GameplayAction action in actions)
        {
            if (
                action.Definition is not null
                && _visible.TryGetValue(
                    action.Definition.Id,
                    out GameplayActionExecutionPresentationSlot? slot
                )
            )
            {
                presentations.Add(Resolve(slot));
            }
        }

        return presentations;
    }

    public bool TryGet(StringName actionId, out GameplayActionExecutionPresentation presentation)
    {
        presentation = default;
        if (
            actionId is null
            || actionId.IsEmpty
            || !_visible.TryGetValue(actionId, out GameplayActionExecutionPresentationSlot? slot)
        )
        {
            return false;
        }

        presentation = Resolve(slot);
        return true;
    }

    public void AddExecution(ulong executionId, GameplayAction action)
    {
        if (action.Definition is null)
        {
            return;
        }

        StringName actionId = action.Definition.Id;
        GameplayActionExecutionPresentationSlot slot;
        if (_pending.Remove(executionId, out GameplayActionExecutionPresentationSlot? pending))
        {
            slot = pending;
        }
        else
        {
            slot = new GameplayActionExecutionPresentationSlot(executionId, actionId);
        }

        slot.ExecutionId = executionId;
        bool structuralChange =
            !_visible.TryGetValue(actionId, out GameplayActionExecutionPresentationSlot? previous)
            || previous.ExecutionId != executionId;
        _visible[actionId] = slot;
        if (structuralChange)
        {
            notifyChanged(actionId);
        }
    }

    public void RemoveExecution(ulong executionId, GameplayAction action)
    {
        _pending.Remove(executionId);
        if (
            action.Definition is not GameplayActionDefinition definition
            || !_visible.TryGetValue(
                definition.Id,
                out GameplayActionExecutionPresentationSlot? slot
            )
            || slot.ExecutionId != executionId
        )
        {
            return;
        }

        _visible.Remove(definition.Id);
        notifyChanged(definition.Id);
    }

    public void RemoveAction(StringName actionId)
    {
        if (_visible.Remove(actionId))
        {
            notifyChanged(actionId);
        }
    }

    public bool ReportPublished(ulong executionId, GameplayAction action, float? progress)
    {
        if (progress.HasValue && !float.IsFinite(progress.Value))
        {
            GD.PushWarning($"{owner.GetPath()}: execution progress must be finite.");
            return false;
        }

        bool wasVisible = IsVisible(action, executionId);
        GameplayActionExecutionPresentationSlot slot = GetProgressSlot(executionId, action);
        if (slot.Progress.RejectPublishedOverride(owner, executionId))
        {
            return false;
        }

        float normalized = progress.HasValue ? Mathf.Clamp(progress.Value, 0.0f, 1.0f) : 0.0f;
        if (slot.Progress.MatchesPublished(progress.HasValue ? normalized : null))
        {
            return false;
        }

        slot.Progress.Publish(progress.HasValue ? normalized : null);
        if (wasVisible)
        {
            notifyChanged(slot.ActionId);
        }

        return true;
    }

    public bool SetSource(ulong executionId, Callable source)
    {
        if (
            executionId == 0ul
            || !TryGetProgressSlot(executionId, out GameplayActionExecutionPresentationSlot? slot)
            || slot is null
        )
        {
            return false;
        }

        slot.Progress.SetSource(source);
        if (IsVisible(slot.ActionId, slot.ExecutionId))
        {
            notifyChanged(slot.ActionId);
        }

        return true;
    }

    public bool ClearSource(ulong executionId)
    {
        if (
            executionId == 0ul
            || !TryGetProgressSlot(executionId, out GameplayActionExecutionPresentationSlot? slot)
            || slot is null
        )
        {
            return false;
        }

        slot.Progress.ClearSource();
        if (IsVisible(slot.ActionId, slot.ExecutionId))
        {
            notifyChanged(slot.ActionId);
        }

        return true;
    }

    public bool ReportLinear(
        ulong executionId,
        GameplayAction action,
        float progressBase,
        float progressPerSecond
    )
    {
        bool wasVisible = IsVisible(action, executionId);
        GameplayActionExecutionPresentationSlot slot = GetProgressSlot(executionId, action);
        slot.Progress.ReportLinear(progressBase, progressPerSecond, CurrentTimeSeconds());
        if (wasVisible)
        {
            notifyChanged(slot.ActionId);
        }

        return true;
    }

    public bool TryGetSample(
        ulong executionId,
        out bool hasProgress,
        out GameplayActionProgressSample sample
    )
    {
        hasProgress = false;
        sample = default;
        return TryGetProgressSlot(executionId, out GameplayActionExecutionPresentationSlot? slot)
            && slot is not null
            && slot.Progress.TryGetSample(out hasProgress, out sample);
    }

    public Godot.Collections.Array BuildReplicatedEntries(
        IEnumerable<GameplayActionExecutionPresentationSource> executions
    )
    {
        Godot.Collections.Array entries = new();
        foreach (GameplayActionExecutionPresentationSource execution in executions)
        {
            GameplayAction action = execution.Action;
            if (
                action.Definition is not GameplayActionDefinition definition
                || action.ExecutionVisibility != GameplayActionExecutionVisibility.Replicated
            )
            {
                continue;
            }

            GameplayActionExecutionPresentationSlot slot = GetProgressSlot(
                execution.ExecutionId,
                action
            );
            slot.Progress.TryGetSample(
                out bool hasProgress,
                out GameplayActionProgressSample sample
            );
            entries.Add(
                new Godot.Collections.Dictionary
                {
                    ["action_id"] = definition.Id,
                    ["execution_id"] = checked((long)execution.ExecutionId),
                    ["progress_present"] = hasProgress,
                    ["progress_base"] = sample.ProgressBase,
                    ["progress_per_second"] = sample.ProgressPerSecond,
                    ["revision"] = sample.Revision,
                }
            );
        }

        return entries;
    }

    public void ApplyReplicatedEntries(Godot.Collections.Array entries)
    {
        HashSet<StringName> presentActions = new();
        foreach (Variant entryValue in entries)
        {
            if (entryValue.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary entry = entryValue.AsGodotDictionary();
            if (!TryReadReplicatedEntry(entry, out ReplicatedExecutionEntry decoded))
            {
                continue;
            }

            GameplayAction? action = resolveAction(decoded.ActionId);
            if (action?.Definition is null)
            {
                if (_warnedUnknownReplicatedActions.Add(decoded.ActionId))
                {
                    GD.PushWarning(
                        $"{owner.GetPath()}: replicated execution references unknown action '{decoded.ActionId}'."
                    );
                }

                continue;
            }

            if (action.ExecutionVisibility != GameplayActionExecutionVisibility.Replicated)
            {
                continue;
            }

            presentActions.Add(decoded.ActionId);
            ApplyReplicatedExecution(decoded);
        }

        List<StringName> removed = new();
        foreach (
            KeyValuePair<StringName, GameplayActionExecutionPresentationSlot> visible in _visible
        )
        {
            GameplayAction? action = resolveAction(visible.Key);
            if (
                action?.ExecutionVisibility == GameplayActionExecutionVisibility.Replicated
                && !presentActions.Contains(visible.Key)
                && visible.Value.ExecutionId != 0ul
            )
            {
                removed.Add(visible.Key);
            }
        }

        foreach (StringName actionId in removed)
        {
            _visible.Remove(actionId);
            notifyChanged(actionId);
        }
    }

    private GameplayActionExecutionPresentation Resolve(
        GameplayActionExecutionPresentationSlot slot
    ) =>
        new(
            slot.ExecutionId,
            slot.ActionId,
            slot.Progress.Resolve(owner, slot.ActionId, CurrentTimeSeconds())
        );

    private bool TryGetProgressSlot(
        ulong executionId,
        out GameplayActionExecutionPresentationSlot? slot
    )
    {
        foreach (GameplayActionExecutionPresentationSlot candidate in _visible.Values)
        {
            if (candidate.ExecutionId == executionId)
            {
                slot = candidate;
                return true;
            }
        }

        return _pending.TryGetValue(executionId, out slot);
    }

    private bool IsVisible(GameplayAction action, ulong executionId)
    {
        return action.Definition is not null && IsVisible(action.Definition.Id, executionId);
    }

    private bool IsVisible(StringName actionId, ulong executionId)
    {
        return _visible.TryGetValue(actionId, out GameplayActionExecutionPresentationSlot? slot)
            && slot.ExecutionId == executionId;
    }

    private GameplayActionExecutionPresentationSlot GetProgressSlot(
        ulong executionId,
        GameplayAction action
    )
    {
        if (
            TryGetProgressSlot(executionId, out GameplayActionExecutionPresentationSlot? existing)
            && existing is not null
        )
        {
            return existing;
        }

        GameplayActionExecutionPresentationSlot slot = new(executionId, action.Definition!.Id);
        _pending.Add(executionId, slot);
        return slot;
    }

    private void ApplyReplicatedExecution(in ReplicatedExecutionEntry entry)
    {
        if (
            _visible.TryGetValue(
                entry.ActionId,
                out GameplayActionExecutionPresentationSlot? existing
            )
            && existing.ExecutionId == entry.ExecutionId
        )
        {
            if (
                existing.Progress.ApplyNewerSample(
                    entry.HasProgress,
                    entry.Sample,
                    CurrentTimeSeconds(),
                    owner,
                    entry.ActionId
                )
            )
            {
                notifyChanged(entry.ActionId);
            }

            return;
        }

        GameplayActionExecutionPresentationSlot slot = new(entry.ExecutionId, entry.ActionId);
        slot.Progress.Confirm(
            entry.HasProgress,
            entry.Sample,
            CurrentTimeSeconds(),
            owner,
            entry.ActionId
        );
        _visible[entry.ActionId] = slot;
        notifyChanged(entry.ActionId);
    }

    private static bool TryReadReplicatedEntry(
        Godot.Collections.Dictionary entry,
        out ReplicatedExecutionEntry decoded
    )
    {
        decoded = default;
        if (
            !entry.TryGetValue("action_id", out Variant actionValue)
            || !entry.TryGetValue("execution_id", out Variant executionValue)
            || !entry.TryGetValue("progress_present", out Variant presentValue)
            || !entry.TryGetValue("progress_base", out Variant baseValue)
            || !entry.TryGetValue("progress_per_second", out Variant rateValue)
            || !entry.TryGetValue("revision", out Variant revisionValue)
            || actionValue.VariantType != Variant.Type.StringName
            || executionValue.VariantType != Variant.Type.Int
            || presentValue.VariantType != Variant.Type.Bool
            || baseValue.VariantType != Variant.Type.Float
            || rateValue.VariantType != Variant.Type.Float
            || revisionValue.VariantType != Variant.Type.Int
        )
        {
            return false;
        }

        long signedExecutionId = executionValue.AsInt64();
        if (signedExecutionId <= 0)
        {
            return false;
        }

        StringName actionId = actionValue.AsStringName();
        if (actionId.IsEmpty)
        {
            return false;
        }

        decoded = new ReplicatedExecutionEntry(
            actionId,
            (ulong)signedExecutionId,
            presentValue.AsBool(),
            new GameplayActionProgressSample(
                (float)baseValue.AsDouble(),
                (float)rateValue.AsDouble(),
                revisionValue.AsInt64()
            )
        );
        return true;
    }

    private static double CurrentTimeSeconds() => Time.GetTicksMsec() / 1000.0;

    private readonly record struct ReplicatedExecutionEntry(
        StringName ActionId,
        ulong ExecutionId,
        bool HasProgress,
        GameplayActionProgressSample Sample
    );
}
