using System;
using System.Collections.Generic;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Provides quest-related functionality including completed quest tracking.
	/// </summary>
	public static class Questing
	{
		// CGQuestLog__GetQuestIDByIndex function address (3.3.5a 12340)
		private const uint CGQuestLogGetQuestIDByIndex = 0x5E3E40; // 6174784

		/// <summary>
		/// Gets all completed quest IDs for the current character.
		/// </summary>
		/// <returns>A copied completed-quest set, or an empty set while authority is unavailable.</returns>
		[Obsolete("Use StyxWoW.Me.QuestLog.TryGetAuthoritativeCompletedQuests(out ids) instead.")]
		public static HashSet<uint> GetCompletedQuestIDs()
		{
			var questLog = StyxWoW.Me?.QuestLog;
			return questLog != null && questLog.TryGetAuthoritativeCompletedQuests(out var completedQuestIds)
				? new HashSet<uint>(completedQuestIds)
				: new HashSet<uint>();
		}

		/// <summary>
		/// Queries the server for completed quests.
		/// </summary>
		public static void QueryQuestsCompleted()
		{
			Lua.DoString("QueryQuestsCompleted()");
		}

		/// <summary>
		/// Gets quest ID by index in quest log using native function.
		/// </summary>
		internal static int GetQuestIDByIndex(uint index)
		{
			var executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("push {0}", index);
				executor.AddLine("call {0}", CGQuestLogGetQuestIDByIndex);
				executor.AddLine("add esp, 0x4");
				executor.AddLine("retn");
				executor.Execute();

				return executor.Memory.Read<int>(executor.ReturnPointer);
			}
		}
	}
}
