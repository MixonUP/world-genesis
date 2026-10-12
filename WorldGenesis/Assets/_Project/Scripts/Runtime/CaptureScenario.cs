using System;
using System.IO;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Explicitly requested capture assistance, using the same power command as the player.</summary>
    public static class CaptureScenario
    {
        // Called by the capture coroutine itself so command and screenshot cannot race.
        public static bool ApplyRequested(GameBootstrap game, string imagePath)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--demo-power"), capture = Array.IndexOf(args, "--capture");
            if (index < 0 || index + 1 >= args.Length || capture < 0 || capture + 1 >= args.Length) return false;
            if (!Enum.TryParse(args[index + 1], true, out PowerType power) || (int)power < 0 || (int)power > 6) return false;
            if (game == null || game.World.Settlements.Count == 0) return false;
            SettlementData town = game.World.Settlements[0];
            float before = Health(game.World);
            int beforePopulation = game.World.Units.Count;
            game.Engine.ApplyPower(power, town.X, town.Y, 3);
            Receipt receipt = new Receipt
            {
                Assistance = "Automated production ApplyPower command at first settlement; no physical mouse input.",
                Power = power.ToString(), X = town.X, Y = town.Y, Radius = 3, Tick = game.World.Tick,
                BeforeHealth = before, AfterHealth = Health(game.World),
                BeforePopulation = beforePopulation, AfterPopulation = game.World.Units.Count
            };
            try
            {
                string path = Path.GetFullPath(imagePath) + ".scenario.json";
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            }
            catch (Exception exception) { Debug.LogError("Assisted capture receipt failed: " + exception.Message); }
            return true;
        }
        private static float Health(WorldState world) { float value = 0; foreach (UnitData unit in world.Units) value += unit.Health; return value; }
        [Serializable]
        private sealed class Receipt
        {
            public string Assistance, Power;
            public int X, Y, Radius, BeforePopulation, AfterPopulation;
            public long Tick;
            public float BeforeHealth, AfterHealth;
        }
    }
}
