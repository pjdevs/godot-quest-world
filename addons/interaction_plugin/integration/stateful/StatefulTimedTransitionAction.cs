using Godot;
using QuestWorld.Interaction.Runtime.Actions;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Timed Stateful running transition for actions whose duration is their completion policy.</summary>
[GlobalClass]
public partial class StatefulTimedTransitionAction : StatefulRunningTransitionAction
{
    /// <summary>Gets or sets the positive duration used by the timed transition.</summary>
    [Export]
    public float Duration { get; set; }

    /// <summary>Gets or sets the interval between authoritative timing corrections.</summary>
    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    /// <inheritdoc />
    protected override InteractionActionExecutor CreateComposedExecutor() =>
        new TimedTransitionStateInteractionExecutor();

    /// <inheritdoc />
    protected override void ConfigureComposedExecutor(InteractionActionExecutor executor)
    {
        base.ConfigureComposedExecutor(executor);
        TimedTransitionStateInteractionExecutor timed =
            (TimedTransitionStateInteractionExecutor)executor;
        timed.Duration = Duration;
        timed.CorrectionInterval = CorrectionInterval;
    }
}
