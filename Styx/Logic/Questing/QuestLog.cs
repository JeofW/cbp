#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
	public enum CompletedQuestCacheStatus
	{
		Unknown,
		Valid,
		RefreshFailed
	}

	/// <summary>
	/// Provides access to the player's quest log.
	/// Matches HB 4.3.4 API while using Lua for completed quests (more reliable than memory reads).
	/// </summary>
	public class QuestLog
	{
		// WoW 3.3.5a Quest Log Offsets
		private const int OFFSET_COMPLETED_QUEST_LIST = 5005;  // Completed quest linked list head
		private const int MaximumCompletedQuestNodes = 10000;

		private static readonly object CompletedQuestCacheLock = new object();
		private static readonly List<uint> _completedQuestIds = new List<uint>();
		private static DateTime _completedQuestCacheTime = DateTime.MinValue;
		private static DateTime _completedQuestRefreshAttemptTime = DateTime.MinValue;
		private static CompletedQuestCacheStatus _completedQuestCacheStatus = CompletedQuestCacheStatus.Unknown;
		private static string _completedQuestCacheIdentity;
		private static readonly TimeSpan CompletedQuestCacheDuration = TimeSpan.FromMinutes(1);

		/// <summary>
		/// Reports whether the completed-quest cache is backed by a successful live refresh.
		/// </summary>
		public CompletedQuestCacheStatus CompletedQuestCacheStatus
		{
			get
			{
				string identity = CaptureCompletedQuestCacheIdentity();
				lock (CompletedQuestCacheLock)
				{
					EnsureCompletedQuestCacheIdentity(identity);
					return _completedQuestCacheStatus;
				}
			}
		}

		/// <summary>
		/// Number of quests in the log.
		/// Address: 12729040 (0xC24350)
		/// </summary>
		public uint QuestCount
		{
			get
			{
				return ObjectManager.Wow.Read<uint>(12729040U);
			}
		}

		/// <summary>
		/// Gets the quest log index for a quest ID.
		/// </summary>
		public int GetIndexForQuest(uint questId)
		{
			for (int i = 0; i < 25; i++)
			{
				uint offset = (uint)(158 + i * 5);
				uint id = ObjectManager.Me.ReadDescriptor<uint>(offset);
				if (id == questId)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// Gets all quests in the log.
		/// </summary>
		public List<PlayerQuest> GetAllQuests()
		{
			List<PlayerQuest> list = new List<PlayerQuest>();
			for (byte i = 0; i < 25; i++)
			{
				PlayerQuest quest = GetQuest((uint)i);
				if (quest != null)
					list.Add(quest);
			}
			return list;
		}

		/// <summary>
		/// Gets a quest by log index.
		/// </summary>
		public PlayerQuest GetQuest(uint index)
		{
			if (index >= 25U)
				throw new ArgumentOutOfRangeException("index");

			uint questId = GetQuestIdAtIndex(index);
			if (questId <= 0U)
				return null;

			return PlayerQuest.FromId(questId);
		}

		/// <summary>
		/// Gets the quest ID at a log index.
		/// </summary>
		public uint GetQuestId(uint index)
		{
			if (index >= 25U)
				throw new ArgumentOutOfRangeException("index");

			return GetQuestIdAtIndex(index);
		}

		private uint GetQuestIdAtIndex(uint index)
		{
			uint offset = 158U + index * 5U;
			return ObjectManager.Me.ReadDescriptor<uint>(offset);
		}

		/// <summary>
		/// Checks if a quest is in the log.
		/// </summary>
		public bool ContainsQuest(uint questId)
		{
			for (int i = 0; i < 25; i++)
			{
				uint offset = (uint)(158 + i * 5);
				uint id = ObjectManager.Me.ReadDescriptor<uint>(offset);
				if (id == questId)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Gets a quest by ID.
		/// </summary>
		public PlayerQuest GetQuestById(uint questId)
		{
			if (!ContainsQuest(questId))
				return null;
			return PlayerQuest.FromId(questId);
		}

		/// <summary>
		/// Gets quest info at a log index.
		/// descriptor_ptr + (field * 4) — quest log starts at field 158, each entry is 5 fields (20 bytes).
		/// So byte offset = (158 + index * 5) * 4, NOT 158 + index * 5 * 4.
		/// </summary>
		public QuestLogEntry GetQuestInfo(int index)
		{
			uint offset = (uint)((158 + index * 5) * 4);
			uint descriptorPtr = ObjectManager.Wow.Read<uint>(StyxWoW.Me.BaseAddress + 8U);
			return ObjectManager.Wow.ReadStruct<QuestLogEntry>(descriptorPtr + offset);
		}

		/// <summary>
		/// Abandons a quest by ID.
		/// Address: 12729052 (0xC2435C), Call: 6163648 (0x5E1CC0)
		/// </summary>
		public void AbandonQuestById(uint questId)
		{
			ExecutorRand executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor used in AbandonQuestById.");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("mov esi, {0}", 12729052U);
				executor.AddLine("mov ebx, [esi]");
				executor.AddLine("mov eax, {0}", questId);
				executor.AddLine("mov [esi], eax");
				executor.AddLine("call {0}", 6163648U);
				executor.AddLine("mov [esi], ebx");
				executor.AddLine("retn");
				executor.Execute();
			}
		}

		/// <summary>
		/// Abandons a quest by log slot.
		/// </summary>
		public void AbandonQuest(byte slot)
		{
			if (slot >= 25)
				throw new ArgumentOutOfRangeException("slot");

			RemoveQuestFromLog(slot);
		}

		/// <summary>
		/// Removes a quest from the log.
		/// Call: ClntObjMgrGetActivePlayerObj (4208880), CGPlayer_C__QuestLogRemoveQuest (7163776)
		/// </summary>
		private static void RemoveQuestFromLog(byte slot)
		{
			ExecutorRand executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor used in CGPlayer_C__QuestLogRemoveQuest.");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("call {0}", 4208880U);
				executor.AddLine("mov ecx, eax");
				executor.AddLine("push {0}", slot);
				executor.AddLine("call {0}", 7163776U);
				executor.AddLine("retn");
				executor.Execute();
			}
		}

		/// <summary>
		/// Gets a read-only collection of quest IDs that have been completed.
		/// Uses Lua QueryQuestsCompleted() which triggers QUEST_QUERY_COMPLETE event.
		/// Then reads the completed quest linked list from memory.
		/// Results are cached for 1 minute.
		/// </summary>
		public ReadOnlyCollection<uint> GetCompletedQuests()
		{
			TryGetAuthoritativeCompletedQuests(out ReadOnlyCollection<uint> completedQuestIds);
			return completedQuestIds;
		}

		/// <summary>
		/// Gets completed quests only when the current character and realm have a valid live refresh.
		/// </summary>
		public bool TryGetAuthoritativeCompletedQuests(out ReadOnlyCollection<uint> completedQuestIds)
		{
			return TryGetAuthoritativeCompletedQuestsForCacheIdentity(
				CaptureCompletedQuestCacheIdentity(),
				TryRefreshCompletedQuestCache,
				out completedQuestIds);
		}

		internal static bool TryGetAuthoritativeCompletedQuestsForIdentity(
			string character,
			string realm,
			Func<List<uint>> refresh,
			out ReadOnlyCollection<uint> completedQuestIds)
		{
			return TryGetAuthoritativeCompletedQuestsForCacheIdentity(
				CreateCompletedQuestCacheIdentity(character, realm), refresh, out completedQuestIds);
		}

		private static bool TryGetAuthoritativeCompletedQuestsForCacheIdentity(
			string identity,
			Func<List<uint>> refresh,
			out ReadOnlyCollection<uint> completedQuestIds)
		{
			lock (CompletedQuestCacheLock)
			{
				EnsureCompletedQuestCacheIdentity(identity);
				if (identity != null && ShouldRefreshCompletedQuestCache())
				{
					List<uint> refreshedQuestIds = refresh();
					if (refreshedQuestIds == null)
					{
						_completedQuestCacheStatus = CompletedQuestCacheStatus.RefreshFailed;
					}
					else
					{
						_completedQuestIds.Clear();
						_completedQuestIds.AddRange(refreshedQuestIds);
						_completedQuestCacheTime = DateTime.Now;
						_completedQuestCacheStatus = CompletedQuestCacheStatus.Valid;
					}
				}

				completedQuestIds = CreateCompletedQuestSnapshot();
				return identity != null && _completedQuestCacheStatus == CompletedQuestCacheStatus.Valid;
			}
		}

		internal static CompletedQuestCacheStatus GetCompletedQuestCacheStatusForIdentity(string character, string realm)
		{
			string identity = CreateCompletedQuestCacheIdentity(character, realm);
			lock (CompletedQuestCacheLock)
			{
				EnsureCompletedQuestCacheIdentity(identity);
				return _completedQuestCacheStatus;
			}
		}

		/// <summary>
		/// Checks if the completed quest cache should be refreshed.
		/// </summary>
		private static bool ShouldRefreshCompletedQuestCache()
		{
			DateTime now = DateTime.Now;
			return now - _completedQuestCacheTime > CompletedQuestCacheDuration
				&& now - _completedQuestRefreshAttemptTime > CompletedQuestCacheDuration;
		}

		/// <summary>
		/// Refreshes the completed quest cache using Lua QueryQuestsCompleted().
		/// Waits for QUEST_QUERY_COMPLETE event, then reads memory.
		/// </summary>
		private static List<uint> TryRefreshCompletedQuestCache()
		{
			_completedQuestRefreshAttemptTime = DateTime.Now;
			try
			{
				using (LuaEventWait questQueryWait = new LuaEventWait("QUEST_QUERY_COMPLETE"))
				{
					Lua.DoString("QueryQuestsCompleted()");
					if (!questQueryWait.Wait(5000))
					{
						Styx.Helpers.Logging.Write("[QuestLog] Timeout waiting for QUEST_QUERY_COMPLETE event");
						return null;
					}
				}

				if (TryPopulateCompletedQuestIdsFromMemory(out List<uint> completedQuestIds))
					return completedQuestIds;

				Styx.Helpers.Logging.Write("[QuestLog] Failed to read completed quest cache");
				return null;
			}
			catch (Exception ex)
			{
				Styx.Helpers.Logging.WriteException(ex);
				return null;
			}
		}

		/// <summary>
		/// Reads completed quest IDs from the WoW memory linked list.
		/// Structure: CompletedQuestNode { padding, next_ptr, quest_id }
		/// </summary>
		private static bool TryPopulateCompletedQuestIdsFromMemory(out List<uint> completedQuestIds)
		{
			completedQuestIds = new List<uint>();
			Memory wow = ObjectManager.Wow;
			if (wow == null)
				return false;

			uint completedQuestListHead = StyxWoW.Offsets.GetOffsetByIndex(OFFSET_COMPLETED_QUEST_LIST);
			if (completedQuestListHead == 0)
			{
				return false;
			}

			// Read the head pointer of the linked list
			uint nodeAddress = wow.Read<uint>(completedQuestListHead);
			if (!TryTraverseCompletedQuestNodes(nodeAddress, address =>
			{
				CompletedQuestNode node = wow.Read<CompletedQuestNode>(address);
				return (node.Next, node.QuestId);
			}, out completedQuestIds))
				return false;
			return true;
		}

		private static string CaptureCompletedQuestCacheIdentity()
		{
			try
			{
				var me = ObjectManager.Me;
				return me == null ? null : CreateCompletedQuestCacheIdentity(me.Name, me.RealmName);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static string CreateCompletedQuestCacheIdentity(string character, string realm)
		{
			if (string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(realm))
				return null;

			return character + "\u001f" + realm;
		}

		private static void EnsureCompletedQuestCacheIdentity(string identity)
		{
			if (identity != null && string.Equals(_completedQuestCacheIdentity, identity, StringComparison.OrdinalIgnoreCase))
				return;

			_completedQuestCacheIdentity = identity;
			_completedQuestIds.Clear();
			_completedQuestCacheTime = DateTime.MinValue;
			_completedQuestRefreshAttemptTime = DateTime.MinValue;
			_completedQuestCacheStatus = CompletedQuestCacheStatus.Unknown;
		}

		private static ReadOnlyCollection<uint> CreateCompletedQuestSnapshot()
		{
			return new ReadOnlyCollection<uint>(new List<uint>(_completedQuestIds));
		}

		/// <summary>
		/// Traverses the 32-bit client's completed-quest linked list without allowing malformed memory to spin.
		/// A valid pointer is a non-zero, DWORD-aligned address; null terminates a valid list.
		/// </summary>
		internal static bool TryTraverseCompletedQuestNodes(
			uint headAddress,
			Func<uint, (uint Next, uint QuestId)> readNode,
			out List<uint> questIds)
		{
			questIds = new List<uint>();
			if (headAddress == 0)
				return true;

			HashSet<uint> visitedAddresses = new HashSet<uint>();
			HashSet<uint> seenQuestIds = new HashSet<uint>();
			uint nodeAddress = headAddress;

			for (int nodeCount = 0; nodeAddress != 0; nodeCount++)
			{
				if (nodeCount == MaximumCompletedQuestNodes || (nodeAddress & 3U) != 0U || !visitedAddresses.Add(nodeAddress))
					return false;

				try
				{
					(uint next, uint questId) = readNode(nodeAddress);
					if (questId != 0 && seenQuestIds.Add(questId))
						questIds.Add(questId);
					nodeAddress = next;
				}
				catch (Exception)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Adds a quest ID to the completed quests cache.
		/// </summary>
		public void AddCompletedQuest(uint questId)
		{
			string identity = CaptureCompletedQuestCacheIdentity();
			lock (CompletedQuestCacheLock)
			{
				EnsureCompletedQuestCacheIdentity(identity);
				if (identity != null && questId != 0 && !_completedQuestIds.Contains(questId))
					_completedQuestIds.Add(questId);
			}
		}

		/// <summary>
		/// Adds a quest ID to the completed quests cache (HB 3.3.5a alias).
		/// </summary>
		public void AddCompletedQuestId(uint questId) => AddCompletedQuest(questId);

		/// <summary>
		/// Completed quest linked list node structure (WoW 3.3.5a).
		/// </summary>
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct CompletedQuestNode
		{
			private readonly uint _padding;
			public readonly uint Next;
			public readonly uint QuestId;
		}
	}
}
