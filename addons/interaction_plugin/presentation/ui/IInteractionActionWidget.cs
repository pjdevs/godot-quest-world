namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Contract implemented by controls presenting a single action of the focused target.</summary>
/// <remarks>
/// One instance exists per presented action. The widget shows the allowed or blocked state of that
/// action alone and never summarizes the target.
/// </remarks>
public interface IInteractionActionWidget
{
    /// <summary>Refreshes the local widget from the latest action presentation.</summary>
    /// <param name="presentation">Immutable snapshot of the single action shown by this widget.</param>
    void Bind(in InteractionActionPresentation presentation);
}
