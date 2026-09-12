using System.Collections.Generic;
using System.Linq;

using CommonBehaviors.Actions;
using Styx;
using Styx.Common;
using Styx.CommonBot;
using Styx.CommonBot.Profiles;
using Styx.Pathing;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;

using Action = Styx.TreeSharp.Action;


namespace Honorbuddy.Quest_Behaviors.PickPocket
{
    [CustomBehaviorFileName(@"PickPocket")]
    public class PickPocket : CustomForcedBehavior
    {
        public PickPocket(Dictionary<string, string> args)
            : base(args)
        {
            try
            {
                if (!IsDone)
                {
                    int MobCount = new int();
                    MobCount = GetAttributeAsNullable<int>("MobCount", true, ConstrainAs.CollectionCount, null) ?? 0;
                    PickPocketNearest(MobCount);
                }
            }
            catch (Exception except)
            {
                LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message
                        + "\nFROM HERE:\n"
                        + except.StackTrace + "\n");
                IsAttributeProblem = true;
            }
 
        }
        private bool _isBehaviorDone;

        private Composite _root;
        private WoWUnit unit;
        
        private List<WoWUnit> unitsToJew;
        private List<WoWUnit> AllUnits;
        private WoWUnit SelectedAliveTarget { get; set; }
        public int MobCount { get; private set; }
        
        public override bool IsDone
        {
            get
            {
                return _isBehaviorDone;
            }
        }

       
        private LocalPlayer Me
        {
            get { return (StyxWoW.Me); }
        }
        
        public override void OnStart()
        {
            OnStart_HandleAttributeProblem();

        }


        protected override Composite CreateBehavior()
        {
            return _root ?? (_root = new PrioritySelector(DoneYet));
        }



        public bool IsPickPocketComplete()
        {
            bool looted = _isBehaviorDone;
            return looted;
        }

        public Composite DoneYet
        {
            get
            {
                return
                    new Decorator(ret => IsPickPocketComplete(), new Action(delegate
                    {
                        TreeRoot.StatusText = "Finished!";
                        _isBehaviorDone = true;
                        return RunStatus.Success;
                    }));

            }
        }

        private List<WoWUnit> GetClosestUnits(int mobCount)
        {
            AllUnits = (ObjectManager.GetObjectsOfType<WoWUnit>(true, true)
                        .Where(unit =>
                        !unit.IsDead &&
                        !unit.IsCritter &&
                        unit.IsHumanoid &&
                        !unit.IsFriendly)
                        .OrderBy(unit => unit.Distance)).ToList<WoWUnit>();
            List<WoWUnit> units = new List<WoWUnit>();
            if (AllUnits != null && AllUnits.Count >= mobCount)
            {
                for (int index = 0; index < mobCount; index++)
                {
                    units.Add(AllUnits[index]);
                }
            }
            else if (AllUnits != null)
            {
                for (int index = 0; index < AllUnits.Count; index++)
                {
                    units.Add(AllUnits[index]);
                }                
            }
                return units;
        }  

        private void PickPocketNearest(int mobCount)
        {
            unitsToJew = new List<WoWUnit>();
            unitsToJew = GetClosestUnits(mobCount);
            for (int i = 0; i < unitsToJew.Count(); i++)
            {
                unitsToJew[i].Target();
                if ((unitsToJew[i].Distance < 10))
                {
                    SpellManager.Cast(921);
                    WoWMovement.MoveStop();
                    // set this longer if you have shit latency
                    System.Threading.Thread.Sleep(250);
                }
                Me.ClearTarget();
                _isBehaviorDone = true;
            }
            _isBehaviorDone = true;
        }





    }
}
