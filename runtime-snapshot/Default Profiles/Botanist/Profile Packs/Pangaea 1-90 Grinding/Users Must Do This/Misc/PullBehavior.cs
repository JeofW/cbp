// Behavior originally contributed by Wigglez.
//
// LICENSE:
// This work is licensed under the
//     Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
// also known as CC-BY-NC-SA.  To view a copy of this license, visit
//      http://creativecommons.org/licenses/by-nc-sa/3.0/
// or send a letter to
//      Creative Commons // 171 Second Street, Suite 300 // San Francisco, California, 94105, USA.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using CommonBehaviors.Actions;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Profiles;
using Styx.CommonBot.Routines;
using Styx.Pathing;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Action = Styx.TreeSharp.Action;

namespace Styx.Bot.Quest_Behaviors {
    [CustomBehaviorFileName(@"Misc\PullBehavior")]
    public class PullBehavior : CustomForcedBehavior {

        // ===========================================================
        // Constants
        // ===========================================================

        // ===========================================================
        // Fields
        // ===========================================================

        // ===========================================================
        // Constructors
        // ===========================================================

        public PullBehavior(Dictionary<string, string> args)
            : base(args) {
            try {
                MobId = GetAttributeAsNullable("MobId", true, ConstrainAs.MobId, null) ?? 0;
            } catch(Exception except) {
                // Maintenance problems occur for a number of reasons.  The primary two are...
                // * Changes were made to the behavior, and boundary conditions weren't properly tested.
                // * The Honorbuddy core was changed, and the behavior wasn't adjusted for the new changes.
                // In any case, we pinpoint the source of the problem area here, and hopefully it
                // can be quickly resolved.
                LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message + "\nFROM HERE:\n" + except.StackTrace + "\n");
                IsAttributeProblem = true;
            }
        }

        // ===========================================================
        // Getter & Setter
        // ===========================================================

        // Attributes
        public static int MobId { get; set; }

        // Overrides
        public static Composite Root { get; set; }

        public static bool IsDisposed { get; set; }

        public static bool IsBehaviorDone {
            get {
                return Enemy.IsDead;
            }
        }

        public static WoWUnit Enemy {
            get { return ObjectManager.GetObjectsOfType<WoWUnit>().Where(u => u != null && u.IsValid && u.Entry == MobId && u.IsAlive).OrderBy(u => u.Distance).FirstOrDefault(); }
        }

        // ===========================================================
        // Methods for/from SuperClass/Interfaces
        // ===========================================================

        public override void OnStart() {
            // This reports problems, and stops BT processing if there was a problem with attributes...
            // We had to defer this action, as the 'profile line number' is not available during the element's
            // constructor call.
            OnStart_HandleAttributeProblem();

            IsDisposed = false;

            BotEvents.OnBotStopped += BotEvents_OnBotStopped;
        }

        public override void OnFinished() {
            if(!IsDisposed) {
                BotEvents.OnBotStopped -= BotEvents_OnBotStopped;

                MobId = 0;

                GC.SuppressFinalize(this);

                base.OnFinished();
            }

            IsDisposed = true;
        }

        public override bool IsDone {
            get {
                return IsBehaviorDone;
            }
        }

        protected override Composite CreateBehavior() {
            return Root ?? (Root =
                new Decorator(ret => !IsBehaviorDone,
                    new PrioritySelector(
                        new Decorator(ret => StyxWoW.Me.CurrentTarget != Enemy,
                            new Sequence(
                                new Action(ret => Enemy.Target()),
                                new ActionAlwaysFail()
                            )
                        ),
                        PullRoutine()
                    )
                )
            );
        }

        // ===========================================================
        // Methods
        // ===========================================================

        public static void CustomNormalLog(string message, params object[] args) {
            Logging.Write(Colors.DeepSkyBlue, "[PullBehavior]: " + message, args);
        }

        public static void CustomDiagnosticLog(string message, params object[] args) {
            Logging.WriteDiagnostic(Colors.DeepSkyBlue, "[PullBehavior]: " + message, args);
        }

        public static Composite PullRoutine() {
            return new Decorator(ctx => !StyxWoW.Me.IsFlying && !StyxWoW.Me.IsActuallyInCombat,
                new PrioritySelector(
                    new Decorator(ctx => StyxWoW.Me.CurrentTarget == null || !StyxWoW.Me.CurrentTarget.Attackable || StyxWoW.Me.CurrentTarget.IsPlayer || StyxWoW.Me.CurrentTarget.TaggedByOther,
                        new ActionAlwaysFail()
                    ),
                    new Decorator(ctx => StyxWoW.Me.CurrentTarget != null && StyxWoW.Me.CurrentTarget.Attackable && !StyxWoW.Me.CurrentTarget.IsPlayer && !StyxWoW.Me.CurrentTarget.TaggedByOther,
                        new Sequence(
                            RoutineManager.Current.PullBehavior,
                            new ActionAlwaysFail()
                        )
                    )
                )
            );
        }

        // ===========================================================
        // Inner and Anonymous Classes
        // ===========================================================

        private void BotEvents_OnBotStopped(EventArgs args) {
            OnFinished();
        }
    }
}
