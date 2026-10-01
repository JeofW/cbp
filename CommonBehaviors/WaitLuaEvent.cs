using Styx.WoWInternals;
using System;
using System.Collections.Generic;
using TreeSharp;

namespace CommonBehaviors
{
    public class WaitLuaEvent : WaitContinue
    {
        private readonly string _luaEvent;
        private bool _eventFired;
        private readonly Func<bool> _beginAfterSubscription;
        private bool _beginAccepted = true;

        // Subscribe before a request that can synchronously raise its reply.
        // Existing constructors retain their original behavior and deadlines.
        public WaitLuaEvent(string luaEvent, WaitGetTimeoutDelegate timeoutRetriever,
            Func<bool> beginAfterSubscription, Composite child)
            : base(timeoutRetriever, child)
        {
            _luaEvent = luaEvent;
            _beginAfterSubscription = beginAfterSubscription ?? throw new ArgumentNullException(nameof(beginAfterSubscription));
        }

        public WaitLuaEvent(string luaEvent, int timeoutSeconds, Composite child)
            : base(timeoutSeconds, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, WaitGetTimeoutDelegate timeoutRetriever, Composite child)
            : base(timeoutRetriever, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, int timeoutSeconds, CanRunDecoratorDelegate runFunc, Composite child)
            : base(timeoutSeconds, runFunc, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, TimeSpan timeout, Composite child)
            : base(timeout, child)
        {
            _luaEvent = luaEvent;
        }

        public WaitLuaEvent(string luaEvent, WaitGetTimeoutDelegate timeoutRetriever, CanRunDecoratorDelegate runFunc, Composite child)
            : base(timeoutRetriever, runFunc, child)
        {
            _luaEvent = luaEvent;
        }

        public override void Start(object context)
        {
            _eventFired = false;
            _beginAccepted = true;
            Lua.Events.AttachEvent(_luaEvent, OnLuaEvent);
            try
            {
                base.Start(context);
                _beginAccepted = _beginAfterSubscription?.Invoke() ?? true;
            }
            catch
            {
                Lua.Events.DetachEvent(_luaEvent, OnLuaEvent);
                _eventFired = false;
                throw;
            }
        }

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if (!_beginAccepted)
            {
                yield return RunStatus.Failure;
                yield break;
            }
            foreach (RunStatus status in base.Execute(context))
                yield return status;
        }

        private void OnLuaEvent(object sender, LuaEventArgs e)
        {
            _eventFired = true;
        }

        protected override bool CanRun(object context)
        {
            return _eventFired;
        }

        public override void Stop(object context)
        {
            Lua.Events.DetachEvent(_luaEvent, OnLuaEvent);
            _eventFired = false;
            base.Stop(context);
        }
    }
}
