using System.Collections;
using Newtonsoft.Json;
using PEAKSaveManager;

internal static class SavedMapRestorationTests
{
    internal static void Run()
    {
        int dailyCalls = 0;
        string Daily(int _) { dailyCalls++; return "Level_77"; }
        SaveMetadata oldSave = new() { sceneName = "Level_2", levelName = "Level_2", levelNumber = 2, dailyLevelIndex = 493 };
        Assert(SavedMapRestoration.ResolveScene(oldSave, Daily) == "Level_2" && dailyCalls == 0,
            "The saved Level_2 must not become the daily Level_77.");
        Assert(SavedMapRestoration.ResolveScene(new() { levelNumber = 0, dailyLevelIndex = 493 }, Daily) == "Level_0", "Level zero is valid.");
        Assert(SavedMapRestoration.ResolveScene(new() { dailyLevelIndex = 493 }, Daily) == "Level_77" && dailyCalls == 1, "Legacy daily-only files still resolve.");
        Assert(SavedMapRestoration.ResolveScene(null, Daily) == null, "Missing metadata cannot choose a map.");
        Expect<InvalidOperationException>(() => SavedMapRestoration.ResolveScene(new() { sceneName = "Level_2", levelName = "Level_77" }, Daily));
        Expect<InvalidOperationException>(() => SavedMapRestoration.ResolveScene(new() { sceneName = "Level_2", levelNumber = 77 }, Daily));
        Expect<InvalidOperationException>(() => SavedMapRestoration.ResolveScene(new() { sceneName = "OtherScene", dailyLevelIndex = 493 }, Daily));
        Expect<InvalidOperationException>(() => SavedMapRestoration.ResolveScene(new() { levelNumber = -1 }, Daily));
        Expect<InvalidOperationException>(() => SavedMapRestoration.ResolveScene(new() { dailyLevelIndex = 1 }, _ => "Airport"));

        FakeLoader loader = new();
        IEnumerator prepare = SavedMapRestoration.Prepare("Level_2", loader);
        Assert(prepare.MoveNext() && loader.Requested == 2, "An unloaded saved scene requests its own bundle.");
        Assert(prepare.MoveNext(), "Wait for the asynchronous bundle load.");
        loader.IsLoading = false;
        loader.Ready = true;
        Assert(!prepare.MoveNext(), "Continue only once the exact saved scene can load.");

        loader = new() { Ready = true };
        Assert(!SavedMapRestoration.Prepare("Level_2", loader).MoveNext() && loader.Requested == null, "Already loaded scenes need no new bundle.");
        loader = new() { BundleExists = false };
        Expect<FileNotFoundException>(() => SavedMapRestoration.Prepare("Level_2", loader).MoveNext());
        Assert(loader.Requested == 2, "Missing resources must not choose today's map.");
        loader = new();
        prepare = SavedMapRestoration.Prepare("Level_2", loader);
        prepare.MoveNext();
        loader.IsLoading = false;
        Expect<FileNotFoundException>(() => prepare.MoveNext());
        loader = new();
        prepare = SavedMapRestoration.Prepare("Level_2", loader);
        prepare.MoveNext();
        loader.Realtime = SavedMapRestoration.BundleTimeoutSeconds;
        Expect<TimeoutException>(() => prepare.MoveNext());
        Expect<InvalidOperationException>(() => SavedMapRestoration.Prepare("Airport", loader).MoveNext());

        SaveMetadata volcano = new() { currentSegment = 4, biomeId = "STAV" };
        SavedMapRestoration.ValidateBiome(volcano, "Volcano");
        Expect<InvalidOperationException>(() => SavedMapRestoration.ValidateBiome(volcano, "Temple"));
        volcano.currentBiomeName = "Temple";
        SavedMapRestoration.ValidateBiome(volcano, "Temple");
        Expect<InvalidOperationException>(() => SavedMapRestoration.ValidateBiome(volcano, "Volcano"));
        SavedMapRestoration.ValidateBiome(new() { currentSegment = 2, biomeId = "STAV" }, "Alpine");
        SavedMapRestoration.ValidateBiome(new() { currentSegment = 4, biomeId = "STAS" }, "Swamp");
        SaveMetadata roundTrip = JsonConvert.DeserializeObject<SaveMetadata>(JsonConvert.SerializeObject(volcano));
        Assert(roundTrip.currentBiomeName == "Temple", "Capture the actual biome independently of the daily code.");
        SaveMetadata legacy = JsonConvert.DeserializeObject<SaveMetadata>("{\"currentSegment\":4,\"biomeId\":\"STAV\"}");
        Assert(legacy.currentBiomeName == null, "Old save format remains readable.");
        Expect<InvalidOperationException>(() => SavedMapRestoration.ValidateBiome(legacy, "Temple"));
        Console.WriteLine("Saved-map regressions passed: exact identity, asynchronous preload, missing/failed/timed-out bundles, legacy saves and volcano/temple mismatch.");
    }

    private sealed class FakeLoader : ISavedMapLoader
    {
        public float Realtime { get; set; }
        public bool Ready;
        public bool BundleExists = true;
        public int? Requested;
        public bool IsLoading { get; set; }
        public bool CanLoad(string sceneName) => Ready && sceneName == "Level_2";
        public bool BeginBundleLoad(int number) { Requested = number; IsLoading = BundleExists; return BundleExists; }
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
