namespace QuestWorld.GameSession;

internal sealed class GameSessionClientTravelState
{
    public long PendingCompletionId { get; private set; }

    public long LastCompletedId { get; private set; }

    public bool CanBegin(long travelId) => travelId > LastCompletedId;

    public void ObserveCompletion(long travelId)
    {
        if (travelId > LastCompletedId)
        {
            PendingCompletionId = travelId;
        }
    }

    public bool CanComplete(long travelId) =>
        travelId > LastCompletedId && PendingCompletionId == travelId;

    public bool MarkCompleted(long travelId)
    {
        if (!CanComplete(travelId))
        {
            return false;
        }

        LastCompletedId = travelId;
        PendingCompletionId = 0;
        return true;
    }

    public void Clear()
    {
        PendingCompletionId = 0;
        LastCompletedId = 0;
    }
}
