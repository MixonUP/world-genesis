namespace WorldGenesis.Simulation
{
    /// <summary>Building foundations require actual dry land; a walkable ford or bridge is not land.</summary>
    public static class PlacementRules
    {
        public static bool IsDryGround(WorldState world,int x,int y)
        {
            if(world==null||!world.InBounds(x,y))return false;
            int cell=world.Index(x,y);TileData tile=world.Cells[cell];
            return tile.Terrain!=TerrainType.DeepWater&&tile.Terrain!=TerrainType.ShallowWater&&tile.Terrain!=TerrainType.Mountain
                &&tile.WaterDepth<=.00001f&&(world.Infrastructure==null||world.Infrastructure[cell].BridgeLevel==0)&&world.IsTerrainWalkable(x,y);
        }
        public static bool CanPlaceBuilding(WorldState world,BuildingType type,int x,int y)
        {return (int)type>=0&&(int)type<=26&&IsDryGround(world,x,y)&&world.Cells[world.Index(x,y)].BuildingId==0&&world.Cells[world.Index(x,y)].Fire<=0;}
    }
}
