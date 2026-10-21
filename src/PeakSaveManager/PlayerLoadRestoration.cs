using System;
using System.Collections;

namespace PEAKSaveManager;

// Keep timing and ordering independent of Unity so failures can be exercised
// without pretending that a managed test simulates ragdoll or Photon physics.
internal interface IPlayerLoadTarget
{
    bool IsAvailable { get; }
    float Realtime { get; }
    bool IsWarping { get; }
    bool WarpCompleted { get; }
    bool IsAtDestination { get; }
    bool IsEquipmentReady { get; }
    void ClearHeldItem();
    void ApplyLook();
    void BeginWarp();
    void RestoreLifeState();
    void HaltMotion();
    void EquipSavedSlot();
    void Finish(bool succeeded);
}

internal static class PlayerLoadRestoration
{
    internal const float WarpTimeoutSeconds = 10f;
    internal const float EquipTimeoutSeconds = 5f;

    internal static IEnumerator Restore(IPlayerLoadTarget target)
    {
        bool succeeded = false;
        try
        {
            // In particular, do not interrupt a spawn/revive warp already running.
            float deadline = target.Realtime + WarpTimeoutSeconds;
            while (target.IsWarping)
            {
                Check(target, deadline, "Previous player teleport did not finish.");
                yield return null;
            }

            CheckAvailable(target);
            target.ClearHeldItem();
            target.ApplyLook();
            target.BeginWarp();
            deadline = target.Realtime + WarpTimeoutSeconds;
            while (!target.WarpCompleted || target.IsWarping || !target.IsAtDestination)
            {
                Check(target, deadline, "Player teleport did not complete at the saved position.");
                yield return null;
            }

            CheckAvailable(target);
            // The native warp always enables body collision at the end. Restore
            // the saved life state on the owner before equipping or confirming.
            target.RestoreLifeState();
            target.HaltMotion();
            target.EquipSavedSlot();
            deadline = target.Realtime + EquipTimeoutSeconds;
            while (!target.IsEquipmentReady)
            {
                Check(target, deadline, "Saved item did not finish equipping.");
                yield return null;
            }

            CheckAvailable(target);
            target.HaltMotion();
            succeeded = true;
        }
        finally
        {
            target.Finish(succeeded);
        }
    }

    private static void Check(IPlayerLoadTarget target, float deadline, string message)
    {
        CheckAvailable(target);
        if (target.Realtime >= deadline)
        {
            throw new TimeoutException(message);
        }
    }

    private static void CheckAvailable(IPlayerLoadTarget target)
    {
        if (!target.IsAvailable)
        {
            throw new InvalidOperationException("Player disappeared or changed ownership during load.");
        }
    }

    internal static byte? ResolveEquippedSlot(InventorySnapshot snapshot, int mainSlotCount, Func<byte, ushort?> getItemId)
    {
        // A remembered selection does not mean an item was held. This also clears
        // stale selections when switching from an old held-item save to empty hands.
        if (snapshot == null || snapshot.heldItemId == ushort.MaxValue)
        {
            return null;
        }

        if (Matches(snapshot.selectedSlotId, snapshot.heldItemId, mainSlotCount, getItemId))
        {
            return (byte)snapshot.selectedSlotId.Value;
        }
        if (Matches(snapshot.equippedMainSlotIndex, snapshot.heldItemId, mainSlotCount, getItemId))
        {
            return (byte)snapshot.equippedMainSlotIndex.Value;
        }
        if (snapshot.equippedTempSlot && Matches(250, snapshot.heldItemId, mainSlotCount, getItemId))
        {
            return 250;
        }

        for (int i = 0; i < mainSlotCount && i < 250; i++)
        {
            if (Matches(i, snapshot.heldItemId, mainSlotCount, getItemId))
            {
                return (byte)i;
            }
        }
        if (Matches(250, snapshot.heldItemId, mainSlotCount, getItemId)) return 250;
        if (Matches(3, snapshot.heldItemId, mainSlotCount, getItemId)) return 3;
        // Backpack contents are not native equippable slots; do not invent a
        // slot or instantiate the database template when a saved item is absent.
        return null;
    }

    private static bool Matches(int? slot, ushort heldItemId, int mainSlotCount, Func<byte, ushort?> getItemId)
    {
        return slot.HasValue && slot.Value >= 0 && slot.Value <= byte.MaxValue
            && (slot.Value < mainSlotCount || slot.Value == 3 || slot.Value == 250)
            && getItemId((byte)slot.Value) == heldItemId;
    }
}
