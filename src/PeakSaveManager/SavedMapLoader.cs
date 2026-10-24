using UnityEngine;

namespace PEAKSaveManager;

public sealed partial class SaveManagerPlugin
{
    private sealed class SavedMapLoader : ISavedMapLoader
    {
        private MapBundleService.MapBundleData bundle;
        public float Realtime => Time.realtimeSinceStartup;
        public bool CanLoad(string sceneName) => Application.CanStreamedLevelBeLoaded(sceneName);
        public bool IsLoading => bundle != null && bundle.Loading;

        public bool BeginBundleLoad(int levelNumber)
        {
            MapBundleService service = GameHandler.GetService<MapBundleService>();
            bundle = service?.LoadBundlesNearestIndex(levelNumber, false);
            return bundle != null;
        }
    }
}
