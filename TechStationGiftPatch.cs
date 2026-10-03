using HarmonyLib;

namespace TowerFactory
{
    /// <summary>
    /// 指定科技解锁时赠送行星内物流运输塔 (2103)，不改动科技树的 UnlockRecipes / UnlockFunctions。
    /// Tech IDs: dsp-wiki.com/Modding:Tech_IDs
    /// </summary>
    [HarmonyPatch(typeof(GameHistoryData))]
    public static class TechStationGiftPatch
    {
        private const int PlanetaryLogisticsStationItemId = 2103;
        private const int StationGiftCount = 2;
        private const int FeatureKeyBase = 99001000;

        private static readonly int[] GiftTechIds =
        {
            1001, // Electromagnetics / 电磁学
            1201, // Basic assembling process / 基础制造
            1002, // Electromagnetic matrix / 电磁矩阵
            1401, // Automatic metallurgy / 自动化冶金
            1601, // Basic logistics system / 基础物流系统
        };

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameHistoryData.NotifyTechUnlock))]
        public static void NotifyTechUnlock_Postfix(GameHistoryData __instance, int _techId, int _level, bool _unlockedDirect)
        {
            if (__instance == null || !IsGiftTech(_techId))
            {
                return;
            }
            if (!__instance.techStates.TryGetValue(_techId, out TechState state) || !state.unlocked)
            {
                return;
            }
            int featureKey = FeatureKeyBase + _techId;
            if (__instance.HasFeatureKey(featureKey))
            {
                return;
            }
            __instance.RegFeatureKey(featureKey);
            __instance.GainTechAwards(PlanetaryLogisticsStationItemId, StationGiftCount);
            string techLabel = LDB.techs.Select(_techId)?.name ?? _techId.ToString();
            TowerFactory.Log.LogInfo($"科技 {techLabel} 已解锁：赠送 {StationGiftCount} 个行星内物流运输塔（未解锁物流科技配方）");
        }

        private static bool IsGiftTech(int techId)
        {
            foreach (int id in GiftTechIds)
            {
                if (id == techId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
