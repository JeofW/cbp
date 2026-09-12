#region System Namespace
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
#endregion System Namespace

#region Foreign Namespace
#endregion Foreign Namespace

#region Styx Namespace
using Styx;
using Styx.Helpers;
using Styx.Plugins;
using Styx.Common;
using Styx.CommonBot;
using Styx.WoWInternals.WoWObjects;
#endregion Styx Namespace

namespace AlwaysHere
{
    public class AlwaysHere : HBPlugin
    {
        public override string Name { get { return "AlwaysHere"; } }
        public override string Author { get { return "Giwin"; } }
        public override Version Version { get { return new Version(1, 4); } }
        public override bool WantButton { get { return true; } }


        public override void Initialize()
        {
            Logging.Write("AlwaysHere - Loaded Version " + Version);
        }

        public override void Pulse()
        {
            if (StyxWoW.IsInGame)
            {
                //to call a setting its 
                if (StyxWoW.Me.IsAlive)
                {
                    if (StyxWoW.Me.IsAFKFlagged && !StyxWoW.Me.IsCasting && !StyxWoW.Me.IsMoving && !StyxWoW.Me.Combat)
                    {
                        KeyboardManager.KeyUpDown((char)KeyboardManager.eVirtualKeyMessages.VK_SPACE);
                        Logging.Write("[AlwaysHere] I'm AFK flagged, Anti-Afking at " + DateTime.Now.ToString());
                    }
                }
            }
        }



    }
}