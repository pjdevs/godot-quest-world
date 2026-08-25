using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

public partial class Button : Node3D
{
    [Export]
    public InteractiveComponent? Interactive { get; set; } = null;

    [Export]
    public Node? TargetStateful { get; set; } = null;

    [Export]
    public InteractionState TargetState { get; set; } = InteractionState.Activating;

    public override void _EnterTree()
    {
        if (Interactive == null)
        {
            GD.PushError("Interactive component is not assigned.");
            return;
        }

        if (TargetStateful is not IStatefulProvider)
        {
            GD.PushError("Target stateful component is not assigned to an IStateful instance.");
            return;
        }

        Interactive.InteractionInputStarted += OnInteractionInputStarted;
    }

    public override void _ExitTree()
    {
        Interactive?.InteractionInputStarted -= OnInteractionInputStarted;
    }

    private void OnInteractionInputStarted(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        (TargetStateful as IStatefulProvider)?.Stateful?.SetState(TargetState);
    }
}
