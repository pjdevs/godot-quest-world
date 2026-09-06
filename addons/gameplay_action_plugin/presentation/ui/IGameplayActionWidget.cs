using QuestWorld.GameplayActions;

namespace QuestWorld.GameplayActions.Presentation.UI;

/// <summary>Contract implemented by controls presenting one gameplay action.</summary>
public interface IGameplayActionWidget
{
    /// <summary>Refreshes the widget from the action and its optional execution snapshot.</summary>
    /// <param name="action">Immutable snapshot of the single action shown by this widget.</param>
    /// <param name="execution">Matching execution, or null when none is visible.</param>
    void Bind(in GameplayActionPresentation action, GameplayActionExecutionPresentation? execution);
}
