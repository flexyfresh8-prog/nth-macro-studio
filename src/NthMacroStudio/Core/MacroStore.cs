using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NthMacroStudio.Core
{
    /// <summary>
    /// Persists macros as one JSON file per macro under
    /// %AppData%\NTH Macro Studio\Macros. Fully local — no network.
    /// </summary>
    public static class MacroStore
    {
        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            WriteIndented = true
        };

        public static string Dir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NTH Macro Studio", "Macros");

        private static readonly object _lock = new();

        static MacroStore() => Directory.CreateDirectory(Dir);

        public static List<Macro> LoadAll()
        {
            var list = new List<Macro>();
            lock (_lock)
            {
                if (!Directory.Exists(Dir)) return list;
                foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
                {
                    try
                    {
                        var macro = JsonSerializer.Deserialize<Macro>(File.ReadAllText(file), Json);
                        if (macro != null && !string.IsNullOrEmpty(macro.Id)) list.Add(macro);
                    }
                    catch
                    {
                        // Corrupt file: skip it rather than failing the whole library.
                    }
                }
            }
            list.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            return list;
        }

        public static Macro Save(Macro macro)
        {
            macro.UpdatedAt = DateTime.UtcNow;
            lock (_lock)
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, macro.Id + ".json"), JsonSerializer.Serialize(macro, Json));
            }
            return macro;
        }

        public static void Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_lock)
            {
                var path = Path.Combine(Dir, id + ".json");
                if (File.Exists(path)) File.Delete(path);
            }
        }

        public static string Export(string id)
        {
            lock (_lock) return File.ReadAllText(Path.Combine(Dir, id + ".json"));
        }

        public static Macro Import(string json)
        {
            var macro = JsonSerializer.Deserialize<Macro>(json, Json)
                        ?? throw new InvalidDataException("Invalid macro JSON.");
            foreach (var existing in LoadAll())
            {
                if (existing.Id == macro.Id)
                {
                    macro.Id = "m_" + Guid.NewGuid().ToString("N");
                    break;
                }
            }
            return Save(macro);
        }
    }
}
