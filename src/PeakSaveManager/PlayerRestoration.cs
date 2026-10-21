using System;
using System.Collections.Generic;

namespace PEAKSaveManager;

internal static class PlayerRestoration
{
    internal static bool HasUniqueSavedName(PlayerSnapshot snapshot, IEnumerable<PlayerSnapshot> snapshots)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.playerName))
        {
            return false;
        }

        int count = 0;
        foreach (PlayerSnapshot saved in snapshots)
        {
            if (saved != null && string.Equals(saved.playerName, snapshot.playerName, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count == 1;
    }

    // Actor numbers belong to a room, not a person. Old saves only contain a
    // nickname, so reject ambiguous names rather than restoring another scout.
    internal static T FindUniquePlayer<T>(PlayerSnapshot snapshot, IEnumerable<T> players,
        Func<T, string> getName, out string reason) where T : class
    {
        reason = null;
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.playerName))
        {
            reason = "snapshot has no player name";
            return null;
        }

        T match = null;
        foreach (T player in players)
        {
            if (player == null || !string.Equals(getName(player), snapshot.playerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match != null)
            {
                reason = "multiple connected players have this name; identity is ambiguous";
                return null;
            }

            match = player;
        }

        if (match == null)
        {
            reason = "no connected player has the saved name (absent or renamed)";
        }

        return match;
    }

    internal static void ApplyBeforeWarp(CharacterSnapshot snapshot,
        Action<string, object[]> broadcast, Action restoreLifeFlags,
        Action restoreInventory, Action restoreCharacter)
    {
        if (snapshot == null)
        {
            throw new InvalidOperationException("Cannot restore a player without a character snapshot.");
        }

        // Broadcast even when the host already sees a living scout: the owning
        // client can still be dead. Both RPCs exist in the unmodified game.
        // ReviveCharacter resets collision/timers and invokes reviveAction, but
        // neither drops inventory nor starts a warp nor records a new revival.
        if (snapshot.dead)
        {
            broadcast("RPCA_SetDead", new object[0]);
        }
        else
        {
            broadcast("ReviveCharacter", new object[] { false });
        }

        restoreLifeFlags();
        restoreInventory();
        restoreCharacter();
    }

    internal static void BuildStatusReplay(float[] caps, bool[] damageScaled, float[] savedAmounts,
        float damageMultiplier, out float[] clear, out float[] replay)
    {
        clear = new float[caps.Length];
        replay = new float[caps.Length];
        for (int i = 0; i < caps.Length; i++)
        {
            // A zero cap denotes a derived status excluded by the native RPC.
            if (caps[i] <= 0f)
            {
                continue;
            }

            float amount = Math.Max(0f, savedAmounts[i]);
            if (damageScaled[i] && amount > 0f)
            {
                if (damageMultiplier <= 0f)
                {
                    throw new InvalidOperationException("Cannot restore saved statuses with a zero ascent damage multiplier.");
                }

                amount /= damageMultiplier;
            }

            clear[i] = -(caps[i] + 1f);
            replay[i] = amount;
        }
    }

    internal static int GetSavedPetrify(IEnumerable<CharacterStatusSnapshot> statuses)
    {
        int amount = 0;
        if (statuses != null)
        {
            foreach (CharacterStatusSnapshot status in statuses)
            {
                if (status != null && string.Equals(status.statusType, "Petrify", StringComparison.OrdinalIgnoreCase))
                {
                    // Captured values are whole percentages stored as floats.
                    // Rounding avoids turning 0.53f into 52% after multiplication.
                    amount = Math.Max(0, Math.Min(100, (int)Math.Round(status.amount * 100f)));
                }
            }
        }

        return amount;
    }
}
