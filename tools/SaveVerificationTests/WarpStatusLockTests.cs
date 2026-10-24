using PEAKSaveManager;

internal static class WarpStatusLockTests
{
    private sealed class CurrentCharacter
    {
        public bool statusesLocked { get; private set; } = true;
    }
    private sealed class LegacyCharacter
    {
        private bool _warpStatusLock = true;
        public bool Locked => _warpStatusLock;
    }
    private sealed class UnsupportedCharacter
    {
        public int statusesLocked { get; set; } = 1;
        public readonly bool _warpStatusLock = true;
    }
    private sealed class BothCharacter
    {
        public bool statusesLocked { get; private set; } = true;
        public bool _warpStatusLock = true;
    }

    internal static void Run()
    {
        CurrentCharacter current = new();
        WarpStatusLock contract = new(typeof(CurrentCharacter));
        Assert(contract.IsSupported, "PEAK 3.0 private setter is supported.");
        contract.Release(current);
        Assert(!current.statusesLocked, "Release the current native status property.");
        LegacyCharacter legacy = new();
        contract = new(typeof(LegacyCharacter));
        Assert(contract.IsSupported, "Previous private warp lock remains supported.");
        contract.Release(legacy);
        Assert(!legacy.Locked, "Release the legacy warp lock.");
        BothCharacter both = new();
        new WarpStatusLock(typeof(BothCharacter)).Release(both);
        Assert(!both.statusesLocked && both._warpStatusLock, "Prefer the current status setter over a legacy field.");
        contract = new(typeof(UnsupportedCharacter));
        Assert(!contract.IsSupported, "Reject unsupported or read-only members.");
        try { contract.Release(new UnsupportedCharacter()); }
        catch (InvalidOperationException) { Console.WriteLine("Warp status-lock regressions passed for current, legacy and unsupported APIs."); return; }
        throw new InvalidOperationException("An unknown native lock must not silently proceed.");
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
