using ProcPerfMon.Nvidia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ProcPerfMon
{
    public class SensorValue
    {
        protected float value;

        public SensorValue(float value, DateTime time)
        {
            this.value = value;
            Time = time;
        }

        public DateTime Time
        {
            get;
            protected set;
        }
    }

    public abstract class Sensor
    {
        public string Name
        {
            get;
            protected set;
        }

        public abstract float NextValue();
    }

    public class CpuSensor : Sensor
    {
        private readonly PerformanceCounter counter;

        public CpuSensor(Process process)
        {
            Name = "CPU Load";
            counter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName);
        }

        public override float NextValue()
        {
			return counter.NextValue() / Environment.ProcessorCount;
        }
    }

    public class RamSensor : Sensor
    {
        private readonly PerformanceCounter counter;

        public float TotalRam
        {
            private set;
            get;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        public RamSensor(Process process)
        {
            Name = "RAM Load";
            counter = new PerformanceCounter("Process", "Working Set - Private", process.ProcessName);

            MemoryStatusEx status = new MemoryStatusEx
            {
                Length = checked((uint)Marshal.SizeOf(typeof(MemoryStatusEx)))
            };

            TotalRam = GlobalMemoryStatusEx(status) ? (status.TotalPhys / (1024 * 1024)) : 1f;
        }

        public override float NextValue()
        {
			return counter.NextValue() / (1024 * 1024); // usage in MB
        }
    }

    public abstract class GpuSensor : Sensor
    {
        protected NvPStates states;
        protected NvPhysicalGpuHandle gpuHandle;
        protected NvDisplayHandle displayHandle;

        public GpuSensor()
        {
            if (!NVAPI.IsAvailable)
            {
                throw new Exception("Unable to obtain primary GPU; NVAPI is not available");
            }

            NvPhysicalGpuHandle[] handles = new NvPhysicalGpuHandle[NVAPI.MAX_PHYSICAL_GPUS];

            int numGpus;
            if (NVAPI.NvAPI_EnumPhysicalGPUs == null)
            {
                throw new Exception("Unable to obtain primary GPU; NvAPI_EnumPhysicalGPUs not available");
            }
            else
            {
                NvStatus status = NVAPI.NvAPI_EnumPhysicalGPUs(handles, out numGpus);
                if (status != NvStatus.OK)
                {
                    throw new Exception("Unable to obtain primary GPU");
                }
            }

            if (numGpus < 1)
            {
                throw new Exception("Unable to obtain primary GPU");
            }

            gpuHandle = handles[0];

            IDictionary<NvPhysicalGpuHandle, NvDisplayHandle> displayHandles = new Dictionary<NvPhysicalGpuHandle, NvDisplayHandle>();

            if (NVAPI.NvAPI_EnumNvidiaDisplayHandle != null && NVAPI.NvAPI_GetPhysicalGPUsFromDisplay != null)
            {
                int i = 0;
                NvStatus status = NvStatus.OK;
                while (status == NvStatus.OK)
                {
                    NvDisplayHandle displayHandle = new NvDisplayHandle();
                    status = NVAPI.NvAPI_EnumNvidiaDisplayHandle(i, ref displayHandle);
                    i++;

                    if (status == NvStatus.OK)
                    {
                        NvPhysicalGpuHandle[] handlesFromDisplay = new NvPhysicalGpuHandle[NVAPI.MAX_PHYSICAL_GPUS];
                        if (NVAPI.NvAPI_GetPhysicalGPUsFromDisplay(displayHandle, handlesFromDisplay, out uint countFromDisplay) == NvStatus.OK)
                        {
                            for (int j = 0; j < countFromDisplay; j++)
                            {
                                if (!displayHandles.ContainsKey(handlesFromDisplay[j]))
                                {
                                    displayHandles.Add(handlesFromDisplay[j], displayHandle);
                                }
                            }
                        }
                    }
                }
            }

            displayHandles.TryGetValue(handles[0], out displayHandle);
        }
    }

    public class GpuCoreSensor : GpuSensor
    {
        public GpuCoreSensor()
        {
            Name = "GPU Core Load";
        }

        public override float NextValue()
        {
            // Update GPU processor load
            NvPStates states = new NvPStates
            {
                Version = NVAPI.GPU_PSTATES_VER,
                PStates = new NvPState[NVAPI.MAX_PSTATES_PER_GPU]
            };

            if (NVAPI.NvAPI_GPU_GetPStates != null && NVAPI.NvAPI_GPU_GetPStates(gpuHandle, ref states) == NvStatus.OK)
            {
                if (states.PStates[0].Present)
                {
                    return states.PStates[0].Percentage;
                }
            }
            else
            {
                NvUsages usages = new NvUsages
                {
                    Version = NVAPI.GPU_USAGES_VER,
                    Usage = new uint[NVAPI.MAX_USAGES_PER_GPU]
                };

                if (NVAPI.NvAPI_GPU_GetUsages != null && NVAPI.NvAPI_GPU_GetUsages(gpuHandle, ref usages) == NvStatus.OK)
                {
                    return usages.Usage[2];
                }
            }

            return 0;
        }
    }

    public class GpuVideoSensor : GpuSensor
    {
        public GpuVideoSensor()
        {
            Name = "GPU Video Engine Load";
        }

        public override float NextValue()
        {
            // Update GPU processor load
            NvPStates states = new NvPStates
            {
                Version = NVAPI.GPU_PSTATES_VER,
                PStates = new NvPState[NVAPI.MAX_PSTATES_PER_GPU]
            };

            if (NVAPI.NvAPI_GPU_GetPStates != null && NVAPI.NvAPI_GPU_GetPStates(gpuHandle, ref states) == NvStatus.OK)
            {
                if (states.PStates[0].Present)
                {
                    return states.PStates[2].Percentage;
                }
            }
            else
            {
                NvUsages usages = new NvUsages
                {
                    Version = NVAPI.GPU_USAGES_VER,
                    Usage = new uint[NVAPI.MAX_USAGES_PER_GPU]
                };

                if (NVAPI.NvAPI_GPU_GetUsages != null && NVAPI.NvAPI_GPU_GetUsages(gpuHandle, ref usages) == NvStatus.OK)
                {
                    return usages.Usage[10];
                }
            }

            return 0;
        }
    }

    public class GpuVramSensor : GpuSensor
    {
        public float TotalVram // VRAM in MB
        {
            private set;
            get;
        }

        public GpuVramSensor()
        {
            Name = "GPU Memory Load";

            NvMemoryInfo memoryInfo = new NvMemoryInfo
            {
                Version = NVAPI.GPU_MEMORY_INFO_VER,
                Values = new uint[NVAPI.MAX_MEMORY_VALUES_PER_GPU]
            };

            if (NVAPI.NvAPI_GPU_GetMemoryInfo != null && NVAPI.NvAPI_GPU_GetMemoryInfo(displayHandle, ref memoryInfo) == NvStatus.OK)
            {
                TotalVram = memoryInfo.Values[0] / 1024f;
            }
            else
            {
                TotalVram = 1f;
            }
        }

        public override float NextValue()
        {
            // Update GPU memory load
            NvMemoryInfo memoryInfo = new NvMemoryInfo
            {
                Version = NVAPI.GPU_MEMORY_INFO_VER,
                Values = new uint[NVAPI.MAX_MEMORY_VALUES_PER_GPU]
            };

            if (NVAPI.NvAPI_GPU_GetMemoryInfo != null && NVAPI.NvAPI_GPU_GetMemoryInfo(displayHandle, ref memoryInfo) == NvStatus.OK)
            {
                uint freeMemory = memoryInfo.Values[4] / 1024;
                return Math.Max((uint)TotalVram - freeMemory, 0);
            }

            return 0;
        }
    }
}
