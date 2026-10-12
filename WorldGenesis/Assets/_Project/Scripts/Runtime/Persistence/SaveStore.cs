using System;
using System.IO;
using System.Text;
using UnityEngine;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    public static class SaveStore
    {
        private const int CurrentSchema = 1;
        private const long MaxBytes = 64 * 1024 * 1024;
        [Serializable]
        private sealed class Envelope
        {
            public int Schema;
            public string Format;
            public WorldState World;
        }
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "Saves", "world.json");
        public static bool TrySave(string path, WorldState world, out string error)
        {
            error = null;
            if (world == null) { error = "Мир отсутствует"; return false; }
            if (!world.Validate(out error)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path); string directory = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directory);
                string json = JsonUtility.ToJson(new Envelope { Schema = CurrentSchema, Format = "WorldGenesis", World = world });
                if (Encoding.UTF8.GetByteCount(json) > MaxBytes) { error = "Сохранение превышает допустимый размер"; return false; }
                string temporary = fullPath + ".tmp", backup = fullPath + ".bak";
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(json); writer.Flush(); stream.Flush(true); }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, backup, true); else File.Move(temporary, fullPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            { error = "Не удалось записать сохранение: " + exception.Message; return false; }
        }
        public static bool TryLoad(string path, out WorldState world, out string error)
        {
            world = null; error = null;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) { error = "Сохранение пока не создано"; return false; }
                if (info.Length <= 0 || info.Length > MaxBytes) { error = "Недопустимый размер сохранения"; return false; }
                Envelope envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                if (envelope == null || envelope.Schema != CurrentSchema || envelope.Format != "WorldGenesis" || envelope.World == null) { error = "Неизвестный формат или версия сохранения"; return false; }
                if (!envelope.World.Validate(out error)) { error = "Сохранение повреждено: " + error; return false; }
                world = envelope.World; return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException || exception is OverflowException)
            { error = "Не удалось прочитать сохранение: " + exception.Message; return false; }
        }
    }
}
