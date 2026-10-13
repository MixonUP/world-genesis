using System;

namespace WorldGenesis.Simulation
{
    public static class MarketCatalog
    {
        public static readonly ItemType[] Coins = { ItemType.CopperCoin, ItemType.SilverCoin, ItemType.GoldCoin };
        public static readonly int[] Denominations = { 1, 10, 100 };
        public static bool Tradable(ItemType item) { return (int)item > 0 && (int)item < ResourceCatalog.ItemCount && !IndustryCatalog.IsCoin(item); }
        public static bool Tradable(AgriculturalProduct item) { return (int)item > 0 && (int)item < FishingCatalog.AgriculturalProductCount; }
        public static int CoinValue(ItemType item) { return item == ItemType.CopperCoin ? 1 : item == ItemType.SilverCoin ? 10 : item == ItemType.GoldCoin ? 100 : 0; }
        public static string Name(ItemType item)
        {
            switch (item) { case ItemType.Log: return "брёвна"; case ItemType.Plank: return "доски"; case ItemType.RawStone: return "камень"; case ItemType.IronOre: return "железная руда"; case ItemType.CopperOre: return "медная руда"; case ItemType.GoldOre: return "золотая руда"; case ItemType.SilverOre: return "серебряная руда"; case ItemType.TinOre: return "оловянная руда"; case ItemType.Firewood: return "дрова"; case ItemType.Clay: return "глина"; case ItemType.Salt: return "соль"; default: return IndustryCatalog.ItemName(item); }
        }
        public static float BasePrice(ItemType item)
        {
            switch (item)
            {
                case ItemType.Log: case ItemType.RawStone: case ItemType.Clay: case ItemType.Sand: return 1;
                case ItemType.Plank: case ItemType.Firewood: case ItemType.Coal: case ItemType.Charcoal: case ItemType.Limestone: return 2;
                case ItemType.IronOre: case ItemType.CopperOre: case ItemType.TinOre: case ItemType.Brick: case ItemType.StoneBlock: case ItemType.StoneTile: return 3;
                case ItemType.Salt: case ItemType.SilverOre: return 6;
                case ItemType.GoldOre: return 12;
                case ItemType.IronIngot: case ItemType.CopperIngot: case ItemType.TinIngot: case ItemType.Leather: case ItemType.LinenCloth: return 8;
                case ItemType.BronzeIngot: case ItemType.SilverIngot: return 12;
                case ItemType.GoldIngot: return 25;
                case ItemType.Rope: return 5;
                case ItemType.IronTools: case ItemType.BronzeTools: case ItemType.LinenClothes: case ItemType.LeatherClothes: return 18;
                case ItemType.Furniture: case ItemType.Bow: case ItemType.Spear: return 15;
                case ItemType.Sword: return 25;
                case ItemType.Armor: return 40;
                case ItemType.Ballista: return 70;
                case ItemType.Catapult: return 95;
                case ItemType.Trebuchet: return 140;
                case ItemType.BallistaBolts: case ItemType.StoneShot: return 4;
                default: return 0;
            }
        }
        public static float BasePrice(AgriculturalProduct item)
        {
            switch (item)
            {
                case AgriculturalProduct.RiverPearl: return 60;
                case AgriculturalProduct.AncientScale: return 120;
                case AgriculturalProduct.RareHorn: case AgriculturalProduct.RarePelt: return 80;
                case AgriculturalProduct.Hide: case AgriculturalProduct.DelicacyFish: return 6;
                case AgriculturalProduct.Ale: return 4;
                default: return Tradable(item) ? 2 : 0;
            }
        }
        // Full coins only. Tender and change are separate real stocks, never an abstract wallet.
        public static bool PlanPayment(int[] buyer, int[] seller, int price, out int[] net)
        {
            net = new int[3]; if (buyer == null || seller == null || buyer.Length != 3 || seller.Length != 3 || price <= 0 || price > 10000000) return false;
            long wealth = 0; for (int i = 0; i < 3; i++) { if (buyer[i] < 0 || seller[i] < 0) return false; wealth += (long)buyer[i] * Denominations[i]; }
            if (wealth < price) return false;
            int remaining = price;
            for (int i = 2; i >= 0; i--) { net[i] = Math.Min(buyer[i], remaining / Denominations[i]); remaining -= net[i] * Denominations[i]; }
            if (remaining == 0) return true;
            // Overpay using one higher denomination, then return existing smaller coins.
            for (int high = 1; high < 3; high++)
            {
                if (buyer[high] <= net[high] || Denominations[high] <= remaining) continue;
                int change = Denominations[high] - remaining; int[] candidate = (int[])net.Clone(); candidate[high]++;
                for (int i = high - 1; i >= 0; i--) { int give = Math.Min(seller[i] + candidate[i], change / Denominations[i]); candidate[i] -= give; change -= give * Denominations[i]; }
                if (change == 0) { net = candidate; return true; }
            }
            return false;
        }
    }
}
