using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NthMacroStudio.Core;

namespace NthMacroStudio
{
    public class AppSettings
    {
        public Dictionary<string, string> Hotkeys { get; set; } = new()
        {
            ["startStop"] = "Ctrl+Alt+S",
            ["pauseResume"] = "Ctrl+Alt+P",
            ["emergency"] = "Ctrl+Alt+X",
            ["record"] = "Ctrl+Alt+R"
        };
        public bool CloseToTray { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public string Accent { get; set; } = "violet";
        public bool ReduceMotion { get; set; } = false;
        public bool BackgroundEffects { get; set; } = true;
        public int MouseMoveSampleMs { get; set; } = 40;
        public double DefaultSpeed { get; set; } = 1;
        public int DefaultLoops { get; set; } = 1;
    }

    public static class Settings
    {
        private static readonly object _lock = new();
        private static AppSettings? _current;

        public static string Path { get; } = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NTH Macro Studio", "settings.json");

        public static AppSettings Current
        {
            get { if (_current == null) Load(); return _current!; }
        }

        public static void Load()
        {
            var defaults = new AppSettings();
            try
            {
                if (File.Exists(Path))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), MacroStore.Json);
                    if (loaded != null)
                    {
                        // Merge loaded hotkeys over defaults so new slots always exist.
                        foreach (var kv in loaded.Hotkeys) defaults.Hotkeys[kv.Key] = kv.Value;
                        loaded.Hotkeys = defaults.Hotkeys;
                        _current = loaded;
                    }
                }
            }
            catch
            {
                // Fall back to defaults on any IO/JSON error.
            }
            _current ??= defaults;
        }

        public static void Replace(AppSettings settings)
        {
            _current = settings;
            Save();
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    File.WriteAllText(Path, JsonSerializer.Serialize(Current, MacroStore.Json));
                }
                catch
                {
                    // Settings are non-critical; never crash the app over them.
                }
            }
        }
    }
}
