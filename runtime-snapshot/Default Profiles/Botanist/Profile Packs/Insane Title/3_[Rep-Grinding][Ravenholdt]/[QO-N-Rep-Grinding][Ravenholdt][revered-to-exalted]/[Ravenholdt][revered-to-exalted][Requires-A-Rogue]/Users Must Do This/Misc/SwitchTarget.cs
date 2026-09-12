// CustomBehavior by Wigglez
// If my target's ID is not XXX, move to XYZ

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Profiles;
using Styx.Pathing;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Action = Styx.TreeSharp.Action;

namespace Styx.Bot.Quest_Behaviors {
    [CustomBehaviorFileName(@"Misc\SwitchTarget")]
    public class SwitchTarget : CustomForcedBehavior {
        // ===========================================================
        // Constants
        // ===========================================================

        // ===========================================================
        // Fields
        // ===========================================================

        // ===========================================================
        // Constructors
        // ===========================================================

        public SwitchTarget(Dictionary<string, string> args)
            : base(args) {
            try {
                MobId = GetAttributeAsNullable("MobId", true, ConstrainAs.MobId, null) ?? 0;
                Destination = GetAttributeAsNullable("", true, ConstrainAs.WoWPointNonEmpty, null) ?? WoWPoint.Empty;
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
        public static WoWPoint Destination { get; set; }

        // Overrides
        public static Composite Root { get; set; }

        public static bool IsDisposed { get; set; }

        public static bool IsBehaviorDone { get; set; }

        // My shit
        public static WoWUnit Enemy {
            get { return ObjectManager.GetObjectsOfType<WoWUnit>().Where(u => u != null && u.Entry == MobId && u.IsAlive).OrderBy(u => u.Distance).FirstOrDefault(); }
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
                Destination = WoWPoint.Empty;

                IsBehaviorDone = false;

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
                    new Action(ret => CheckTarget())
                )
            );
        }

        // ===========================================================
        // Methods
        // ===========================================================

        public static void CustomNormalLog(string message, params object[] args) {
            Logging.Write(Colors.DeepSkyBlue, "[SwitchTarget]: " + message, args);
        }

        public static void CustomDiagnosticLog(string message, params object[] args) {
            Logging.WriteDiagnostic(Colors.DeepSkyBlue, "[SwitchTarget]: " + message, args);
        }

        public static void CheckTarget() {
            if(StyxWoW.Me.CurrentTarget != Enemy) {
                CustomNormalLog("Current target is correct, returning");
                IsBehaviorDone = true;

                return;
            }

            CustomNormalLog("Moving to destination");
            Navigator.MoveTo(Destination);

            if(StyxWoW.Me.Location.Distance(Destination) >= 5) {
                return;
            }

            CustomNormalLog("Reached destination, behavior done");
            IsBehaviorDone = true;
        }

        // ===========================================================
        // Inner and Anonymous Classes
        // ===========================================================

        private void BotEvents_OnBotStopped(EventArgs args) {
            OnFinished();
        }
    }
}

