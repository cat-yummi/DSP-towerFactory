using HarmonyLib;

namespace TowerFactory
{
    /// <summary>戴森云太阳帆数量 mod 上限（原版可远高于此）。</summary>
    [HarmonyPatch(typeof(DysonSwarm), nameof(DysonSwarm.AddSolarSail))]
    public static class DysonSwarmCapPatch
    {
        public const int MaxSwarmSailCount = 10000;

        [HarmonyPrefix]
        public static bool Prefix(DysonSwarm __instance, ref int __result)
        {
            if (__instance.sailCount >= MaxSwarmSailCount)
            {
                __result = 0;
                return false;
            }
            return true;
        }
    }
}
