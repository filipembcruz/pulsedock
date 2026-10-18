using System.Collections.Generic;

namespace PulseDock.Models
{
    public sealed class GpuReading
    {
        public string Name { get; set; } = "";
        public bool Integrated { get; set; }
        public double? Load { get; set; }
        public double? Temperature { get; set; }
        public double? MemoryUsedMb { get; set; }
        public double? MemoryTotalMb { get; set; }
    }

    /// <summary>A complete system reading.</summary>
    public sealed class Snapshot
    {
        public double CpuLoad { get; set; }
        public double? CpuGhz { get; set; }
        public double? CpuTemperature { get; set; }

        public double MemoryUsedBytes { get; set; }
        public double MemoryTotalBytes { get; set; }
        public double MemoryLoad { get; set; }

        public string DiskName { get; set; } = "C:";
        public double DiskActive { get; set; }
        public double DiskReadBps { get; set; }
        public double DiskWriteBps { get; set; }
        public double? DiskTemperature { get; set; }

        /// <summary>Ordered: integrated first (GPU 0), then dedicated.</summary>
        public List<GpuReading> Gpus { get; set; } = new();

        public string NetworkName { get; set; } = "";
        /// <summary>Bytes per second.</summary>
        public double NetDownBps { get; set; }
        public double NetUpBps { get; set; }

        public bool HardwareSensorsAvailable { get; set; }
    }

    public sealed class ProcInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double CpuPercent { get; set; }
        public long MemoryBytes { get; set; }
        public double DiskBps { get; set; }
    }
}
