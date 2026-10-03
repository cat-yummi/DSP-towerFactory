using HarmonyLib;

namespace TowerFactory
{
    /// <summary>
    /// 解锁科技「电磁学」(1001) 时赠送 3 个行星内物流运输塔 (2103)，不改动科技树解锁项。
    /// </summary>
    [HarmonyPatch(typeof(GameHistoryData))]
    public static class ElectromagnetismStationRewardPatch
    {
        private const int ElectromagnetismTechId = 1001;
        private const int PlanetaryLogisticsStationItemId = 2103;
        private const int StationGiftCount = 3;
        /// <summary>存档标记：已发放过电磁学塔厂礼包（避免重复 Notify）。</summary>
        private const int FeatureKeyStationGiftGranted = 99001001;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameHistoryData.NotifyTechUnlock))]
        public static void NotifyTechUnlock_Postfix(GameHistoryData __instance, int _techId, int _level, bool _unlockedDirect)
        {
            if (_techId != ElectromagnetismTechId || __instance == null)
            {
                return;
            }
            if (!__instance.techStates.TryGetValue(ElectromagnetismTechId, out TechState state) || !state.unlocked)
            {
                return;
            }
            if (__instance.HasFeatureKey(FeatureKeyStationGiftGranted))
            {
                return;
            }
            __instance.RegFeatureKey(FeatureKeyStationGiftGranted);
            __instance.GainTechAwards(PlanetaryLogisticsStationItemId, StationGiftCount);
            TowerFactory.Log.LogInfo($"电磁学已解锁：赠送 {StationGiftCount} 个行星内物流运输塔（未解锁物流科技配方）");
        }
    }
}
