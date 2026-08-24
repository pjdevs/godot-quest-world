using System;
using Godot;

public static class NetworkPlayerIdentity
{
    public const string PlayerNamePrefix = "Player_";

    public static string GetPlayerName(int peerId)
    {
        return $"{PlayerNamePrefix}{peerId}";
    }

    public static bool TryGetPeerId(string playerName, out int peerId)
    {
        peerId = 0;
        if (
            !playerName.StartsWith(PlayerNamePrefix, StringComparison.Ordinal)
            || playerName.Length == PlayerNamePrefix.Length
        )
        {
            return false;
        }

        return int.TryParse(playerName[PlayerNamePrefix.Length..], out peerId) && peerId > 0;
    }

    public static Vector3 GetSpawnPosition(int peerId)
    {
        int slot = Math.Max(peerId - 1, 0) % 16;
        return new Vector3(10.0f + (slot % 4) * 2.5f, 0.0f, 10.0f + (slot / 4) * 2.5f);
    }
}
