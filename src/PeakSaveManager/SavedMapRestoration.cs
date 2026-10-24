using System;
using System.Collections;
using System.Globalization;
using System.IO;

namespace PEAKSaveManager;

internal interface ISavedMapLoader
{
    float Realtime { get; }
    bool CanLoad(string sceneName);
    bool BeginBundleLoad(int levelNumber);
    bool IsLoading { get; }
}

internal static class SavedMapRestoration
{
    internal const float BundleTimeoutSeconds = 120f;

    internal static string ResolveScene(SaveMetadata metadata, Func<int, string> resolveDailyLevel)
    {
        if (metadata == null) return null;
        string savedScene = null;
        foreach (string value in new[] { metadata.sceneName, metadata.levelName })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("Airport", StringComparison.OrdinalIgnoreCase)) continue;
            string candidate = value.Trim();
            if (!TryGetLevelNumber(candidate, out _))
                throw new InvalidOperationException($"Invalid saved map name '{candidate}'.");
            if (savedScene != null && !savedScene.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Saved map identifiers disagree; refusing to choose another map.");
            savedScene = candidate;
        }
        if (metadata.levelNumber.HasValue)
        {
            if (metadata.levelNumber.Value < 0) throw new InvalidOperationException("Invalid saved map number.");
            string numberedScene = "Level_" + metadata.levelNumber.Value.ToString(CultureInfo.InvariantCulture);
            if (savedScene != null && !savedScene.Equals(numberedScene, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Saved map name and number disagree.");
            savedScene = numberedScene;
        }
        // A daily index is not a stable scene identity across game updates. Use
        // it only for legacy files that contain no explicit saved scene at all.
        if (savedScene == null && metadata.dailyLevelIndex.HasValue)
        {
            savedScene = resolveDailyLevel(metadata.dailyLevelIndex.Value);
            if (!TryGetLevelNumber(savedScene, out _))
                throw new InvalidOperationException("The legacy save's daily map cannot be resolved.");
        }
        return savedScene;
    }

    internal static bool TryGetLevelNumber(string sceneName, out int number)
    {
        number = -1;
        return sceneName != null && sceneName.StartsWith("Level_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(sceneName.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && number >= 0;
    }

    internal static IEnumerator Prepare(string sceneName, ISavedMapLoader loader)
    {
        if (!TryGetLevelNumber(sceneName, out int number))
            throw new InvalidOperationException("A valid saved map is required before loading.");
        if (loader.CanLoad(sceneName)) yield break;
        if (!loader.BeginBundleLoad(number))
            throw new FileNotFoundException($"Saved map '{sceneName}' has no installed resource bundle.");
        float deadline = loader.Realtime + BundleTimeoutSeconds;
        while (loader.IsLoading)
        {
            if (loader.Realtime >= deadline)
                throw new TimeoutException($"Timed out loading the resource bundle for '{sceneName}'.");
            yield return null;
        }
        if (!loader.CanLoad(sceneName))
            throw new FileNotFoundException($"Saved map '{sceneName}' is unavailable after loading its resource bundle. No other map was selected.");
    }

    internal static void ValidateBiome(SaveMetadata metadata, string actualBiome)
    {
        string expected = metadata.currentBiomeName;
        // Older files captured the daily biome code rather than a live biome.
        // Only infer the unambiguous volcano code for the two volcano segments.
        if (string.IsNullOrWhiteSpace(expected) && (metadata.currentSegment == 3 || metadata.currentSegment == 4)
            && metadata.biomeId != null && metadata.biomeId.Length == 4
            && char.ToUpperInvariant(metadata.biomeId[3]) == 'V') expected = "Volcano";
        if (!string.IsNullOrWhiteSpace(expected) && !expected.Equals(actualBiome, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Saved biome '{expected}' differs from loaded biome '{actualBiome}'. The map may have changed in this game version; player and world restoration stopped.");
    }
}
