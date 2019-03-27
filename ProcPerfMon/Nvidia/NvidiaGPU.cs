/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
  Copyright (C) 2009-2015 Michael Möller <mmoeller@openhardwaremonitor.org>
	Copyright (C) 2011 Christian Vallières
 
*/

using System;
using System.Collections.Generic;

namespace ProcPerfMon.Nvidia
{
    internal struct SensorValue
    {
        public SensorValue(float value, DateTime time)
        {
            Value = value;
            Time = time;
        }

        public float Value { get; }
        public DateTime Time { get; }
    }

    internal struct Sensor
    {
        private float value;

        public readonly string Name;

        public float Value
        {
            get
            {
                return value;
            }
            set
            {
                this.value = value;
                if (this.value <= MinValue)
                {
                    MinValue = value;
                }
                if (this.value >= MaxValue)
                {
                    MaxValue = value;
                }
            }
        }
        public float MinValue;
        public float MaxValue;

        public Sensor(string name)
        {
            Name = name;
            value = 0;
            MinValue = 0;
            MaxValue = 0;
        }
    }

    internal class NvidiaGPU
    {
        public readonly string Identifier;
        private readonly int adapterIndex;
        private readonly NvPhysicalGpuHandle handle;
        private readonly NvDisplayHandle? displayHandle;

        private Sensor[] loads;
        private Sensor memoryLoad;

        public Sensor CoreLoad
        {
            get { return loads[0]; }
        }

        public Sensor MemoryControllerLoad
        {
            get { return loads[1]; }
        }

        public Sensor VideoEngineLoad
        {
            get { return loads[2]; }
        }

        public Sensor MemoryLoad
        {
            get {  return memoryLoad; }
        }

        internal NvidiaGPU(int adapterIndex, NvPhysicalGpuHandle handle, NvDisplayHandle? displayHandle)
        {
            Identifier = "Nvidia GPU";

            this.adapterIndex = adapterIndex;
            this.handle = handle;
            this.displayHandle = displayHandle;

            loads = new Sensor[3];
            loads[0] = new Sensor("GPU Core Load");
            loads[1] = new Sensor("GPU Memory Controller");
            loads[2] = new Sensor("GPU Video Engine Load");

            memoryLoad = new Sensor("GPU Memory Load");

            Update();
        }

        private static string GetName(NvPhysicalGpuHandle handle)
        {
            return "NVIDIA" + ((NVAPI.NvAPI_GPU_GetFullName(handle, out string gpuName) == NvStatus.OK) ? gpuName.Trim() : "");
        }

        public void Update()
        {
            NvPStates states = new NvPStates();
            states.Version = NVAPI.GPU_PSTATES_VER;
            states.PStates = new NvPState[NVAPI.MAX_PSTATES_PER_GPU];

            // Update GPU processor load
            if (NVAPI.NvAPI_GPU_GetPStates != null && NVAPI.NvAPI_GPU_GetPStates(handle, ref states) == NvStatus.OK)
            {
                for (int i = 0; i < loads.Length; i++)
                {
                    if (states.PStates[i].Present)
                    {
                        loads[i].Value = states.PStates[i].Percentage;
                    }
                }
            }
            else
            {
                NvUsages usages = new NvUsages();
                usages.Version = NVAPI.GPU_USAGES_VER;
                usages.Usage = new uint[NVAPI.MAX_USAGES_PER_GPU];
                if (NVAPI.NvAPI_GPU_GetUsages != null && NVAPI.NvAPI_GPU_GetUsages(handle, ref usages) == NvStatus.OK)
                {
                    loads[0].Value = usages.Usage[2];
                    loads[1].Value = usages.Usage[6];
                    loads[2].Value = usages.Usage[10];
                }
            }

            // Update GPU memory load
            NvMemoryInfo memoryInfo = new NvMemoryInfo();
            memoryInfo.Version = NVAPI.GPU_MEMORY_INFO_VER;
            memoryInfo.Values = new uint[NVAPI.MAX_MEMORY_VALUES_PER_GPU];
            if (NVAPI.NvAPI_GPU_GetMemoryInfo != null && displayHandle.HasValue && NVAPI.NvAPI_GPU_GetMemoryInfo(displayHandle.Value, ref memoryInfo) == NvStatus.OK)
            {
                uint totalMemory = memoryInfo.Values[0];
                uint freeMemory = memoryInfo.Values[4];
                float usedMemory = Math.Max(totalMemory - freeMemory, 0);

                memoryLoad.Value = 100f * usedMemory / totalMemory;
            }
        }
    }
}
