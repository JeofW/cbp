#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GreenMagic;
using Styx.Logic.Questing;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.Quest
{
    public class QuestFrame : Frame
    {
        public static readonly QuestFrame Instance;
        private static Predicate<uint> predicate_0;
        private static Predicate<uint> predicate_1;

        public QuestFrame() : base("QuestFrame")
        {
        }

        static QuestFrame()
        {
            QuestFrame.Instance = new QuestFrame();
        }

        public void AcceptQuest()
        {
            Lua.DoString("AcceptQuest() if QuestFrameAcceptButton:IsVisible() then QuestFrameAcceptButton:Click() end");
        }

        public void ClickContinue() => Lua.DoString("QuestFrameCompleteButton:Click()");

        public void CompleteQuest()
        {
            // Original3.3.5 separates the progress button (CompleteQuest) from
            // the reward button (GetQuestReward). Dispatch one visible action;
            // the caller must still wait for its own quest/server acknowledgement.
            Lua.DoString("if not QuestFrame or not QuestFrame:IsVisible() then return end " +
                "local reward=QuestFrameCompleteQuestButton " +
                "local progress=QuestFrameCompleteButton " +
                "if reward and reward:IsVisible() then " +
                "if reward:IsEnabled() then reward:Click() end " +
                "elseif progress and progress:IsVisible() and progress:IsEnabled() then progress:Click() end");
        }

        public void DeclineQuest()
        {
            Lua.DoString("DeclineQuest()");
        }

        public void SelectQuestReward(int index)
        {
            string format = "QuestInfoItem{0}:Click()";
            object[] array = new object[]
            {
                index + 1
            };
            Lua.DoString(format, array);
        }

        public new void Hide()
        {
            this.Close();
        }

        public void Close()
        {
            Lua.DoString("CloseQuest()");
        }

        public uint CurrentShownQuestId
        {
            get
            {
                return ObjectManager.Wow.Read<uint>(12637788U);
            }
        }

        public Styx.Logic.Questing.Quest CurrentShownQuest
        {
            get
            {
                return Styx.Logic.Questing.Quest.FromId(this.CurrentShownQuestId);
            }
        }

        public List<uint> Quests
        {
            get
            {
                List<uint> list = new List<uint>();
                uint questListPointer = 13227856U;
                Memory wow = ObjectManager.Wow;
                for (uint questId = wow.Read<uint>(13227856U); questId != 0U; questId = ObjectManager.Wow.Read<uint>(questListPointer))
                {
                    list.Add(questId);
                    questListPointer += 4U;
                }
                return list;
            }
        }

        public List<uint> AvailableQuests
        {
            get
            {
                List<uint> quests = this.Quests;
                if (QuestFrame.predicate_0 == null)
                {
                    QuestFrame.predicate_0 = new Predicate<uint>(IsNotInQuestLog);
                }
                return quests.FindAll(QuestFrame.predicate_0);
            }
        }

        public List<uint> ActiveQuests
        {
            get
            {
                List<uint> quests = this.Quests;
                if (QuestFrame.predicate_1 == null)
                {
                    QuestFrame.predicate_1 = new Predicate<uint>(IsInQuestLog);
                }
                return quests.FindAll(QuestFrame.predicate_1);
            }
        }

        private static bool IsNotInQuestLog(uint ret)
        {
            return !StyxWoW.Me.QuestLog.ContainsQuest(ret);
        }

        private static bool IsInQuestLog(uint ret)
        {
            return StyxWoW.Me.QuestLog.ContainsQuest(ret);
        }
    }
}
