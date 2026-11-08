using System;
using System.Collections.Generic;
using NthMacroStudio.Interop;

namespace NthMacroStudio.Hotkeys
{
    /// <summary>
    /// Registers system-wide hotkeys with RegisterHotKey. Combos use the
    /// "Ctrl+Alt+M" format shared with the UI (parts: Ctrl, Alt, Shift, Win,
    /// plus one final key).
    /// </summary>
    public static class GlobalHotkeyManager
    {
        public static event Action<string>? SlotTriggered;

        private static readonly Dictionary<string, int> SlotIds = new();
        private static IntPtr _hwnd;

        public static bool Register(string slot, string combo, IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(combo)) return false;
            _hwnd = hwnd;
            int id = StableId(slot);
            Unregister(slot);
            ParseCombo(combo, out uint mods, out uint vk);
            if (vk == 0) return false;
            if (!NativeMethods.RegisterHotKey(hwnd, id, mods, vk)) return false;
            SlotIds[slot] = id;
            return true;
        }

        public static void Unregister(string slot)
        {
            if (SlotIds.Remove(slot, out int id) && _hwnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
        }

        public static void UnregisterAll()
        {
            if (_hwnd != IntPtr.Zero)
            {
                foreach (int id in SlotIds.Values)
                {
                    NativeMethods.UnregisterHotKey(_hwnd, id);
                }
            }
            SlotIds.Clear();
        }

        public static void Dispatch(int id)
        {
            foreach (var kv in SlotIds)
            {
                if (kv.Value == id)
                {
                    SlotTriggered?.Invoke(kv.Key);
                    return;
                }
            }
        }

        private static int StableId(string slot)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in slot) hash = hash * 31 + c;
                return hash & 0x0FFFFFFF;
            }
        }

        public static void ParseCombo(string combo, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (var raw in combo.Split('+'))
            {
                var part = raw.Trim().ToLowerInvariant();
                switch (part)
                {
                    case "ctrl" or "control": mods |= 0x2; break;   // MOD_CONTROL
                    case "alt": mods |= 0x1; break;                 // MOD_ALT
                    case "shift": mods |= 0x4; break;               // MOD_SHIFT
                    case "win" or "meta": mods |= 0x8; break;       // MOD_WIN
                    default: vk = VkFromString(part); break;
                }
            }
        }

        private static uint VkFromString(string key)
        {
            if (key.Length == 1)
            {
                char c = char.ToUpperInvariant(key[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
                return 0;
            }
            if (key.Length >= 2 && key[0] == 'f' && uint.TryParse(key.Substring(1), out uint fn) && fn is >= 1 and <= 24)
                return 0x70 + fn - 1;
            return key switch
            {
                "enter" or "return" => 0x0D,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                "space" => 0x20,
                "backspace" => 0x08,
                "delete" or "del" => 0x2E,
                "insert" or "ins" => 0x2D,
                "home" => 0x24, "end" => 0x23,
                "pageup" or "pgup" => 0x21,
                "pagedown" or "pgdn" => 0x22,
                "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
                "capslock" => 0x14,
                "printscreen" or "prtsc" => 0x2C,
                _ => 0
            };
        }
    }
}
