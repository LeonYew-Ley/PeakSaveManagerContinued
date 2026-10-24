using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace PEAKSaveManager;

public sealed partial class SaveManagerPlugin
{
    private readonly List<string> landingRestoreWarnings = new List<string>();
    private static readonly FieldInfo ConditionalStandableField = AccessTools.Field(typeof(CollisionModifier), "hasStandableRange");

    // Resolve on the host before sending the snapshot to its physical owner.
    // Only the runtime copy changes: neither the envelope nor the save file is rewritten.
    private PlayerSnapshot ResolvePlayerLanding(Character character, PlayerSnapshot snapshot)
    {
        if (!LandingSelection.NeedsSupport(snapshot?.character)) return snapshot;
        if (snapshot.position == null || double.IsNaN(LandingSelection.DistanceSquared(snapshot.position, snapshot.position)))
            throw new InvalidOperationException("Saved landing position is missing or nonfinite.");
        if (ConditionalStandableField == null) throw new InvalidOperationException("Native terrain standability API changed.");
        Vector3 original = snapshot.position.ToUnity();
        int mapLayer = LayerMask.NameToLayer("Map");
        if (mapLayer < 0) throw new InvalidOperationException("Cannot check saved landing: Map collision layer is missing.");
        Physics.SyncTransforms();
        int mask = 1 << mapLayer;
        // Normal standing torso height; use the native hip target plus the torso/hip separation.
        float torsoHipDistance = 0.3f;
        foreach (Bodypart part in character.refs.ragdoll.partList)
            if (part != null && part.partType == BodypartType.Hip)
                torsoHipDistance = Vector3.Distance(character.Center, part.transform.position);
        float torsoHeight = Mathf.Clamp(character.data.targetHipHeight + torsoHipDistance, 0.9f, 1.4f);
        if (HasLandingSupport(character, original, torsoHeight, mask, true)) return snapshot;

        var candidates = new List<Vector3Snapshot>();
        const float step = 0.5f;
        for (float x = -LandingSelection.HorizontalLimit; x <= LandingSelection.HorizontalLimit; x += step)
        for (float z = -LandingSelection.HorizontalLimit; z <= LandingSelection.HorizontalLimit; z += step)
        {
            if (x * x + z * z > LandingSelection.HorizontalLimit * LandingSelection.HorizontalLimit) continue;
            Vector3 origin = original + new Vector3(x, LandingSelection.VerticalLimit + 2f, z);
            // Inspect stacked surfaces too; clearance excludes buried/occluded destinations.
            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down,
                LandingSelection.VerticalLimit * 2f + 4f, mask, QueryTriggerInteraction.Ignore))
            {
                if (!IsSafeLandingSurface(character, hit)) continue;
                Vector3 destination = hit.point + Vector3.up * torsoHeight;
                Vector3Snapshot candidate = new Vector3Snapshot(destination);
                if (LandingSelection.Closest(snapshot.position, new[] { candidate }) == null) continue;
                if (HasLandingSupport(character, destination, torsoHeight, mask, false)) candidates.Add(candidate);
            }
        }
        Vector3Snapshot best = LandingSelection.Closest(snapshot.position, candidates);
        if (best == null) throw new InvalidOperationException(
            $"No stable landing within 8m horizontally / 8m vertically (10m total) of '{snapshot.playerName}'. Saved position is unsafe; player teleport cancelled.");
        PlayerSnapshot adjusted = JsonConvert.DeserializeObject<PlayerSnapshot>(JsonConvert.SerializeObject(snapshot));
        adjusted.position = best;
        float distance = Vector3.Distance(original, best.ToUnity());
        string warning = $"{snapshot.playerName}: stable landing offset {distance:F1}m";
        landingRestoreWarnings.Add(warning);
        Logger.LogWarning($"{warning}; saved={original.ToString("F3")}, landing={best.ToUnity().ToString("F3")}. Save file unchanged.");
        return adjusted;
    }

    private static bool IsSafeLandingSurface(Character character, RaycastHit hit)
    {
        if (hit.collider == null || hit.normal.y < LandingSelection.MinimumNormalY
            || hit.collider.attachedRigidbody != null) return false;
        if (hit.collider.GetComponentInParent<Lava>() != null
            || hit.collider.GetComponentInParent<LavaRiver>() != null
            || hit.collider.GetComponentInParent<MovingLava>() != null
            || hit.collider.GetComponentInParent<Peak.KillPlane>() != null) return false;
        CollisionModifier modifier = hit.collider.GetComponent<CollisionModifier>();
        // Conditional platforms, bounce surfaces and damage/status terrain are not safe fallback ground.
        return modifier == null || (modifier.standable && !(bool)ConditionalStandableField.GetValue(modifier)
            && !modifier.applyEffects && !modifier.applyPetrify && modifier.knockback <= 0f
            && modifier.superBouncePow <= 0f);
    }

    private static bool HasLandingSupport(Character character, Vector3 torso, float torsoHeight, int mask, bool preserveOriginal)
    {
        float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
        // Nine points across a standing footprint reject narrow edges and holes.
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        {
            Vector3 origin = torso + new Vector3(x * 0.3f, 0.35f, z * 0.3f);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, torsoHeight + 0.65f, mask, QueryTriggerInteraction.Ignore)
                || !IsSafeLandingSurface(character, hit)) return false;
            float belowTorso = torso.y - hit.point.y;
            if (!LandingSelection.IsSupportSample(belowTorso, torsoHeight, hit.normal.y, preserveOriginal)) return false;
            lowest = Mathf.Min(lowest, hit.point.y);
            highest = Mathf.Max(highest, hit.point.y);
        }
        if (highest - lowest > 0.35f) return false;
        // Check all layers for body space, including props, players and trigger hazards.
        // The ground itself ends below the capsule's bottom.
        Vector3 bottom = new Vector3(torso.x, highest + 0.4f, torso.z);
        Vector3 top = new Vector3(torso.x, Mathf.Max(torso.y + 0.35f, bottom.y), torso.z);
        foreach (Collider collider in Physics.OverlapCapsule(bottom, top, 0.3f, ~0, QueryTriggerInteraction.Collide))
        {
            if (collider.GetComponentInParent<Character>() == character) continue;
            // Triggers are conservatively rejected; native lava/damage volumes need not be Map.
            return false;
        }
        return true;
    }
}
