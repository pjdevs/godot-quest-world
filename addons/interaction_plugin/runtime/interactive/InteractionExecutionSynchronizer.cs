using Godot;
using QuestWorld.GameplayActions.Runtime.Execution;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>
/// Interaction-facing adapter over the generic gameplay-action execution synchronizer.
/// </summary>
/// <remarks>
/// Interaction keeps the spatial target reference for authoring, while execution presentation and
/// replication are owned entirely by the target's <see cref="InteractiveComponent.ActionComponent"/>.
/// This adapter exists during the Interaction migration so existing consumers do not need to know
/// about the generic component directly.
/// </remarks>
[GlobalClass]
public partial class InteractionExecutionSynchronizer : GameplayActionExecutionSynchronizer
{
    /// <summary>Gets or sets the interaction target whose generic execution state is replicated.</summary>
    [Export]
    public InteractiveComponent? Interactive { get; set; }

    public override void _Ready()
    {
        SyncComponent();
        if (Component is null)
        {
            GD.PushError($"{GetPath()}: InteractionExecutionSynchronizer requires an Interactive with a GameplayActionComponent.");
            return;
        }

        base._Ready();
    }

    internal new Godot.Collections.Dictionary CaptureSnapshot()
    {
        SyncComponent();
        return base.CaptureSnapshot();
    }

    internal new bool ApplySnapshot(Godot.Collections.Dictionary snapshot)
    {
        SyncComponent();
        return base.ApplySnapshot(snapshot);
    }

    private void SyncComponent()
    {
        Component = Interactive?.ActionComponent;
    }
}
