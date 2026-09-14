using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.ExceptionServices;

namespace TreeSharp
{
    public abstract class Composite : IEquatable<Composite>
    {
        protected static readonly object Locker = new object();
        
        public Guid Guid { get; private set; }
        public Composite? Parent { get; set; }
        public virtual IList<Composite> Children { get { return new List<Composite>(); } }
        public RunStatus? LastStatus { get; set; }
        protected ContextChangeHandler? ContextChanger { get; set; }
        protected Stack<CleanupHandler> CleanupHandlers { get; set; }

        private IEnumerator<RunStatus>? _enumerator;

        [DebuggerStepThrough]
        protected Composite()
        {
            Guid = Guid.NewGuid();
            CleanupHandlers = new Stack<CleanupHandler>();
        }

        public virtual RunStatus Tick(object context)
        {
            if (LastStatus.HasValue && LastStatus.Value != RunStatus.Running)
                return LastStatus.Value;

            if (_enumerator == null)
            {
                // Defensive: if Stop() was called between Start() and Tick(),
                // return Failure instead of throwing (prevents NRE spam on shutdown)
                LastStatus = RunStatus.Failure;
                return RunStatus.Failure;
            }

            try
            {
                if (!_enumerator.MoveNext())
                    throw new ApplicationException(
                        $"Iterator completed unexpectedly in {GetType().FullName} - did Execute() yield all status values correctly?");

                LastStatus = _enumerator.Current;
            }
            catch (Exception signal) when (signal is ThreadInterruptedException
                || signal is OperationCanceledException)
            {
                // A run-stop signal is not a failed branch: letting the parent select
                // a fallback can issue another movement command during shutdown.
                StopAfterStopSignal(context);
                throw;
            }
            catch (Exception ex)
            {
                Styx.Helpers.Logging.WriteException(ex);
                LastStatus = RunStatus.Failure;
                Stop(context);
                return LastStatus.Value;
            }

            if (LastStatus.Value != RunStatus.Running)
            {
                Stop(context);
            }

            return LastStatus ?? RunStatus.Failure;
        }

        public RunStatus Run(object context)
        {
            return Tick(context);
        }

        /// <summary>
        /// HonorBuddy-compatible property: whether this composite is currently running.
        /// Many third-party bots reference `IsRunning` as a property.
        /// </summary>
        public bool IsRunning
        {
            get { return LastStatus.HasValue && LastStatus.Value == RunStatus.Running; }
        }

        protected virtual IEnumerable<RunStatus> Execute(object context)
        {
            yield return RunStatus.Failure;
        }

        public virtual void Start(object context)
        {
            LastStatus = null;
            
            try
            {
                var executeResult = Execute(context);
                if (executeResult == null)
                    throw new ApplicationException($"Execute() returned null for {GetType().Name}");
                
                _enumerator = executeResult.GetEnumerator();
                
                if (_enumerator == null)
                    throw new ApplicationException($"GetEnumerator() returned null for {GetType().Name}");
            }
            catch (Exception signal) when (signal is ThreadInterruptedException
                || signal is OperationCanceledException)
            {
                // A run-stop signal is not a failed branch: letting the parent select
                // a fallback can issue another movement command during shutdown.
                StopAfterStopSignal(context);
                throw;
            }
            catch (Exception ex)
            {
                Styx.Helpers.Logging.WriteException(ex);
                throw;
            }
        }

        private void StopAfterStopSignal(object context)
        {
            LastStatus = RunStatus.Failure;
            try
            {
                Stop(context);
            }
            catch (Exception cleanupError)
            {
                // Preserve the original stop signal even when cleanup/log subscribers
                // fail. An ordinary cleanup error cannot authorize parent fallback.
                try { Styx.Helpers.Logging.WriteException(cleanupError); }
                catch { }
            }
        }

        public virtual void Stop(object context)
        {
            // Detach before user cleanup, including reentrant/throwing cleanup.
            // A later Start must never reuse the stopped iterator.
            var enumerator = _enumerator;
            _enumerator = null;
            ExceptionDispatchInfo? failure = null;
            try { Cleanup(); }
            catch (Exception error) { PreserveCleanupFailure(ref failure, error); }
            finally
            {
                if (LastStatus == RunStatus.Running)
                    LastStatus = RunStatus.Failure;
                // Iterator finally blocks are cleanup owners too. Drain them even
                // after handler failure without replacing the first stop signal.
                try { enumerator?.Dispose(); }
                catch (Exception error) { PreserveCleanupFailure(ref failure, error); }
            }
            failure?.Throw();
        }

        internal static void PreserveCleanupFailure(ref ExceptionDispatchInfo? failure, Exception error)
        {
            // Cancellation and interruption are equally authoritative stop signals.
            // Preserve the first signal; ordinary failures cannot mask either one.
            bool stop = error is ThreadInterruptedException || error is OperationCanceledException;
            if (failure == null || (stop && failure.SourceException is not ThreadInterruptedException
                && failure.SourceException is not OperationCanceledException))
                failure = ExceptionDispatchInfo.Capture(error);
        }

        [DebuggerStepThrough]
        protected void Cleanup()
        {
            if (CleanupHandlers.Count == 0)
                return;
                
            ExceptionDispatchInfo? failure = null;
            while (CleanupHandlers.Count != 0)
            {
                try { CleanupHandlers.Pop().Dispose(); }
                catch (Exception error)
                {
                    // Drain all owned cleanup before propagating the original error.
                    // Stop signals take precedence over ordinary cleanup failures.
                    PreserveCleanupFailure(ref failure, error);
                }
            }
            failure?.Throw();
        }

        public bool Equals(Composite? other)
        {
            return other != null && Guid == other.Guid;
        }
        
        protected abstract class CleanupHandler : IDisposable
        {
            private bool _isDisposed;

            [DebuggerStepThrough]
            protected CleanupHandler(Composite owner, object context)
            {
                Owner = owner;
                Context = context;
            }

            public Composite Owner { get; private set; }
            public object Context { get; private set; }
            public bool IsDisposed => _isDisposed;

            [DebuggerStepThrough]
            public void Dispose()
            {
                if (IsDisposed)
                    return;
                    
                _isDisposed = true;
                DoCleanup(Context);
            }

            [DebuggerStepThrough]
            protected abstract void DoCleanup(object context);
        }
    }
}
