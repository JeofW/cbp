using System;
using System.Runtime.InteropServices;

namespace Styx.WoWInternals.Misc
{
	public class WoWClient
	{
		[DllImport("kernel32.dll", EntryPoint = "QueryPerformanceCounter", SetLastError = true)]
		private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

		[DllImport("kernel32.dll", EntryPoint = "GetTickCount")]
		private static extern uint GetTickCount();

		internal WoWClient()
		{
		}

		public uint Latency
		{
			get
			{
				float downKBs;
				float upKBs;
				uint latency;
				GetNetStats(out downKBs, out upKBs, out latency);
				return latency;
			}
		}

		public NetStats NetStats
		{
			get
			{
				// Original build12340 GetNetStats/510AC0 obtains NetClient via
				// 6B0970 -> dword_C79CF4 (13081844), then 6320D0 reads +11860.
				// C7B1F4 is unrelated memory, not an alternate client layout.
				var memory = ObjectManager.Wow;
				if (memory == null)
					throw new Styx.Helpers.ObservationUnavailableException("network-latency", "NetClient memory owner unavailable.");
				return memory.Read<NetStats>(new uint[] { 0x00C79CF4, 11860 });
			}
		}

		public ulong PerformanceCounter()
		{
			// IDA-verified 3.3.5a 12340: PerformanceCounter() calls sub_86ADC0((double*)dword_D4159C)
			// dword_D4159C stores the pointer to the timer struct.
			// sub_86ADC0 struct layout (double* this):
			//   +0  : double multiplier  (*this)
			//   +8  : uint   type        (*((_DWORD*)this + 2)) — 2=QPC, else GetTickCount
			//   +24 : double base        (*(this + 3))
			uint structPtr = ObjectManager.Wow.Read<uint>(0xD4159C);
			if (structPtr == 0)
				return GetTickCount(); // fallback: WoW not initialized yet

			double multiplier = ObjectManager.Wow.Read<double>(structPtr + 0);
			uint timerType    = ObjectManager.Wow.Read<uint>(structPtr + 8);
			double baseVal    = ObjectManager.Wow.Read<double>(structPtr + 24);

			if (timerType != 2)
				return (ulong)(GetTickCount() * multiplier + baseVal);

			long perfCount;
			QueryPerformanceCounter(out perfCount);
			return (ulong)((double)perfCount * multiplier + baseVal);
		}

		public void GetNetStats(out float downKBs, out float upKBs, out uint latency)
		{
				NetStats netStats = NetStats;
				// Both positions belong to the original sixteen-slot latency ring.
				// A torn/unavailable end index above sixteen made the old wrapping
				// loop spin forever; invalid data is not zero latency.
				if (netStats.Latencies == null || netStats.Latencies.Length != 16
					|| netStats.LatencyIndex > 16 || netStats.LatencyCount > 16)
					throw new Styx.Helpers.ObservationUnavailableException("network-latency", "Latency ring unavailable or out of bounds.");
			double elapsedSeconds = (PerformanceCounter() - (ulong)netStats.StartTime) * 0.001;
			downKBs = (float)(netStats.BytesReceived * 0.001 / elapsedSeconds);
			upKBs = (float)(netStats.BytesSent * 0.001 / elapsedSeconds);

			uint latencyIndex = netStats.LatencyIndex;
			uint latencyCount = netStats.LatencyCount;
				ulong totalLatency = 0;
			uint count = 0;

			if (latencyIndex == latencyCount)
			{
				latency = 0;
			}
			else
			{
				do
				{
					if (latencyIndex >= 16)
					{
						latencyIndex = 0;
						if (latencyCount == 0)
						{
							break;
						}
					}
					totalLatency += netStats.Latencies[latencyIndex++];
					count++;
				}
					while (latencyIndex != latencyCount && count < 16);

				if (count != 0)
				{
						latency = (uint)(totalLatency / count);
				}
				else
				{
					latency = 0;
				}
			}
		}
	}
}
