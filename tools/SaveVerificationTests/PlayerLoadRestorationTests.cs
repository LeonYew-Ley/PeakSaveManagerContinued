using System.Collections;
using PEAKSaveManager;

internal static class PlayerLoadRestorationTests
{
    private static int cases;

    internal static void Run()
    {
        SelectionCases();
        WorkflowCases();
        LandingCases();
        Console.WriteLine($"{cases} player-load selection/ordering/failure regression cases passed. Unity/Photon physics was not simulated.");
    }

    private static void SelectionCases()
    {
        Dictionary<byte, ushort> slots = new() { [0] = 10, [1] = 20, [2] = 10, [3] = 40, [250] = 50 };
        byte? Resolve(InventorySnapshot snapshot) => PlayerLoadRestoration.ResolveEquippedSlot(snapshot, 3,
            slot => slots.TryGetValue(slot, out ushort id) ? id : null);
        void Case(string name, InventorySnapshot snapshot, byte? expected)
        {
            Assert(Resolve(snapshot) == expected, name);
            cases++;
        }

        Case("null inventory is empty", null, null);
        Case("empty hands override stale valid selection", new() { selectedSlotId = 1 }, null);
        Case("exact selection wins among duplicate item IDs", new() { heldItemId = 10, selectedSlotId = 2 }, 2);
        Case("saved main-slot fallback", new() { heldItemId = 10, selectedSlotId = 99, equippedMainSlotIndex = 2 }, 2);
        Case("mismatched selected item is not equipped", new() { heldItemId = 20, selectedSlotId = 0 }, 1);
        Case("negative slot falls back safely", new() { heldItemId = 20, selectedSlotId = -1 }, 1);
        Case("overflow slot falls back safely", new() { heldItemId = 20, selectedSlotId = 256 }, 1);
        Case("temp selection", new() { heldItemId = 50, selectedSlotId = 250 }, 250);
        Case("legacy temp flag", new() { heldItemId = 50, equippedTempSlot = true }, 250);
        Case("legacy ID-only temp selection", new() { heldItemId = 50 }, 250);
        Case("held backpack uses native slot 3", new() { heldItemId = 40, selectedSlotId = 3 }, 3);
        Case("missing item stays empty", new() { heldItemId = 70, selectedSlotId = 0 }, null);
        Case("backpack contents cannot invent a native slot", new() { heldItemId = 70, equippedBackpackSlotIndex = 0 }, null);
        slots.Remove(1);
        Case("empty restored slot stays empty", new() { heldItemId = 20, selectedSlotId = 1 }, null);
        Case("empty hands after held save", new(), null);
        Case("second held save resolves independently", new() { heldItemId = 50 }, 250);
    }

    private static void WorkflowCases()
    {
        // Run the production iterator against controllable signals. Completion,
        // position and attachment are independent: none can substitute for another.
        FakeTarget normal = new() { IsWarping = true };
        IEnumerator restore = PlayerLoadRestoration.Restore(normal);
        Step(restore, normal);
        Assert(normal.Actions.Count == 0, "Do not interrupt an existing spawn/revive warp");
        normal.IsWarping = false;
        Step(restore, normal);
        Assert(normal.Actions.SequenceEqual(new[] { "clear", "look", "warp" }), "Clear and orient before warp");
        normal.WarpCompleted = true;
        normal.IsAtDestination = true;
        Step(restore, normal);
        Assert(!normal.Actions.Contains("equip"), "Completion cannot bypass an active warp");
        normal.IsWarping = false;
        normal.IsAtDestination = false;
        Step(restore, normal);
        Assert(!normal.Actions.Contains("equip"), "Completion must be at the destination");
        normal.IsAtDestination = true;
        Step(restore, normal);
        Assert(normal.Actions.Last() == "equip", "Equip only after warp completion");
        normal.IsEquipmentReady = true;
        Assert(!restore.MoveNext(), "Restore finishes after attachment");
        Assert(normal.Actions.SequenceEqual(new[] { "clear", "look", "warp", "life", "halt", "equip", "halt", "success" }),
            "Exactly one native equip and two full-body halts");
        cases++;

        FakeTarget noEvent = new();
        restore = PlayerLoadRestoration.Restore(noEvent);
        Step(restore, noEvent);
        noEvent.IsWarping = false;
        noEvent.IsAtDestination = true;
        ExpectTimeout(restore, noEvent, "A false warping flag is not a completion acknowledgment");

        FakeTarget warpException = new();
        restore = PlayerLoadRestoration.Restore(warpException);
        Step(restore, warpException);
        ExpectTimeout(restore, warpException, "A broken native coroutine must fail and clean up");

        FakeTarget offTarget = new();
        restore = PlayerLoadRestoration.Restore(offTarget);
        Step(restore, offTarget);
        offTarget.IsWarping = false;
        offTarget.WarpCompleted = true;
        ExpectTimeout(restore, offTarget, "A completed but displaced warp is a failure");

        FakeTarget noAttachment = new();
        restore = PlayerLoadRestoration.Restore(noAttachment);
        Step(restore, noAttachment);
        noAttachment.IsWarping = false;
        noAttachment.IsAtDestination = noAttachment.WarpCompleted = true;
        Step(restore, noAttachment);
        ExpectTimeout(restore, noAttachment, "A spawned item without its native attachment is a failure");

        FakeTarget priorWarp = new() { IsWarping = true };
        restore = PlayerLoadRestoration.Restore(priorWarp);
        Step(restore, priorWarp);
        ExpectTimeout(restore, priorWarp, "A prior warp cannot wait forever");
        Assert(!priorWarp.Actions.Contains("clear"), "Do not touch inventory/held items during a prior warp");

        foreach (string phase in new[] { "before", "warp", "equip" })
        {
            FakeTarget lost = new();
            restore = PlayerLoadRestoration.Restore(lost);
            if (phase != "before") Step(restore, lost);
            if (phase == "equip")
            {
                lost.IsWarping = false;
                lost.IsAtDestination = lost.WarpCompleted = true;
                Step(restore, lost);
            }
            lost.IsAvailable = false; // Destruction, disconnect or ownership change.
            Expect<InvalidOperationException>(() => restore.MoveNext(), "Unavailable player at " + phase);
            Assert(lost.Actions.Last() == "failure", "Unavailable player cleanup at " + phase);
            cases++;
        }

        FakeTarget cancelled = new();
        restore = PlayerLoadRestoration.Restore(cancelled);
        Step(restore, cancelled);
        ((IDisposable)restore).Dispose();
        Assert(cancelled.Actions.Last() == "failure" && !cancelled.Actions.Contains("equip"), "Cancellation must run cleanup");
        cases++;

        FakeTarget failingEquip = new() { ThrowOnEquip = true };
        restore = PlayerLoadRestoration.Restore(failingEquip);
        Step(restore, failingEquip);
        failingEquip.IsWarping = false;
        failingEquip.IsAtDestination = failingEquip.WarpCompleted = true;
        Expect<InvalidOperationException>(() => restore.MoveNext(), "Equipment failure must reach the caller");
        Assert(failingEquip.Actions.Last() == "failure", "Equipment exception cleanup");
        cases++;
    }

    private static void Step(IEnumerator routine, FakeTarget target)
    {
        Assert(routine.MoveNext(), "Routine ended before expected signal");
        Assert(routine.Current == null, "Managed workflow should yield a frame");
        target.Realtime += 0.25f;
    }

    private static void ExpectTimeout(IEnumerator routine, FakeTarget target, string message)
    {
        target.Realtime += 31f;
        Expect<TimeoutException>(() => routine.MoveNext(), message);
        Assert(target.Actions.Last() == "failure" && !target.Actions.Contains("success"), "Timeout cannot report success");
        cases++;
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void LandingCases()
    {
        var target = new FakeTarget { RequiresStableLanding = true, IsEquipmentReady = true };
        var routine = PlayerLoadRestoration.Restore(target);
        Step(routine, target);
        target.IsWarping = false;
        target.IsAtDestination = target.WarpCompleted = true;
        Step(routine, target);
        Assert(!target.Actions.Contains("success"), "Teleport completion alone must not confirm grounded save");
        int halts = target.Actions.Count(a => a == "halt");
        target.IsLandingStable = true;
        Step(routine, target);
        target.IsLandingStable = false;
        Step(routine, target);
        target.IsLandingStable = true;
        Step(routine, target);
        Step(routine, target);
        Assert(!routine.MoveNext() && target.Actions.Last() == "success", "Require continuously stable interval after slip");
        Assert(target.Actions.Count(a => a == "halt") == halts, "Landing wait must not freeze physics");
        cases++;

        target = new FakeTarget { RequiresStableLanding = true, IsEquipmentReady = true };
        routine = PlayerLoadRestoration.Restore(target);
        Step(routine, target);
        target.IsWarping = false;
        target.IsAtDestination = target.WarpCompleted = true;
        Step(routine, target);
        ExpectTimeout(routine, target, "Unsupported landing cannot report success");

        target = new FakeTarget { RequiresStableLanding = true, IsEquipmentReady = true };
        routine = PlayerLoadRestoration.Restore(target);
        Step(routine, target);
        target.IsWarping = false;
        target.IsAtDestination = target.WarpCompleted = true;
        Step(routine, target);
        ((IDisposable)routine).Dispose();
        Assert(target.Actions.Last() == "failure", "Cancelled landing cleans up");
        cases++;
    }

    private sealed class FakeTarget : IPlayerLoadTarget, IPlayerLandingTarget
    {
        internal readonly List<string> Actions = new();
        internal bool ThrowOnEquip;
        public bool RequiresStableLanding { get; set; }
        public bool IsLandingStable { get; set; }
        public bool IsAvailable { get; set; } = true;
        public float Realtime { get; set; }
        public bool IsWarping { get; set; }
        public bool WarpCompleted { get; set; }
        public bool IsAtDestination { get; set; }
        public bool IsEquipmentReady { get; set; }
        public void ClearHeldItem() => Actions.Add("clear");
        public void ApplyLook() => Actions.Add("look");
        public void BeginWarp() { Actions.Add("warp"); IsWarping = true; }
        public void RestoreLifeState() => Actions.Add("life");
        public void HaltMotion() => Actions.Add("halt");
        public void EquipSavedSlot()
        {
            Actions.Add("equip");
            if (ThrowOnEquip) throw new InvalidOperationException("Native equip failure");
        }
        public void Finish(bool succeeded) => Actions.Add(succeeded ? "success" : "failure");
    }
}
