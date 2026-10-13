using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public static class RulerPresentation
    {
        public static bool IsRuler(WorldState world, UnitData unit)
        {
            if (world == null || unit == null || unit.Health <= 0 || unit.IsChild || !CitizenSprites.IsCivilized(unit.Species) || unit.InsideBuildingId != 0 || unit.ShipId != 0) return false;
            SettlementData city = world.GetSettlement(unit.SettlementId);
            return city != null && city.RulerId == unit.Id && city.Government != GovernmentType.Unset;
        }
        public static void Draw(QuadBatch batch, UnitData unit, Vector2 head, float footY)
        {
            float pixel = OriginalCitizenAtlas.RaceScale(unit.Species) / OriginalCitizenAtlas.PixelsPerUnit;
            batch.SetFootDepth(footY, WorldDepth.Front + 1);
            // The lower band overlaps the top hair row; three peaks form a crown, not a floating marker.
            float y = head.y - pixel;
            Color edge = PixelArt.Hex("79502e"), gold = PixelArt.Hex("d6ad4c"), light = PixelArt.Hex("f1d887");
            batch.Sprite(head.x, y, 11 * pixel, 3 * pixel, 12, edge);
            batch.Sprite(head.x, y + pixel, 9 * pixel, 2 * pixel, 12, gold);
            for (int spike = -1; spike <= 1; spike++)
            {
                float x = head.x + spike * 4 * pixel, height = (spike == 0 ? 5 : 4) * pixel;
                batch.Sprite(x, y + pixel, 3 * pixel, height, 12, edge);
                batch.Sprite(x, y + 2 * pixel, pixel, height - pixel, 12, light);
            }
            batch.Sprite(head.x, y + pixel, pixel, pixel, 12, PixelArt.Hex("af514e"));
            batch.Sprite(head.x, y, 9 * pixel, pixel, 12, gold);
        }
    }
}
