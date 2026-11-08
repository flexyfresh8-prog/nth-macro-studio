using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NthMacroStudio.Core;
using NthMacroStudio.Input;

namespace NthMacroStudio.Playback
{
    public class ProgressInfo
    {
        public string MacroId { get; set; } = "";
        public int Index { get; set; }
        public int Total { get; set; }
        public int Loop { get; set; }
        public int Loops { get; set; }        // 0 = infinite
        public double ElapsedMs { get; set; }
        public MacroAction? Action { get; set; }
        public string Status { get; set; } = "running";   // running | paused | done | stopped
    }

    /// <summary>
    /// Replays a recorded macro on a background thread with cancellation,
    /// pause/resume, speed scaling and loop counts. Emergency stop cancels
    /// instantly and releases any physically-held key.
    /// </summary>
    public static class MacroPlayer
    {
        public static bool IsPlaying { get; private set; }
        public static event Action<ProgressInfo>? Progress;

        private static CancellationTokenSource? _cts;
        private static readonly ManualResetEventSlim PauseGate = new(initialState: true);
        private static volatile bool _paused;
        private static ushort _lastSentVk;

        public static void Start(Macro macro, int loops, double speed)
        {
            if (IsPlaying) Stop("user");
            if (macro.Actions.Count == 0) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _paused = false;
            PauseGate.Set();
            _lastSentVk = 0;
            IsPlaying = true;

            int effectiveLoops = loops <= 0 ? int.MaxValue : Math.Max(1, loops);
            double effectiveSpeed = speed <= 0 ? 1 : speed;
            var watch = Stopwatch.StartNew();

            Task.Run(() =>
            {
                try
                {
                    for (int loop = 1; loop <= effectiveLoops; loop++)
                    {
                        for (int i = 0; i < macro.Actions.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            WaitWhilePaused(token);
                            token.ThrowIfCancellationRequested();

                            var action = macro.Actions[i];
                            DelaySlices(Math.Max(0, action.DelayMs) / effectiveSpeed, token);

                            switch (action.Kind)
                            {
                                case "key":
                                {
                                    ushort vk = InputInjector.VkMap(action.Key);
                                    var modVks = new System.Collections.Generic.List<ushort>();
                                    foreach (var mod in action.Mods)
                                    {
                                        ushort mv = InputInjector.VkMap(mod);
                                        if (mv != 0 && mv != vk) modVks.Add(mv);
                                    }
                                    foreach (ushort mv in modVks) InputInjector.SendKey(mv, false);
                                    if (vk != 0)
                                    {
                                        _lastSentVk = vk;
                                        InputInjector.SendKey(vk, false);
                                        if (action.DurationMs > 40) DelaySlices(action.DurationMs / effectiveSpeed, token);
                                        InputInjector.SendKey(vk, true);
                                        _lastSentVk = 0;
                                    }
                                    for (int k = modVks.Count - 1; k >= 0; k--) InputInjector.SendKey(modVks[k], true);
                                    break;
                                }
                                case "click":
                                    InputInjector.SendMouseButton(action.Button, false, action.X, action.Y);
                                    DelaySlices(Math.Max(30, action.DurationMs) / effectiveSpeed, token);
                                    InputInjector.SendMouseButton(action.Button, true, action.X, action.Y);
                                    break;
                                case "move":
                                    InputInjector.MoveTo(action.X, action.Y);
                                    break;
                            }

                            Fire(new ProgressInfo
                            {
                                MacroId = macro.Id,
                                Index = i,
                                Total = macro.Actions.Count,
                                Loop = loop,
                                Loops = effectiveLoops == int.MaxValue ? 0 : effectiveLoops,
                                ElapsedMs = watch.Elapsed.TotalMilliseconds,
                                Action = action,
                                Status = "running"
                            });
                        }
                    }

                    Fire(new ProgressInfo
                    {
                        MacroId = macro.Id,
                        Index = macro.Actions.Count - 1,
                        Total = macro.Actions.Count,
                        Loop = effectiveLoops,
                        Loops = effectiveLoops == int.MaxValue ? 0 : effectiveLoops,
                        ElapsedMs = watch.Elapsed.TotalMilliseconds,
                        Action = null,
                        Status = "done"
                    });
                }
                catch (OperationCanceledException)
                {
                    Fire(new ProgressInfo
                    {
                        MacroId = macro.Id,
                        Index = 0,
                        Total = macro.Actions.Count,
                        Loop = 0,
                        Loops = 0,
                        ElapsedMs = watch.Elapsed.TotalMilliseconds,
                        Action = null,
                        Status = "stopped"
                    });
                }
                finally
                {
                    IsPlaying = false;
                    _paused = false;
                    PauseGate.Set();
                }
            }, token);
        }

        public static void Pause()
        {
            if (!IsPlaying || _paused) return;
            _paused = true;
            PauseGate.Reset();
            FireStatus("paused");
        }

        public static void Resume()
        {
            if (!IsPlaying || !_paused) return;
            _paused = false;
            PauseGate.Set();
            FireStatus("running");
        }

        public static void Stop(string reason)
        {
            if (!IsPlaying) return;
            _cts?.Cancel();
            PauseGate.Set();
        }

        /// <summary>Instantly cancels playback and releases any held key.</summary>
        public static void EmergencyStop()
        {
            if (IsPlaying && _lastSentVk != 0)
            {
                try { InputInjector.SendKey(_lastSentVk, true); } catch { /* best effort */ }
            }
            _paused = false;
            PauseGate.Set();
            _cts?.Cancel();
        }

        private static void FireStatus(string status)
        {
            Fire(new ProgressInfo { Status = status });
        }

        private static void WaitWhilePaused(CancellationToken token)
        {
            while (_paused && !token.IsCancellationRequested)
            {
                PauseGate.Wait(80);
            }
        }

        private static void DelaySlices(double ms, CancellationToken token)
        {
            if (ms <= 0) return;
            double slept = 0;
            while (slept < ms)
            {
                token.ThrowIfCancellationRequested();
                WaitWhilePaused(token);
                int slice = (int)Math.Min(15, ms - slept);
                Thread.Sleep(slice);
                slept += slice;
            }
        }

        private static void Fire(ProgressInfo info) => Progress?.Invoke(info);
    }
}
