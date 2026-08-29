using System;
using Godot;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>Owns every local presentation strategy for one generic execution progress value.</summary>
internal sealed class InteractionExecutionProgressState
{
    private bool _hasPublished;
    private float _published;
    private bool _hasTransportSample;
    private float _linearBase;
    private float _linearPerSecond;
    private double _sampleReceivedAt;
    private bool _hasWarnedLinearOverride;
    private Callable? _source;

    public long Revision { get; private set; }

    public bool OwnsLinearProgress => _hasTransportSample && _linearPerSecond > 0.0f;

    public bool MatchesPublished(float? progress)
    {
        float normalized = progress.HasValue ? Mathf.Clamp(progress.Value, 0.0f, 1.0f) : 0.0f;
        return _hasPublished == progress.HasValue
            && (!progress.HasValue || Mathf.IsEqualApprox(_published, normalized));
    }

    public bool RejectPublishedOverride(Node owner, ulong executionId)
    {
        if (!OwnsLinearProgress)
        {
            return false;
        }

        if (!_hasWarnedLinearOverride)
        {
            GD.PushWarning(
                $"{owner.GetPath()}: execution '{executionId}' already owns a linear progress source."
            );
            _hasWarnedLinearOverride = true;
        }
        return true;
    }

    public void Publish(float? progress)
    {
        _hasTransportSample = false;
        _hasPublished = progress.HasValue;
        _published = progress.HasValue ? Mathf.Clamp(progress.Value, 0.0f, 1.0f) : 0.0f;
        Revision++;
    }

    public void ReportLinear(float progressBase, float progressPerSecond, double receivedAt)
    {
        _hasTransportSample = true;
        _hasPublished = false;
        _linearBase = Mathf.Clamp(progressBase, 0.0f, 1.0f);
        _linearPerSecond = Mathf.Max(progressPerSecond, 0.0f);
        _sampleReceivedAt = receivedAt;
        Revision++;
    }

    public void Predict(InteractionProgressSample sample, double receivedAt)
    {
        ApplyUnchecked(hasProgress: true, sample, receivedAt);
    }

    public void Confirm(
        bool hasSample,
        InteractionProgressSample sample,
        double receivedAt,
        Node owner,
        StringName actionId
    )
    {
        bool hadPrediction = _hasTransportSample;
        float visible = hadPrediction ? Resolve(owner, actionId, receivedAt) ?? 0.0f : 0.0f;
        _source = null;
        if (hasSample && sample.ProgressPerSecond > 0.0f && hadPrediction)
        {
            sample = sample.PreserveVisibleProgress(visible);
        }
        ApplyUnchecked(hasSample, sample, receivedAt);
    }

    public bool ApplyNewerSample(
        bool hasProgress,
        InteractionProgressSample sample,
        double receivedAt,
        Node owner,
        StringName actionId
    )
    {
        if (sample.Revision <= Revision)
        {
            return false;
        }

        if (hasProgress && sample.ProgressPerSecond > 0.0f)
        {
            sample = sample.PreserveVisibleProgress(Resolve(owner, actionId, receivedAt) ?? 0.0f);
        }
        ApplyUnchecked(hasProgress, sample, receivedAt);
        return true;
    }

    public void SetSource(Callable source)
    {
        _source = IsCallableUsable(source) ? source : null;
    }

    public void ClearSource()
    {
        _source = null;
    }

    public float? Resolve(Node owner, StringName actionId, double currentTime)
    {
        if (_source is Callable source)
        {
            if (!IsCallableUsable(source))
            {
                _source = null;
            }
            else
            {
                bool clearSource = false;
                try
                {
                    Variant value = source.Call();
                    if (value.VariantType is Variant.Type.Float or Variant.Type.Int)
                    {
                        float numeric = (float)value.AsDouble();
                        if (float.IsFinite(numeric))
                        {
                            return Mathf.Clamp(numeric, 0.0f, 1.0f);
                        }

                        GD.PushWarning(
                            $"{owner.GetPath()}: execution progress source for '{actionId}' returned a non-finite value."
                        );
                        clearSource = true;
                    }
                    else if (value.VariantType != Variant.Type.Nil)
                    {
                        GD.PushWarning(
                            $"{owner.GetPath()}: execution progress source for '{actionId}' returned a non-numeric value."
                        );
                        clearSource = true;
                    }
                }
                catch (Exception exception)
                {
                    GD.PushWarning(
                        $"{owner.GetPath()}: execution progress source for '{actionId}' failed: {exception.Message}"
                    );
                    clearSource = true;
                }

                if (clearSource)
                {
                    _source = null;
                }
            }
        }

        if (_hasTransportSample)
        {
            float elapsed = Mathf.Max((float)(currentTime - _sampleReceivedAt), 0.0f);
            return Mathf.Clamp(_linearBase + _linearPerSecond * elapsed, 0.0f, 1.0f);
        }

        return _hasPublished ? Mathf.Clamp(_published, 0.0f, 1.0f) : null;
    }

    public bool TryGetSample(out bool hasProgress, out InteractionProgressSample sample)
    {
        hasProgress = _hasTransportSample || _hasPublished;
        sample = _hasTransportSample
            ? new InteractionProgressSample(_linearBase, _linearPerSecond, Revision)
            : new InteractionProgressSample(_published, 0.0f, Revision);
        return true;
    }

    private void ApplyUnchecked(
        bool hasProgress,
        InteractionProgressSample sample,
        double receivedAt
    )
    {
        Revision = sample.Revision;
        _sampleReceivedAt = receivedAt;
        _hasTransportSample = hasProgress;
        _hasPublished = hasProgress && sample.ProgressPerSecond <= 0.0f;
        _published = Mathf.Clamp(sample.ProgressBase, 0.0f, 1.0f);
        _linearBase = _published;
        _linearPerSecond = Mathf.Max(sample.ProgressPerSecond, 0.0f);
    }

    private static bool IsCallableUsable(in Callable source)
    {
        if (source.Delegate is not null)
        {
            return true;
        }

        return source.Target is GodotObject target
            && GodotObject.IsInstanceValid(target)
            && !source.Method.IsEmpty;
    }
}
