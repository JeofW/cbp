using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Patchables;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals
{
    public static class Lua
    {
        #region Private Fields

        private static AllocatedMemory? _returnBuffer;
        private static readonly Dictionary<string, string> EscapeSequences = new Dictionary<string, string>
        {
            { "\\", "\\\\" },
            { "\"", "\\\"" },
            { "'", "\\'" },
            { "[", "\\[" },
            { "]", "\\]" },
            { "\n", "\\n" },
            { "\r", "\\r" },
            { "\t", "\\t" }
        };

        #endregion

        #region Public Methods

        public static string Escape(string unescaped)
        {
            if (string.IsNullOrEmpty(unescaped))
                return unescaped;

            foreach (var kvp in EscapeSequences)
            {
                unescaped = unescaped.Replace(kvp.Key, kvp.Value);
            }
            return unescaped;
        }

        // Shared buffer for Lua return values (reused across calls like HB 3.3.5a)
        private static readonly byte[] _luaBuffer = new byte[4000];
        private const int ObservedReturnLimit = 64;
        private const int ObservedStringLimit = 384;

        public static List<string> GetReturnValues(string lua)
        {
            return GetReturnValues(lua, "CopilotBuddy.lua");
        }

        /// <summary>
        /// Executes Lua and returns multiple values with script name.
        /// HB 3.3.5a exact implementation.
        /// </summary>
        public static List<string> GetReturnValues(string lua, string scriptName)
        {
            return GetReturnValuesCore(lua, scriptName, 0);
        }

        /// <summary>
        /// Returns a complete bounded scalar observation. Missing transport,
        /// unreadable fields and unsupported Lua values remain unavailable.
        /// </summary>
        public static List<string> GetObservedReturnValues(string lua)
        {
            return GetReturnValuesCore(BuildObservedReturnScript(lua), "CopilotBuddy.Observed.lua", 0, true, lua);
        }

        /// <summary>
        /// A complete observation with caller-owned native entry. The predicate
        /// must be pure memory/state observation: executing Lua or preparing a
        /// native command from it would replace this request's assembly buffer.
        /// Returned values are not an acknowledgement of a world/server effect.
        /// </summary>
        internal static List<string> GetObservedReturnValues(string lua, Func<bool> admitted)
        {
            ArgumentNullException.ThrowIfNull(admitted);
            return GetReturnValuesCore(BuildObservedReturnScript(lua), "CopilotBuddy.Observed.lua", 0, true, lua, admitted);
        }

        private static List<string> GetReturnValuesCore(string lua, string scriptName, ulong expectedCursorGuid,
            bool requireComplete = false, string? recoveryRequest = null, Func<bool>? admitted = null)
        {
            if (admitted != null && !admitted())
                throw new ObservationUnavailableException("lua-return", "The caller no longer owns this Lua request.");
            var executor = ObjectManager.Executor;
            if (executor == null)
            {
                if (requireComplete) throw new ObservationUnavailableException("lua-return", "The current Lua executor is unavailable.");
                return new List<string>();
            }

            var wow = ObjectManager.Wow;
            if (wow == null)
            {
                if (requireComplete) throw new ObservationUnavailableException("lua-return", "The current Lua memory owner is unavailable.");
                return new List<string>();
            }

            try
            {
                IntPtr processHandle = requireComplete ? wow.ProcessHandle : IntPtr.Zero;
                if (requireComplete && processHandle == IntPtr.Zero)
                    throw new ObservationUnavailableException("lua-return", "The observed Lua process handle is unavailable.");
                // Read Lua full state (same offset as HB 3.3.5a)
                uint fullState = requireComplete ? ReadObservedLuaWord(wow, (uint)GlobalOffsets.LuaState)
                    : wow.Read<uint>((uint)GlobalOffsets.LuaState);
                if (fullState == 0)
                {
                    if (requireComplete) throw new ObservationUnavailableException("lua-return", "The current Lua state is unavailable.");
                    return new List<string>();
                }

                byte[] bytes = Encoding.UTF8.GetBytes(lua);
                byte[] bytes2 = Encoding.UTF8.GetBytes(scriptName);
                List<byte> list = new List<byte>(bytes.Length + 1 + bytes2.Length + 1);
                list.AddRange(bytes);
                list.Add(0);
                list.AddRange(bytes2);
                list.Add(0);

                using (var allocatedMemory = new AllocatedMemory(list.Count))
                {
                    allocatedMemory.WriteBytes(0, list.ToArray());
                    uint address = allocatedMemory.Address;
                    uint fileNameOffset = (uint)(allocatedMemory.Address + bytes.Length + 1);

                    lock (executor.AssemblyLock)
                    {
                        // Observed calls own their output allocation. A nested
                        // observation or replaced process cannot reuse its bytes.
                        using var observedBuffer = requireComplete ? new AllocatedMemory(4000) : null;
                        if (!requireComplete && _returnBuffer == null)
                            _returnBuffer = new AllocatedMemory(4000);
                        var resultBuffer = observedBuffer ?? _returnBuffer!;
                        resultBuffer.WriteBytes(0, _luaBuffer);
                        if (requireComplete)
                        {
                            using (wow.TemporaryCacheState(false))
                            {
                                if (!ReferenceEquals(ObjectManager.Wow, wow) || !ReferenceEquals(ObjectManager.Executor, executor)
                                    || wow.ProcessHandle != processHandle
                                    || ReadObservedLuaWord(wow, (uint)GlobalOffsets.LuaState) != fullState
                                    || ReadObservedLuaWord(wow, resultBuffer.Address) != 0)
                                    throw new ObservationUnavailableException("lua-return", "The observed Lua request or output owner changed before dispatch.");
                                byte[] written = wow.ReadBytes(address, list.Count);
                                if (written == null || written.Length != list.Count)
                                    throw new ObservationUnavailableException("lua-return", "The complete Lua request bytes could not be verified.");
                                for (int index = 0; index < written.Length; index++)
                                    if (written[index] != list[index])
                                        throw new ObservationUnavailableException("lua-return", "The observed Lua request bytes differ from the prepared query.");
                            }
                        }
                        executor.Clear();

                            // HB 3.3.5a exact ASM sequence:
                            // 1. lua_gettop to get current stack position
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                            executor.AddLine("add esp, 0x4");
                            executor.AddLine("mov ebx, eax");   // ebx = old top
                            executor.AddLine("push ebx");       // save ebx on stack

                            // 2. luaL_loadbuffer
                            executor.AddLine("push {0}", fileNameOffset);
                            executor.AddLine("push {0}", bytes.Length);
                            executor.AddLine("push {0}", address);
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_Load);
                            executor.AddLine("add esp, 0x10");
                            executor.AddLine("test eax, eax");
                            executor.AddLine("jnz @Finally");

                            // Compare both physical GUID words inside the same
                            // client-thread dispatch as the Lua operation. A
                            // managed memory pre-read would leave a race window.
                            if (expectedCursorGuid != 0)
                                EmitCursorItemGuard(executor, expectedCursorGuid);

                            // 3. lua_pcall with error handler at -2
                            executor.AddLine("push {0}", -2);
                            executor.AddLine("push {0}", -1);
                            executor.AddLine("push {0}", 0);
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_PCall);
                            executor.AddLine("add esp, 0x10");
                            executor.AddLine("test eax, eax");
                            executor.AddLine("jnz @Finally");

                            // 4. Get new top, calculate return count
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                            executor.AddLine("add esp, 0x4");
                            executor.AddLine("cmp eax, ebx");
                            if (requireComplete)
                            {
                                executor.AddLine("jl @FailObservedReturnLimit");
                                executor.AddLine("je @FailNoRetValues");
                            }
                            else executor.AddLine("jle @FailNoRetValues");
                            executor.AddLine("sub eax, ebx");
                            if (requireComplete)
                            {
                                executor.AddLine("cmp eax, {0}", ObservedReturnLimit);
                                executor.AddLine("ja @FailObservedReturnLimit");
                            }

                            // 5. Store count in return buffer
                            executor.AddLine("mov ecx, {0}", resultBuffer.Address);
                            executor.AddLine("mov [ecx], eax");
                            executor.AddLine("add eax, ebx");   // eax = new top (for loop comparison)

                            // 6. Loop to read each return value
                            executor.AddLine("@LoopStart:");
                            executor.AddLine("add ecx, 0x4");
                            executor.AddLine("inc ebx");
                            executor.AddLine("push eax");       // save eax
                            executor.AddLine("push ecx");       // save ecx

                            // lua_tolstring(pState, index, NULL)
                            executor.AddLine("push {0}", 0);
                            executor.AddLine("push ebx");
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_ToLString);
                            executor.AddLine("add esp, 0xC");

                            executor.AddLine("pop ecx");        // restore ecx
                            executor.AddLine("mov [ecx], eax"); // store string pointer
                            executor.AddLine("pop eax");        // restore eax
                            executor.AddLine("cmp ebx, eax");
                            executor.AddLine("jl @LoopStart");

                            // Success - return 0
                            executor.AddLine("mov eax, 0");
                            executor.AddLine("jmp @Finally");

                            // No return values
                            executor.AddLine("@FailNoRetValues:");
                            executor.AddLine("mov eax, -1");
                            executor.AddLine("jmp @Finally");

                            if (requireComplete)
                            {
                                executor.AddLine("@FailObservedReturnLimit:");
                                executor.AddLine("mov eax, -2");
                                executor.AddLine("jmp @Finally");
                            }

                            // Cleanup: restore Lua stack with lua_settop
                            executor.AddLine("@Finally:");
                            executor.AddLine("pop ebx");        // restore ebx (old top)
                            executor.AddLine("push eax");       // save result
                            executor.AddLine("push ebx");
                            executor.AddLine("push {0}", fullState);
                            executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript__SetTop);
                            executor.AddLine("add esp, 0x8");
                            executor.AddLine("pop eax");        // restore result
                            executor.AddLine("retn");

                            if (requireComplete && (!ReferenceEquals(ObjectManager.Wow, wow)
                                || !ReferenceEquals(ObjectManager.Executor, executor) || wow.ProcessHandle != processHandle))
                                throw new ObservationUnavailableException("lua-return", "The observed Lua owner changed before native entry.");
                            if (!RecoveryActions.BeforeLuaSubmission(recoveryRequest ?? lua))
                            {
                                if (requireComplete) throw new ObservationUnavailableException("recovery-action", "The prepared Lua action no longer owns native entry.");
                                return new List<string>();
                            }
                            if (admitted != null && !admitted())
                                throw new ObservationUnavailableException("lua-return", "The caller no longer owns prepared Lua native entry.");
                            if (requireComplete)
                            {
                                using (wow.TemporaryCacheState(false))
                                    if (!ReferenceEquals(ObjectManager.Wow, wow) || !ReferenceEquals(ObjectManager.Executor, executor)
                                        || wow.ProcessHandle != processHandle
                                        || ReadObservedLuaWord(wow, (uint)GlobalOffsets.LuaState) != fullState)
                                        throw new ObservationUnavailableException("lua-return", "The observed Lua owner changed during final admission.");
                            }
                            // The final state read is itself an external memory
                            // boundary. Its callbacks cannot leave an earlier
                            // caller admission valid for a replaced actor/work.
                            if (admitted != null && !admitted())
                                throw new ObservationUnavailableException("lua-return", "The caller changed during the final Lua-state observation.");
                            executor.Execute();

                        // Read result from executor (disable cache like HB)
                        using ((requireComplete ? wow : StyxWoW.Memory).TemporaryCacheState(false))
                        {
                            if (requireComplete)
                            {
                                if (!ReferenceEquals(ObjectManager.Wow, wow) || !ReferenceEquals(ObjectManager.Executor, executor)
                                    || wow.ProcessHandle != processHandle
                                    || ReadObservedLuaWord(wow, (uint)GlobalOffsets.LuaState) != fullState)
                                    throw new ObservationUnavailableException("lua-return", "The observed Lua reply belongs to a replaced owner.");
                                int status = unchecked((int)ReadObservedLuaWord(wow, executor.ReturnPointer));
                                int count = checked((int)ReadObservedLuaWord(wow, resultBuffer.Address));
                                if (status == -1 && count == 0) return new List<string>();
                                if (status != 0)
                                    throw new ObservationUnavailableException("lua-return", "The observed Lua query did not produce a complete result. Status=" + status);
                                if (count == 0)
                                    throw new ObservationUnavailableException("lua-return", "The Lua execution status and empty result vector contradict each other.");
                                var observed = ReadObservedLuaValues(wow, resultBuffer.Address, count);
                                if (!ReferenceEquals(ObjectManager.Wow, wow) || !ReferenceEquals(ObjectManager.Executor, executor)
                                    || wow.ProcessHandle != processHandle
                                    || ReadObservedLuaWord(wow, (uint)GlobalOffsets.LuaState) != fullState)
                                    throw new ObservationUnavailableException("lua-return", "The Lua owner changed while reading the complete result.");
                                return observed;
                            }
                            int luaStatus = executor.Memory.Read<int>(executor.ReturnPointer);
                            if (luaStatus == 0)
                            {
                                // Success - read return values
                                int resultCount = resultBuffer.Read<int>(0);
                                var results = new List<string>(resultCount);
                                for (int i = 0; i < resultCount; i++)
                                {
                                    uint strPtr = resultBuffer.Read<uint>((i + 1) * 4);
                                    results.Add(executor.Memory.ReadString(strPtr));
                                }
                                return results;
                            }
                            else if (luaStatus < 0)
                            {
                                // No return values
                                return new List<string>();
                            }
                            else
                            {
                                // log the failing script for diagnostics
                                Logging.WriteDebug("Lua failed! status={0}, script=\"{1}\"", luaStatus, lua);
                                return new List<string>();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RecoveryActions.RethrowControlFlow(ex);
                if (requireComplete)
                {
                    if (ex is ObservationUnavailableException) throw;
                    throw new ObservationUnavailableException("lua-return", "The observed Lua transport is unavailable: " + ex.GetType().Name);
                }
                Logging.WriteDebug("Exception in GetReturnValues: {0}", ex.Message);
                return new List<string>();
            }
        }

        private static uint ReadObservedLuaWord(Memory memory, uint address)
        {
            if (address == 0 || address > uint.MaxValue - 3)
                throw new ObservationUnavailableException("lua-return", "An observed Lua word has no valid address.");
            try
            {
                byte[] bytes = memory.ReadBytes(address, sizeof(uint));
                if (bytes == null || bytes.Length != sizeof(uint))
                    throw new ObservationUnavailableException("lua-return", "An observed Lua word could not be read completely.");
                return BitConverter.ToUInt32(bytes, 0);
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (error is ObservationUnavailableException) throw;
                throw new ObservationUnavailableException("lua-return", "An observed Lua word read failed.");
            }
        }

        private static string ReadObservedLuaString(Memory memory, uint address)
        {
            if (address == 0)
                throw new ObservationUnavailableException("lua-return", "The Lua string pointer is unavailable.");
            try
            {
                var result = new List<byte>();
                while (result.Count <= ObservedStringLimit)
                {
                    uint pointer = checked(address + (uint)result.Count);
                    int pageRemaining = Environment.SystemPageSize - (int)(pointer % (uint)Environment.SystemPageSize);
                    int count = Math.Min(64, Math.Min(pageRemaining, ObservedStringLimit + 1 - result.Count));
                    byte[] bytes = memory.ReadBytes(pointer, count);
                    if (bytes == null || bytes.Length != count)
                        throw new ObservationUnavailableException("lua-return", "A Lua string could not be read completely.");
                    foreach (byte value in bytes)
                    {
                        if (value == 0) return new UTF8Encoding(false, true).GetString(result.ToArray());
                        result.Add(value);
                    }
                }
                throw new ObservationUnavailableException("lua-return", "The Lua scalar exceeds its bounded string contract.");
            }
            catch (Exception error)
            {
                RecoveryActions.RethrowControlFlow(error);
                if (error is ObservationUnavailableException) throw;
                throw new ObservationUnavailableException("lua-return", "A Lua scalar could not be decoded completely.");
            }
        }

        private static List<string> ReadObservedLuaValues(Memory memory, uint address, int count)
        {
            if (address == 0 || count < 0 || count > ObservedReturnLimit)
                throw new ObservationUnavailableException("lua-return", "The Lua result vector is unavailable or exceeds its limit.");
            var results = new List<string>(count);
            for (int index = 0; index < count; index++)
                results.Add(ReadObservedLuaString(memory, ReadObservedLuaWord(memory, checked(address + (uint)(index + 1) * 4U))));
            return results;
        }

        private static string BuildObservedReturnScript(string script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            return "local function observe(...) local n=select('#',...); if n>64 then error('observed return count') end; "
                + "local t={}; for i=1,n do local v=select(i,...); local k=type(v); "
                + "if k~='string' and k~='number' and k~='boolean' then error('unavailable observed scalar') end; "
                + "local s=tostring(v); if #s>384 then error('observed scalar length') end; t[i]=s end; return unpack(t,1,n) end; "
                + "return observe((function()\n" + script + "\nend)())";
        }

        private static void EmitCursorItemGuard(ExecutorRand executor, ulong expectedCursorGuid)
        {
            executor.AddLine("cmp dword [{0}], {1}", (uint)GlobalOffsets.CursorKind, 1U);
            executor.AddLine("jne @FailNoRetValues");
            executor.AddLine("cmp dword [{0}], {1}", (uint)GlobalOffsets.CursorItemGuid, (uint)expectedCursorGuid);
            executor.AddLine("jne @FailNoRetValues");
            executor.AddLine("cmp dword [{0}], {1}", (uint)GlobalOffsets.CursorItemGuid + 4U, (uint)(expectedCursorGuid >> 32));
            executor.AddLine("jne @FailNoRetValues");
        }

        /// <summary>
        /// Arms one cursor-selection lifetime before the caller's validated pickup.
        /// The ordinary build-12340 pickup emits empty, then selected cursor state.
        /// Any later cursor change revokes this attempt, even for the same item.
        /// </summary>
        public static bool BeginEquipCursorOwnership(uint itemEntry, string owner)
        {
            Guid identity;
            if (itemEntry == 0 || !Guid.TryParseExact(owner, "N", out identity) ||
                identity == Guid.Empty || owner != identity.ToString("N"))
                return false;

            var values = GetReturnValues(BuildEquipCursorOwnershipLua(itemEntry, owner));
            return values.Count == 1 && values[0] == "1";
        }

        private static string BuildEquipCursorOwnershipLua(uint itemEntry, string owner)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "local expectedEntry={0}; local owner='{1}';\n", itemEntry, owner) + @"
if GetCursorInfo() or CursorHasItem() then return 0 end
local f=_G.CopilotBuddy_EquipCursorFrame
if not f then
 f=CreateFrame('Frame')
 _G.CopilotBuddy_EquipCursorFrame=f
end
f.owner=nil
f:UnregisterAllEvents()
f:SetScript('OnEvent',nil)
f.phase=0
f.owner=owner
f.handler=function(self)
 if self.owner~=owner then return end
 local kind,entry=GetCursorInfo()
 if self.phase==0 and not kind and not CursorHasItem() then
  self.phase=1
 elseif self.phase==1 and kind=='item' and CursorHasItem() and tonumber(entry)==expectedEntry then
  self.phase=2
 else
  self.owner=nil
  self.phase=nil
  self:UnregisterAllEvents()
  self:SetScript('OnEvent',nil)
  self.handler=nil
 end
end
f:SetScript('OnEvent',f.handler)
f:RegisterEvent('CURSOR_UPDATE')
return 1";
        }

        /// <summary>
        /// Submits an equip only for the original physical selection. Inventory
        /// slot -1 preserves automatic destination selection; 0 remains ammo.
        /// Any bind confirmation belongs to this synchronous call; no pending
        /// array index or popup authority survives into the next bot tick.
        /// A true receipt is local submission, not equipment acknowledgement.
        /// </summary>
        public static bool TryEquipCursorItem(ulong itemGuid, uint itemEntry, int inventorySlot, string owner)
        {
            Guid identity;
            if (itemGuid == 0 || itemEntry == 0 || inventorySlot < -1 || inventorySlot > 23 ||
                !Guid.TryParseExact(owner, "N", out identity) || identity == Guid.Empty ||
                owner != identity.ToString("N"))
                return false;

            var values = GetReturnValuesCore(BuildEquipCursorSubmissionLua(itemEntry, inventorySlot, owner),
                "CopilotBuddy.EquipCursor.lua", itemGuid);
            return values.Count == 1 && values[0] == "1";
        }

        private static string BuildEquipCursorSubmissionLua(uint itemEntry, int inventorySlot, string owner)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "local expectedEntry={0}; local slot={1}; local owner='{2}';\n", itemEntry, inventorySlot, owner) + @"
local cursor=_G.CopilotBuddy_EquipCursorFrame
local function currentCursor()
 return cursor and _G.CopilotBuddy_EquipCursorFrame==cursor and cursor.owner==owner
  and cursor.phase==2 and type(cursor.handler)=='function'
  and cursor:GetScript('OnEvent')==cursor.handler and cursor:IsEventRegistered('CURSOR_UPDATE')
end
if not currentCursor() then return 0 end
local kind,entry=GetCursorInfo()
if kind~='item' or not CursorHasItem() or tonumber(entry)~=expectedEntry then return 0 end
if slot~=-1 and (not CursorCanGoInSlot(slot) or IsInventoryItemLocked(slot)) then return 0 end
if StaticPopup_FindVisible('EQUIP_BIND') or StaticPopup_FindVisible('AUTOEQUIP_BIND') then return 0 end
-- Both GetItemInfo and the native equip route consult DBItemCache. Do not
-- create a deferred pending record whose eventual event has no owned call.
if not GetItemInfo(expectedEntry) then return 0 end
local f=_G.CopilotBuddy_EquipSubmissionFrame
if not f then
 f=CreateFrame('Frame')
 _G.CopilotBuddy_EquipSubmissionFrame=f
end
if f.busy then return 0 end
f.busy=true
local count,index,popupKind,changed=0,nil,nil,false
local ok,receipt=pcall(function()
 f:SetScript('OnEvent',function(self,event,value)
  if event=='CURSOR_UPDATE' then changed=true;return end
  count=count+1
  index=tonumber(value)
  popupKind=event=='EQUIP_BIND_CONFIRM' and 'EQUIP_BIND' or 'AUTOEQUIP_BIND'
 end)
 f:RegisterEvent('EQUIP_BIND_CONFIRM')
 f:RegisterEvent('AUTOEQUIP_BIND_CONFIRM')
 f:RegisterEvent('CURSOR_UPDATE')
 if not currentCursor() then return 0 end
 -- AutoEquipCursorItem retains the build12340 bind check. EquipCursorItem(0)
 -- is a different native route, not the host's None=-1 destination sentinel.
 if slot==-1 then AutoEquipCursorItem() else EquipCursorItem(slot) end
 -- Build12340 emits the pending index synchronously. Reject nested/replaced
 -- events and cursor changes; never equate this index with an inventory slot.
 if count==1 and not changed and index and index>=0 and index==math.floor(index) then
  local p=StaticPopup_FindVisible(popupKind)
  if p and p.which==popupKind and tonumber(p.data)==index and p.button1 then p.button1:Click() end
 end
 return 1
end)
f:UnregisterAllEvents()
f:SetScript('OnEvent',nil)
f.busy=nil
return ok and receipt or 0";
        }

        /// <summary>
        /// Releases only the still-owned physical cursor selection. It neither
        /// moves a bag item nor cancels a reusable pending-equip array index.
        /// </summary>
        public static bool TryCancelEquipCursorItem(ulong itemGuid, uint itemEntry, string owner)
        {
            Guid identity;
            if (itemGuid == 0 || itemEntry == 0 || !Guid.TryParseExact(owner, "N", out identity) ||
                identity == Guid.Empty || owner != identity.ToString("N"))
                return false;

            var values = GetReturnValuesCore(BuildCancelEquipCursorLua(itemEntry, owner),
                "CopilotBuddy.CancelEquipCursor.lua", itemGuid);
            return values.Count == 1 && values[0] == "1";
        }

        private static string BuildCancelEquipCursorLua(uint itemEntry, string owner)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "local expectedEntry={0}; local owner='{1}';\n", itemEntry, owner) + @"
local f=_G.CopilotBuddy_EquipCursorFrame
if not f or f.owner~=owner or f.phase~=2 or type(f.handler)~='function'
 or f:GetScript('OnEvent')~=f.handler or not f:IsEventRegistered('CURSOR_UPDATE') then return 0 end
local kind,entry=GetCursorInfo()
if kind~='item' or not CursorHasItem() or tonumber(entry)~=expectedEntry then return 0 end
-- Revoke before the client emits its release event or any nested callback.
f.owner=nil
f.phase=nil
f:UnregisterAllEvents()
f:SetScript('OnEvent',nil)
f.handler=nil
ClearCursor()
return not GetCursorInfo() and not CursorHasItem() and 1 or 0";
        }

        [Obsolete("Use GetReturnValues instead. They do the same.")]
        public static List<string> LuaGetReturnValue(string lua, string scriptName)
        {
            return GetReturnValues(lua, scriptName);
        }

        public static T GetReturnVal<T>(string lua, int retVal)
        {
            return GetReturnVal<T>(lua, (uint)retVal);
        }

        public static T GetReturnVal<T>(string lua, uint retVal)
        {
            try
            {
                var returnValues = GetReturnValues(lua);

                if (retVal >= returnValues.Count)
                    return default(T)!;

                string value = returnValues[(int)retVal];

                // Handle special types
                if (typeof(T) == typeof(bool))
                {
                    // In Lua, nil and false are false, everything else is true
                    bool result = !string.IsNullOrEmpty(value) &&
                                  !value.Equals("nil", StringComparison.OrdinalIgnoreCase) &&
                                  !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                                  !value.Equals("0", StringComparison.OrdinalIgnoreCase);
                    return (T)(object)result;
                }

                if (string.IsNullOrEmpty(value) || value.Equals("nil", StringComparison.OrdinalIgnoreCase))
                    return default(T)!;

                // WoW returns numeric values as hex strings prefixed with "0x" (e.g. GUIDs).
                // Convert.ChangeType / integer Parse(string) don't accept the prefix —
                // strip it and use NumberStyles.HexNumber so 0x000000000000002E -> 0x2E.
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && IsLuaIntegerType(typeof(T)))
                {
                    return ParseInteger<T>(value.Substring(2));
                }

                // Convert to target type
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                ObservationUnavailableException.RethrowCancellation(ex);
                Logging.WriteDebug("Exception in GetReturnVal<{0}>: {1}", typeof(T).Name, ex.Message);
                return default(T)!;
            }
        }

        private static bool IsLuaIntegerType(Type t)
        {
            return t == typeof(ulong) || t == typeof(uint)
                || t == typeof(long) || t == typeof(int)
                || t == typeof(ushort) || t == typeof(short)
                || t == typeof(byte) || t == typeof(sbyte);
        }

        // Parse an integer string (already stripped of the "0x" prefix) with
        // NumberStyles.HexNumber. Convert.ChangeType has no 4-arg overload that
        // accepts NumberStyles, so dispatch to the correct per-type Parse overload.
        private static T ParseInteger<T>(string hex)
        {
            var styles = System.Globalization.NumberStyles.HexNumber;
            var provider = CultureInfo.InvariantCulture;

            if (typeof(T) == typeof(ulong))  return (T)(object)ulong.Parse(hex, styles, provider);
            if (typeof(T) == typeof(uint))   return (T)(object)uint.Parse(hex, styles, provider);
            if (typeof(T) == typeof(long))   return (T)(object)long.Parse(hex, styles, provider);
            if (typeof(T) == typeof(int))    return (T)(object)int.Parse(hex, styles, provider);
            if (typeof(T) == typeof(ushort)) return (T)(object)ushort.Parse(hex, styles, provider);
            if (typeof(T) == typeof(short))  return (T)(object)short.Parse(hex, styles, provider);
            if (typeof(T) == typeof(byte))   return (T)(object)byte.Parse(hex, styles, provider);
            if (typeof(T) == typeof(sbyte))  return (T)(object)sbyte.Parse(hex, styles, provider);

            return (T)Convert.ChangeType(hex, typeof(T), provider);
        }

        public static void DoString(string lua)
        {
            DoString(lua, "CopilotBuddy", 0);
        }

        /// <summary>
        /// FEAT-30: Parses a Lua string value to a typed C# value.
        /// Handles nil, empty, bool, and numeric/string conversion.
        /// Ported from HB 5.4.8.
        /// </summary>
        public static T ParseLuaValue<T>(string val)
        {
            if (string.IsNullOrEmpty(val) || val == "nil")
                return default(T)!;

            if (typeof(T) == typeof(bool))
            {
                string lower = val.ToLower();
                return (T)(object)(lower != "false" && lower != "0");
            }

            // WoW returns numeric values as hex strings prefixed with "0x" (e.g. GUIDs).
            // Mirror GetReturnVal's handling so ParseLuaValue behaves identically.
            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && IsLuaIntegerType(typeof(T)))
            {
                try
                {
                    return ParseInteger<T>(val.Substring(2));
                }
                catch
                {
                    return default(T)!;
                }
            }

            try
            {
                return (T)Convert.ChangeType(val, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return default(T)!;
            }
        }

        public static void DoString(string lua, string luaFile, uint pState)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return;

            var wow = ObjectManager.Wow;
            if (wow == null)
                return;

            try
            {
                // Get Lua state if not provided
                if (pState == 0)
                    pState = wow.Read<uint>((uint)GlobalOffsets.LuaState);

                if (pState == 0)
                    return;

                // Encode script and name
                byte[] luaBytes = Encoding.UTF8.GetBytes(lua);
                byte[] nameBytes = Encoding.UTF8.GetBytes(luaFile);

                var scriptMemory = new AllocatedMemory(luaBytes.Length + nameBytes.Length + 2);
                try
                {
                    scriptMemory.WriteBytes(0, luaBytes);
                    scriptMemory.WriteByte(luaBytes.Length, 0);
                    scriptMemory.WriteBytes(luaBytes.Length + 1, nameBytes);
                    scriptMemory.WriteByte(luaBytes.Length + 1 + nameBytes.Length, 0);

                    uint luaPtr = scriptMemory.Address;
                    uint namePtr = (uint)(scriptMemory.Address + luaBytes.Length + 1);

                    lock (executor.AssemblyLock)
                    {
                        executor.Clear();

                        // luaL_loadbuffer(pState, lua, len, name)
                        executor.AddLine("push {0}", namePtr);
                        executor.AddLine("push {0}", luaBytes.Length);
                        executor.AddLine("push {0}", luaPtr);
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_Load);
                        executor.AddLine("add esp, 16");

                        // lua_pcall(pState, 0, 0, 0)
                        executor.AddLine("push 0");
                        executor.AddLine("push 0");
                        executor.AddLine("push 0");
                        executor.AddLine("push {0}", pState);
                        executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_PCall);
                        executor.AddLine("add esp, 16");

                        executor.AddLine("retn");
                        executor.Execute();
                    }
                }
                finally
                {
                    scriptMemory?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("Exception in DoString: {0} StackTrace:{1}", ex.Message, ex.StackTrace);
            }
        }

        public static void DoString(string szLua, string szLuaFile)
        {
            DoString(szLua, szLuaFile, 0);
        }

        public static void DoString(string format, params object[] args)
        {
            DoString(string.Format(format, args), "CopilotBuddy");
        }

        public static int GetTop(uint pState)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return 0;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", pState);
                executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_GetTop);
                executor.AddLine("add esp, 4");
                executor.AddLine("retn");
                executor.Execute();

                return executor.Memory.Read<int>(executor.ReturnPointer);
            }
        }

        public static void ShowLuaStack(uint pState)
        {
            int top = GetTop(pState);
            for (int i = 1; i <= top; i++)
            {
                string value = ToLString(pState, i, 0);
                Logging.WriteDebug("Stack[{0}]: {1}", i, value);
            }
        }

        public static string ToLString(uint pState, int index, int len)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return string.Empty;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", len);
                executor.AddLine("push {0}", index);
                executor.AddLine("push {0}", pState);
                executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript_ToLString);
                executor.AddLine("add esp, 12");
                executor.AddLine("retn");
                executor.Execute();

                uint strPtr = executor.Memory.Read<uint>(executor.ReturnPointer);
                if (strPtr == 0)
                    return string.Empty;

                return ObjectManager.Wow?.Read<string>(strPtr) ?? string.Empty;
            }
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static T GetLocalizedText<T>(string szLuaVariable)
        {
            string text = GetLocalizedText(szLuaVariable, StyxWoW.Me?.BaseAddress ?? 0);
            return (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static string GetLocalizedText(string szLuaVariable)
        {
            return GetLocalizedText(szLuaVariable, StyxWoW.Me?.BaseAddress ?? 0);
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static string GetLocalizedText(string szLuaVariable, uint lpLocalPlayer)
        {
            var executor = ObjectManager.Executor;
            if (executor == null)
                return string.Empty;

            if (string.IsNullOrEmpty(szLuaVariable))
                return string.Empty;

            if (lpLocalPlayer == 0)
                return string.Empty;

            uint varPtr = 0;
            try
            {
                lock (executor.AssemblyLock)
                {
                    byte[] varBytes = Encoding.UTF8.GetBytes(szLuaVariable + "\0");
                    varPtr = executor.Memory.AllocateMemory(varBytes.Length);
                    executor.Memory.Write(varPtr, varBytes);

                    executor.Clear();
                    executor.AddLine("push -1");
                    executor.AddLine("push {0}", varPtr);
                    executor.AddLine("mov ecx, {0}", lpLocalPlayer);
                    executor.AddLine("call {0}", (uint)GlobalOffsets.FrameScript__GetLocalizedText);
                    executor.AddLine("retn");
                    executor.Execute();

                    uint resultPtr = executor.Memory.Read<uint>(executor.ReturnPointer);
                    if (resultPtr != 0)
                        return executor.Memory.Read<string>(resultPtr) ?? string.Empty;

                    return string.Empty;
                }
            }
            finally
            {
                if (varPtr != 0)
                    executor.Memory.FreeMemory(varPtr);
            }
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static int GetLocalizedInt32(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return int.TryParse(text, out int result) ? result : 0;
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static uint GetLocalizedUInt32(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return uint.TryParse(text, out uint result) ? result : 0;
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static long GetLocalizedInt64(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return long.TryParse(text, out long result) ? result : 0;
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static ulong GetLocalizedUInt64(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return 0;
            return ulong.TryParse(text, out ulong result) ? result : 0;
        }

        [Obsolete("GetLocalizedText is deprecated. Use GetReturnValues instead.")]
        public static bool GetLocalizedBool(string szLuaVariable, uint lpLocalPlayer)
        {
            string text = GetLocalizedText(szLuaVariable, lpLocalPlayer);
            if (string.IsNullOrEmpty(text) || text == "nil")
                return false;
            return bool.TryParse(text, out bool result) && result;
        }

        public static LuaState State
        {
            get
            {
                var wow = ObjectManager.Wow;
                if (wow == null)
                    return new LuaState(0);
                return new LuaState(wow.Read<uint>((uint)GlobalOffsets.LuaState));
            }
        }

        private static LuaEvents _events;

        public static LuaEvents Events
        {
            get { return _events ??= new LuaEvents(); }
        }

        internal static void ProcessEvents()
        {
            try
            {
                Events.ProcessEvents();
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents the Lua interpreter state in WoW memory.
    /// </summary>
    public class LuaState
    {
        // Offset to globals table in LuaState structure for WotLK 3.3.5a (build 12340)
        private const uint GlobalsOffset = 72; // 0x48

        private LuaTable _cachedGlobals;

        public uint Address { get; }

        public LuaState(uint address)
        {
            Address = address;
        }

        public bool IsValid => Address != 0;

        /// <summary>
        /// Gets the global variables table from the Lua state.
        /// This allows direct memory reading of Lua tables without executing Lua code.
        /// </summary>
        public LuaTable Globals
        {
            get
            {
                if (_cachedGlobals == null && Address != 0)
                {
                    // Read the globals table from LuaState + offset
                    // The globals table is stored as a LuaTValue at this offset
                    var tvalue = new LuaTValue(Address + GlobalsOffset);
                    if (tvalue.Type == LuaType.Table)
                    {
                        _cachedGlobals = tvalue.Value.Table;
                    }
                }
                return _cachedGlobals;
            }
        }
    }
}
