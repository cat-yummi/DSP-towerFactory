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
        private const string SlotsNotEnoughMessage = "原料格子不够，请加装运输塔扩容mod";
        private const double VeinMinerTicksPerItem = 120.0;
        private const double OilExtractorTicksPerItem = 60.0;
        private const int AdvancedMinerMultiplier = 3;
        private const double FractionatorTicksPerItem = 300.0;
        private const float GammaPhotonModeMultiplier = 8f;
        private const float GammaFullWarmupMultiplier = 2.5f;
        private const float GammaFullWarmupLossFactor = 0.6f;

        private enum PlanKind
        {
            Recipe,
            Miner,
            Gamma
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
        }

        private class State
        {
            public int[] signature = Array.Empty<int>();
            public Plan plan;
            public int stableTicks;
            public bool active;
            public double progress;
            public long energyBuffer;
        }

        private static readonly ConditionalWeakTable<StationComponent, State> states = new ConditionalWeakTable<StationComponent, State>();

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
                return;
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
                default:
                    return 0;
            }
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
                times = Math.Min(times, FreeSpace(storage[1]) / resultCount);
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
                storage[1].count += times * resultCount;

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
            if (!prefabDesc.isAssembler && !prefabDesc.isLab && !prefabDesc.isFractionator && !isMiner && !isPump && !isOilExtractor && !prefabDesc.gammaRayReceiver)
            {
                return null;
            }

            if (buildingSlot.localLogic != ELogisticStorage.None
                || (station.isStellar && buildingSlot.remoteLogic != ELogisticStorage.None))
            {
                reason = "第一格设置了需求或供应";
                return null;
            }
            if (storage[1].itemId <= 0)
            {
                reason = "第二格为空";
                return null;
            }

            if (prefabDesc.isAssembler)
            {
                return MatchRecipePlan(storage, prefabDesc.assemblerRecipeType, out reason);
            }
            if (prefabDesc.isLab)
            {
                return MatchRecipePlan(storage, ERecipeType.Research, out reason);
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
            return MatchGammaPlan(factory, storage, prefabDesc, out reason);
        }

        private static Plan MatchRecipePlan(StationStore[] storage, ERecipeType recipeType, out string reason)
        {
            reason = null;
            int productId = storage[1].itemId;
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
            ELogisticStorage remoteLogic = station.isStellar ? ELogisticStorage.Demand : ELogisticStorage.None;
            for (int j = 0; j < ingredients.Length; j++)
            {
                transport.SetStationStorage(station.id, FirstIngredientSlot + j, ingredients[j], int.MaxValue, ELogisticStorage.Demand, remoteLogic, null);
            }
            message = $"已填入原料：{string.Join("、", ingredients.Select(ItemName))}";
            TowerFactory.Log.LogInfo($"{StationLabel(transport.factory, station)} {message}");
            return true;
        }

        private static int[] SuggestIngredients(StationComponent station, out string reason)
        {
            StationStore[] storage = station?.storage;
            if (storage == null || station.isCollector || station.isVeinCollector || storage.Length <= FirstIngredientSlot)
            {
                reason = "这座塔不能做塔厂";
                return null;
            }
            StationStore buildingSlot = storage[0];
            PrefabDesc prefabDesc = buildingSlot.itemId > 0 ? LDB.items.Select(buildingSlot.itemId)?.prefabDesc : null;
            if (prefabDesc == null || !(prefabDesc.isAssembler || prefabDesc.isLab || prefabDesc.isFractionator))
            {
                reason = "第一格需要放有配方的生产建筑";
                return null;
            }
            if (buildingSlot.localLogic != ELogisticStorage.None || (station.isStellar && buildingSlot.remoteLogic != ELogisticStorage.None))
            {
                reason = "第一格需要设为仓储";
                return null;
            }
            int productId = storage[1].itemId;
            if (productId <= 0)
            {
                reason = "第二格需要放产品";
                return null;
            }
            for (int i = FirstIngredientSlot; i < storage.Length; i++)
            {
                if (storage[i].itemId != 0)
                {
                    reason = "第三格起需要全部为空";
                    return null;
                }
            }

            var candidates = new List<int[]>();
            if (prefabDesc.isFractionator)
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
                reason = $"{ItemName(productId)} 不是该建筑能生产的产品";
                return null;
            }

            if (!candidates.Any(ingredients => FitsSlotCount(storage, ingredients)))
            {
                reason = SlotsNotEnoughMessage;
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
            reason = "放得下的配方里，原料与塔里已有物品重复";
            return null;
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
            for (int slot = 1; slot <= lastSlot; slot++)
            {
                int itemId = storage[slot].itemId;
                if (itemId <= 0)
                {
                    continue;
                }
                if (!IsMineral(itemId))
                {
                    reason = $"第 {slot + 1} 格 {ItemName(itemId)} 不是采矿机能采的矿物";
                    return null;
                }
                if (!PlanetHasMineral(factory.planet, itemId))
                {
                    reason = $"本行星没有第 {slot + 1} 格 {ItemName(itemId)} 的矿簇";
                    return null;
                }
                slots.Add(slot);
                names.Add(ItemName(itemId));
            }
            return new Plan
            {
                kind = PlanKind.Miner,
                description = $"{(isAdvanced ? "大型采矿" : "采矿")} {string.Join("、", names)}",
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
