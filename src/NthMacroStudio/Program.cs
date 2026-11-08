using System;
using System.Threading;
using System.Windows.Forms;

namespace NthMacroStudio
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using var mutex = new Mutex(true, "NTH_MacroStudio_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "NTH Macro Studio is already running.\n\nLook for the tray icon near the clock.",
                    "NTH Macro Studio",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new AppWindow());
            GC.KeepAlive(mutex);
        }
    }
}
