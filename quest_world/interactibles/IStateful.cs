using QuestWorld.Interaction.Runtime.State;

public interface IStatefulProvider
{
    InteractionStateful? Stateful { get; }
}
