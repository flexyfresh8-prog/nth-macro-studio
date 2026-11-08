using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NthMacroStudio.Core;
using NthMacroStudio.Interop;

namespace NthMacroStudio.Recording
{
    public class RecorderOptions
    {
        public bool CaptureKeyboard = true;
        public bool CaptureMouseClicks = true;
        public bool CaptureMouseMoves = false;
        public int MouseMoveSampleMs = 40;
        public string DelayMode = "auto";
    }

    public class RawEvent
    {
        public string Kind = "";          // keyDown | keyUp | mouseDown | mouseUp | mouseMove
        public string Key = "";
        public List<string> Mods = new();
        public string Button = "left";
        public int X;
        public int Y;
        public double TMs;
    }

    /// <summary>
    /// System-wide input recorder built on low-level Windows hooks.
    /// Hooks are installed ONLY while the user explicitly arms a recording
    /// session and are removed the moment it stops.
    /// </summary>
    public static class HookRecorder
    {
        public static bool IsRecording { get; private set; }
        public static event Action<RawEvent>? EventCaptured;

        private static IntPtr _kbdHook = IntPtr.Zero;
        private static IntPtr _mouseHook = IntPtr.Zero;
        private static NativeMethods.LowLevelHookProc? _kbdProc;
        private static NativeMethods.LowLevelHookProc? _mouseProc;
        private static readonly Stopwatch _watch = new();
        private static readonly List<RawEvent> _events = new();
        private static readonly object _lock = new();
        private static bool _paused;
        private static DateTime _pauseStart;
        private static double _pauseOffsetMs;
        private static RecorderOptions _options = new();

        public static void Start(RecorderOptions options)
        {
            lock (_lock)
            {
                if (IsRecording) return;
                _options = options ?? new RecorderOptions();
                _events.Clear();
                _paused = false;
                _pauseOffsetMs = 0;

                _kbdProc = KeyboardHookProc;
                _mouseProc = MouseHookProc;
                var module = NativeMethods.GetModuleHandleW(null);
                _kbdHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _kbdProc, module, 0);
                _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
                _watch.Restart();
                IsRecording = true;
            }
        }

        public static void Pause()
        {
            lock (_lock)
            {
                if (!IsRecording || _paused) return;
                _paused = true;
                _pauseStart = DateTime.UtcNow;
            }
        }

        public static void Resume()
        {
            lock (_lock)
            {
                if (!IsRecording || !_paused) return;
                _pauseOffsetMs += (DateTime.UtcNow - _pauseStart).TotalMilliseconds;
                _paused = false;
            }
        }

        public static Macro Stop()
        {
            lock (_lock)
            {
                if (!IsRecording) return new Macro { Name = "Empty Recording", Description = "Nothing was captured." };
                if (_kbdHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_kbdHook); _kbdHook = IntPtr.Zero; }
                if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                IsRecording = false;
                return Build();
            }
        }

        private static double Now() => _watch.Elapsed.TotalMilliseconds - _pauseOffsetMs;

        private static IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_paused)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool up = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
                if (down || up)
                {
                    Add(new RawEvent
                    {
                        Kind = down ? "keyDown" : "keyUp",
                        Key = VkName(info.vkCode),
                        Mods = ModifierSnapshot(),
                        TMs = Now()
                    });
                }
            }
            return NativeMethods.CallNextHookEx(_kbdHook, nCode, wParam, lParam);
        }

        private static IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_paused)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                string? button = msg switch
                {
                    NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP => "left",
                    NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP => "right",
                    NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP => "middle",
                    _ => null
                };

                if (button != null)
                {
                    bool down = msg == NativeMethods.WM_LBUTTONDOWN
                             || msg == NativeMethods.WM_RBUTTONDOWN
                             || msg == NativeMethods.WM_MBUTTONDOWN;
                    Add(new RawEvent
                    {
                        Kind = down ? "mouseDown" : "mouseUp",
                        Button = button,
                        X = info.pt.X,
                        Y = info.pt.Y,
                        TMs = Now()
                    });
                }
                else if (msg == NativeMethods.WM_MOUSEMOVE && _options.CaptureMouseMoves)
                {
                    double t = Now();
                    var last = _events.Count > 0 ? _events[^1] : null;
                    if (last == null || last.Kind != "mouseMove" || t - last.TMs >= _options.MouseMoveSampleMs)
                    {
                        Add(new RawEvent { Kind = "mouseMove", X = info.pt.X, Y = info.pt.Y, TMs = t });
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private static void Add(RawEvent ev)
        {
            bool overflow = false;
            lock (_lock)
            {
                _events.Add(ev);
                overflow = _events.Count >= 20000;
            }
            EventCaptured?.Invoke(ev);
            if (overflow)
            {
                // Hard safety cap: stop recording automatically.
                Stop();
            }
        }

        private static List<string> ModifierSnapshot()
        {
            var mods = new List<string>();
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0) mods.Add("ctrl");
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0) mods.Add("alt");
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0) mods.Add("shift");
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0) mods.Add("win");
            return mods;
        }

        private static string VkName(uint vk)
        {
            if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
            if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
            if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
            return vk switch
            {
                0x0D => "enter", 0x09 => "tab", 0x1B => "esc", 0x20 => "space",
                0x08 => "backspace", 0x2E => "delete", 0x2D => "insert",
                0x24 => "home", 0x23 => "end", 0x21 => "pageup", 0x22 => "pagedown",
                0x25 => "left", 0x26 => "up", 0x27 => "right", 0x28 => "down",
                0x11 => "ctrl", 0x12 => "alt", 0x10 => "shift",
                0x5B or 0x5C => "win",
                0x14 => "capslock", 0x2C => "printscreen",
                0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".",
                0xBF => "/", 0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
                _ => FallbackName(vk)
            };
        }

        private static string FallbackName(uint vk)
        {
            uint scan = NativeMethods.MapVirtualKeyW(vk, 0);
            var sb = new StringBuilder(64);
            if (NativeMethods.GetKeyNameTextW((int)(scan << 16), sb, 64) > 0)
            {
                return sb.ToString().ToLowerInvariant();
            }
            return "key" + vk;
        }

        /// <summary>
        /// Converts the chronological raw event stream into a Macro:
        /// down/up pairs become key/click actions with their gap as delay,
        /// sampled moves become move actions, long silences become delays.
        /// </summary>
        private static Macro Build()
        {
            var macro = new Macro
            {
                Name = "Recording " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Description = "Recorded with NTH Macro Studio system-wide input hooks.",
                Tags = { "recording" }
            };
            bool autoTrim = _options.DelayMode == "auto";
            double prevT = 0;
            int i = 0;
            bool first = true;

            while (i < _events.Count)
            {
                var e = _events[i];
                double gap = Math.Max(0, e.TMs - prevT);
                if (autoTrim) gap = Math.Min(gap, 5000);

                if (e.Kind == "keyDown")
                {
                    int j = i + 1;
                    while (j < _events.Count && !(_events[j].Kind == "keyUp" && _events[j].Key == e.Key)) j++;
                    var up = j < _events.Count ? _events[j] : null;
                    macro.Actions.Add(new MacroAction
                    {
                        Kind = "key",
                        Key = e.Key,
                        Mods = e.Mods,
                        DelayMs = first ? 0 : gap,
                        DurationMs = up != null ? Math.Clamp(up.TMs - e.TMs, 30, 300) : 45,
                        TMs = e.TMs
                    });
                    prevT = up != null ? up.TMs : e.TMs;
                    i = up != null ? j + 1 : i + 1;
                    first = false;
                }
                else if (e.Kind == "mouseDown")
                {
                    int j = i + 1;
                    while (j < _events.Count && _events[j].Kind != "mouseUp") j++;
                    var up = j < _events.Count ? _events[j] : null;
                    macro.Actions.Add(new MacroAction
                    {
                        Kind = "click",
                        Button = e.Button,
                        X = e.X,
                        Y = e.Y,
                        DelayMs = first ? 0 : gap,
                        DurationMs = up != null ? Math.Clamp(up.TMs - e.TMs, 30, 300) : 55,
                        TMs = e.TMs
                    });
                    prevT = up != null ? up.TMs : e.TMs;
                    i = up != null ? j + 1 : i + 1;
                    first = false;
                }
                else if (e.Kind == "mouseMove")
                {
                    macro.Actions.Add(new MacroAction
                    {
                        Kind = "move",
                        X = e.X,
                        Y = e.Y,
                        DelayMs = first ? 0 : gap,
                        TMs = e.TMs
                    });
                    prevT = e.TMs;
                    i++;
                    first = false;
                }
                else
                {
                    i++;
                }
            }
            return macro;
        }
    }
}
