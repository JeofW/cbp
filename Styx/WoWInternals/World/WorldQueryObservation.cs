using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Offsets;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals.World;

/// <summary>
/// Original-client world queries with complete outputs and observed ownership.
/// An unavailable query throws UNKNOWN; it never supplies a clear ray, a hit or
/// an indoor/outdoor answer. These observations are not physical traversal proof.
/// </summary>
internal static class WorldQueryObservation
{
    /// <summary>
    /// Capture the existing strict native-session identity for a later pure
    /// entry check. Rechecking this owner performs no Lua/native execution and
    /// does not mutate the executor's prepared command.
    /// </summary>
    internal static Func<bool> CaptureLocalOwner(LocalPlayer player) => Observe<Func<bool>>("world-local-owner", player, session =>
    {
        if (!ReferenceEquals(ObjectManager.Me, player)) throw session.Unavailable("player is no longer current");
        return () =>
        {
            session.RequireCurrent();
            return ReferenceEquals(ObjectManager.Me, player);
        };
    });

    /// <summary>
    /// Executes the original interaction ABI under the same native identity
    /// checks as world observations. True covers local executor return only;
    /// an exception after entry leaves the world/UI result unknown.
    /// The optional owner predicate must only inspect state, never prepare or
    /// execute another native command while this command is being assembled.
    /// </summary>
    internal static bool SubmitInteraction(WoWObject subject, uint vtableOffset,
        Func<bool>? ownerCurrent, Action prepare) => Observe("world-interaction", subject, session =>
    {
        void RequireAdmission()
        {
            session.RequireCurrent();
            bool allowed = ownerCurrent == null || ownerCurrent();
            session.RequireCurrent();
            if (!allowed) throw session.Unavailable("interaction owner changed");
        }

        RequireAdmission();
        prepare(); // Timer/AFK/logging callbacks remain inside the captured session.
        RequireAdmission();
        var executor = session.Executor;
        executor.Clear();
        executor.AddLine("mov ecx, {0}", subject.BaseAddress);
        executor.AddLine("mov eax, [ecx]");
        executor.AddLine("add eax, {0}", vtableOffset);
        executor.AddLine("mov eax, [eax]");
        executor.AddLine("call eax");
        executor.AddLine("retn");
        RequireAdmission();
        executor.Execute();
        RequireAdmission();
        Logging.WriteDebug("[Interact] Local executor returned for object at 0x{0:X}; awaiting world/UI observation", subject.BaseAddress);
        RequireAdmission();
        return true;
    });

    internal static bool ReadOutdoors(WoWObject subject) => Observe("object-outdoors", subject, session =>
    {
        session.Executor.Clear();
        session.Executor.AddLine("mov ecx, {0}", subject.BaseAddress);
        session.Executor.AddLine("call {0}", (uint)Styx.Patchables.GlobalOffsets.IsOutdoors);
        session.Executor.AddLine("retn");
        session.RequireCurrent();
        session.Executor.Execute();
        session.RequireCurrent();
        return session.ReadReturnedBoolean();
    });

    internal static bool ReadLocalOutdoors(LocalPlayer player) => Observe("player-outdoors", player, session =>
    {
        if (!ReferenceEquals(ObjectManager.Me, player))
            throw session.Unavailable("player is no longer current");
        // In the original API nil is a valid indoor answer. Only a complete
        // envelope can distinguish it from a missing/failed Lua return.
        var values = Lua.GetObservedReturnValues("local v=IsOutdoors(); return 'world-outdoors',v and '1' or '0'");
        session.RequireCurrent();
        if (values == null || values.Count != 2 || values[0] != "world-outdoors"
            || values[1] != "0" && values[1] != "1")
            throw session.Unavailable("incomplete outdoor envelope");
        return values[1] == "1";
    });

    internal static bool ReadLocalVehicle(LocalPlayer player) => Observe("player-vehicle", player, session =>
    {
        if (!ReferenceEquals(ObjectManager.Me, player))
            throw session.Unavailable("player is no longer current");
        // A complete original-client Lua envelope distinguishes a valid nil
        // (not in a vehicle) from failed execution or an incomplete result.
        var values = Lua.GetObservedReturnValues("local v=UnitInVehicle('player'); return 'world-vehicle',v and '1' or '0'");
        session.RequireCurrent();
        if (values == null || values.Count != 2 || values[0] != "world-vehicle"
            || values[1] != "0" && values[1] != "1")
            throw session.Unavailable("incomplete vehicle envelope");
        return values[1] == "1";
    });

    internal readonly record struct GroundUnitState(uint MountDisplayId, ShapeshiftForm Form, uint Flags)
    {
        internal bool Mounted => MountDisplayId != 0 || Form is ShapeshiftForm.FlightForm or ShapeshiftForm.EpicFlightForm;
        internal bool OnTaxi => (Flags & (uint)UnitFlags.OnTaxi) != 0;
        internal bool Rooted => (Flags & (uint)UnitFlags.Rooted) != 0;
        internal bool Stunned => (Flags & (uint)UnitFlags.Stunned) != 0;
    }

    /// <summary>
    /// Complete original-build mount/form/unit flags. Pure memory observation:
    /// safe inside a prepared command's entry guard; never executes Lua/native
    /// code or substitutes the legacy descriptor getter's failed-read zero.
    /// </summary>
    internal static GroundUnitState ReadGroundUnitState(LocalPlayer player) => Observe("ground-unit-state", player, session =>
    {
        uint address = player.BaseAddress;
        ulong guid = player.Guid;
        if (!ReferenceEquals(ObjectManager.Me, player) || address == 0 || address > uint.MaxValue - 55 || guid == 0)
            throw session.Unavailable("player descriptor owner unavailable");

        uint UInt(uint pointer) => BitConverter.ToUInt32(session.ReadCompletedBytes(pointer, 4), 0);
        ulong GuidAt(uint pointer) => BitConverter.ToUInt64(session.ReadCompletedBytes(pointer, 8), 0);
        // Existing build12340 object+8 descriptor pointer / object+48 raw GUID,
        // and absolute original unit-field indices already used by WoWUnit.
        uint descriptor = UInt(address + 8);
        if (descriptor == 0 || GuidAt(address + 48) != guid || GuidAt(descriptor) != guid)
            throw session.Unavailable("raw object/descriptor identity mismatch");
        uint Field(WoWUnitFields field) => UInt(checked(descriptor + (uint)field * 4u));
        GroundUnitState Read()
        {
            uint mount = Field(WoWUnitFields.MountDisplayId);
            uint bytes = Field(WoWUnitFields.Bytes2);
            uint flags = Field(WoWUnitFields.Flags);
            var form = (ShapeshiftForm)(bytes >> 24);
            if (!Enum.IsDefined(typeof(ShapeshiftForm), form))
                throw session.Unavailable("unresolved original-client shapeshift form");
            return new GroundUnitState(mount, form, flags);
        }
        var observed = Read();
        if (Read() != observed || UInt(address + 8) != descriptor
            || GuidAt(address + 48) != guid || GuidAt(descriptor) != guid)
            throw session.Unavailable("mount/form/flags or descriptor owner changed during observation");
        session.RequireCurrent();
        return observed;
    });

    internal static bool TraceLine(WoWPoint from, WoWPoint to, float distance,
        GameWorld.CGWorldFrameHitFlags flags, out WoWPoint hitPoint)
    {
        WoWPoint observed = WoWPoint.Zero;
        bool hit = Observe("world-collision", null, session =>
        {
            if (!Finite(from) || !Finite(to) || from.DistanceSqr(to) <= 0
                || !float.IsFinite(distance) || distance <= 0 || distance > 1)
                throw session.Unavailable("invalid collision segment");
            return WithBuffer(session, 40, buffer =>
            {
                // Keep the original 12340 intersect ABI and its 40-byte layout.
                buffer.Write(0, from);
                buffer.Write(12, to);
                buffer.Write(24, distance);
                buffer.Write(28, new WoWPoint(float.NaN, float.NaN, float.NaN));
                var executor = session.Executor;
                executor.Clear();
                executor.AddLine("push 0");
                executor.AddLine("push {0}", (uint)flags);
                executor.AddLine("push {0}", buffer.At(24));
                executor.AddLine("push {0}", buffer.At(28));
                executor.AddLine("push {0}", buffer.At(12));
                executor.AddLine("push {0}", buffer.At(0));
                executor.AddLine("call {0}", GlobalOffsets.CGWorldFrame_Intersect);
                executor.AddLine("add esp, 0x18");
                executor.AddLine("retn");
                session.RequireCurrent();
                executor.Execute();
                session.RequireCurrent();
                bool intersects = session.ReadReturnedBoolean();
                if (intersects)
                {
                    byte[] bytes = session.ReadCompletedBytes(buffer.At(28), 12);
                    observed = new WoWPoint(BitConverter.ToSingle(bytes, 0),
                        BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
                    if (!OnSegment(from, to, observed, distance))
                        throw session.Unavailable("collision point is not on the requested segment");
                }
                return intersects;
            });
        });
        hitPoint = observed;
        return hit;
    }

    private static bool Finite(WoWPoint point) => float.IsFinite(point.X)
        && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    internal static void TraceLines(WorldLine[] lines, GameWorld.CGWorldFrameHitFlags[] flags,
        out bool[] hitResults, out WoWPoint[] hitPoints)
    {
        if (lines == null || flags == null || lines.Length != flags.Length)
            throw new ArgumentException("Collision lines and flags must have matching lengths.");
        if (lines.Length == 0)
        {
            hitResults = Array.Empty<bool>(); hitPoints = Array.Empty<WoWPoint>();
            return; // The native loop has no precondition branch for an empty batch.
        }
        // Bound allocation and native execution. Callers can partition a larger
        // observation, but must not present separately observed batches as atomic.
        if (lines.Length > 4096)
            throw new ObservationUnavailableException("world-collision-batch", "collision batch exceeds 4096 segments");
        var segments = (WorldLine[])lines.Clone();
        var masks = (GameWorld.CGWorldFrameHitFlags[])flags.Clone();
        int length = segments.Length;
        bool[] results = new bool[length];
        WoWPoint[] points = new WoWPoint[length];
        Observe("world-collision-batch", null, session =>
        {
            bool InputsCurrent()
            {
                for (int i = 0; i < length; i++)
                    if (!lines[i].Start.Equals(segments[i].Start) || !lines[i].End.Equals(segments[i].End) || flags[i] != masks[i]) return false;
                return true;
            }
            for (int i = 0; i < length; i++)
                if (!Finite(segments[i].Start) || !Finite(segments[i].End) || segments[i].Start.DistanceSqr(segments[i].End) <= 0)
                    throw session.Unavailable("invalid batch segment");
            return WithBuffer(session, checked(length * 41 + 4), buffer =>
            {
                // Preserve the original packed input layout and one executor
                // round trip. Every output slot starts with an invalid sentinel.
                int pointOffset = length, inputOffset = length * 13, distanceOffset = length * 41;
                for (int i = 0; i < length; i++)
                {
                    buffer.Write(i, byte.MaxValue);
                    buffer.Write(pointOffset + i * 12, new WoWPoint(float.NaN, float.NaN, float.NaN));
                    buffer.Write(inputOffset + i * 28, (uint)masks[i]);
                    buffer.Write(inputOffset + i * 28 + 4, segments[i].End);
                    buffer.Write(inputOffset + i * 28 + 16, segments[i].Start);
                }
                var executor = session.Executor;
                executor.Clear();
                executor.AddLine("mov ebx, 0");
                executor.AddLine("mov esi, {0}", buffer.At(pointOffset));
                executor.AddLine("mov edi, {0}", buffer.At(inputOffset));
                executor.AddLine("@loop:");
                executor.AddLine("mov eax, {0}", buffer.At(distanceOffset));
                executor.AddLine("mov edx, {0}", BitConverter.ToUInt32(BitConverter.GetBytes(1f), 0));
                executor.AddLine("mov [eax], edx");
                executor.AddLine("push 0");
                executor.AddLine("mov eax, edi");
                executor.AddLine("mov eax, [eax]");
                executor.AddLine("push eax");
                executor.AddLine("push {0}", buffer.At(distanceOffset));
                executor.AddLine("push esi");
                executor.AddLine("mov eax, edi");
                executor.AddLine("add eax, 4");
                executor.AddLine("push eax");
                executor.AddLine("add eax, 12");
                executor.AddLine("push eax");
                executor.AddLine("call {0}", GlobalOffsets.CGWorldFrame_Intersect);
                executor.AddLine("add esp, 0x18");
                executor.AddLine("mov edx, {0}", buffer.At(0));
                executor.AddLine("add edx, ebx");
                executor.AddLine("mov [edx], al");
                executor.AddLine("inc ebx");
                executor.AddLine("add esi, 12");
                executor.AddLine("add edi, 28");
                executor.AddLine("cmp ebx, {0}", length);
                executor.AddLine("jl @loop");
                executor.AddLine("retn");
                session.RequireCurrent();
                if (!InputsCurrent()) throw session.Unavailable("collision input changed before dispatch");
                executor.Execute();
                session.RequireCurrent();
                var frame = executor.FrameCount;
                uint returned = executor.ReturnPointer;
                byte[] hitBytes = session.ReadCompletedBytes(buffer.At(0), length);
                byte[] pointBytes = session.ReadCompletedBytes(buffer.At(pointOffset), checked(length * 12));
                if (executor.FrameCount != frame || executor.ReturnPointer != returned || !InputsCurrent())
                    throw session.Unavailable("collision batch or result epoch changed");
                for (int i = 0; i < length; i++)
                {
                    if (hitBytes[i] > 1) throw session.Unavailable("invalid batch collision boolean");
                    results[i] = hitBytes[i] == 1;
                    if (!results[i]) continue;
                    points[i] = new WoWPoint(BitConverter.ToSingle(pointBytes, i * 12),
                        BitConverter.ToSingle(pointBytes, i * 12 + 4), BitConverter.ToSingle(pointBytes, i * 12 + 8));
                    if (!OnSegment(segments[i].Start, segments[i].End, points[i], 1f))
                        throw session.Unavailable("batch collision point is not on its segment");
                }
                return true;
            });
        });
        hitResults = results; hitPoints = points;
    }

    private static bool OnSegment(WoWPoint from, WoWPoint to, WoWPoint point, float distance)
    {
        if (!Finite(point)) return false;
        double x = to.X - from.X, y = to.Y - from.Y, z = to.Z - from.Z;
        double length = x * x + y * y + z * z;
        double fraction = ((point.X - from.X) * x + (point.Y - from.Y) * y + (point.Z - from.Z) * z) / length;
        if (!double.IsFinite(fraction) || fraction < -0.001 || fraction > distance + 0.001) return false;
        double dx = point.X - (from.X + fraction * x), dy = point.Y - (from.Y + fraction * y), dz = point.Z - (from.Z + fraction * z);
        return dx * dx + dy * dy + dz * dz <= 0.01;
    }

    private static T Observe<T>(string observation, WoWObject? subject, Func<Session, T> query)
    {
        try
        {
            var session = new Session(observation, subject);
            lock (session.Executor.AssemblyLock)
            {
                session.RequireCurrent();
                T result = query(session);
                session.RequireCurrent();
                return result;
            }
        }
        catch (Exception error)
        {
            RecoveryActions.RethrowControlFlow(error);
            if (error is ObservationUnavailableException) throw;
            throw new ObservationUnavailableException(observation, error.GetType().Name + ": " + error.Message);
        }
    }

    private static T WithBuffer<T>(Session session, int count, Func<Buffer, T> query)
    {
        var buffer = new Buffer(session, count);
        T result = default!;
        Exception? failure = null;
        try { result = query(buffer); }
        catch (Exception error) { failure = error; }
        try { buffer.Dispose(); }
        catch (Exception cleanup)
        {
            // Preserve the first actual stop/loss signal through cleanup. A new
            // stop signal outranks an ordinary observation failure.
            if (failure == null || !ControlSignal(failure) && ControlSignal(cleanup)) failure = cleanup;
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static bool ControlSignal(Exception error)
    {
        while (error is TargetInvocationException { InnerException: not null } wrapped) error = wrapped.InnerException;
        return error is OperationCanceledException or ThreadInterruptedException or InvalidProcessException or InvalidExecutorException;
    }

    private sealed class Session
    {
        private readonly string observation;
        internal readonly Memory Memory;
        internal readonly ExecutorRand Executor;
        private readonly LocalPlayer actor;
        private readonly WoWObject? subject;
        private readonly object? run, bot;
        private readonly bool running, alive, ghost;
        private readonly ulong actorGuid, subjectGuid;
        private readonly uint actorAddress, subjectAddress, map;
        private readonly int processId;
        private readonly IntPtr processHandle;

        internal Session(string observation, WoWObject? subject)
        {
            this.observation = observation;
            Memory = ObjectManager.Wow ?? throw Unavailable("memory unavailable");
            Executor = ObjectManager.Executor ?? throw Unavailable("executor unavailable");
            actor = ObjectManager.Me ?? throw Unavailable("player unavailable");
            this.subject = subject;
            processId = Memory.ProcessId; processHandle = Memory.ProcessHandle;
            actorGuid = actor.Guid; actorAddress = actor.BaseAddress; map = actor.MapId;
            alive = actor.IsAlive; ghost = actor.IsGhost;
            subjectGuid = subject?.Guid ?? 0; subjectAddress = subject?.BaseAddress ?? 0;
            run = TreeRoot.RunIdentity; bot = TreeRoot.Current; running = TreeRoot.IsRunning;
            RequireCurrent();
        }

        internal ObservationUnavailableException Unavailable(string reason) => new(observation, reason);
        internal bool OwnsProcess => Memory.ProcessId == processId && Memory.ProcessHandle == processHandle && processHandle != IntPtr.Zero;

        internal void RequireCurrent()
        {
            if (Memory.ProcessHandle == IntPtr.Zero) throw new InvalidProcessException("World query process is closed.");
            if (!Executor.IsOpen || !Executor.IsInitialized) throw new InvalidExecutorException("World query executor is closed.");
            if (!OwnsProcess || !ReferenceEquals(ObjectManager.Wow, Memory) || !ReferenceEquals(ObjectManager.Executor, Executor)
                || !ReferenceEquals(Executor.Memory, Memory) || !ReferenceEquals(ObjectManager.Me, actor)
                || actorGuid == 0 || actorAddress == 0 || !actor.IsValid || actor.Guid != actorGuid || actor.BaseAddress != actorAddress
                || actor.MapId != map || actor.IsAlive != alive || actor.IsGhost != ghost
                || !ReferenceEquals(TreeRoot.RunIdentity, run) || !ReferenceEquals(TreeRoot.Current, bot) || TreeRoot.IsRunning != running
                || subject != null && (subjectGuid == 0 || subjectAddress == 0 || !subject.IsValid
                    || subject.Guid != subjectGuid || subject.BaseAddress != subjectAddress))
                throw Unavailable("world query owner changed");
        }

        internal byte[] ReadCompletedBytes(uint pointer, int count)
        {
            RequireCurrent();
            if (pointer == 0 || count <= 0 || pointer > uint.MaxValue - (uint)count + 1)
                throw Unavailable("invalid result range");
            var frame = Executor.FrameCount;
            uint returnPointer = Executor.ReturnPointer;
            byte[] bytes;
            using (Memory.TemporaryCacheState(false)) bytes = Memory.ReadBytes(pointer, count);
            RequireCurrent();
            if (bytes == null || bytes.Length != count || Executor.FrameCount != frame || Executor.ReturnPointer != returnPointer)
                throw Unavailable("incomplete result or replaced executor result epoch");
            return bytes;
        }

        internal bool ReadReturnedBoolean()
        {
            byte[] bytes = ReadCompletedBytes(Executor.ReturnPointer, 1);
            if (bytes[0] > 1) throw Unavailable("invalid native boolean");
            return bytes[0] == 1;
        }
    }

    /// <summary>Never resolve global memory again when writing or freeing a native query buffer.</summary>
    private sealed class Buffer : IDisposable
    {
        private readonly Session session;
        private readonly int count;
        private uint address;
        internal Buffer(Session session, int count)
        {
            this.session = session; this.count = count;
            session.RequireCurrent();
            address = session.Memory.AllocateMemory(count);
            if (address == 0 || address > uint.MaxValue - (uint)count + 1)
                throw session.Unavailable("native buffer allocation unavailable");
            try { session.RequireCurrent(); }
            catch (Exception error)
            {
                Exception failure = error;
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    if (!ControlSignal(failure) && ControlSignal(cleanup)) failure = cleanup;
                }
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }
        internal uint At(int offset)
        {
            if (address == 0 || offset < 0 || offset >= count) throw session.Unavailable("native buffer range unavailable");
            return checked(address + (uint)offset);
        }
        internal void Write<T>(int offset, T value) where T : struct
        {
            session.RequireCurrent();
            session.Memory.Write(At(offset), value);
            session.RequireCurrent();
        }
        public void Dispose()
        {
            uint release = address; address = 0;
            if (release != 0 && session.OwnsProcess && !session.Memory.FreeMemory(release))
                throw session.Unavailable("native buffer release failed");
        }
    }
}
