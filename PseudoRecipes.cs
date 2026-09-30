using System;
using System.Collections.Generic;
using System.Linq;

namespace TowerFactory
{
    /// <summary>
    /// 一组物品及数量，用于按生产次数登记到生产/消耗统计。
    /// </summary>
    internal class ItemAmounts
    {
        public readonly int[] ids;
        public readonly int[] counts;

        public ItemAmounts(IDictionary<int, int> amounts)
        {
            ids = amounts.Keys.ToArray();
            counts = ids.Select(id => amounts[id]).ToArray();
        }

        public static ItemAmounts Of(int[] ids, int[] counts)
        {
            var amounts = new Dictionary<int, int>();
            for (int i = 0; i < ids.Length; i++)
            {
                amounts.TryGetValue(ids[i], out int current);
                amounts[ids[i]] = current + counts[i];
            }
            return new ItemAmounts(amounts);
        }

        public void Register(int[] register, int times)
        {
            lock (register)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    register[ids[i]] += counts[i] * times;
                }
            }
        }
    }

    /// <summary>
    /// 由多步真实配方组合成的伪配方。净原料/净产物、统计与耗时都由真实配方计算。
    /// </summary>
    internal class PseudoRecipe
    {
        public string name;
        public ERecipeType buildingType;
        public int[] ingredients;
        public int[] ingredientCounts;
        public Dictionary<int, int> outputs;
        public ItemAmounts produced;
        public ItemAmounts consumed;
        public int ticks;
    }

    internal static class PseudoRecipes
    {
        private const int Coal = 1006;
        private const int CrudeOil = 1007;
        private const int FireIce = 1011;
        private const int OpticalGrating = 1014;
        private const int Graphite = 1109;
        private const int RefinedOil = 1114;
        private const int TitaniumCrystal = 1118;
        private const int Hydrogen = 1120;
        private const int Graphene = 1123;
        private const int Casimir = 1126;
        private const int EnergyMatrix = 6002;

        private static readonly Lazy<List<PseudoRecipe>> all = new Lazy<List<PseudoRecipe>>(Build);

        public static List<PseudoRecipe> All => all.Value;

        private static List<PseudoRecipe> Build()
        {
            var list = new List<PseudoRecipe>();
            RecipeProto plasma = Find(ERecipeType.Refine, new[] { CrudeOil }, RefinedOil);
            RecipeProto xray = Find(ERecipeType.Refine, new[] { RefinedOil, Hydrogen }, Graphite);
            RecipeProto reforming = Find(ERecipeType.Refine, new[] { RefinedOil, Hydrogen, Coal }, RefinedOil);
            RecipeProto energyMatrix = Find(ERecipeType.Research, new[] { Graphite, Hydrogen }, EnergyMatrix);
            RecipeProto fireIceGraphene = Find(ERecipeType.Chemical, new[] { FireIce }, Graphene);
            RecipeProto casimir = Find(ERecipeType.Assemble, new[] { TitaniumCrystal, Graphene, Hydrogen }, Casimir);
            RecipeProto casimirGrating = Find(ERecipeType.Assemble, new[] { OpticalGrating, Graphene, Hydrogen }, Casimir);

            Add(list, "能量矩阵（原油+石墨）", ERecipeType.Refine, new[] { CrudeOil, Graphite },
                (plasma, 2), (xray, 4), (energyMatrix, 3));
            Add(list, "X 射线裂解（净）", ERecipeType.Refine, new[] { RefinedOil },
                (xray, 1));
            Add(list, "精炼油（原油+煤）", ERecipeType.Refine, new[] { CrudeOil, Coal },
                (plasma, 1), (reforming, 1));
            AddGrapheneSwap(list, "卡西米尔晶体（可燃冰）", casimir, fireIceGraphene);
            AddGrapheneSwap(list, "卡西米尔晶体（光栅石+可燃冰）", casimirGrating, fireIceGraphene);
            return list;
        }

        private static void AddGrapheneSwap(List<PseudoRecipe> list, string name, RecipeProto target, RecipeProto grapheneRecipe)
        {
            if (target == null || grapheneRecipe == null)
            {
                TowerFactory.Log.LogWarning($"伪配方 {name} 未启用：找不到对应的真实配方");
                return;
            }
            int need = target.ItemCounts[Array.IndexOf(target.Items, Graphene)];
            int made = grapheneRecipe.ResultCounts[Array.IndexOf(grapheneRecipe.Results, Graphene)];
            int lcm = need / Gcd(need, made) * made;
            int[] order = target.Items.Select(id => id == Graphene ? FireIce : id).ToArray();
            Add(list, name, target.Type, order, (grapheneRecipe, lcm / made), (target, lcm / need));
        }

        private static void Add(List<PseudoRecipe> list, string name, ERecipeType buildingType, int[] ingredientOrder, params (RecipeProto recipe, int times)[] steps)
        {
            if (steps.Any(step => step.recipe == null))
            {
                TowerFactory.Log.LogWarning($"伪配方 {name} 未启用：找不到对应的真实配方");
                return;
            }

            var produced = new Dictionary<int, int>();
            var consumed = new Dictionary<int, int>();
            int ticks = 0;
            foreach (var (recipe, times) in steps)
            {
                Accumulate(produced, recipe.Results, recipe.ResultCounts, times);
                Accumulate(consumed, recipe.Items, recipe.ItemCounts, times);
                ticks += recipe.TimeSpend * times;
            }

            var net = new Dictionary<int, int>(produced);
            foreach (var pair in consumed)
            {
                net.TryGetValue(pair.Key, out int current);
                net[pair.Key] = current - pair.Value;
            }
            var inputs = net.Where(pair => pair.Value < 0).Select(pair => pair.Key).ToList();
            if (inputs.Count != ingredientOrder.Length || !ingredientOrder.All(inputs.Contains))
            {
                string actual = string.Join("、", inputs.Select(id => $"{id}×{-net[id]}"));
                TowerFactory.Log.LogWarning($"伪配方 {name} 未启用：按真实配方算出的净原料为 {actual}，与设计不符");
                return;
            }

            var pseudo = new PseudoRecipe
            {
                name = name,
                buildingType = buildingType,
                ingredients = ingredientOrder,
                ingredientCounts = ingredientOrder.Select(id => -net[id]).ToArray(),
                outputs = net.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => pair.Value),
                produced = new ItemAmounts(produced),
                consumed = new ItemAmounts(consumed),
                ticks = Math.Max(ticks, 1)
            };
            list.Add(pseudo);

            string inText = string.Join(" + ", pseudo.ingredients.Select((id, i) => $"{pseudo.ingredientCounts[i]} {ItemName(id)}"));
            string outText = string.Join(" + ", pseudo.outputs.Select(pair => $"{pair.Value} {ItemName(pair.Key)}"));
            TowerFactory.Log.LogInfo($"伪配方 {name}：{inText} → {outText}，{ticks / 60.0:0.##} 秒");
        }

        private static void Accumulate(Dictionary<int, int> target, int[] ids, int[] counts, int times)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                target.TryGetValue(ids[i], out int current);
                target[ids[i]] = current + counts[i] * times;
            }
        }

        private static RecipeProto Find(ERecipeType type, int[] items, int result)
        {
            foreach (RecipeProto recipe in LDB.recipes.dataArray)
            {
                if (recipe != null && recipe.Type == type
                    && Array.IndexOf(recipe.Results, result) >= 0
                    && recipe.Items.Length == items.Length && items.All(id => Array.IndexOf(recipe.Items, id) >= 0))
                {
                    return recipe;
                }
            }
            return null;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }

        private static string ItemName(int itemId)
        {
            return LDB.items.Select(itemId)?.name ?? itemId.ToString();
        }
    }
}
