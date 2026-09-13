
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace TreeSharp
{
    public abstract class GroupComposite : Composite
    {
        protected GroupComposite(params Composite[] children)
        {
            Children = new List<Composite>(children);
            foreach (Composite composite in Children)
            {
                if (composite != null)
                {
                    composite.Parent = this;
                }
            }
        }

        public new List<Composite> Children { get; set; }

        public Composite? Selection { get; protected set; }

        public override void Start(object context)
        {
            CleanupHandlers.Push(new ChildrenCleanupHandler(this, context));
            base.Start(context);
        }

        protected class ChildrenCleanupHandler : CleanupHandler
        {
            public ChildrenCleanupHandler(GroupComposite owner, object context) 
                : base(owner, context)
            {
            }

            protected override void DoCleanup(object context)
            {
                var owner = Owner as GroupComposite;
                if (owner?.Children == null)
                    return;
                    
                ExceptionDispatchInfo? failure = null;
                // Snapshot so one child's cleanup cannot invalidate enumeration.
                foreach (Composite child in new List<Composite>(owner.Children))
                {
                    try { child?.Stop(context); }
                    catch (Exception error)
                    {
                        if (failure == null || (error is ThreadInterruptedException
                            && failure.SourceException is not ThreadInterruptedException))
                            failure = ExceptionDispatchInfo.Capture(error);
                    }
                }
                failure?.Throw();
            }
        }

        public void AddChild(Composite child)
        {
            if (child != null)
            {
                child.Parent = this;
                Children.Add(child);
            }
        }

        public void InsertChild(int index, Composite child)
        {
            if (child != null)
            {
                child.Parent = this;
                Children.Insert(index, child);
            }
        }
    }
}