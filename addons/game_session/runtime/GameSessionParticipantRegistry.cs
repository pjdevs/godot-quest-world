using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GameSessionPlugin;

internal sealed class GameSessionParticipantRegistry
{
    private readonly List<PlayerState> _players = [];
    private readonly Dictionary<long, PlayerState> _byParticipantId = [];
    private readonly Dictionary<long, PlayerState> _byPeerId = [];

    public IReadOnlyList<PlayerState> Players => _players;

    public bool TryAdd(PlayerState playerState)
    {
        if (
            playerState.ParticipantId <= 0
            || playerState.PeerId <= 0
            || _byParticipantId.ContainsKey(playerState.ParticipantId)
            || _byPeerId.ContainsKey(playerState.PeerId)
        )
        {
            return false;
        }

        _players.Add(playerState);
        _byParticipantId.Add(playerState.ParticipantId, playerState);
        _byPeerId.Add(playerState.PeerId, playerState);
        return true;
    }

    public bool Remove(PlayerState playerState)
    {
        if (
            !_byParticipantId.TryGetValue(
                playerState.ParticipantId,
                out PlayerState? participantMatch
            )
            || !ReferenceEquals(participantMatch, playerState)
            || !_byPeerId.TryGetValue(playerState.PeerId, out PlayerState? peerMatch)
            || !ReferenceEquals(peerMatch, playerState)
        )
        {
            return false;
        }

        _byParticipantId.Remove(playerState.ParticipantId);
        _byPeerId.Remove(playerState.PeerId);
        _players.Remove(playerState);
        return true;
    }

    public bool TryGetByPeerId(long peerId, [NotNullWhen(true)] out PlayerState? playerState) =>
        _byPeerId.TryGetValue(peerId, out playerState);

    public bool TryGetByParticipantId(
        long participantId,
        [NotNullWhen(true)] out PlayerState? playerState
    ) => _byParticipantId.TryGetValue(participantId, out playerState);

    public void Clear()
    {
        _players.Clear();
        _byParticipantId.Clear();
        _byPeerId.Clear();
    }
}
