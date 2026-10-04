// QuestState.cs - Global quest state management
// Ported from HB 4.3.4

using System.Collections.Generic;
using Styx;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Bots.Quest
{
    /// <summary>
    /// Singleton managing the global quest bot state.
    /// </summary>
    public class QuestState
    {
        private long profileInitialization;
        /// <summary>
        /// Global singleton instance.
        /// </summary>
        public static readonly QuestState Instance = new QuestState();

        public QuestState()
        {
            Order = new QuestOrder.QuestOrder();
            BotEvents.Profile.OnNewProfileLoaded += OnNewProfileLoaded;
        }

        private void OnNewProfileLoaded(BotEvents.Profile.NewProfileLoadedEventArgs args)
        {
            if (args.OldProfile?.QuestOrder == args.NewProfile?.QuestOrder)
                return;
            
            InitializeFromProfile(args.NewProfile);
        }

        /// <summary>
        /// Initialize quest state from a profile.
        /// </summary>
        internal void InitializeFromProfile(Profile profile)
        {
            long initialization = ++profileInitialization;
            var order = Order;
            var previousNodes = order.Nodes;
            var nextNodes = new OrderNodeCollection(profile?.QuestOrder?.Count ?? 0);
            if (profile?.QuestOrder != null)
            {
                nextNodes.AddRange(profile.QuestOrder);
                nextNodes.IgnoreCheckpoints = profile.QuestOrder.IgnoreCheckpoints;
            }
            // Publication can precede the executor's next IsDone/Dispose pulse.
            // Retire its complete nested lifetime before replacing the order.
            order.RetireCurrentBehavior();
            bool Current() => profileInitialization == initialization && ReferenceEquals(Order, order)
                && order.CurrentBehavior == null;
            if (!Current() || !ReferenceEquals(order.Nodes, previousNodes)) return;
            order.Nodes = nextNodes;
            if (nextNodes.Count > 0)
            {
                ObjectManager.Update();
                if (Current() && ReferenceEquals(order.Nodes, nextNodes))
                    order.UpdateNodes();
            }
        }

        /// <summary>
        /// The current quest order being executed.
        /// </summary>
        public QuestOrder.QuestOrder Order { get; private set; }

        /// <summary>
        /// Current vendors for the quest area.
        /// </summary>
        public List<Vendor> CurrentVendors { get; set; }

        /// <summary>
        /// Current mailboxes for the quest area.
        /// </summary>
        public List<Mailbox> CurrentMailboxes { get; set; }

        /// <summary>
        /// Current grind area for the quest.
        /// </summary>
        public GrindArea CurrentGrindArea { get; set; }
    }
}
