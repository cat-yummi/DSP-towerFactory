using HarmonyLib;

namespace TowerFactory
{
    /// <summary>进档 / 开始游戏时强制所有物流站重新识别塔厂配置。</summary>
    [HarmonyPatch(typeof(GameMain), nameof(GameMain.Begin))]
    public static class StationTowerRevalidatePatch
    {
        [HarmonyPostfix]
        public static void Begin_Postfix()
        {
            StationProductionPatch.InvalidateAllStationPlans();
            TowerFactory.Log?.LogInfo("塔厂：已请求进档后全站重新识别");
        }
    }
}
