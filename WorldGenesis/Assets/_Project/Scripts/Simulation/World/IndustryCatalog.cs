using System;

namespace WorldGenesis.Simulation
{
    public static class IndustryCatalog
    {
        private static ItemAmount M(ItemType item, float amount) { return new ItemAmount(item, amount); }
        private static AgriculturalAmount A(AgriculturalProduct item, float amount) { return new AgriculturalAmount(item, amount); }
        public static readonly IndustryRecipe[] All = {
            new IndustryRecipe(IndustryRecipeType.Charcoal, BuildingType.Smelter, UnitProfession.Smelter, "z01_charcoal", ItemType.Charcoal, 3, 8, new[] { M(ItemType.Log, 2) }),
            new IndustryRecipe(IndustryRecipeType.IronIngot, BuildingType.Smelter, UnitProfession.Smelter, "z02_ironworking", ItemType.IronIngot, 1, 10, new[] { M(ItemType.IronOre, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.CopperIngot, BuildingType.Smelter, UnitProfession.Smelter, "z06_nonferrous", ItemType.CopperIngot, 1, 8, new[] { M(ItemType.CopperOre, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.TinIngot, BuildingType.Smelter, UnitProfession.Smelter, "z06_nonferrous", ItemType.TinIngot, 1, 8, new[] { M(ItemType.TinOre, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.BronzeIngot, BuildingType.Smelter, UnitProfession.Smelter, "z07_bronze", ItemType.BronzeIngot, 2, 10, new[] { M(ItemType.CopperIngot, 2), M(ItemType.TinIngot, 1), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.GoldIngot, BuildingType.Smelter, UnitProfession.Smelter, "z06_nonferrous", ItemType.GoldIngot, 1, 12, new[] { M(ItemType.GoldOre, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.SilverIngot, BuildingType.Smelter, UnitProfession.Smelter, "z06_nonferrous", ItemType.SilverIngot, 1, 10, new[] { M(ItemType.SilverOre, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.IronTools, BuildingType.Smithy, UnitProfession.Blacksmith, "z02_ironworking", ItemType.IronTools, 1, 8, new[] { M(ItemType.IronIngot, 1), M(ItemType.Plank, 1), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.BronzeTools, BuildingType.Smithy, UnitProfession.Blacksmith, "z07_bronze", ItemType.BronzeTools, 1, 7, new[] { M(ItemType.BronzeIngot, 1), M(ItemType.Plank, 1) }),
            new IndustryRecipe(IndustryRecipeType.Sword, BuildingType.Smithy, UnitProfession.Blacksmith, "d03_iron_arms", ItemType.Sword, 1, 12, new[] { M(ItemType.IronIngot, 2), M(ItemType.Charcoal, 1), M(ItemType.Leather, 1) }),
            new IndustryRecipe(IndustryRecipeType.Spear, BuildingType.Smithy, UnitProfession.Blacksmith, "d03_iron_arms", ItemType.Spear, 1, 9, new[] { M(ItemType.IronIngot, 1), M(ItemType.Plank, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.Armor, BuildingType.Smithy, UnitProfession.Blacksmith, "d03_iron_arms", ItemType.Armor, 1, 16, new[] { M(ItemType.IronIngot, 3), M(ItemType.Leather, 2), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.Leather, BuildingType.Tannery, UnitProfession.Tanner, "m03_tanning", ItemType.Leather, 2, 8, new[] { M(ItemType.Log, 1) }, new[] { A(AgriculturalProduct.Hide, 2) }),
            new IndustryRecipe(IndustryRecipeType.LinenCloth, BuildingType.Weaver, UnitProfession.Weaver, "t02_linen", ItemType.LinenCloth, 2, 7, Array.Empty<ItemAmount>(), new[] { A(AgriculturalProduct.Fiber, 3) }),
            new IndustryRecipe(IndustryRecipeType.LeatherClothes, BuildingType.Tailor, UnitProfession.Tailor, "t03_leather_tailoring", ItemType.LeatherClothes, 1, 8, new[] { M(ItemType.Leather, 2) }, new[] { A(AgriculturalProduct.Fiber, 1) }),
            new IndustryRecipe(IndustryRecipeType.LinenClothes, BuildingType.Tailor, UnitProfession.Tailor, "t02_linen", ItemType.LinenClothes, 1, 6, new[] { M(ItemType.LinenCloth, 2) }),
            new IndustryRecipe(IndustryRecipeType.Furniture, BuildingType.Carpentry, UnitProfession.Carpenter, "m02_carpentry", ItemType.Furniture, 1, 9, new[] { M(ItemType.Plank, 3) }),
            new IndustryRecipe(IndustryRecipeType.Bow, BuildingType.Carpentry, UnitProfession.Carpenter, "d01_hunting_bows", ItemType.Bow, 1, 8, new[] { M(ItemType.Plank, 2), M(ItemType.LinenCloth, 1) }),
            new IndustryRecipe(IndustryRecipeType.CopperCoin, BuildingType.Mint, UnitProfession.MintWorker, "z08_coinage", ItemType.CopperCoin, 10, 6, new[] { M(ItemType.CopperIngot, 1), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.SilverCoin, BuildingType.Mint, UnitProfession.MintWorker, "z08_coinage", ItemType.SilverCoin, 10, 8, new[] { M(ItemType.SilverIngot, 1), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.GoldCoin, BuildingType.Mint, UnitProfession.MintWorker, "z08_coinage", ItemType.GoldCoin, 10, 10, new[] { M(ItemType.GoldIngot, 1), M(ItemType.Charcoal, 1) }),
            new IndustryRecipe(IndustryRecipeType.Rope, BuildingType.Weaver, UnitProfession.Weaver, "t02_linen", ItemType.Rope, 2, 6, Array.Empty<ItemAmount>(), new[] { A(AgriculturalProduct.Fiber, 3) }),
            new IndustryRecipe(IndustryRecipeType.Ballista, BuildingType.Carpentry, UnitProfession.Carpenter, "d11_ballista", ItemType.Ballista, 1, 24, new[] { M(ItemType.Plank, 6), M(ItemType.IronIngot, 2), M(ItemType.Rope, 2) }),
            new IndustryRecipe(IndustryRecipeType.Catapult, BuildingType.Carpentry, UnitProfession.Carpenter, "d12_catapult", ItemType.Catapult, 1, 30, new[] { M(ItemType.Plank, 8), M(ItemType.IronIngot, 3), M(ItemType.Rope, 3) }),
            new IndustryRecipe(IndustryRecipeType.Trebuchet, BuildingType.Carpentry, UnitProfession.Carpenter, "d13_trebuchet", ItemType.Trebuchet, 1, 40, new[] { M(ItemType.Plank, 12), M(ItemType.IronIngot, 4), M(ItemType.Rope, 4), M(ItemType.RawStone, 4) }),
            new IndustryRecipe(IndustryRecipeType.BallistaBolts, BuildingType.Carpentry, UnitProfession.Carpenter, "d11_ballista", ItemType.BallistaBolts, 5, 8, new[] { M(ItemType.Plank, 2), M(ItemType.IronIngot, 1) }),
            new IndustryRecipe(IndustryRecipeType.StoneShot, BuildingType.Carpentry, UnitProfession.Carpenter, "d12_catapult", ItemType.StoneShot, 4, 7, new[] { M(ItemType.RawStone, 2) })
        };
        public static IndustryRecipe Get(IndustryRecipeType id) { int i = (int)id - 1; return i >= 0 && i < All.Length ? All[i] : null; }
        public static string Name(IndustryRecipeType id) { IndustryRecipe r = Get(id); return r == null ? "Нет заказа" : ItemName(r.Output); }
        public static bool IsWorkshop(BuildingType type) { return type >= BuildingType.Smelter && type <= BuildingType.Mint; }
        public static bool IsTool(ItemType item) { return item == ItemType.BronzeTools || item == ItemType.IronTools; }
        public static bool IsClothes(ItemType item) { return item == ItemType.LeatherClothes || item == ItemType.LinenClothes; }
        public static bool IsCoin(ItemType item) { return item == ItemType.CopperCoin || item == ItemType.SilverCoin || item == ItemType.GoldCoin; }
        public static float ToolMultiplier(ItemType item) { return item == ItemType.IronTools ? 1.35f : item == ItemType.BronzeTools ? 1.2f : 1; }
        public static float ClothingFatigueMultiplier(ItemType item) { return item == ItemType.LeatherClothes ? .85f : item == ItemType.LinenClothes ? .90f : 1; }
        public static int FurnitureCapacity(BuildingData b) { return b.Type == BuildingType.House ? 2 : b.Type == BuildingType.Hall ? 1 : 0; }
        public static string RequiredTechnology(BuildingType type)
        { switch (type) { case BuildingType.Smelter: return "z01_charcoal"; case BuildingType.Smithy: return "z02_ironworking"; case BuildingType.Tannery: return "m03_tanning"; case BuildingType.Weaver: case BuildingType.Tailor: return "t02_linen"; case BuildingType.Carpentry: return "m02_carpentry"; case BuildingType.Mint: return "z08_coinage"; default: return ""; } }
        public static string BuildingName(BuildingType type)
        { switch (type) { case BuildingType.Smelter: return "Плавильня"; case BuildingType.Smithy: return "Кузница"; case BuildingType.Tannery: return "Кожевня"; case BuildingType.Weaver: return "Ткацкая мастерская"; case BuildingType.Tailor: return "Портняжная мастерская"; case BuildingType.Carpentry: return "Столярная мастерская"; case BuildingType.Mint: return "Монетный двор"; default: return type.ToString(); } }
        private static readonly string[] Names = { "Древесный уголь", "Железный слиток", "Медный слиток", "Оловянный слиток", "Бронзовый слиток", "Золотой слиток", "Серебряный слиток", "Железные инструменты", "Бронзовые инструменты", "Меч", "Копьё", "Железная броня", "Выделанная кожа", "Льняная ткань", "Кожаная одежда", "Льняная одежда", "Мебель", "Лук", "Медные монеты", "Серебряные монеты", "Золотые монеты", "Верёвка", "Комплект баллисты", "Комплект катапульты", "Комплект требушета", "Болты баллисты", "Каменные ядра" };
        public static string ItemName(ItemType item) { int i = (int)item - 18; return i >= 0 && i < Names.Length ? Names[i] : item.ToString(); }
        public static float TargetStock(ItemType item, int population)
        {
            if (item == ItemType.Ballista || item == ItemType.Catapult || item == ItemType.Trebuchet) return 1;
            if (item == ItemType.BallistaBolts || item == ItemType.StoneShot) return 12;
            if (IsCoin(item)) return Math.Max(20, population * 2);
            if (IsTool(item) || IsClothes(item)) return Math.Max(2, population * .25f);
            if (item == ItemType.Furniture) return Math.Max(2, population * .2f);
            if (item == ItemType.Sword || item == ItemType.Spear || item == ItemType.Armor || item == ItemType.Bow) return Math.Max(2, population * .12f);
            return item == ItemType.Charcoal ? 8 : 6;
        }
        public static bool HasLocalInputs(BuildingData b, IndustryRecipe recipe)
        {
            if (b == null || recipe == null || b.Inventory == null || b.AgriculturalStock == null) return false;
            float materialConsumed = 0;
            foreach (ItemAmount input in recipe.Inputs) { if ((int)input.Item >= b.Inventory.Length || b.Inventory[(int)input.Item] + .00001f < input.Amount) return false; materialConsumed += input.Amount; }
            foreach (AgriculturalAmount input in recipe.Agriculture) if ((int)input.Product >= b.AgriculturalStock.Length || b.AgriculturalStock[(int)input.Product] + .00001f < input.Amount) return false;
            return StorageCapacity.FreeMaterials(b) + materialConsumed + .00001f >= recipe.OutputAmount;
        }
        public static bool IsWorking(WorldState world, BuildingData b)
        {
            if (world == null || b == null || b.Health <= 0 || b.IndustryWorkerId == 0 || !BuildingAccess.IsUsable(world,b)) return false;
            IndustryRecipe recipe = Get(b.ActiveIndustryRecipe); UnitData u = world.GetUnit(b.IndustryWorkerId);
            if (u == null || u.Health <= 0 || u.Age < 16 || u.Task != UnitTask.Produce || u.TargetBuildingId != b.Id || u.PathCursor < u.Path.Count || (world.BuildingAccessVersion != 0 && u.InsideBuildingId != b.Id) || recipe == null || !world.HasTechnology(b.SettlementId, recipe.Technology) || !HasLocalInputs(b, recipe)) return false;
            float dx = u.X - b.X - .5f, dy = u.Y - b.Y - .5f;
            return dx * dx + dy * dy < 2 && SimulationEngine.CarriedLoad(u) <= .0001f;
        }
        public static string WaitingReason(WorldState world, BuildingData b)
        {
            if (b == null || !IsWorkshop(b.Type)) return "Не производственная мастерская";
            if (!BuildingAccess.IsUsable(world,b)) return "Постройка или вход недоступны";
            IndustryRecipe recipe = Get(b.ActiveIndustryRecipe);
            if (recipe == null) return "Нет заказа: проверяются спрос, сырьё и исследования";
            if (!world.HasTechnology(b.SettlementId, recipe.Technology)) return "Нужно исследование: " + recipe.Technology;
            if (b.IndustryWorkerId == 0) return "Ожидает свободного взрослого работника";
            if (!HasLocalInputs(b, recipe)) return "Доставка сырья или освобождение места";
            return IsWorking(world, b) ? "Производство" : "Работник идёт к мастерской";
        }
    }
}
