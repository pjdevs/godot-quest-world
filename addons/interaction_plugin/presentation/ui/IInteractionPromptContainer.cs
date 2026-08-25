using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Contract implemented by the target-level frame that stacks the action prompts.</summary>
/// <remarks>
/// The container is instantiated once for the focused target, carries the target-level data, and
/// exposes where <c>InteractionPresenter</c> adds one action widget per presented action.
/// </remarks>
public interface IInteractionPromptContainer : IInteractionWidget
{
    /// <summary>Gets the control receiving one action widget per presented action.</summary>
    Control ActionsContainer { get; }
}
