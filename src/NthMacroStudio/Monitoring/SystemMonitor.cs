using System;
using System.Diagnostics;
using System.Timers;
using NthMacroStudio.Interop;

namespace NthMacroStudio.Monitoring
{
    /// <summary>
    /// Samples CPU and RAM once per second and pushes them to the UI bridge.
    /// </summary>
    public sealed class SystemMonitor : IDisposable
    {
        public static SystemMonitor Instance { get; } = new();

        public event Action<double, double>? StatsTick;

        private readonly System.Timers.Timer _timer = new(1000) { AutoReset = true };
        private PerformanceCounter? _cpuCounter;
        private bool _primed;

        private SystemMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            }
            catch
            {
                _cpuCounter = null;   // Counters unavailable (e.g. stripped-down systems): report RAM only.
            }
            _timer.Elapsed += (_, _) => Tick();
        }

        public void Start()
        {
            _primed = false;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void Tick()
        {
            double cpu = 0;
            if (_cpuCounter != null)
            {
                try
                {
                    float value = _cpuCounter.NextValue();
                    if (!_primed)
                    {
                        _primed = true;   // first sample is always 0; skip it
                        return;
                    }
                    cpu = Math.Clamp(value, 0, 100);
                }
                catch
                {
                    cpu = 0;
                }
            }

            var memory = new NativeMethods.MEMORYSTATUSEX();
            double ram = NativeMethods.GlobalMemoryStatusEx(memory) ? memory.dwMemoryLoad : 0;
            StatsTick?.Invoke(Math.Round(cpu, 1), ram);
        }

        public void Dispose()
        {
            _timer.Dispose();
            _cpuCounter?.Dispose();
        }
    }
}
