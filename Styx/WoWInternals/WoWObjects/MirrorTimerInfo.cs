using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals.WoWObjects
{
	[StructLayout(LayoutKind.Sequential, Size = 28)]
	public struct MirrorTimerInfo
	{
		[MarshalAs(UnmanagedType.U4)]
		public MirrorTimerType Type;

		public int InitialValue;

		public int MaxValue;

		public int ChangePerMillisecond;

		private uint _paused;

		public uint SpellID;

		public uint StartTime;

		public uint CurrentTime
		{
			get
			{
				// Client ticks wrap as uint; remaining time must not. A negative
				// countdown is empty, not billions of milliseconds of oxygen.
				if (MaxValue <= 0)
					return 0;
				uint elapsed = _paused != 0 ? 0U : unchecked(ObjectManager.PerformanceCounter - StartTime);
				long remaining = (long)InitialValue + (long)ChangePerMillisecond * elapsed;
				return (uint)Math.Clamp(remaining, 0L, (long)MaxValue);
			}
		}

		public bool IsVisible
		{
			get
			{
				return this.StartTime != 0U;
			}
		}

		public override string ToString()
		{
			return string.Format("Type: {0}, InitialValue: {1}, MaxValue: {2}, ChangePerMillisecond: {3}, Paused: {4}, StartTime: {5}, SpellID: {6}",
				this.Type,
				this.InitialValue,
				this.MaxValue,
				this.ChangePerMillisecond,
				this._paused,
				this.StartTime,
				this.SpellID);
		}
	}
}
