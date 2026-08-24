using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.State;
using System;

public partial class LeverWall : Node3D, IStatefulProvider
{
	[Export]
	public InteractionStateful? Stateful { get; set; } = null;

	[Export]
	public AnimationPlayer? AnimationPlayer { get; set; } = null;

	[Export]
	public string LeverWallUpAnimationName { get; set; } = "lever_wall_up";

	public override void _Ready()
	{
		if (Stateful == null)
		{
			GD.PushError("Stateful component is not assigned.");
			return;
		}

		Stateful.InteractionStateChangedPresentation += OnInteractionStateChanged;
	}

	private void OnInteractionStateChanged(int oldState, int newState)
	{
		if (AnimationPlayer == null)
		{
			return;
		}

		AnimationPlayer.AssignedAnimation = LeverWallUpAnimationName;

		switch (newState)
		{
			case (int)InteractionState.Idle:
				AnimationPlayer.Seek(0.0f, update: true);
				break;

			case (int)InteractionState.Activating:
				AnimationPlayer.Play("lever_wall_up");
				break;

			case (int)InteractionState.Activated:
				Animation animation = AnimationPlayer.GetAnimation(LeverWallUpAnimationName);
				AnimationPlayer.Seek(animation.Length, update: true);
				break;

			case (int)InteractionState.Deactivating:
				AnimationPlayer.PlayBackwards("lever_wall_up");
				break;

			default:
				break;
		}
	}
}
