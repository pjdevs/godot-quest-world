using Godot;
using QuestWorld.State;

public partial class Door : Node3D
{
	[Export]
	public StatefulComponent? StatefulComponent { get; set; }

	[Export]
	public AnimationPlayer? AnimationPlayer { get; set; }

	[Export]
	public CollisionShape3D? Collision { get; set; }

	[Export]
	public AudioStreamPlayer3D? AudioPlayer { get; set; }


	public override void _EnterTree()
	{
		StatefulComponent?.StateChanged += OnStateChanged;
		StatefulComponent?.StateChangedPresentation += OnStateChangedPresentation;
	}

	public override void _ExitTree()
	{
		StatefulComponent?.StateChanged -= OnStateChanged;
		StatefulComponent?.StateChangedPresentation -= OnStateChangedPresentation;
	}

	private void OnStateChanged(StringName oldState, StringName newState, bool isSynchronization)
	{
		switch (newState)
		{
			case "opened":
				Collision?.Disabled = true;
				break;
			case "closed":
				Collision?.Disabled = false;
				break;
		}
	}

	private void OnStateChangedPresentation(StringName oldState, StringName newState, bool isSynchronization)
	{
		switch ((oldState, newState))
		{
			case var (state, _) when state == "locked":
				AudioPlayer?.Play(0.1f);
				break;
			case var (_, state) when state == "opened":
				AnimationPlayer?.Play("open");
				break;
			case var (_, state) when state == "closed":
				AnimationPlayer?.Play(oldState == "opened" ? "close" : "RESET");
				break;
		}

	}
}
