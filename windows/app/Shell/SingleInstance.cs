using System;
using System.Threading;

namespace SentrySix.Shell
{
    /// <summary>
    /// One app per Windows user: a named mutex; a second launch signals a
    /// named event, the first one brings its window to the front, and the
    /// second exits.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        const string MutexName = "Local\\SentrySixManager.App";
        const string ShowName = "Local\\SentrySixManager.App.Show";
        readonly Mutex mutex;
        readonly EventWaitHandle show;
        readonly bool first;
        Thread waiter;
        volatile bool disposed;

        public SingleInstance()
        {
            bool created;
            mutex = new Mutex(true, MutexName, out created);
            first = created;
            show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        }

        public bool IsFirst { get { return first; } }

        /// <summary>The second launch: asks the first to show itself.</summary>
        public void SignalFirst()
        {
            try { show.Set(); } catch (Exception) { }
        }

        /// <summary>The first launch: calls back (on a background thread) whenever another launch asks.</summary>
        public void Listen(Action onShow)
        {
            waiter = new Thread(() =>
            {
                while (!disposed)
                {
                    try
                    {
                        if (show.WaitOne(500) && !disposed) onShow();
                    }
                    catch (ObjectDisposedException) { return; }
                }
            });
            waiter.IsBackground = true;
            waiter.Name = "single-instance";
            waiter.Start();
        }

        public void Dispose()
        {
            disposed = true;
            try { if (first) mutex.ReleaseMutex(); } catch (Exception) { }
            mutex.Dispose();
        }
    }
}
