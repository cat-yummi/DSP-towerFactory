using System.Collections.Concurrent;
using HarmonyLib;

namespace TowerFactory
{
    /// <summary>
    /// AddTechHash 会触发科技解锁 UI，只能在主线程调用；塔厂在并行 PlanetTransport.GameTick 里排队到本帧 Update 执行。
    /// </summary>
    public static class TechHashMainThreadPatch
    {
        private static readonly ConcurrentQueue<long> pendingTechHash = new ConcurrentQueue<long>();

        internal static void EnqueueTechHash(long amount)
        {
            if (amount > 0)
            {
                pendingTechHash.Enqueue(amount);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameMain), "Update")]
        public static void GameMain_Update_Postfix()
        {
            GameHistoryData history = GameMain.history;
            if (history == null)
            {
                return;
            }
            while (pendingTechHash.TryDequeue(out long hash))
            {
                history.AddTechHash(hash);
            }
        }
    }
}
