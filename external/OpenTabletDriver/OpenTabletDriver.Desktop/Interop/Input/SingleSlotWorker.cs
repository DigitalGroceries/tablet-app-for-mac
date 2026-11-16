using System;
using System.Threading;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Tablet App for Mac V28 (#85): runs one job at a time on its own background thread. <see cref="Run"/> hands a
    /// job over and waits for it at most the given time; a job handed over while one is still running is dropped.
    /// A job that throws is reported to the error callback and the thread keeps running.
    /// </summary>
    public sealed class SingleSlotWorker
    {
        public enum Outcome { Finished, StillRunning, Dropped }

        private readonly string _name;
        private readonly Action<Exception>? _onError;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _wake = new(0);
        private Thread? _thread;
        private Action? _job;
        private ManualResetEventSlim? _done;

        public SingleSlotWorker(string name, Action<Exception>? onError = null)
        {
            _name = name;
            _onError = onError;
        }

        public bool IsBusy
        {
            get { lock (_lock) return _done != null; }
        }

        public Outcome Run(Action job, int waitMs)
        {
            ManualResetEventSlim done;
            lock (_lock)
            {
                if (_done != null) return Outcome.Dropped;
                _job = job;
                _done = done = new ManualResetEventSlim(false);
                if (_thread == null)
                {
                    _thread = new Thread(Loop) { IsBackground = true, Name = _name };
                    _thread.Start();
                }
            }
            _wake.Release();
            return done.Wait(waitMs) ? Outcome.Finished : Outcome.StillRunning;
        }

        private void Loop()
        {
            while (true)
            {
                _wake.Wait();
                Action? job;
                ManualResetEventSlim? done;
                lock (_lock)
                {
                    job = _job;
                    _job = null;
                    done = _done;
                }
                try
                {
                    job?.Invoke();
                }
                catch (Exception ex)
                {
                    try { _onError?.Invoke(ex); } catch { }
                }
                finally
                {
                    lock (_lock) _done = null;
                    done?.Set();
                }
            }
        }
    }
}
