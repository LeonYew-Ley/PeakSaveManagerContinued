using Newtonsoft.Json;
using PEAKSaveManager;

internal static class LuggageRestorationTests
{
    internal static void Run()
    {
        int transitions = 0;
        foreach (bool wasOpen in new[] { false, true })
        foreach (bool savedOpen in new[] { false, true })
        foreach (bool delayedOpening in new[] { false, true })
        {
            FakeBox host = new(wasOpen, delayedOpening);
            FakeBox peer = new(!wasOpen, delayedOpening);
            LuggageRestoreState state = new() { open = savedOpen, activated = savedOpen, spent = savedOpen, revivedPlayers = savedOpen };
            for (int pass = 0; pass < 3; pass++)
            {
                LuggageRestoration.Apply(host, state);
                LuggageRestoration.Apply(peer, state);
                host.Tick(); peer.Tick();
                foreach (FakeBox box in new[] { host, peer })
                {
                    Assert(box.Open == savedOpen && box.VisibleOpen == savedOpen, "Logic and appearance must agree on both peers.");
                    Assert(box.Available == !savedOpen && box.Entries == (savedOpen ? 0 : 1), "Closed boxes are usable exactly once in the native list.");
                    Assert(box.Activated == savedOpen && box.Revived == savedOpen, "Associated objects and revival state follow the save.");
                    Assert(box.SpawnCount == 0, "Restoring an open box must never generate loot.");
                    if (!savedOpen) Assert(!box.HasSpawnHistory, "A closed snapshot must clear stale native spawn history.");
                }
            }
            if (!savedOpen)
            {
                host.OpenNormally(); peer.OpenNormally();
                Assert(host.SpawnCount == 1 && peer.SpawnCount == 1, "A reopened restored box can produce its normal loot once.");
            }
            transitions++;
        }

        LuggageRestoreState saved = Identity(10);
        List<LuggageRestoreState> boxes = new() { Identity(30), Identity(0), Identity(10) };
        Assert(LuggageRestoration.FindMatch(saved, boxes) == 2, "Same-path siblings must match by position, not enumeration order.");
        boxes.Reverse();
        Assert(LuggageRestoration.FindMatch(saved, boxes) == 0, "Changing native object order must not change the selected physical box.");
        boxes = new() { Identity(10), Identity(10) };
        Assert(LuggageRestoration.FindMatch(saved, boxes) == -1, "Overlapping same-type boxes are ambiguous.");
        boxes = new() { Identity(1000) };
        Assert(LuggageRestoration.FindMatch(saved, boxes) == -1, "A shared path must not bypass the distance bound.");
        boxes = new() { Identity(10) };
        boxes[0].type = "RespawnChest";
        Assert(LuggageRestoration.FindMatch(saved, boxes) == -1, "Different chest types must not match.");
        boxes[0] = Identity(10); boxes[0].name = "LuggageSmall";
        Assert(LuggageRestoration.FindMatch(saved, boxes) == -1, "Different prefab names must not match.");
        boxes[0] = Identity(10); boxes[0].position.x = float.NaN;
        Assert(LuggageRestoration.FindMatch(saved, boxes) == -1, "Invalid coordinates cannot choose a box.");
        saved.type = null; boxes[0] = Identity(10);
        Assert(LuggageRestoration.FindMatch(saved, boxes) == 0, "Legacy snapshots without a type still match by name and position.");

        // A historical map can lose its first saved box while later boxes still
        // exist. That is a partial world restore, not a failed player load.
        boxes = new() { Identity(10), Identity(60) };
        int[] matches = LuggageRestoration.FindMatches(new[] { Identity(1000), Identity(10), Identity(60) }, boxes, true);
        Assert(matches.SequenceEqual(new[] { -1, 0, 1 }), "A missing first box must not discard later valid matches.");
        matches = LuggageRestoration.FindMatches(new[] { Identity(1000), Identity(2000) }, boxes, true);
        Assert(matches.All(index => index == -1), "A changed map with no matching boxes must finish with skips, allowing player restoration.");
        matches = LuggageRestoration.FindMatches(new[] { Identity(10), Identity(10) }, new[] { Identity(10) }, true);
        Assert(matches.SequenceEqual(new[] { 0, -1 }), "A live box cannot consume two saved states.");
        matches = LuggageRestoration.FindMatches(new[] { Identity(10), Identity(60) }, new[] { Identity(10), Identity(10), Identity(60) }, true);
        Assert(matches.SequenceEqual(new[] { -1, 2 }), "An ambiguous box must stay untouched without blocking other boxes.");
        boxes[0].viewId = 0;
        matches = LuggageRestoration.FindMatches(new[] { Identity(10), Identity(60) }, boxes, true);
        Assert(matches.SequenceEqual(new[] { -1, 1 }), "A missing network view is skipped without blocking later network objects.");
        Assert(LuggageRestoration.FindMatches(new[] { Identity(10) }, boxes, false)[0] == 0, "Offline matching does not require a Photon identity.");
        Assert(LuggageRestoration.FindMatches(new[] { Identity(10) }, Array.Empty<LuggageRestoreState>(), true)[0] == -1, "An empty live segment is a partial world restore.");

        LuggageRevisions revisions = new();
        LuggageRestoreState first = Identity(10), second = Identity(30);
        second.viewId = 101;
        revisions.Receive(1, "Level_2", 1, new[] { first, second });
        revisions.Receive(1, "Level_2", 2, new[] { first });
        Assert(!revisions.IsCurrent(1, "Level_2", 1, first.viewId), "A delayed join snapshot cannot overwrite a newer load.");
        Assert(revisions.IsCurrent(1, "Level_2", 1, second.viewId), "An unrelated box's pending state remains valid.");
        revisions.Receive(1, "Level_2", 1, new[] { first });
        Assert(revisions.IsCurrent(1, "Level_2", 2, first.viewId), "Out-of-order delivery must not lower a box's revision.");
        revisions.Receive(2, "Level_2", 1, new[] { first });
        Assert(revisions.IsCurrent(2, "Level_2", 1, first.viewId) && !revisions.IsCurrent(1, "Level_2", 2, first.viewId), "Host migration invalidates old-host snapshots.");
        revisions.Receive(2, "Level_3", 1, new[] { first });
        Assert(!revisions.IsCurrent(2, "Level_2", 1, first.viewId), "A scene change invalidates pending batches from the old map.");

        ContainerSnapshot old = JsonConvert.DeserializeObject<ContainerSnapshot>("{\"state\":1}");
        Assert(old.activatedObjectActive == null, "Old saves keep an explicit missing-value fallback.");
        ContainerSnapshot roundTrip = JsonConvert.DeserializeObject<ContainerSnapshot>(JsonConvert.SerializeObject(new ContainerSnapshot { activatedObjectActive = false }));
        Assert(roundTrip.activatedObjectActive == false, "A deliberately inactive attached object must survive serialization.");
        LuggageSnapshot legacy = JsonConvert.DeserializeObject<LuggageSnapshot>(JsonConvert.SerializeObject(new LuggageSnapshot { activatedObjectActive = true }));
        Assert(legacy.activatedObjectActive == true, "Legacy and modern capture fields both retain attached-object state.");
        Console.WriteLine($"Luggage verification passed: {transitions} host/client transitions with three repeated passes, delayed opening, reopening, missing/ambiguous/networkless partial batches, message ordering and save compatibility.");
    }

    private static LuggageRestoreState Identity(float x) => new()
    {
        viewId = 100, type = "Luggage", name = "LuggageBig", path = "Map/Spawner/LuggageBig", position = new Vector3Snapshot { x = x }
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class FakeBox : ILuggageRestoreTarget
    {
        internal bool Open, VisibleOpen, Available, Activated, Revived, HasSpawnHistory, Pending;
        internal int Entries, SpawnCount;
        internal FakeBox(bool open, bool pending)
        {
            Open = VisibleOpen = HasSpawnHistory = open;
            Available = !open; Entries = open ? 0 : 1; Pending = pending;
        }
        internal void Tick() { if (Pending) { Open = VisibleOpen = true; Available = false; } }
        internal void OpenNormally()
        {
            if (!Available || HasSpawnHistory) throw new Exception("Restored closed box cannot open normally.");
            Open = VisibleOpen = true; Available = false; HasSpawnHistory = true; SpawnCount++;
        }
        public void CancelOpening() => Pending = false;
        public void ResetSpawnHistory() => HasSpawnHistory = false;
        public void SetOpen(bool open) => Open = open;
        public void SetVisual(bool open) => VisibleOpen = open;
        public void SetAvailable(bool available) { Available = available; Entries = available ? 1 : 0; }
        public void SetActivatedObject(bool active) => Activated = active;
        public void SetRevivalState(bool spent, bool revivedPlayers) => Revived = revivedPlayers;
    }
}
