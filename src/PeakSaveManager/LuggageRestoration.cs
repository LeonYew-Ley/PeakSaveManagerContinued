using System;
using System.Collections.Generic;

namespace PEAKSaveManager;

internal interface ILuggageRestoreTarget
{
    void CancelOpening();
    void ResetSpawnHistory();
    void SetOpen(bool open);
    void SetVisual(bool open);
    void SetAvailable(bool available);
    void SetActivatedObject(bool active);
    void SetRevivalState(bool spent, bool revivedPlayers);
}

[Serializable]
internal sealed class LuggageRestoreState
{
    public int viewId;
    public string type;
    public string name;
    public string path;
    public Vector3Snapshot position;
    public bool open;
    public bool activated;
    public bool spent;
    public bool revivedPlayers;
}

internal static class LuggageRestoration
{
    internal const float MaxMatchDistanceSquared = 400f;

    internal static void Apply(ILuggageRestoreTarget target, LuggageRestoreState state)
    {
        // A native delayed opening must not overwrite a subsequent load. Never
        // call the native opening RPC here: it skips already-open objects, and
        // its other branch spawns loot. Ground loot has its own restore pass.
        target.CancelOpening();
        if (!state.open) target.ResetSpawnHistory();
        target.SetOpen(state.open);
        target.SetVisual(state.open);
        target.SetAvailable(!state.open);
        target.SetActivatedObject(state.activated);
        target.SetRevivalState(state.spent, state.revivedPlayers);
    }

    internal static int FindMatch(LuggageRestoreState saved, IList<LuggageRestoreState> candidates)
    {
        if (saved?.position == null || !IsFinite(saved.position)) return -1;
        int best = -1;
        bool ambiguous = false;
        int bestRank = -1;
        float bestDistance = MaxMatchDistanceSquared;
        for (int i = 0; i < candidates.Count; i++)
        {
            LuggageRestoreState candidate = candidates[i];
            if (candidate?.position == null || !IsFinite(candidate.position)) continue;
            if (!string.IsNullOrEmpty(saved.type) && !Same(saved.type, candidate.type)) continue;
            if (!string.IsNullOrEmpty(saved.name) && !Same(saved.name, candidate.name)) continue;
            float dx = saved.position.x - candidate.position.x;
            float dy = saved.position.y - candidate.position.y;
            float dz = saved.position.z - candidate.position.z;
            float distance = dx * dx + dy * dy + dz * dz;
            if (distance > MaxMatchDistanceSquared) continue;
            int rank = !string.IsNullOrEmpty(saved.path) && Same(saved.path, candidate.path) ? 1 : 0;
            if (rank < bestRank) continue;
            if (rank > bestRank || distance < bestDistance - 0.0001f)
            {
                best = i;
                bestRank = rank;
                bestDistance = distance;
                ambiguous = false;
            }
            else if (Math.Abs(distance - bestDistance) <= 0.0001f) ambiguous = true;
        }
        return ambiguous ? -1 : best;
    }

    internal static bool IsFinite(Vector3Snapshot position) => position != null
        && !float.IsNaN(position.x) && !float.IsInfinity(position.x)
        && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
        && !float.IsNaN(position.z) && !float.IsInfinity(position.z);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

// A late-join snapshot can still be waiting for a view when a newer load arrives.
// Keep that older coroutine from applying its state after the newer batch.
internal sealed class LuggageRevisions
{
    private readonly Dictionary<int, long> revisions = new Dictionary<int, long>();
    private int host;
    private string scene;

    internal void Receive(int actor, string sceneName, long revision, IEnumerable<LuggageRestoreState> states)
    {
        if (host != actor || scene != sceneName)
        {
            revisions.Clear();
            host = actor;
            scene = sceneName;
        }
        foreach (LuggageRestoreState state in states)
            if (!revisions.TryGetValue(state.viewId, out long current) || revision > current)
                revisions[state.viewId] = revision;
    }

    internal bool IsCurrent(int actor, string sceneName, long revision, int viewId) => host == actor && scene == sceneName
        && revisions.TryGetValue(viewId, out long current) && current == revision;
}
