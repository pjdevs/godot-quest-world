using System.Collections.Generic;

namespace QuestWorld.GameSession;

internal sealed class GameSessionTravelState
{
    private readonly HashSet<long> _pendingPeers;

    public GameSessionTravelState(long id, string resourcePath, IEnumerable<long> pendingPeers)
    {
        Id = id;
        ResourcePath = resourcePath;
        _pendingPeers = [.. pendingPeers];
    }

    public long Id { get; }

    public string ResourcePath { get; }

    public bool IsLocalWorldReady { get; private set; }

    public bool IsComplete => IsLocalWorldReady && _pendingPeers.Count == 0;

    public void MarkLocalWorldReady() => IsLocalWorldReady = true;

    public bool MarkPeerReady(long peerId) => _pendingPeers.Remove(peerId);

    public bool RemovePeer(long peerId) => _pendingPeers.Remove(peerId);

    public bool ExpectsPeer(long peerId) => _pendingPeers.Contains(peerId);
}
