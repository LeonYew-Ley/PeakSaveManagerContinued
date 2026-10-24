using PEAKSaveManager;

internal static class LandingSelectionTests
{
    internal static void Run()
    {
        Vector3Snapshot P(float x = 0, float y = 0, float z = 0) => new() { x = x, y = y, z = z };
        var original = P();
        var near = P(2, -1);
        var far = P(3, -5);
        Assert(LandingSelection.Closest(original, new[] { far, near }) == near, "Rank by total displacement");
        Assert(LandingSelection.Closest(original, new[] { near, far }) == near, "Enumeration order must not select farther ledge");
        Assert(LandingSelection.Closest(original, new[] { near, original }) == original, "Keep supported original");
        Assert(LandingSelection.Closest(original, new[] { P(8.01f), P(y: 8.01f), P(8, 8), P(float.NaN), P(float.PositiveInfinity) }) == null, "Reject nonfinite and out-of-bounds landings");
        Assert(LandingSelection.Closest(original, Array.Empty<Vector3Snapshot>()) == null, "No safe geometry means no landing");
        Assert(LandingSelection.Closest(P(100, 1000, 2000), new[] { P(102, 999, 2000) }) != null, "Limits are relative to original world coordinates");
        for (int state = 0; state < 128; state++)
        {
            var saved = new CharacterSnapshot { isGrounded = true, dead = (state & 1) != 0, passedOut = (state & 2) != 0,
                fullyPassedOut = (state & 4) != 0, isClimbing = (state & 8) != 0, isRopeClimbing = (state & 16) != 0,
                isVineClimbing = (state & 32) != 0 };
            saved.isGrounded = (state & 64) == 0;
            Assert(LandingSelection.NeedsSupport(saved) == (state == 0), "Do not ground dead, airborne or climbing saves");
        }
        Assert(!LandingSelection.NeedsSupport(null), "Missing saved state is not grounded");
        Assert(LandingSelection.IsSupportSample(1.1f, 1.1f, 1f, true), "Flat ground supports original torso");
        Assert(!LandingSelection.IsSupportSample(1.1f, 1.1f, 0.257f, true), "Recorded 75-degree b5 slope is unsafe");
        Assert(!LandingSelection.IsSupportSample(2.183f, 1.1f, 1f, true), "Recorded b5 ground gap is unsafe even on flat surface");
        Assert(!LandingSelection.IsSupportSample(0.3f, 1.1f, 1f, false), "Torso may not intersect rising ground");
        Assert(!LandingSelection.IsSupportSample(float.NaN, 1.1f, 1f, false), "Invalid hit must fail closed");
        Console.WriteLine("140 landing policy cases passed; Unity terrain/clearance needs gameplay verification.");
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
