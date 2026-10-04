using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace TowerFactory
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class TowerFactory : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.yummi.dsp.towerfactory";
        public const string PLUGIN_NAME = "塔厂 (Tower Factory)";
        public const string PLUGIN_VERSION = "1.2.0";

        private static readonly Harmony harmony = new Harmony(PLUGIN_GUID);
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            harmony.PatchAll(typeof(StationProductionPatch));
            harmony.PatchAll(typeof(UIStationWindowPatch));
            harmony.PatchAll(typeof(TechStationGiftPatch));
            harmony.PatchAll(typeof(DysonSwarmCapPatch));
            harmony.PatchAll(typeof(StationTowerRevalidatePatch));
            harmony.PatchAll(typeof(TechHashMainThreadPatch));

            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载");
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }
}
