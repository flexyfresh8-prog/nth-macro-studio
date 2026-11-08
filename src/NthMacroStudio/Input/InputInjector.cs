using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NthMacroStudio.Interop;

namespace NthMacroStudio.Input
{
    /// <summary>
    /// Injects keyboard and mouse input with SendInput — the standard,
    /// user-authorized Windows automation channel. Nothing here touches
    /// other processes: no DLL injection, no memory access, no driver calls.
    /// </summary>
    public static class InputInjector
    {
        private static readonly uint[] ExtendedKeys =
        {
            0x21, 0x22, 0x23, 0x24,            // page up/down, home, end
            0x25, 0x26, 0x27, 0x28,            // arrows
            0x2D, 0x2E,                        // insert, delete
            0x5B, 0x5C                         // L/R win
        };

        public static void SendKey(ushort vk, bool up)
        {
            uint flags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
            foreach (var ext in ExtendedKeys)
            {
                if (ext == vk) { flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY; break; }
            }
            uint scan = NativeMethods.MapVirtualKeyW(vk, 0);
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            input.u.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            _ = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        public static void MoveTo(int x, int y)
        {
            int sw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int sh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            if (sw < 2) sw = 2;
            if (sh < 2) sh = 2;
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
            input.u.mi = new NativeMethods.MOUSEINPUT
            {
                dx = (x * 65535) / (sw - 1),
                dy = (y * 65535) / (sh - 1),
                mouseData = 0,
                dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            _ = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        public static void SendMouseButton(string button, bool up, int x, int y)
        {
            MoveTo(x, y);
            uint flags = (button) switch
            {
                "right" => up ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_RIGHTDOWN,
                "middle" => up ? NativeMethods.MOUSEEVENTF_MIDDLEUP : NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
                _ => up ? NativeMethods.MOUSEEVENTF_LEFTUP : NativeMethods.MOUSEEVENTF_LEFTDOWN
            };
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
            input.u.mi = new NativeMethods.MOUSEINPUT
            {
                dx = 0, dy = 0, mouseData = 0,
                dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero
            };
            _ = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        /// <summary>Presses a key with modifier chords held, then releases in reverse order.</summary>
        public static void PressCombo(List<string> mods, ushort vk, double holdMs)
        {
            var modVks = new List<ushort>();
            foreach (var m in mods)
            {
                ushort mv = VkMap(m);
                if (mv != 0 && mv != vk) modVks.Add(mv);
            }
            foreach (ushort mv in modVks) SendKey(mv, false);
            if (vk != 0)
            {
                SendKey(vk, false);
                if (holdMs > 40) Thread.Sleep((int)Math.Min(holdMs, 3000));
                SendKey(vk, true);
            }
            for (int i = modVks.Count - 1; i >= 0; i--) SendKey(modVks[i], true);
        }

        /// <summary>Maps a friendly key name ("enter", "a", "f5", ";") to a virtual-key code.</summary>
        public static ushort VkMap(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            key = key.Trim().ToLowerInvariant();

            if (key.Length == 1)
            {
                char c = key[0];
                if (c is >= 'a' and <= 'z') return (ushort)char.ToUpperInvariant(c);
                if (c is >= '0' and <= '9') return (ushort)c;
                return c switch
                {
                    ';' => 0xBA, '=' => 0xBB, ',' => 0xBC, '-' => 0xBD, '.' => 0xBE,
                    '/' => 0xBF, '`' => 0xC0, '[' => 0xDB, '\\' => 0xDC, ']' => 0xDD, '\'' => 0xDE,
                    _ => 0
                };
            }

            if (key[0] == 'f' && key.Length >= 2 && int.TryParse(key.Substring(1), out int f) && f is >= 1 and <= 24)
                return (ushort)(0x70 + f - 1);

            return key switch
            {
                "enter" or "return" => 0x0D,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                "space" => 0x20,
                "backspace" => 0x08,
                "delete" or "del" => 0x2E,
                "insert" or "ins" => 0x2D,
                "home" => 0x24,
                "end" => 0x23,
                "pageup" or "pgup" => 0x21,
                "pagedown" or "pgdn" => 0x22,
                "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
                "ctrl" or "control" => 0x11,
                "alt" => 0x12,
                "shift" => 0x10,
                "win" or "meta" => 0x5B,
                "capslock" => 0x14,
                "printscreen" or "prtsc" => 0x2C,
                _ => 0
            };
        }
    }
}
