using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NthMacroStudio.Core;
using NthMacroStudio.Hotkeys;
using NthMacroStudio.Interop;
using NthMacroStudio.Monitoring;
using NthMacroStudio.Playback;

namespace NthMacroStudio
{
    /// <summary>
    /// Main window: a dark native shell hosting the WebView2 premium UI.
    /// All macro logic lives in native services reached through Bridge.
    /// </summary>
    public class AppWindow : Form
    {
        private readonly WebView2 _webView = new()
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(11, 14, 20)
        };

        public AppWindow()
        {
            Text = "NTH Macro Studio";
            ClientSize = new System.Drawing.Size(850, 520);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = System.Drawing.Color.FromArgb(10, 10, 10);
            Icon = LoadAppIcon();
            Controls.Add(_webView);
            Shown += OnShownAsync;
            FormClosing += OnFormClosing;
        }

        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch
            {
                // Fall back to the generic application icon.
            }
            return System.Drawing.SystemIcons.Application;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int enabled = 1;
            _ = NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        }

        private async void OnShownAsync(object? sender, EventArgs e)
        {
            try
            {
                // Pre-flight: is the Evergreen WebView2 Runtime actually installed?
                // If it is missing, CreateAsync fails with the unhelpful
                // "Value does not fall within the expected range" — catch that
                // BEFORE creating the environment and offer a one-click fix.
                string runtimeVersion;
                try
                {
                    runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                }
                catch (Exception runtimeEx)
                {
                    StartupLog($"WebView2 Runtime NOT found: {runtimeEx.GetType().Name}: {runtimeEx.Message}");
                    ShowRuntimeMissingDialog(runtimeEx);
                    Close();
                    return;
                }
                StartupLog($"WebView2 Runtime {runtimeVersion}");

                CoreWebView2Environment environment;
                try
                {
                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NTHMacroStudio", "WebView2");
                    Directory.CreateDirectory(userDataFolder);
                    environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                }
                catch (Exception envEx)
                {
                    // The custom user-data folder can be blocked by policy or
                    // antivirus on some machines — retry with loader defaults.
                    StartupLog($"Custom user-data folder failed ({envEx.Message}); retrying with defaults.");
                    environment = await CoreWebView2Environment.CreateAsync();
                }

                await _webView.EnsureCoreWebView2Async(environment);

                var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "nth.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
                _webView.CoreWebView2.Navigate("https://nth.local/index.html");
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
            }
            catch (Exception ex)
            {
                StartupLog($"FATAL: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show(this,
                    "NTH Macro Studio could not start its embedded browser (Microsoft Edge WebView2).\n\n" +
                    "Fix: install the free Microsoft Edge WebView2 Runtime, then start the app again.\n" +
                    "Direct installer: https://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n" +
                    "Technical details: " + ex.GetType().Name + ": " + ex.Message + "\n\n" +
                    "Details were logged to:\n" + StartupLogPath(),
                    "NTH Macro Studio — startup error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            RegisterHotkeys();
            TrayService.Instance.Init(this);
            SystemMonitor.Instance.Start();
        }

        private void ShowRuntimeMissingDialog(Exception detail)
        {
            var choice = MessageBox.Show(this,
                "NTH Macro Studio needs the free Microsoft Edge WebView2 Runtime, which is not installed on this PC.\n\n" +
                "One-time fix (about 2 minutes):\n" +
                "  1. Click OK — the official Microsoft installer page opens.\n" +
                "  2. Run MicrosoftEdgeWebview2Setup.exe.\n" +
                "  3. Start NTH Macro Studio again.\n\n" +
                "(Windows 10/11 normally includes it; cleanup tools and Windows N editions often remove it.)\n\n" +
                "Technical details: " + detail.Message,
                "WebView2 Runtime required",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (choice == DialogResult.OK)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
                }
                catch
                {
                    // Browser launch blocked — the URL is in the dialog text anyway.
                }
            }
        }

        internal static string StartupLogPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NTHMacroStudio", "logs", "startup.log");

        private static void StartupLog(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath())!);
                File.AppendAllText(StartupLogPath(),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never break startup.
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
            => Bridge.OnWebMessage(this, e);

        private void RegisterHotkeys()
        {
            foreach (var kv in Settings.Current.Hotkeys)
            {
                if (!GlobalHotkeyManager.Register(kv.Key, kv.Value, Handle))
                {
                    // Another app owns this combo; keep the rest alive.
                    System.Diagnostics.Debug.WriteLine($"Hotkey '{kv.Value}' for '{kv.Key}' could not be registered.");
                }
            }
        }

        public void PostToUi(string type, object payload)
        {
            if (IsDisposed || _webView.CoreWebView2 == null) return;
            try
            {
                BeginInvoke(() =>
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(new { type, payload }, MacroStore.Json);
                    _webView.CoreWebView2.PostWebMessageAsString(json);
                });
            }
            catch
            {
                // Window shutting down; drop the message.
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                GlobalHotkeyManager.Dispatch(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && Settings.Current.CloseToTray)
            {
                Hide();
                TrayService.Instance.ShowBalloon(
                    "NTH Macro Studio keeps running in the tray.\nGlobal hotkeys stay active. Double-click the icon to restore.");
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (MacroPlayer.IsPlaying) MacroPlayer.EmergencyStop();
            GlobalHotkeyManager.UnregisterAll();
            SystemMonitor.Instance.Stop();
            TrayService.Instance.Dispose();
        }
    }
}
