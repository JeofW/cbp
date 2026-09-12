// @Plugin : DeathRunFix - Instance Farming Death Fix
// @Author : Onsit
// @Version : 2
// @Date : 5/29/2014

using Styx;
using Styx.Common;
using Styx.Common.Helpers;
using Styx.CommonBot;
using Styx.Pathing;
using Styx.Plugins;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.CommonBot.Coroutines;
using Buddy.Coroutines;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Threading.Tasks;


using CommonBehaviors.Actions;
using Action = Styx.TreeSharp.Action;

namespace DeathRunFix
{
    public class DeathRunFix : HBPlugin
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }

        public override string Name { get { return "DeathRunFix"; } }
        public override string Author { get { return "Onsit"; } }
        public override Version Version { get { return new Version(2, 0); } }
        public override bool WantButton { get { return true; } }
        public override string ButtonText { get { return "DeathRunFix"; } }
        public override void OnButtonPress() { }
        public static void OGlog(string message, params object[] args) { Logging.Write(Colors.DeepSkyBlue, "[DeathRunFix]: " + message, args); }
        private Composite _hookedCorpseRun;
        private bool _Initialized;

        private void BotEvent_OnBotStarted(EventArgs args) { Initialize(); }
        private void BotEvent_OnBotStopped(EventArgs args) { Dispose(); }

        public override void Initialize()
        {
            _hookedCorpseRun = null;
            if (_Initialized) return;
            _Initialized = true;
            BotEvents.OnBotStarted += BotEvent_OnBotStarted;
            BotEvents.OnBotStopped += BotEvent_OnBotStopped;
        }

        public override void Dispose()
        {
            OGlog("unHooked");
            TreeHooks.Instance.RemoveHook("Death_Main", _hookedCorpseRun);
            _hookedCorpseRun = null;
        }

        public override void Pulse()
        {
            if (_Initialized && _hookedCorpseRun == null && isDead() && !_isBehaviorDone && nearSupportedArea())
            {
                OGlog("Hooked");
                _hookedCorpseRun = CreateBehaviorCorpseRun();
                TreeHooks.Instance.ReplaceHook("Death_Main", _hookedCorpseRun);
            }

            if (!isDead())
            {
                _isBehaviorDone = false;
            }
        }

        private bool _isBehaviorDone = false;
        private Queue<WoWPoint> _pathToDest = null;
        private Composite CreateBehaviorCorpseRun()
        {
            return new PrioritySelector(
                    new Decorator(ret => (Me.IsDead && !Me.IsGhost),
                       new Action(delegate
                       {
                           Lua.DoString("RepopMe(); ");
                       })),

                    new Decorator(ret => (!Me.IsGhost),
                        new Action(delegate { })),

                    new Decorator(ret => (_pathToDest == null),
                       new Action(delegate
                       {
                           _pathToDest = buildCorpseRun();
                       })),

                   new Decorator(ret => (Me.Location.Distance(_pathToDest.Peek()) > 3),
                       new Sequence(
                           new Action(delegate { Flightor.MoveTo(_pathToDest.Peek()); })
                       )),

                   new Action(delegate
                   {
                       _pathToDest.Dequeue();
                       if (_pathToDest.Count() > 0)
                       { return (RunStatus.Success); }
                       _isBehaviorDone = true;
                       _pathToDest = null;
                       Dispose();
                       return (RunStatus.Failure);
                   }));
        }

        public Queue<WoWPoint> buildCorpseRun()
        {
            Queue<WoWPoint> tempQueue = new Queue<WoWPoint>();

            if (nearICC())
            {
                tempQueue.Enqueue(new WoWPoint(6445.129, 2062.137, 642.3521));
                tempQueue.Enqueue(new WoWPoint(6280.585, 2245.11, 605.2169));
                tempQueue.Enqueue(new WoWPoint(6022.311, 2177.462, 696.2386));
                tempQueue.Enqueue(new WoWPoint(5825.292, 2312.368, 839.6663));
                tempQueue.Enqueue(new WoWPoint(5698.298, 2288.899, 811.287));
                tempQueue.Enqueue(new WoWPoint(5687.403, 2102.313, 810.6955));
                tempQueue.Enqueue(new WoWPoint(5648.667, 2098.861, 810.9717));
                tempQueue.Enqueue(new WoWPoint(5639.65, 2080.922, 811.1208));
                tempQueue.Enqueue(new WoWPoint(5633.731, 2031.583, 813.0566));
				tempQueue.Enqueue(new WoWPoint(5594.156, 2014.238, 811.3767));
				tempQueue.Enqueue(new WoWPoint(5583.315, 2006.013, 807.6586));

            }
            else if (nearGrimBatol())
            {
                tempQueue.Enqueue(new WoWPoint(-4153.02, -3690.4, 258.9438));
                tempQueue.Enqueue(new WoWPoint(-4140.393, -3618.822, 259.7476));
                tempQueue.Enqueue(new WoWPoint(-4111.154, -3470.732, 281.4245));
                tempQueue.Enqueue(new WoWPoint(-4071.709, -3454.895, 284.4695));
                tempQueue.Enqueue(new WoWPoint(-4042.643, -3444.804, 288.3671));
            }
            else if (nearTolVir())
            {
                tempQueue.Enqueue(new WoWPoint(-10730.74, -1550.357, 35.9071));
                tempQueue.Enqueue(new WoWPoint(-10734.54, -1385.231, 54.76054));
                tempQueue.Enqueue(new WoWPoint(-10647.75, -1299.349, 34.30367));
                tempQueue.Enqueue(new WoWPoint(-10690.87, -1311.008, 17.79055));
            }

            return tempQueue;
        }

        public bool isDead()
        {
            if ((Me.IsGhost || Me.IsDead))
            {
                return true;
            }
            return false;
        }

        public bool nearICC()
        {
            return (Me.Location.Distance(new WoWPoint(6445.129, 2062.137, 563.6693)) < 1000);
        }

        public bool nearGrimBatol()
        {
            return (Me.Location.Distance(new WoWPoint(-4153.02, -3690.4, 207.425)) < 500);
        }

        public bool nearTolVir()
        {
            return (Me.Location.Distance(new WoWPoint(-10720, -1550.27, 1.91465)) < 500);
        }

        public bool nearSupportedArea()
        {
            return nearICC() || nearGrimBatol() || nearTolVir();
        }
    }
}