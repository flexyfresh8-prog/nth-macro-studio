using System;
using System.Collections.Generic;

namespace NthMacroStudio.Core
{
    /// <summary>
    /// Macro model. Serialized with camelCase naming (MacroStore.Json) so the
    /// JSON shape matches the UI schema exactly:
    /// { id, name, description, tags, hotkey, speed, loops, createdAt, updatedAt, actions[] }
    /// action: { id, kind:"key|click|move|delay", tMs, key, mods[], button, x, y, delayMs, durationMs }
    /// </summary>
    public class Macro
    {
        public string Id { get; set; } = "m_" + Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New Macro";
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string Hotkey { get; set; } = "";
        public double Speed { get; set; } = 1;
        public int Loops { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public List<MacroAction> Actions { get; set; } = new();
    }

    public class MacroAction
    {
        public string Id { get; set; } = "a_" + Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "delay";
        public double TMs { get; set; }
        public string Key { get; set; } = "";
        public List<string> Mods { get; set; } = new();
        public string Button { get; set; } = "left";
        public int X { get; set; }
        public int Y { get; set; }
        public double DelayMs { get; set; }
        public double DurationMs { get; set; }
    }
}
