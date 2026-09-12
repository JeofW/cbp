// Behavior originally contributed by mastahg, heavily modified by mjj23, rewritten by Wigglez & AknA

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Profiles;
using Styx.CommonBot.Routines;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using Action = Styx.TreeSharp.Action;

// ReSharper disable once CheckNamespace
namespace Styx.Bot.Quest_Behaviors {
    [CustomBehaviorFileName(@"Misc\TargetOfInterest")]
    public class TargetOfInterest : CustomForcedBehavior {

        // ===========================================================
        // Constants
        // ===========================================================

        // ===========================================================
        // Fields
        // ===========================================================

        // Private variables for internal state
        private bool _isBehaviorDone;
        private bool _isDisposed;
        private Composite _root;

        private WoWUnit _killUnit;

        // ===========================================================
        // Constructors
        // ===========================================================

        /// <summary>
        /// This is only used when you get a quest that Says, Kill anything x times. Or on the chance the wowhead ID is wrong
        /// ##Syntax##
        /// AddIdN: Ids of the adds involved in the fight
        /// BossId: Id of the "Boss", or immune target that is being switched from
        /// X,Y,Z: The general location where theese objects can be found
        /// AuraId: Aura that causes us to switch from the boss to the adds
        /// </summary>
        public TargetOfInterest(Dictionary<string, string> args)
            : base(args) {
            try {
                AuraId = GetAttributeAsNullable("AuraId", false, ConstrainAs.MobId, new[] { "NpcId", "NpcID" }) ?? 0;
                BossId = GetAttributeAsNullable("BossId", false, ConstrainAs.MobId, new[] { "NpcId", "NpcID" }) ?? 0;
                KillOrder = GetNumberedAttributesAsArray("KillOrder", 0, ConstrainAs.MobId, new[] { "NpcId" });
            } catch(Exception except) {
                LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message + "\nFROM HERE:\n" + except.StackTrace + "\n");
                IsAttributeProblem = true;
            }
        }

        // ===========================================================
        // Getter & Setter
        // ===========================================================

        public int AuraId { get; set; }
        public int BossId { get; set; }
        public int[] KillOrder { get; set; }

        public static LocalPlayer Me {
            get { return (StyxWoW.Me); }
        }

        public WoWUnit PriorityUnit {
            get {
                foreach(var entry in KillOrder) {
                    _killUnit = ObjectManager.GetObjectsOfTypeFast<WoWUnit>().Where(u => u.Entry == entry && u.IsAlive).OrderBy(u => u.Distance).FirstOrDefault();
                    if(_killUnit != null) {
                        return _killUnit;
                    }
                }

                return null;
            }
        }

        public WoWUnit Boss {
            get {
                return (ObjectManager.GetObjectsOfTypeFast<WoWUnit>().FirstOrDefault(u => u.Entry == BossId));
            }
        }

        // ===========================================================
        // Methods for/from SuperClass/Interfaces
        // ===========================================================

        public override void OnStart() {
            OnStart_HandleAttributeProblem();

            if(!IsDone) { return; }

            BotEvents.OnBotStopped += BotEvents_OnBotStop;
        }

        public override void OnFinished() {
            if(!_isDisposed) {
                BotEvents.OnBotStopped -= BotEvents_OnBotStop;

                _isBehaviorDone = false;

                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                GC.SuppressFinalize(this);

                base.OnFinished();
            }

            _isDisposed = true;
        }

        public override bool IsDone { get { return (_isBehaviorDone); } }

        protected override Composite CreateBehavior() {
            return _root ?? (_root =
                new Decorator(ret => !_isBehaviorDone,
                    new PrioritySelector(
                        new Decorator(ret => Boss.IsDead || Boss == null,
                            new Sequence(
                                new Action(r => CustomNormalLog("Behavior finished.")),
                                new Action(r => _isBehaviorDone = true)
                            )
                        ),
                        new Decorator(r => Me.CurrentTarget != PriorityUnit || Me.CurrentTarget.IsDead,
                            new Sequence(
                                new Action(r => PriorityUnit.Target()),
                                UseCombatRoutine
                            )
                        ),
                        new Decorator(r => PriorityUnit == null && Boss != null,
                            new Decorator(r => Me.CurrentTarget != Boss,
                                new Sequence(
                                    new Action(r => Boss.Target()),
                                    UseCombatRoutine
                                )
                            )
                        )
                    )
                )
            );
        }

        // ===========================================================
        // Methods
        // ===========================================================

        public void CustomNormalLog(string message, params object[] args) {
            Logging.Write(Colors.DeepSkyBlue, "[TargetOfInterest]: " + message, args);
        }

        public static void CustomDiagnosticLog(string message, params object[] args) {
            Logging.WriteDiagnostic(Colors.DeepSkyBlue, "[TargetOfInterest]: " + message, args);
        }

        public Composite UseCombatRoutine {
            get {
                return new PrioritySelector(
                    RoutineManager.Current.HealBehavior,
                    RoutineManager.Current.CombatBuffBehavior,
                    RoutineManager.Current.CombatBehavior
                );
            }
        }

        // ===========================================================
        // Inner and Anonymous Classes
        // ===========================================================

        private void BotEvents_OnBotStop(EventArgs args) { OnFinished(); }
    }
}