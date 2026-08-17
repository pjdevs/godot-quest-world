namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Contract implemented by prompt and indication controls that consume presentation snapshots.</summary>
public interface IInteractionWidget
{
    /// <summary>Refreshes the local widget from the latest interaction presentation.</summary>
    /// <param name="presentation">Immutable snapshot produced for the local interactor.</param>
    void Bind(in InteractionPresentation presentation);
}
