using System;
using System.Collections.Generic;

namespace PEAKSaveManager;

// Geometry is supplied by Unity; selection and search limits are independently testable.
internal static class LandingSelection
{
    internal const float HorizontalLimit = 8f;
    internal const float VerticalLimit = 8f;
    internal const float DistanceLimit = 10f;
    internal const float MinimumNormalY = 0.819152f; // 35 degrees, below native sliding threshold.

    internal static bool NeedsSupport(CharacterSnapshot saved) => saved != null && saved.isGrounded
        && !saved.dead && !saved.passedOut && !saved.fullyPassedOut
        && !saved.isClimbing && !saved.isRopeClimbing && !saved.isVineClimbing;

    internal static bool IsSupportSample(float belowTorso, float torsoHeight, float normalY, bool preserveOriginal)
        => normalY >= MinimumNormalY && belowTorso >= 0.65f
            && belowTorso <= torsoHeight + (preserveOriginal ? 0.15f : 0.3f);

    internal static double DistanceSquared(Vector3Snapshot a, Vector3Snapshot b)
    {
        double x = (double)a.x - b.x, y = (double)a.y - b.y, z = (double)a.z - b.z;
        return x * x + y * y + z * z;
    }

    internal static Vector3Snapshot Closest(Vector3Snapshot original, IEnumerable<Vector3Snapshot> safeCandidates)
    {
        Vector3Snapshot best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (Vector3Snapshot candidate in safeCandidates)
        {
            if (candidate == null) continue;
            double dx = (double)candidate.x - original.x, dz = (double)candidate.z - original.z;
            double distance = DistanceSquared(original, candidate);
            if (double.IsNaN(distance) || double.IsInfinity(distance)
                || dx * dx + dz * dz > HorizontalLimit * HorizontalLimit
                || Math.Abs((double)candidate.y - original.y) > VerticalLimit
                || distance > DistanceLimit * DistanceLimit || distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }
}
