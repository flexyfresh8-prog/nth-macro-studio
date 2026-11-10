using System;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NthMacroStudio.Core;
using NthMacroStudio.Monitoring;
using NthMacroStudio.Playback;
using NthMacroStudio.Recording;

namespace NthMacroStudio
{
    /// <summary>
    /// JavaScript <-> C# message bridge. Wire format (both directions):
    ///   { "type": "domain:action", "payload": { ... } }
    /// See docs/BRIDGE.md for the full protocol.
    /// </summary>
    public static class Bridge
    {
        private static AppWindow? _win;
        private static bool _wired;

        public static void OnWebMessage(AppWindow win, CoreWebView2WebMessageReceivedEventArgs e)
        {
            _win = win;
            Wire();
            try
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                JsonElement payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                Handle(type, payload);
            }
            catch (Exception ex)
            {
                Post("app:error", new { message = ex.Message });
            }
        }

        private static void Wire()
        {
            if (_wired) return;
            _wired = true;

            HookRecorder.EventCaptured += ev => Post("record:event", new { @event = ev });
            MacroPlayer.Progress += pi => Post("play:progress", pi);
            SystemMonitor.Instance.StatsTick += (cpu, ram) => Post("sys:stats", new { cpu, ram });
            Hotkeys.GlobalHotkeyManager.SlotTriggered += OnSlot;
        }

        private static void Handle(string type, JsonElement payload)
        {
            switch (type)
            {
                case "app:hello":
                    Post("app:ready", new
                    {
                        version = VersionText(),
                        platform = "windows",
                        macrosPath = MacroStore.Dir,
                        preview = false
                    });
                    break;

                case "macro:list":
                    Post("macro:list", new { macros = MacroStore.LoadAll() });
                    break;

                case "macro:save":
                {
                    var macro = payload.Deserialize<Macro>(MacroStore.Json) ?? new Macro();
                    MacroStore.Save(macro);
                    Post("macro:saved", new { macro });
                    break;
                }

                case "macro:delete":
                {
                    MacroStore.Delete(Str(payload, "id", ""));
                    Post("macro:deleted", new { id = Str(payload, "id", "") });
                    break;
                }

                case "macro:export":
                {
                    var id = Str(payload, "id", "");
                    Post("macro:export", new { id, json = MacroStore.Export(id) });
                    break;
                }

                case "macro:import":
                {
                    var json = Str(payload, "json", "{}");
                    Post("macro:imported", new { macro = MacroStore.Import(json) });
                    break;
                }

                case "record:start":
                {
                    HookRecorder.Start(new RecorderOptions
                    {
                        CaptureKeyboard = Bool(payload, "captureKeyboard", true),
                        CaptureMouseClicks = Bool(payload, "captureMouseClicks", true),
                        CaptureMouseMoves = Bool(payload, "captureMouseMoves", false),
                        MouseMoveSampleMs = Int(payload, "mouseMoveSampleMs", Settings.Current.MouseMoveSampleMs),
                        DelayMode = Str(payload, "delayMode", "auto")
                    });
                    Post("record:started", new { });
                    break;
                }

                case "record:pause":
                    HookRecorder.Pause();
                    Post("record:paused", new { });
                    break;

                case "record:resume":
                    HookRecorder.Resume();
                    Post("record:resumed", new { });
                    break;

                case "record:stop":
                {
                    var macro = HookRecorder.Stop();
                    MacroStore.Save(macro);
                    Post("record:stopped", new { macro, saved = true });
                    break;
                }

                case "play:start":
                {
                    var macroId = Str(payload, "macroId", "");
                    var loops = Int(payload, "loops", 1);
                    var speed = Double(payload, "speed", 1);
                    foreach (var macro in MacroStore.LoadAll())
                    {
                        if (macro.Id == macroId)
                        {
                            MacroPlayer.Start(macro, loops, speed);
                            break;
                        }
                    }
                    Post("play:started", new { });
                    break;
                }

                case "play:pause":
                    MacroPlayer.Pause();
                    Post("play:paused", new { });
                    break;

                case "play:resume":
                    MacroPlayer.Resume();
                    Post("play:resumed", new { });
                    break;

                case "play:stop":
                    MacroPlayer.Stop("user");
                    Post("play:stopped", new { reason = "user" });
                    break;

                case "play:emergency":
                    MacroPlayer.EmergencyStop();
                    Post("play:stopped", new { reason = "emergency" });
                    break;

                case "app:openFolder":
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = MacroStore.Dir,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Post("app:error", new { message = "Could not open macros folder: " + ex.Message });
                    }
                    break;
                }

                case "settings:save":
                {
                    var settings = payload.Deserialize<AppSettings>(MacroStore.Json);
                    if (settings != null) Settings.Replace(settings);
                    Post("settings:saved", new { settings = Settings.Current });
                    break;
                }

                case "hotkey:set":
                {
                    var slot = Str(payload, "slot", "");
                    var combo = Str(payload, "combo", "");
                    var ok = _win != null && Hotkeys.GlobalHotkeyManager.Register(slot, combo, _win.Handle);
                    if (ok)
                    {
                        Settings.Current.Hotkeys[slot] = combo;
                        Settings.Save();
                    }
                    Post("hotkey:set:result", new { slot, ok, combo });
                    break;
                }
            }
        }

        private static void OnSlot(string slot)
        {
            switch (slot)
            {
                case "emergency":
                    MacroPlayer.EmergencyStop();
                    Post("play:stopped", new { reason = "emergency" });
                    break;

                case "record":
                    if (HookRecorder.IsRecording)
                    {
                        var macro = HookRecorder.Stop();
                        MacroStore.Save(macro);
                        Post("record:stopped", new { macro, saved = true });
                    }
                    else
                    {
                        HookRecorder.Start(new RecorderOptions
                        {
                            MouseMoveSampleMs = Settings.Current.MouseMoveSampleMs
                        });
                        Post("record:started", new { });
                    }
                    break;

                default:
                    // startStop / pauseResume: the UI owns the selected macro.
                    Post("hotkey:triggered", new { slot });
                    break;
            }
        }

        private static void Post(string type, object payload)
        {
            var win = _win;
            if (win == null) return;
            win.PostToUi(type, payload);
        }

        private static string VersionText()
        {
            var v = typeof(Bridge).Assembly.GetName().Version;
            return v == null ? "1.0.0" : v.ToString();
        }

        private static bool Bool(JsonElement e, string name, bool fallback)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True ? true
             : e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v2) && v2.ValueKind == JsonValueKind.False ? false
             : fallback;

        private static int Int(JsonElement e, string name, int fallback)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

        private static double Double(JsonElement e, string name, double fallback)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;

        private static string Str(JsonElement e, string name, string fallback)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    }
}
