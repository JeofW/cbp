// Behavior originally contributed by Bobby53.
//
// DOCUMENTATION:
//     http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Custom_Behavior:_ForcedDismount
//
// QUICK DOX:
//      Dismounts a toon from a mount (or Druid flying form).
//      If flying, the behavior will attempt to land before dismounting.
//
//  Parameters (required, then optional--both listed alphabetically):
//      QuestId [Default:none]:
//      QuestCompleteRequirement [Default:NotComplete]:
//      QuestInLogRequirement [Default:InLog]:
//
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Buddy.Coroutines;
using CommonBehaviors.Actions;
using Styx.CommonBot.Coroutines;

using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;
using Action = TreeSharp.Action;


namespace Styx.Bot.Quest_Behaviors
{
    public class ForcedDismount : CustomForcedBehavior
    {
        public ForcedDismount(Dictionary<string, string> args)
            : base(args)
        {
            try
            {
                QuestId = GetAttributeAsNullable<int>("QuestId", false, ConstrainAs.QuestId(this), null) ?? 0;
                QuestRequirementComplete = GetAttributeAsNullable<QuestCompleteRequirement>("QuestCompleteRequirement", false, null, null) ?? QuestCompleteRequirement.NotComplete;
                QuestRequirementInLog = GetAttributeAsNullable<QuestInLogRequirement>("QuestInLogRequirement", false, null, null) ?? QuestInLogRequirement.InLog;
            }
            catch (Exception except)
            {
                LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message
                                    + "\nFROM HERE:\n"
                                    + except.StackTrace + "\n");
                IsAttributeProblem = true;
            }
        }


        // Attributes provided by caller
        public int QuestId { get; private set; }
        public QuestCompleteRequirement QuestRequirementComplete { get; private set; }
        public QuestInLogRequirement QuestRequirementInLog { get; private set; }

        // Private variables for internal state
        private bool _isBehaviorDone;
        private bool _isDisposed;
        private Composite _root;

        // Private properties
        private LocalPlayer Me { get { return (ObjectManager.Me); } }

        // DON'T EDIT THESE--they are auto-populated by Subversion
        public override string SubversionId { get { return ("$Id: ForcedDismount.cs 229 2012-04-25 01:57:29Z natfoth $"); } }
        public override string SubversionRevision { get { return ("$Revision: 229 $"); } }


        ~ForcedDismount()
        {
            Dispose(false);
        }


        public void Dispose(bool isExplicitlyInitiatedDispose)
        {
            if (!_isDisposed)
            {
                if (isExplicitlyInitiatedDispose)
                {
                    // empty, for now
                }

                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                base.Dispose();
            }

            _isDisposed = true;
        }


        #region Overrides of CustomForcedBehavior

        protected override Composite CreateBehavior()
        {
            return _root ?? (_root = new ActionRunCoroutine(ret => ExecuteDismount()));
        }

        private async Task<bool> ExecuteDismount()
        {
            LocalPlayer player = Me;
            if (player == null) return false;
            ulong guid = player.Guid;
            ShapeshiftForm form = player.Shapeshift;
            bool SameActor() => guid != 0 && ReferenceEquals(Me, player) && player.Guid == guid
                && player.IsValid && player.IsAlive;
            if (!SameActor()) return false;
            if (!player.Mounted && form == ShapeshiftForm.Normal)
                return _isBehaviorDone = true;

            TreeRoot.StatusText = "Landing and dismounting";
            if (!SameActor() || player.Shapeshift != form) return false;
            if (player.Mounted || form == ShapeshiftForm.FlightForm || form == ShapeshiftForm.EpicFlightForm)
            {
                if (!await CommonCoroutines.LandAndDismount("ForcedDismount")) return false;
            }
            else
            {
                Mount.ClearShapeshift();
                if (!await Coroutine.Wait(4000, () => !SameActor() || player.Shapeshift == ShapeshiftForm.Normal))
                    return false;
            }

            if (!SameActor() || player.Mounted || player.Shapeshift != ShapeshiftForm.Normal) return false;
            // Only the captured player's observed removal completes this behavior.
            return _isBehaviorDone = true;
        }


        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public override bool IsDone
        {
            get
            {
                return (_isBehaviorDone
                        || !UtilIsProgressRequirementsMet(QuestId, QuestRequirementInLog, QuestRequirementComplete));
            }
        }


        public override void OnStart()
        {
            OnStart_HandleAttributeProblem();

            if (!IsDone)
            {
                TreeRoot.GoalText = "Dismounting";
            }
        }

        #endregion
    }
}
