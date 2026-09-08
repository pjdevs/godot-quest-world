using System.Collections.Generic;

namespace QuestWorld.GameSession;

internal sealed class GameSessionTravelState
{
    private readonly HashSet<long> _pendingPeers;

    public GameSessionTravelState(long id, IEnumerable<long> pendingPeers)
    {
        Id = id;
        _pendingPeers = [.. pendingPeers];
    }

    public long Id { get; }

    public bool IsComplete => _pendingPeers.Count == 0;

    public bool MarkPeerReady(long peerId) => _pendingPeers.Remove(peerId);

    public bool RemovePeer(long peerId) => _pendingPeers.Remove(peerId);
}
