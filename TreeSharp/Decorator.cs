using System;
using System.Collections.Generic;

namespace TreeSharp
{
    public class Decorator : GroupComposite
    {
        public Decorator(Composite decorated, CanRunDecoratorDelegate? func)
            : base(decorated)
        {
            Runner = func;
        }

        public Decorator(CanRunDecoratorDelegate func, Composite decorated)
            : this(decorated, func)
        {
        }

        public Decorator(Composite child)
            : this(child, null)
        {
        }

        public Decorator()
            : base()
        {
        }

        protected CanRunDecoratorDelegate? Runner { get; private set; }

        public Composite DecoratedChild { get { return Children[0]; } }

        protected virtual bool CanRun(object context)
        {
            // HB 4.3.4: Check Runner delegate if set, otherwise return true
            return this.Runner == null || this.Runner(context);
        }

        // An explicitly cooperating child may revalidate this exact owner's
        // predicate after a callback, without ticking or restarting the parent.
        // Other decorators retain their existing scheduling behavior.
        internal bool IsExecutionAllowedFor(Composite child, object context)
        {
            if (Children.Count != 1 || !ReferenceEquals(DecoratedChild, child))
                return false;
            bool allowed = Runner != null ? Runner(context) : CanRun(context);
            return allowed && Children.Count == 1
                && ReferenceEquals(DecoratedChild, child);
        }

        public override void Start(object context)
        {
            if (Children.Count != 1)
            {
                throw new ApplicationException("Decorators must have only one child.");
            }
            base.Start(context);
        }

        protected override IEnumerable<RunStatus> Execute(object context)
        {
            if (Runner != null)
            {
                if (!Runner(context))
                {
                    yield return RunStatus.Failure;
                    yield break;
                }
            }
            else if (!CanRun(context))
            {
                yield return RunStatus.Failure;
                yield break;
            }

            // Check for null decorated child
            if (DecoratedChild == null)
            {
                yield return RunStatus.Failure;
                yield break;
            }

            DecoratedChild.Start(context);
            while (DecoratedChild.Tick(context) == RunStatus.Running)
            {
                yield return RunStatus.Running;
            }

            DecoratedChild.Stop(context);
            if (DecoratedChild.LastStatus == RunStatus.Failure)
            {
                yield return RunStatus.Failure;
                yield break;
            }

            yield return RunStatus.Success;
            yield break;
        }
    }
}
