using Godot;

namespace QuestWorld.GameSession;

[GlobalClass]
public partial class PlayerState : Node
{
    public long ParticipantId { get; private set; }

    public long PeerId { get; private set; }

    public bool IdentityWasInitializedBeforeReady { get; private set; }

    public void InitializeIdentity(long participantId, long peerId)
    {
        if (participantId <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(participantId));
        }

        if (peerId <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(peerId));
        }

        if (ParticipantId != 0 || PeerId != 0)
        {
            throw new System.InvalidOperationException(
                "PlayerState identity can only be initialized once."
            );
        }

        ParticipantId = participantId;
        PeerId = peerId;
    }

    public override void _Ready()
    {
        IdentityWasInitializedBeforeReady = ParticipantId > 0 && PeerId > 0;
    }
}
