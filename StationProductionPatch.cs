using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace TowerFactory
{
    /// <summary>
    /// 塔厂：第一格放生产建筑（不设需求/供应），后续格决定生产内容。
    /// - 制造类建筑 / 研究站：第二格放产品，后续格按配方顺序放原料。
    /// - 采矿机：第二格放本行星有的矿物；大型采矿机以 3 倍速度生产第二格起的全部矿物。
    /// - 射线接收站：第二格放临界光子，从戴森球多余能量中制造光子。
    /// - 弹射器：第二格太阳帆，直接增加戴森壳细胞点数（等同吸收太阳帆，不入戴森云）。
    /// - 发射井：第二格小运载火箭，直接增加结构点数（等同火箭抵达）。
    /// - 矩阵研究站且第二格为空：第三格起放研究矩阵，为 UI 队列当前科技供料（非纯宇宙矩阵且够料则一次完成等级；纯宇宙矩阵科技每秒每座研究站消耗 1 个宇宙矩阵）。
    /// - 分馏塔：第二格放重氢，第三格放氢，每座每半秒把 0.1 个氢转换为重氢。
    /// - 抽水机：第二格放本行星的海洋物品；原油萃取站：第二格放原油，本行星需有油井。
    /// 配置稳定数秒后开始生产：第一格有几座建筑，就以几倍速度生产。
    /// 生产周期为基础周期的 1/10，每周期生产 ceil(建筑数 / 10) 次。
    /// </summary>
    [HarmonyPatch]
    public class StationProductionPatch
    {
        private const int ActivateDelayTicks = 60;
        private const int CycleDivisor = 10;
        private const int FirstIngredientSlot = 2;
        /// <summary>矩阵研究塔：第二格起可放研究矩阵（含供应设置的第二格）。</summary>
        private const int TechResearchMatrixFirstSlot = 1;
        private const string SlotsNotEnoughMessage = "原料格子不够，请加装运输塔扩容mod";
        private const double VeinMinerTicksPerItem = 120.0;
        private const double OilExtractorTicksPerItem = 60.0;
        private const int AdvancedMinerMultiplier = 3;
        private const int AssemblerSelfProductMinBuildings = 100;
        private const double FractionatorTicksPerItem = 300.0;
        private const float GammaPhotonModeMultiplier = 8f;
        private const float GammaFullWarmupMultiplier = 2.5f;
        private const float GammaFullWarmupLossFactor = 0.6f;
        private const double TechResearchTicksPerCycle = 600.0;
        private const int MatrixPointPerItem = 3600;
        private static readonly int[] ResearchMatrixIds = { 6001, 6002, 6003, 6004, 6005, 6006 };

        private enum PlanKind
        {
            Recipe,
            Miner,
            Gamma,
            Dyson,
            TechResearch
        }

        private class Plan
        {
            public PlanKind kind;
            public string description;

            public int[] items;
            public int[] itemCounts;
            public int resultId;
            public int resultCount;
            public double recipeTicks;
            public ItemAmounts statProduced;
            public ItemAmounts statConsumed;

            public int[] outputSlots;
            public int outputMultiplier;
            public double minerTicksPerItem;

            public double photonTicks;
            public long photonHeat;

            public int dysonBulletId;
            /// <summary>弹射器为细胞点（太阳帆）；发射井为结构点（火箭）。</summary>
            public bool dysonCellPoints;

            /// <summary>第二格为空时视同产品为第一格同款制造台，产物写入第一格。</summary>
            public bool implicitSlot2Product;
            public int minBuildingCount;
        }

        private class State
        {
            public int[] signature = Array.Empty<int>();
            public Plan plan;
            public int stableTicks;
            public bool active;
            public double progress;
            public long energyBuffer;
            public int revalidateGeneration;
        }

        private static readonly ConditionalWeakTable<StationComponent, State> states = new ConditionalWeakTable<StationComponent, State>();
        private static int stationPlanRevalidateGeneration;

        internal static void InvalidateAllStationPlans()
        {
            stationPlanRevalidateGeneration++;
        }

        internal static string Tr(string zh, string en)
        {
            return Localization.isZHCN ? zh : en;
        }

        /// <summary>制造台且第二格为空时，产品视为第一格同款制造台。</summary>
        internal static bool TryResolveRecipeProductId(StationStore[] storage, PrefabDesc prefabDesc, int buildingItemId, out int productId, out bool implicitSlot2Product)
        {
            implicitSlot2Product = prefabDesc.isAssembler
                && prefabDesc.assemblerRecipeType == ERecipeType.Assemble
                && storage[1].itemId <= 0
                && buildingItemId > 0;
            productId = implicitSlot2Product ? buildingItemId : storage[1].itemId;
            return productId > 0;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.GameTick))]
        public static void PlanetTransport_GameTick_Postfix(PlanetTransport __instance)
        {
            PlanetFactory factory = __instance.factory;
            FactoryProductionStat stat = GameMain.statistics.production.factoryStatPool[factory.index];
            StationComponent[] stationPool = __instance.stationPool;
            for (int i = 1; i < __instance.stationCursor; i++)
            {
                StationComponent station = stationPool[i];
                if (station == null || station.id != i || station.isCollector || station.isVeinCollector)
                {
                    continue;
                }
                Tick(factory, station, stat.productRegister, stat.consumeRegister);
            }
        }

        private static void Tick(PlanetFactory factory, StationComponent station, int[] productRegister, int[] consumeRegister)
        {
            StationStore[] storage = station.storage;
            if (storage == null)
            {
                return;
            }
            State state = states.GetOrCreateValue(station);
            if (state.revalidateGeneration != stationPlanRevalidateGeneration)
            {
                state.revalidateGeneration = stationPlanRevalidateGeneration;
                state.signature = Array.Empty<int>();
            }
            else if (state.plan == null && StorageMightBeTowerFactory(station, storage)
                && (GameMain.gameTick + station.id) % 60 == 0)
            {
                Plan retry = MatchPlan(factory, station, out string retryReason);
                if (retry != null)
                {
                    state.signature = BuildSignature(storage);
                    state.plan = retry;
                    state.stableTicks = 0;
                    state.active = false;
                    state.progress = 0;
                    state.energyBuffer = 0;
                    LogMatch(factory, station, null, retry, retryReason);
                }
            }

            if (!SignatureEquals(state.signature, storage))
            {
                Plan previous = state.plan;
                state.signature = BuildSignature(storage);
                state.plan = MatchPlan(factory, station, out string reason);
                state.stableTicks = 0;
                state.active = false;
                state.progress = 0;
                state.energyBuffer = 0;
                LogMatch(factory, station, previous, state.plan, reason);
            }
            Plan plan = state.plan;
            if (plan == null)
            {
                return;
            }
            if (!state.active)
            {
                if (++state.stableTicks < ActivateDelayTicks)
                {
                    return;
                }
                state.active = true;
                TowerFactory.Log.LogInfo($"{StationLabel(factory, station)} 塔厂开始生产：{plan.description}");
            }

            int buildingCount = storage[0].count;
            if (buildingCount <= 0)
            {
                if (plan.minBuildingCount <= 0)
                {
                    return;
                }
                buildingCount = plan.minBuildingCount;
            }
            else if (plan.minBuildingCount > 0)
            {
                buildingCount = Math.Max(buildingCount, plan.minBuildingCount);
            }
            int batch = (buildingCount + CycleDivisor - 1) / CycleDivisor;

            double period = GetPeriod(plan);
            if (period <= 0)
            {
                return;
            }
            if (plan.kind == PlanKind.Gamma)
            {
                DrawGammaEnergy(factory, state, plan, batch);
            }
            state.progress += CycleDivisor;
            while (state.progress >= period)
            {
                state.progress -= period;
                switch (plan.kind)
                {
                    case PlanKind.Recipe:
                        CraftRecipe(station, plan, batch, productRegister, consumeRegister);
                        break;
                    case PlanKind.Miner:
                        CraftMiner(station, plan, batch, productRegister);
                        break;
                    case PlanKind.Gamma:
                        CraftGamma(station, state, plan, batch, productRegister);
                        break;
                    case PlanKind.Dyson:
                        CraftDyson(factory, station, plan, batch, buildingCount, consumeRegister);
                        break;
                    case PlanKind.TechResearch:
                        CraftTechResearch(station, buildingCount, consumeRegister);
                        break;
                }
            }
        }

        private static double GetPeriod(Plan plan)
        {
            switch (plan.kind)
            {
                case PlanKind.Recipe:
                    return plan.recipeTicks;
                case PlanKind.Miner:
                    float scale = GameMain.history.miningSpeedScale;
                    return scale > 0f ? plan.minerTicksPerItem / scale : 0;
                case PlanKind.Gamma:
                    return plan.photonTicks;
                case PlanKind.Dyson:
                    return plan.dysonCellPoints ? CycleDivisor : plan.recipeTicks;
                case PlanKind.TechResearch:
                    return plan.recipeTicks;
                default:
                    return 0;
            }
        }

        private static bool IsResearchMatrix(int itemId)
        {
            for (int i = 0; i < ResearchMatrixIds.Length; i++)
            {
                if (ResearchMatrixIds[i] == itemId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsUniverseMatrixOnlyTech(TechProto tech)
        {
            if (tech?.Items == null || tech.Items.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < tech.Items.Length; i++)
            {
                if (tech.Items[i] != 6006)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryGetCurrentLabTech(out TechProto tech, out TechState state)
        {
            tech = null;
            state = default;
            GameHistoryData history = GameMain.history;
            int techId = history.currentTech;
            if (techId <= 0)
            {
                return false;
            }
            tech = LDB.techs.Select(techId);
            if (tech == null || !tech.IsLabTech || (tech.PropertyOverrideItems != null && tech.PropertyOverrideItems.Length > 0))
            {
                return false;
            }
            if (!history.techStates.TryGetValue(techId, out state) || state.unlocked)
            {
                return false;
            }
            return true;
        }

        private static int CountMatrixInStation(StationStore[] storage, int matrixId, int firstSlot = FirstIngredientSlot)
        {
            int total = 0;
            for (int s = firstSlot; s < storage.Length; s++)
            {
                if (storage[s].itemId == matrixId)
                {
                    total += storage[s].count;
                }
            }
            return total;
        }

        private static int CountTechResearchMatrix(StationStore[] storage, int matrixId)
        {
            return CountMatrixInStation(storage, matrixId, TechResearchMatrixFirstSlot);
        }

        /// <summary>第二格为空，或第二格仅为研究矩阵且第三格起无配方原料。</summary>
        private static bool LabStorageIsResearchFeed(StationStore[] storage)
        {
            if (storage[1].itemId <= 0)
            {
                return true;
            }
            if (!IsResearchMatrix(storage[1].itemId))
            {
                return false;
            }
            for (int s = FirstIngredientSlot; s < storage.Length; s++)
            {
                int itemId = storage[s].itemId;
                if (itemId > 0 && !IsResearchMatrix(itemId))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryComputeMatrixNeeds(TechProto tech, long remainingHash, int[] countsOut)
        {
            Array.Clear(countsOut, 0, countsOut.Length);
            if (remainingHash <= 0 || tech.Items == null)
            {
                return false;
            }
            for (int i = 0; i < tech.Items.Length; i++)
            {
                int itemId = tech.Items[i];
                int idx = Array.IndexOf(ResearchMatrixIds, itemId);
                if (idx < 0)
                {
                    return false;
                }
                countsOut[idx] = (int)((remainingHash * (long)tech.ItemPoints[i]) / MatrixPointPerItem);
            }
            return true;
        }

        private static bool StationHasMatrices(StationStore[] storage, TechProto tech, long remainingHash)
        {
            int[] need = new int[ResearchMatrixIds.Length];
            if (!TryComputeMatrixNeeds(tech, remainingHash, need))
            {
                return false;
            }
            for (int i = 0; i < ResearchMatrixIds.Length; i++)
            {
                if (need[i] > 0 && CountTechResearchMatrix(storage, ResearchMatrixIds[i]) < need[i])
                {
                    return false;
                }
            }
            return need.Any(n => n > 0);
        }

        private static bool TryConsumeMatricesFromStation(StationStore[] storage, TechProto tech, long remainingHash, int[] consumeRegister)
        {
            int[] need = new int[ResearchMatrixIds.Length];
            if (!TryComputeMatrixNeeds(tech, remainingHash, need))
            {
                return false;
            }
            for (int i = 0; i < ResearchMatrixIds.Length; i++)
            {
                if (need[i] <= 0)
                {
                    continue;
                }
                if (CountTechResearchMatrix(storage, ResearchMatrixIds[i]) < need[i])
                {
                    return false;
                }
            }
            for (int i = 0; i < ResearchMatrixIds.Length; i++)
            {
                int matrixId = ResearchMatrixIds[i];
                int left = need[i];
                if (left <= 0)
                {
                    continue;
                }
                for (int s = TechResearchMatrixFirstSlot; s < storage.Length && left > 0; s++)
                {
                    ref StationStore slot = ref storage[s];
                    if (slot.itemId != matrixId)
                    {
                        continue;
                    }
                    int take = Math.Min(left, slot.count);
                    slot.count -= take;
                    if (slot.inc > slot.count)
                    {
                        slot.inc = slot.count;
                    }
                    left -= take;
                }
                if (left > 0)
                {
                    return false;
                }
                lock (consumeRegister)
                {
                    consumeRegister[matrixId] += need[i];
                }
            }
            return true;
        }

        private static int MatrixCostForHash(TechProto tech, long hashAmount)
        {
            if (hashAmount <= 0 || tech.Items == null || tech.Items.Length == 0)
            {
                return 0;
            }
            long cost = (hashAmount * (long)tech.ItemPoints[0]) / MatrixPointPerItem;
            if (cost <= 0 && hashAmount > 0)
            {
                cost = 1;
            }
            return (int)Math.Min(cost, int.MaxValue);
        }

        private static bool TryConsumeMatrixAmount(StationStore[] storage, int matrixId, int amount, int[] consumeRegister, int firstSlot = FirstIngredientSlot)
        {
            if (amount <= 0 || CountMatrixInStation(storage, matrixId, firstSlot) < amount)
            {
                return false;
            }
            int left = amount;
            for (int s = firstSlot; s < storage.Length && left > 0; s++)
            {
                ref StationStore slot = ref storage[s];
                if (slot.itemId != matrixId)
                {
                    continue;
                }
                int take = Math.Min(left, slot.count);
                slot.count -= take;
                if (slot.inc > slot.count)
                {
                    slot.inc = slot.count;
                }
                left -= take;
            }
            if (left > 0)
            {
                return false;
            }
            lock (consumeRegister)
            {
                consumeRegister[matrixId] += amount;
            }
            return true;
        }

        private static void CraftTechResearch(StationComponent station, int buildingCount, int[] consumeRegister)
        {
            StationStore[] storage = station.storage;
            lock (storage)
            {
                lock (GameMain.history)
                {
                    if (!TryGetCurrentLabTech(out TechProto tech, out TechState ts))
                    {
                        return;
                    }
                    if (IsUniverseMatrixOnlyTech(tech))
                    {
                        CraftUniverseMatrixResearch(storage, tech, buildingCount, consumeRegister);
                        return;
                    }
                    while (TryGetCurrentLabTech(out tech, out ts))
                    {
                        if (IsUniverseMatrixOnlyTech(tech))
                        {
                            break;
                        }
                        long remaining = ts.hashNeeded - ts.hashUploaded;
                        if (remaining <= 0)
                        {
                            break;
                        }
                        if (!StationHasMatrices(storage, tech, remaining))
                        {
                            break;
                        }
                        if (!TryConsumeMatricesFromStation(storage, tech, remaining, consumeRegister))
                        {
                            break;
                        }
                        TechHashMainThreadPatch.EnqueueTechHash(remaining);
                    }
                }
            }
        }

        private static long HashFromUniverseMatrixCount(TechProto tech, int matrixCount)
        {
            if (matrixCount <= 0 || tech.ItemPoints == null || tech.ItemPoints.Length == 0)
            {
                return 0;
            }
            int itemPoints = tech.ItemPoints[0];
            if (itemPoints <= 0)
            {
                return matrixCount;
            }
            return matrixCount * (long)MatrixPointPerItem / itemPoints;
        }

        /// <summary>纯宇宙矩阵科技：每秒每座矩阵研究站（第一格）消耗 1 个宇宙矩阵 6006。</summary>
        private static void CraftUniverseMatrixResearch(StationStore[] storage, TechProto tech, int buildingCount, int[] consumeRegister)
        {
            if (!GameMain.history.techStates.TryGetValue(GameMain.history.currentTech, out TechState ts))
            {
                return;
            }
            long remaining = ts.hashNeeded - ts.hashUploaded;
            if (remaining <= 0 || buildingCount <= 0)
            {
                return;
            }
            int consume = Math.Min(buildingCount, CountTechResearchMatrix(storage, 6006));
            if (consume <= 0)
            {
                return;
            }
            long hashAdd = HashFromUniverseMatrixCount(tech, consume);
            if (hashAdd <= 0)
            {
                hashAdd = consume;
            }
            if (hashAdd > remaining)
            {
                int itemPoints = tech.ItemPoints[0];
                consume = itemPoints > 0
                    ? (int)((remaining * itemPoints + MatrixPointPerItem - 1) / MatrixPointPerItem)
                    : (int)Math.Min(remaining, consume);
                consume = Math.Max(1, Math.Min(consume, Math.Min(buildingCount, CountTechResearchMatrix(storage, 6006))));
                hashAdd = Math.Min(HashFromUniverseMatrixCount(tech, consume), remaining);
            }
            if (hashAdd <= 0 || !TryConsumeMatrixAmount(storage, 6006, consume, consumeRegister, TechResearchMatrixFirstSlot))
            {
                return;
            }
            TechHashMainThreadPatch.EnqueueTechHash(hashAdd);
        }

        private static bool SphereHasStructureWork(DysonSphere sphere)
        {
            return sphere.GetAutoNodeCount() > 0;
        }

        /// <summary>与原版射线吸收一致：节点结构点满（sp == spMax，通常 30）后才可施工细胞点。</summary>
        private static bool NodeReadyForCellConstruction(DysonNode node)
        {
            return node != null && node.sp == node.spMax;
        }

        private static bool SphereHasCellWork(DysonSphere sphere)
        {
            for (int i = 1; i < sphere.layersIdBased.Length; i++)
            {
                DysonSphereLayer layer = sphere.layersIdBased[i];
                if (layer == null || layer.id != i)
                {
                    continue;
                }
                for (int j = 1; j < layer.nodeCursor; j++)
                {
                    DysonNode node = layer.nodePool[j];
                    if (node != null && node.id == j && NodeReadyForCellConstruction(node) && node.cpReqOrder > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>对壳面上每个仍有细胞点需求的节点各尝试一次（单个失败不阻塞其它节点）。</summary>
        private static int TryConstructCellPointWave(DysonSphere sphere, int maxOps)
        {
            if (maxOps <= 0)
            {
                return 0;
            }
            int waveBuilt = 0;
            for (int i = 1; i < sphere.layersIdBased.Length; i++)
            {
                DysonSphereLayer layer = sphere.layersIdBased[i];
                if (layer == null || layer.id != i)
                {
                    continue;
                }
                for (int j = 1; j < layer.nodeCursor; j++)
                {
                    if (waveBuilt >= maxOps)
                    {
                        return waveBuilt;
                    }
                    DysonNode node = layer.nodePool[j];
                    if (node == null || node.id != j || !NodeReadyForCellConstruction(node) || node.cpReqOrder <= 0)
                    {
                        continue;
                    }
                    node.cpOrdered++;
                    DysonShell shell = node.ConstructCp();
                    if (shell == null)
                    {
                        continue;
                    }
                    sphere.needRecalculatePower = true;
                    int[] productRegister = sphere.productRegister;
                    if (productRegister != null)
                    {
                        lock (productRegister)
                        {
                            productRegister[11903]++;
                        }
                    }
                    waveBuilt++;
                }
            }
            return waveBuilt;
        }

        /// <summary>等同火箭抵达：结构点 sp（统计 11902）。</summary>
        private static bool TryConstructStructurePoint(DysonSphere sphere, int autoNodeIndex)
        {
            if (sphere.GetAutoNodeCount() <= 0)
            {
                return false;
            }
            DysonNode node = sphere.GetAutoDysonNode(autoNodeIndex);
            sphere.OrderConstructSp(node);
            object built = node.ConstructSp();
            if (built == null)
            {
                return false;
            }
            sphere.needRecalculatePower = true;
            if (built is DysonNode builtNode)
            {
                sphere.UpdateProgress(builtNode);
            }
            else if (built is DysonFrame builtFrame)
            {
                sphere.UpdateProgress(builtFrame);
            }
            int[] productRegister = sphere.productRegister;
            if (productRegister != null)
            {
                lock (productRegister)
                {
                    productRegister[11902]++;
                }
            }
            return true;
        }

        private static void CraftDyson(PlanetFactory factory, StationComponent station, Plan plan, int batch, int buildingCount, int[] consumeRegister)
        {
            DysonSphere sphere = factory.dysonSphere;
            if (sphere == null)
            {
                return;
            }
            StationStore[] storage = station.storage;
            int built = 0;
            int sailAvailable;
            lock (storage)
            {
                if (storage[1].itemId != plan.dysonBulletId || storage[1].count <= 0)
                {
                    return;
                }
                sailAvailable = storage[1].count;
            }

            lock (sphere.dysonSphere_mx)
            {
                if (plan.dysonCellPoints)
                {
                    while (built < sailAvailable && SphereHasCellWork(sphere))
                    {
                        int waveBuilt = TryConstructCellPointWave(sphere, sailAvailable - built);
                        if (waveBuilt <= 0)
                        {
                            break;
                        }
                        built += waveBuilt;
                    }
                }
                else
                {
                    int attempts = Math.Min(batch, sailAvailable);
                    for (int i = 0; i < attempts; i++)
                    {
                        if (!TryConstructStructurePoint(sphere, station.id + built))
                        {
                            break;
                        }
                        built++;
                    }
                }
            }
            if (built <= 0)
            {
                return;
            }
            lock (storage)
            {
                int deduct = Math.Min(built, storage[1].count);
                if (deduct <= 0)
                {
                    return;
                }
                storage[1].count -= deduct;
                if (storage[1].inc > storage[1].count)
                {
                    storage[1].inc = storage[1].count;
                }
                built = deduct;
            }
            plan.statConsumed.Register(consumeRegister, built);
        }

        private static void CraftRecipe(StationComponent station, Plan plan, int batch, int[] productRegister, int[] consumeRegister)
        {
            StationStore[] storage = station.storage;
            int[] items = plan.items;
            int[] itemCounts = plan.itemCounts;
            int resultCount = plan.resultCount;

            lock (storage)
            {
                int times = batch;
                for (int j = 0; j < items.Length; j++)
                {
                    times = Math.Min(times, storage[FirstIngredientSlot + j].count / itemCounts[j]);
                }
                ref StationStore productSlot = ref plan.implicitSlot2Product ? ref storage[0] : ref storage[1];
                if (plan.implicitSlot2Product)
                {
                    if (productSlot.itemId != plan.resultId)
                    {
                        return;
                    }
                }
                else if (productSlot.itemId != 0 && productSlot.itemId != plan.resultId)
                {
                    return;
                }
                times = Math.Min(times, FreeSpace(productSlot) / resultCount);
                if (times <= 0)
                {
                    return;
                }

                for (int j = 0; j < items.Length; j++)
                {
                    ref StationStore store = ref storage[FirstIngredientSlot + j];
                    store.count -= times * itemCounts[j];
                    if (store.inc > store.count)
                    {
                        store.inc = store.count;
                    }
                }
                if (!plan.implicitSlot2Product && productSlot.itemId == 0)
                {
                    productSlot.itemId = plan.resultId;
                }
                productSlot.count += times * resultCount;

                plan.statConsumed.Register(consumeRegister, times);
                plan.statProduced.Register(productRegister, times);
            }
        }

        private static void CraftMiner(StationComponent station, Plan plan, int batch, int[] productRegister)
        {
            StationStore[] storage = station.storage;
            lock (storage)
            {
                foreach (int slot in plan.outputSlots)
                {
                    int amount = Math.Min(batch * plan.outputMultiplier, FreeSpace(storage[slot]));
                    if (amount <= 0)
                    {
                        continue;
                    }
                    storage[slot].count += amount;
                    lock (productRegister)
                    {
                        productRegister[storage[slot].itemId] += amount;
                    }
                }
            }
        }

        private static long EnergyPerPhoton(Plan plan)
        {
            float eta = 1f - GameMain.history.solarEnergyLossRate * GammaFullWarmupLossFactor;
            return eta > 0f ? (long)(plan.photonHeat / eta) : 0;
        }

        /// <summary>
        /// 每帧按生产速度从戴森球多余能量中取电存入缓冲，最多攒够一个周期所需。
        /// </summary>
        private static void DrawGammaEnergy(PlanetFactory factory, State state, Plan plan, int batch)
        {
            DysonSphere sphere = factory.dysonSphere;
            long energyPerPhoton = EnergyPerPhoton(plan);
            if (sphere == null || energyPerPhoton <= 0)
            {
                return;
            }
            long bufferCap = batch * energyPerPhoton;
            long want = Math.Min((long)(bufferCap * CycleDivisor / plan.photonTicks) + 1, bufferCap - state.energyBuffer);
            if (want <= 0)
            {
                return;
            }

            long req;
            long take;
            do
            {
                req = Interlocked.Read(ref sphere.energyReqCurrentTick);
                long surplus = sphere.energyGenCurrentTick - req;
                take = Math.Min(want, surplus);
                if (take <= 0)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref sphere.energyReqCurrentTick, req + take, req) != req);
            state.energyBuffer += take;
        }

        private static void CraftGamma(StationComponent station, State state, Plan plan, int batch, int[] productRegister)
        {
            long energyPerPhoton = EnergyPerPhoton(plan);
            if (energyPerPhoton <= 0)
            {
                return;
            }
            StationStore[] storage = station.storage;
            lock (storage)
            {
                int times = (int)Math.Min(Math.Min(batch, FreeSpace(storage[1])), state.energyBuffer / energyPerPhoton);
                if (times <= 0)
                {
                    return;
                }
                state.energyBuffer -= times * energyPerPhoton;
                storage[1].count += times;
                lock (productRegister)
                {
                    productRegister[storage[1].itemId] += times;
                }
            }
        }

        private static int FreeSpace(StationStore store)
        {
            int space = store.max - store.count;
            return space > 0 ? space : 0;
        }

        private static bool StorageMightBeTowerFactory(StationComponent station, StationStore[] storage)
        {
            if (storage == null || storage.Length < 2 || storage[0].itemId <= 0 || storage[0].count <= 0)
            {
                return false;
            }
            PrefabDesc prefabDesc = LDB.items.Select(storage[0].itemId)?.prefabDesc;
            if (prefabDesc == null)
            {
                return false;
            }
            if (storage[0].localLogic != ELogisticStorage.None
                || (station.isStellar && storage[0].remoteLogic != ELogisticStorage.None))
            {
                return false;
            }
            bool isMiner = prefabDesc.minerType == EMinerType.Vein || prefabDesc.isVeinCollector;
            bool isPump = prefabDesc.minerType == EMinerType.Water;
            bool isOil = prefabDesc.minerType == EMinerType.Oil;
            return prefabDesc.isAssembler || prefabDesc.isLab || prefabDesc.isFractionator || isMiner || isPump || isOil
                || prefabDesc.gammaRayReceiver || prefabDesc.isPowerExchanger || prefabDesc.isEjector || prefabDesc.isSilo;
        }

        private static Plan MatchPlan(PlanetFactory factory, StationComponent station, out string reason)
        {
            reason = null;
            StationStore[] storage = station.storage;
            if (storage.Length < 2)
            {
                return null;
            }

            StationStore buildingSlot = storage[0];
            PrefabDesc prefabDesc = buildingSlot.itemId > 0 ? LDB.items.Select(buildingSlot.itemId)?.prefabDesc : null;
            if (prefabDesc == null)
            {
                return null;
            }
            bool isMiner = prefabDesc.minerType == EMinerType.Vein || prefabDesc.isVeinCollector;
            bool isPump = prefabDesc.minerType == EMinerType.Water;
            bool isOilExtractor = prefabDesc.minerType == EMinerType.Oil;
            if (!prefabDesc.isAssembler && !prefabDesc.isLab && !prefabDesc.isFractionator && !isMiner && !isPump && !isOilExtractor
                && !prefabDesc.gammaRayReceiver && !prefabDesc.isPowerExchanger && !prefabDesc.isEjector && !prefabDesc.isSilo)
            {
                return null;
            }

            if (buildingSlot.localLogic != ELogisticStorage.None
                || (station.isStellar && buildingSlot.remoteLogic != ELogisticStorage.None))
            {
                reason = "第一格设置了需求或供应";
                return null;
            }
            if (prefabDesc.isEjector || prefabDesc.isSilo)
            {
                return MatchDysonPlan(factory, storage, prefabDesc, out reason);
            }
            if (prefabDesc.isLab && LabStorageIsResearchFeed(storage))
            {
                return MatchTechResearchPlan(storage, out reason);
            }

            if (!TryResolveRecipeProductId(storage, prefabDesc, buildingSlot.itemId, out int productId, out bool implicitSlot2Product)
                && storage[1].itemId <= 0)
            {
                reason = Tr("第二格为空", "Slot 2 is empty");
                return null;
            }

            if (prefabDesc.isAssembler)
            {
                Plan plan = MatchRecipePlan(storage, prefabDesc.assemblerRecipeType, productId, out reason);
                if (plan != null && implicitSlot2Product)
                {
                    plan.implicitSlot2Product = true;
                    plan.minBuildingCount = AssemblerSelfProductMinBuildings;
                    plan.description += Tr("（第二格视为制造台）", " (slot 2 treated as assembler)");
                }
                return plan;
            }
            if (prefabDesc.isLab)
            {
                return MatchRecipePlan(storage, ERecipeType.Research, storage[1].itemId, out reason);
            }
            if (prefabDesc.isFractionator)
            {
                return MatchFractionatorPlan(storage, out reason);
            }
            if (isMiner)
            {
                return MatchMinerPlan(factory, storage, prefabDesc.isVeinCollector, out reason);
            }
            if (isPump)
            {
                return MatchPumpPlan(factory, storage, prefabDesc, out reason);
            }
            if (isOilExtractor)
            {
                return MatchOilExtractorPlan(factory, storage, out reason);
            }
            if (prefabDesc.isPowerExchanger)
            {
                return MatchExchangerPlan(storage, prefabDesc, out reason);
            }
            return MatchGammaPlan(factory, storage, prefabDesc, out reason);
        }

        private static Plan MatchRecipePlan(StationStore[] storage, ERecipeType recipeType, int productId, out string reason)
        {
            reason = null;
            bool productFound = false;
            bool anyRecipeFitsSlots = false;
            foreach (PseudoRecipe pseudo in PseudoRecipes.All)
            {
                if (pseudo.buildingType != recipeType || !pseudo.outputs.TryGetValue(productId, out int outputCount))
                {
                    continue;
                }
                productFound = true;
                anyRecipeFitsSlots |= FitsSlotCount(storage, pseudo.ingredients);
                if (SlotsMatch(storage, pseudo.ingredients))
                {
                    return new Plan
                    {
                        kind = PlanKind.Recipe,
                        description = $"伪配方 {pseudo.name}",
                        items = pseudo.ingredients,
                        itemCounts = pseudo.ingredientCounts,
                        resultId = productId,
                        resultCount = outputCount,
                        recipeTicks = pseudo.ticks,
                        statProduced = pseudo.produced,
                        statConsumed = pseudo.consumed
                    };
                }
            }
            foreach (RecipeProto recipe in LDB.recipes.dataArray)
            {
                if (recipe == null || recipe.Type != recipeType)
                {
                    continue;
                }
                int index = Array.IndexOf(recipe.Results, productId);
                if (index < 0)
                {
                    continue;
                }
                productFound = true;
                anyRecipeFitsSlots |= FitsSlotCount(storage, recipe.Items);
                if (SlotsMatch(storage, recipe.Items))
                {
                    return new Plan
                    {
                        kind = PlanKind.Recipe,
                        description = $"配方 {recipe.name}",
                        items = recipe.Items,
                        itemCounts = recipe.ItemCounts,
                        resultId = productId,
                        resultCount = recipe.ResultCounts[index],
                        recipeTicks = Math.Max(recipe.TimeSpend, 1),
                        statProduced = ItemAmounts.Of(recipe.Results, recipe.ResultCounts),
                        statConsumed = ItemAmounts.Of(recipe.Items, recipe.ItemCounts)
                    };
                }
            }
            if (!productFound)
            {
                reason = $"第二格 {ItemName(productId)} 不是该建筑能生产的产品";
            }
            else if (!anyRecipeFitsSlots)
            {
                reason = SlotsNotEnoughMessage;
            }
            else
            {
                reason = "第三格起的原料与配方顺序不符";
            }
            return null;
        }

        private static bool FitsSlotCount(StationStore[] storage, int[] ingredients)
        {
            return FirstIngredientSlot + ingredients.Length <= storage.Length;
        }

        private static Plan MatchTechResearchPlan(StationStore[] storage, out string reason)
        {
            reason = null;
            for (int s = TechResearchMatrixFirstSlot; s < storage.Length; s++)
            {
                int itemId = storage[s].itemId;
                if (itemId <= 0)
                {
                    continue;
                }
                if (!IsResearchMatrix(itemId))
                {
                    reason = Tr(
                        $"第 {s + 1} 格 {ItemName(itemId)} 不是研究矩阵（6001–6006）",
                        $"Slot {s + 1} {ItemName(itemId)} is not a research matrix (6001–6006)");
                    return null;
                }
            }
            return new Plan
            {
                kind = PlanKind.TechResearch,
                description = Tr(
                    "矩阵研究塔（第二格起放矩阵，队列当前科技）",
                    "Matrix research tower (matrices from slot 2, UI queue head)"),
                recipeTicks = TechResearchTicksPerCycle
            };
        }

        private static Plan MatchDysonPlan(PlanetFactory factory, StationStore[] storage, PrefabDesc prefabDesc, out string reason)
        {
            reason = null;
            bool isEjector = prefabDesc.isEjector;
            int bulletId = isEjector ? prefabDesc.ejectorBulletId : prefabDesc.siloBulletId;
            if (bulletId <= 0)
            {
                reason = Tr("建筑弹药配置无效", "Invalid ammo config on building");
                return null;
            }
            if (storage[1].itemId != bulletId)
            {
                reason = Tr(
                    $"第二格应为 {ItemName(bulletId)}",
                    $"Slot 2 must hold {ItemName(bulletId)}");
                return null;
            }
            DysonSphere sphere = factory.dysonSphere;
            if (sphere == null)
            {
                reason = Tr("本恒星系无戴森球", "No Dyson sphere in this star system");
                return null;
            }
            int chargeFrames = isEjector
                ? prefabDesc.ejectorChargeFrame + prefabDesc.ejectorColdFrame
                : prefabDesc.siloChargeFrame + prefabDesc.siloColdFrame;
            chargeFrames = Math.Max(chargeFrames, 1);
            string role = isEjector
                ? Tr("弹射器直建细胞点（每帧全节点满速）", "Ejector direct cell points (max speed, all nodes each wave)")
                : Tr("发射井直建结构点", "Silo direct structure points");
            return new Plan
            {
                kind = PlanKind.Dyson,
                dysonCellPoints = isEjector,
                description = isEjector
                    ? role
                    : $"{role}，单座每 {chargeFrames / 60.0:0.##} 秒 1 点",
                dysonBulletId = bulletId,
                recipeTicks = chargeFrames,
                statConsumed = ItemAmounts.Of(new[] { bulletId }, new[] { 1 })
            };
        }

        private static Plan MatchFractionatorPlan(StationStore[] storage, out string reason)
        {
            reason = null;
            if (storage.Length <= FirstIngredientSlot)
            {
                reason = "格子不够";
                return null;
            }
            int productId = storage[1].itemId;
            int inputId = storage[FirstIngredientSlot].itemId;
            foreach (RecipeProto recipe in LDB.recipes.dataArray)
            {
                if (recipe == null || recipe.Type != ERecipeType.Fractionate)
                {
                    continue;
                }
                if (recipe.Results.Length > 0 && recipe.Results[0] == productId
                    && recipe.Items.Length > 0 && recipe.Items[0] == inputId)
                {
                    return new Plan
                    {
                        kind = PlanKind.Recipe,
                        description = $"分馏 {ItemName(inputId)} → {ItemName(productId)}",
                        items = new[] { inputId },
                        itemCounts = new[] { 1 },
                        resultId = productId,
                        resultCount = 1,
                        recipeTicks = FractionatorTicksPerItem,
                        statProduced = ItemAmounts.Of(new[] { productId }, new[] { 1 }),
                        statConsumed = ItemAmounts.Of(new[] { inputId }, new[] { 1 })
                    };
                }
            }
            reason = "第二格/第三格不是分馏配方的产品/原料";
            return null;
        }

        /// <summary>
        /// 自动填原料：第一格是仓储的生产建筑、第二格有产品、第三格起全空时，按配方填入原料（伪配方优先）。
        /// </summary>
        public static bool TryAutoFill(PlanetTransport transport, StationComponent station, out string message)
        {
            int[] ingredients = SuggestIngredients(station, out message);
            if (ingredients == null)
            {
                return false;
            }
            Player player = GameMain.mainPlayer;
            ELogisticStorage remoteLogic = station.isStellar ? ELogisticStorage.Demand : ELogisticStorage.None;
            for (int j = 0; j < ingredients.Length; j++)
            {
                transport.SetStationStorage(station.id, FirstIngredientSlot + j, ingredients[j], int.MaxValue, ELogisticStorage.Demand, remoteLogic, player);
            }
            string names = string.Join(Tr("、", ", "), ingredients.Select(ItemName));
            message = Tr($"已填入原料：{names}", $"Ingredients filled: {names}");
            TowerFactory.Log.LogInfo($"{StationLabel(transport.factory, station)} 已填入原料：{names}");
            return true;
        }

        private static int[] SuggestIngredients(StationComponent station, out string reason)
        {
            StationStore[] storage = station?.storage;
            if (storage == null || station.isCollector || station.isVeinCollector || storage.Length <= FirstIngredientSlot)
            {
                reason = Tr("这座塔不能做塔厂", "This station can't be a tower factory");
                return null;
            }
            StationStore buildingSlot = storage[0];
            PrefabDesc prefabDesc = buildingSlot.itemId > 0 ? LDB.items.Select(buildingSlot.itemId)?.prefabDesc : null;
            if (prefabDesc == null || !(prefabDesc.isAssembler || prefabDesc.isLab || prefabDesc.isFractionator || prefabDesc.isPowerExchanger))
            {
                reason = Tr("第一格需要放有配方的生产建筑", "Slot 1 must hold a production building that uses recipes");
                return null;
            }
            if (buildingSlot.localLogic != ELogisticStorage.None || (station.isStellar && buildingSlot.remoteLogic != ELogisticStorage.None))
            {
                reason = Tr("第一格需要设为仓储", "Slot 1 must be set to Storage");
                return null;
            }
            if (!TryResolveRecipeProductId(storage, prefabDesc, buildingSlot.itemId, out int productId, out _))
            {
                reason = Tr("第二格需要放产品", "Slot 2 must hold the product");
                return null;
            }
            for (int i = FirstIngredientSlot; i < storage.Length; i++)
            {
                if (storage[i].itemId != 0)
                {
                    reason = Tr("第三格起需要全部为空", "Slot 3 and later must all be empty");
                    return null;
                }
            }

            var candidates = new List<int[]>();
            if (prefabDesc.isPowerExchanger)
            {
                if (productId == prefabDesc.fullId && prefabDesc.emptyId > 0)
                {
                    candidates.Add(new[] { prefabDesc.emptyId });
                }
            }
            else if (prefabDesc.isFractionator)
            {
                foreach (RecipeProto recipe in LDB.recipes.dataArray)
                {
                    if (recipe != null && recipe.Type == ERecipeType.Fractionate && recipe.Results.Length > 0 && recipe.Results[0] == productId && recipe.Items.Length > 0)
                    {
                        candidates.Add(new[] { recipe.Items[0] });
                    }
                }
            }
            else
            {
                ERecipeType recipeType = prefabDesc.isAssembler ? prefabDesc.assemblerRecipeType : ERecipeType.Research;
                candidates.AddRange(PseudoRecipes.All
                    .Where(pseudo => pseudo.buildingType == recipeType && pseudo.outputs.ContainsKey(productId))
                    .Select(pseudo => pseudo.ingredients));
                candidates.AddRange(LDB.recipes.dataArray
                    .Where(recipe => recipe != null && recipe.Type == recipeType && Array.IndexOf(recipe.Results, productId) >= 0)
                    .Select(recipe => recipe.Items));
            }
            if (candidates.Count == 0)
            {
                reason = Tr($"{ItemName(productId)} 不是该建筑能生产的产品", $"{ItemName(productId)} can't be produced by this building");
                return null;
            }

            if (!candidates.Any(ingredients => FitsSlotCount(storage, ingredients)))
            {
                reason = Tr(SlotsNotEnoughMessage, "Not enough slots for the ingredients. Please install a station slot expansion mod");
                return null;
            }
            foreach (int[] ingredients in candidates)
            {
                bool fits = FitsSlotCount(storage, ingredients)
                    && ingredients.Distinct().Count() == ingredients.Length
                    && !ingredients.Contains(buildingSlot.itemId)
                    && !ingredients.Contains(productId);
                if (fits)
                {
                    reason = null;
                    return ingredients;
                }
            }
            reason = Tr("放得下的配方里，原料与塔里已有物品重复", "Every recipe that fits has an ingredient that duplicates an item already in the station");
            return null;
        }

        /// <summary>
        /// 能量枢纽只做充电且不耗电：1 空蓄电器 → 1 满蓄电器，耗时 = 蓄电器容量 ÷ 枢纽功率。
        /// </summary>
        private static Plan MatchExchangerPlan(StationStore[] storage, PrefabDesc prefabDesc, out string reason)
        {
            reason = null;
            if (storage.Length <= FirstIngredientSlot)
            {
                reason = SlotsNotEnoughMessage;
                return null;
            }
            if (storage[1].itemId != prefabDesc.fullId || storage[FirstIngredientSlot].itemId != prefabDesc.emptyId)
            {
                reason = $"第二格应为 {ItemName(prefabDesc.fullId)}，第三格应为 {ItemName(prefabDesc.emptyId)}";
                return null;
            }
            if (prefabDesc.exchangeEnergyPerTick <= 0 || prefabDesc.maxExcEnergy <= 0)
            {
                reason = "能量枢纽功率数据异常";
                return null;
            }
            double ticks = prefabDesc.maxExcEnergy / (double)prefabDesc.exchangeEnergyPerTick;
            return new Plan
            {
                kind = PlanKind.Recipe,
                description = $"充电 {ItemName(prefabDesc.emptyId)} → {ItemName(prefabDesc.fullId)}，单座每 {ticks / 60.0:0.##} 秒 1 个",
                items = new[] { prefabDesc.emptyId },
                itemCounts = new[] { 1 },
                resultId = prefabDesc.fullId,
                resultCount = 1,
                recipeTicks = ticks,
                statProduced = ItemAmounts.Of(new[] { prefabDesc.fullId }, new[] { 1 }),
                statConsumed = ItemAmounts.Of(new[] { prefabDesc.emptyId }, new[] { 1 })
            };
        }

        private static bool SlotsMatch(StationStore[] storage, int[] ingredients)
        {
            if (FirstIngredientSlot + ingredients.Length > storage.Length)
            {
                return false;
            }
            for (int j = 0; j < ingredients.Length; j++)
            {
                if (storage[FirstIngredientSlot + j].itemId != ingredients[j])
                {
                    return false;
                }
            }
            return true;
        }

        private static Plan MatchMinerPlan(PlanetFactory factory, StationStore[] storage, bool isAdvanced, out string reason)
        {
            reason = null;
            int lastSlot = isAdvanced ? storage.Length - 1 : 1;
            var slots = new List<int>();
            var names = new List<string>();
            var skipped = new List<string>();
            for (int slot = 1; slot <= lastSlot; slot++)
            {
                int itemId = storage[slot].itemId;
                if (itemId <= 0)
                {
                    continue;
                }
                string slotProblem = null;
                if (!IsMineral(itemId))
                {
                    slotProblem = $"第 {slot + 1} 格 {ItemName(itemId)} 不是采矿机能采的矿物";
                }
                else if (!PlanetHasMineral(factory.planet, itemId))
                {
                    slotProblem = $"本行星没有第 {slot + 1} 格 {ItemName(itemId)} 的矿簇";
                }
                if (slotProblem != null)
                {
                    skipped.Add(slotProblem);
                    continue;
                }
                slots.Add(slot);
                names.Add(ItemName(itemId));
            }
            if (slots.Count == 0)
            {
                reason = skipped.Count > 0 ? string.Join("；", skipped) : "第二格为空";
                return null;
            }
            string skippedText = skipped.Count > 0 ? $"；跳过：{string.Join("；", skipped)}" : "";
            return new Plan
            {
                kind = PlanKind.Miner,
                description = $"{(isAdvanced ? "大型采矿" : "采矿")} {string.Join("、", names)}{skippedText}",
                outputSlots = slots.ToArray(),
                outputMultiplier = isAdvanced ? AdvancedMinerMultiplier : 1,
                minerTicksPerItem = VeinMinerTicksPerItem
            };
        }

        private static Plan MatchPumpPlan(PlanetFactory factory, StationStore[] storage, PrefabDesc prefabDesc, out string reason)
        {
            reason = null;
            int waterItemId = factory.planet?.waterItemId ?? 0;
            if (waterItemId <= 0)
            {
                reason = "本行星没有可抽取的海洋";
                return null;
            }
            if (storage[1].itemId != waterItemId)
            {
                reason = $"第二格应为本行星的海洋物品 {ItemName(waterItemId)}";
                return null;
            }
            if (prefabDesc.minerPeriod <= 0)
            {
                reason = "抽水机速度数据异常";
                return null;
            }
            return new Plan
            {
                kind = PlanKind.Miner,
                description = $"抽取 {ItemName(waterItemId)}",
                outputSlots = new[] { 1 },
                outputMultiplier = 1,
                minerTicksPerItem = prefabDesc.minerPeriod / 10000.0
            };
        }

        private static Plan MatchOilExtractorPlan(PlanetFactory factory, StationStore[] storage, out string reason)
        {
            reason = null;
            int itemId = storage[1].itemId;
            if (LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.Oil)
            {
                reason = $"第二格 {ItemName(itemId)} 不是原油";
                return null;
            }
            if (!PlanetHasMineral(factory.planet, itemId))
            {
                reason = "本行星没有油井";
                return null;
            }
            return new Plan
            {
                kind = PlanKind.Miner,
                description = $"萃取 {ItemName(itemId)}",
                outputSlots = new[] { 1 },
                outputMultiplier = 1,
                minerTicksPerItem = OilExtractorTicksPerItem
            };
        }

        private static bool IsMineral(int itemId)
        {
            EVeinType veinType = LDB.veins.GetVeinTypeByItemId(itemId);
            return veinType != EVeinType.None && veinType != EVeinType.Oil;
        }

        private static bool PlanetHasMineral(PlanetData planet, int itemId)
        {
            EVeinType veinType = LDB.veins.GetVeinTypeByItemId(itemId);
            VeinGroup[] groups = planet?.runtimeVeinGroups;
            if (groups == null)
            {
                return false;
            }
            foreach (VeinGroup group in groups)
            {
                if (group.type == veinType && !group.isEmpty)
                {
                    return true;
                }
            }
            return false;
        }

        private static Plan MatchGammaPlan(PlanetFactory factory, StationStore[] storage, PrefabDesc prefabDesc, out string reason)
        {
            reason = null;
            if (storage[1].itemId != prefabDesc.powerProductId || prefabDesc.powerProductHeat <= 0)
            {
                reason = $"第二格应为 {ItemName(prefabDesc.powerProductId)}";
                return null;
            }
            float energyPerTick = prefabDesc.genEnergyPerTick * GammaPhotonModeMultiplier * GammaFullWarmupMultiplier;
            if (energyPerTick <= 0f)
            {
                reason = "射线接收站功率数据异常";
                return null;
            }
            if (factory.dysonSphere == null)
            {
                reason = "本星系没有戴森球";
                return null;
            }
            var plan = new Plan
            {
                kind = PlanKind.Gamma,
                photonTicks = prefabDesc.powerProductHeat / (double)energyPerTick,
                photonHeat = prefabDesc.powerProductHeat
            };
            plan.description = $"临界光子，单座每 {plan.photonTicks / 60.0:0.##} 秒 1 个，每个耗能 {prefabDesc.powerProductHeat / 1e6:0.##} MJ";
            return plan;
        }

        private static void LogMatch(PlanetFactory factory, StationComponent station, Plan previous, Plan plan, string reason)
        {
            if (plan != null)
            {
                TowerFactory.Log.LogInfo($"{StationLabel(factory, station)} 识别为塔厂（{plan.description}），{ActivateDelayTicks / 60} 秒后生效");
            }
            else if (reason != null)
            {
                TowerFactory.Log.LogInfo($"{StationLabel(factory, station)} 未成为塔厂：{reason}");
            }
            else if (previous != null)
            {
                TowerFactory.Log.LogInfo($"{StationLabel(factory, station)} 不再是塔厂");
            }
        }

        private static string StationLabel(PlanetFactory factory, StationComponent station)
        {
            return $"[{factory.planet?.displayName} 站点 #{station.id}]";
        }

        private static string ItemName(int itemId)
        {
            return LDB.items.Select(itemId)?.name ?? itemId.ToString();
        }

        private static int[] BuildSignature(StationStore[] storage)
        {
            int[] signature = new int[storage.Length * 3];
            for (int i = 0; i < storage.Length; i++)
            {
                signature[i * 3] = storage[i].itemId;
                signature[i * 3 + 1] = (int)storage[i].localLogic;
                signature[i * 3 + 2] = (int)storage[i].remoteLogic;
            }
            return signature;
        }

        private static bool SignatureEquals(int[] signature, StationStore[] storage)
        {
            if (signature.Length != storage.Length * 3)
            {
                return false;
            }
            for (int i = 0; i < storage.Length; i++)
            {
                if (signature[i * 3] != storage[i].itemId
                    || signature[i * 3 + 1] != (int)storage[i].localLogic
                    || signature[i * 3 + 2] != (int)storage[i].remoteLogic)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
