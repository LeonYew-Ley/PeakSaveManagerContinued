using PEAKSaveManager;
using System.Collections;

internal static class SceneLoadBarrierTests
{
    internal static void Run()
    {
        SceneLoadParty party = new(1, new[] { 3, 1, 2 });
        SceneLoadBarrier barrier = new(party, "prepare-a", 120f);
        Assert(!barrier.IsComplete, "A host alone cannot confirm a shared map.");
        barrier.Acknowledge(2, "prepare-a", "");
        Assert(!barrier.IsComplete, "Every peer must confirm before scene load.");
        barrier.Check(1, new[] { 1, 2, 3 }, 119f, "prepare", Name);
        barrier.Acknowledge(1, "prepare-a", "");
        barrier.Acknowledge(9, "prepare-a", "");
        barrier.Acknowledge(3, "old-request", "");
        Assert(!barrier.IsComplete, "Host, outsiders and stale requests cannot satisfy another peer.");
        Expect<TimeoutException>(() => barrier.Check(1, new[] { 1, 2, 3 }, 120f, "prepare", Name), "player-3");
        barrier.Acknowledge(3, "prepare-a", "");
        Assert(barrier.IsComplete, "All peer confirmations permit progression.");
        barrier.Check(1, new[] { 3, 2, 1 }, 120f, "prepare", Name);

        SceneLoadBarrier ready = new(party, "ready-b", 200f);
        ready.Acknowledge(2, "prepare-a", "");
        ready.Acknowledge(3, "prepare-a", "");
        Assert(!ready.IsComplete, "Resource acknowledgements cannot satisfy scene/character readiness.");
        ready.Acknowledge(2, "ready-b", "");
        ready.Acknowledge(3, "ready-b", "scene unavailable");
        ready.Acknowledge(3, "ready-b", "");
        Expect<InvalidOperationException>(() => ready.Check(1, new[] { 1, 2, 3 }, 150f, "ready", Name), "scene unavailable");

        Expect<InvalidOperationException>(() => barrier.Check(2, new[] { 1, 2, 3 }, 10f, "prepare", Name), "host changed");
        Expect<InvalidOperationException>(() => barrier.Check(1, new[] { 1, 2 }, 10f, "prepare", Name), "joined or left");
        Expect<InvalidOperationException>(() => barrier.Check(1, new[] { 1, 2, 3, 4 }, 10f, "prepare", Name), "joined or left");
        Expect<InvalidOperationException>(() => barrier.Check(1, new[] { 1, 2, 4 }, 10f, "prepare", Name), "joined or left");

        SceneLoadBarrier retry = new(party, "prepare-retry", 300f);
        retry.Acknowledge(2, "prepare-a", "");
        retry.Acknowledge(3, "prepare-a", "");
        Assert(!retry.IsComplete, "A cancelled load's replies cannot authorize a retry.");
        SceneLoadBarrier solo = new(new SceneLoadParty(1, new[] { 1 }), "solo", 0f);
        Assert(solo.IsComplete, "An online solo room needs no remote acknowledgements.");
        solo.Check(1, new[] { 1 }, 1f, "prepare", Name);
        Expect<InvalidOperationException>(() => new SceneLoadParty(1, new[] { 1, 1 }), "Invalid");
        Expect<InvalidOperationException>(() => new SceneLoadParty(1, new[] { 2 }), "Invalid");
        Expect<InvalidOperationException>(() => new SceneLoadParty(1, new[] { 1, 0 }), "Invalid");
        VerifyNativeProcessGuard();
        Console.WriteLine("Shared-scene barrier regressions passed: all-peer preparation/readiness, phase/retry isolation, peer errors, timeout, party changes and solo rooms.");
    }

    private static void VerifyNativeProcessGuard()
    {
        bool cancelled = false, switched = false, disposed = false;
        Exception failure = null;
        IEnumerator NativeStep()
        {
            try { yield return null; switched = true; }
            finally { disposed = true; }
        }
        IEnumerator NativeRoot() { yield return NativeStep(); }
        void Validate() { if (cancelled) throw new InvalidOperationException("party changed"); }

        IEnumerator load = SceneLoadBarrier.GuardProcess(NativeRoot(), Validate, ex => failure = ex);
        Assert(load.MoveNext() && !switched, "The nested native process waits before switching.");
        cancelled = true;
        Assert(!load.MoveNext() && !switched && disposed && failure?.Message == "party changed",
            "Cancellation during the loading screen must prevent a later native switch and dispose the process.");

        cancelled = false; disposed = false; failure = null;
        load = SceneLoadBarrier.GuardProcess(NativeRoot(), Validate, ex => failure = ex);
        Assert(load.MoveNext() && !load.MoveNext() && switched && disposed && failure == null,
            "An unchanged party finishes the original nested native process.");

        IEnumerator ThrowingStep() { yield return null; throw new InvalidOperationException("native failure"); }
        IEnumerator ThrowingRoot() { yield return ThrowingStep(); }
        load = SceneLoadBarrier.GuardProcess(ThrowingRoot(), Validate, ex => failure = ex);
        Assert(load.MoveNext() && !load.MoveNext() && failure?.Message == "native failure",
            "Nested native exceptions must reach the host load failure path.");

        switched = false; disposed = false;
        load = SceneLoadBarrier.GuardProcess(NativeRoot(), Validate, _ => { });
        load.MoveNext();
        (load as IDisposable)?.Dispose();
        Assert(disposed && !switched, "Disposing a guarded process cannot leave its nested switch running.");
    }

    private static string Name(int actor) => "player-" + actor;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " containing " + message);
    }
}
