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
            ClientSize = new System.Drawing.Size(1440, 900);
            MinimumSize = new System.Drawing.Size(1180, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = System.Drawing.Color.FromArgb(11, 14, 20);
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
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NTHMacroStudio", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
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
                MessageBox.Show(this,
                    "The Microsoft Edge WebView2 Runtime is required to run NTH Macro Studio.\n\n" +
                    "Download it from:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                    "Technical details: " + ex.Message,
                    "NTH Macro Studio",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            RegisterHotkeys();
            TrayService.Instance.Init(this);
            SystemMonitor.Instance.Start();
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
