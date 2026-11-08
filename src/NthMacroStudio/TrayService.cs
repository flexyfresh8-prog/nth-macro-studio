using System;
using System.Windows.Forms;
using NthMacroStudio.Playback;

namespace NthMacroStudio
{
    /// <summary>
    /// System tray presence so global hotkeys keep working while the
    /// window is hidden, plus a one-click emergency stop.
    /// </summary>
    public sealed class TrayService
    {
        public static TrayService Instance { get; } = new();

        private NotifyIcon? _icon;
        private AppWindow? _window;

        public void Init(AppWindow window)
        {
            _window = window;
            if (_icon != null) return;
            _icon = new NotifyIcon
            {
                Text = "NTH Macro Studio",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => ShowWindow());
            menu.Items.Add("Emergency Stop", null, (_, _) => MacroPlayer.EmergencyStop());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                MacroPlayer.EmergencyStop();
                _icon.Visible = false;
                Application.Exit();
            });
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ShowWindow();
        }

        public void ShowWindow()
        {
            if (_window == null) return;
            _window.Show();
            _window.WindowState = FormWindowState.Normal;
            _window.Activate();
        }

        public void ShowBalloon(string message)
        {
            if (_icon != null)
            {
                _icon.ShowBalloonTip(1500, "NTH Macro Studio", message, ToolTipIcon.Info);
            }
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
