using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

using Styx;
using Styx.Common;
using Styx.Helpers;
using Styx.Plugins;
using Styx.WoWInternals;
using Styx.WoWInternals.Misc;
using Styx.WoWInternals.World;
using Styx.WoWInternals.WoWObjects;


namespace GankOff
{
    public class GankOff : HBPlugin
    {
        public override void Pulse()
        {
            if (StyxWoW.IsInWorld)
            {
                if (StyxWoW.IsInGame)
                {
                    if (StyxWoW.Me.IsGhost && !Battlegrounds.IsInsideBattleground)
                    {
                        Ganked();
                    }
                }
            }
        }
        public void Ganked()
        {
            short yardValue = 200;
            var playerList = ObjectManager.GetObjectsOfType<WoWPlayer>().Where(o => o.Distance <= yardValue);

            foreach (WoWPlayer player in playerList)
            {
                if (playerList != null && player.IsPlayer && player.IsHostile && intMe.Location.Distance(intMe.CorpsePoint) < 300)
                {
                    Logging.Write("[GankOff]: " + player.Name + " is near me and trying to ressurect, assume we're being ganked... will wait 60 seconds and then recheck to see if player has departed from our Corpse Location");

                    Thread.Sleep(60000);
                }
            }
        }
        public override void Initialize()
        {
            Logging.Write("GankOff - Loaded Version " + Version);
        }

        private static LocalPlayer intMe { get { return StyxWoW.Me; } }
        public override string Name { get { return "GankOff"; } }
        public override string Author { get { return "Giwin fixed by BadWolff AGAIN"; } }
        public override Version Version { get { return new Version(1, 3); } }
        public override bool WantButton { get { return false; } }
    }
}
