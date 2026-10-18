using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using PulseDock.Models;

namespace PulseDock.Services
{
    /// <summary>
    /// "Advanced sensors" (optional, off by default): CPU and disk temperatures via LibreHardwareMonitor.
    /// Uses the WinRing0 driver, which Microsoft Defender flags as a hacking tool, and requires admin.
    /// GPUs do NOT depend on this (see GpuSensors).
    /// Use only on the sampler thread.
    /// </summary>
    public sealed class HardwareSensors : IDisposable
    {
        private Computer? _computer;

        public bool Available { get; private set; }

        public void Open()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = false,
                    IsStorageEnabled = true
                };
                _computer.Open();
                Available = true;
                Log.Info("LibreHardwareMonitor: " + string.Join(", ", _computer.Hardware.Select(h => $"{h.HardwareType}:{h.Name}")));
            }
            catch (Exception ex)
            {
                Log.Error("HardwareSensors.Open", ex);
                Available = false;
                _computer = null;
            }
        }

        public void Read(Snapshot snap)
        {
            snap.HardwareSensorsAvailable = Available;
            if (_computer == null) return;

            double? diskTemp = null;

            foreach (var hw in _computer.Hardware.ToArray())
            {
                try
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();

                    switch (hw.HardwareType)
                    {
                        case HardwareType.Cpu:
                            snap.CpuTemperature = PickCpuTemperature(hw) ?? snap.CpuTemperature;
                            break;

                        case HardwareType.Storage:
                            // First disk with an available temperature.
                            if (diskTemp == null)
                                diskTemp = FirstValue(hw, SensorType.Temperature, "Temperature", "Composite Temperature");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("HardwareSensors.Read " + hw.Name, ex);
                }
            }

            snap.DiskTemperature = diskTemp;
        }

        private static double? PickCpuTemperature(IHardware hw)
        {
            return FirstValue(hw, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Tdie", "Core Max", "Core Average")
                   ?? MaxValue(hw, SensorType.Temperature);
        }

        private static IEnumerable<ISensor> AllSensors(IHardware hw)
        {
            foreach (var s in hw.Sensors) yield return s;
            foreach (var sub in hw.SubHardware)
                foreach (var s in sub.Sensors)
                    yield return s;
        }

        private static double? FirstValue(IHardware hw, SensorType type, params string[] names)
        {
            var sensors = AllSensors(hw).Where(s => s.SensorType == type && s.Value.HasValue).ToList();
            foreach (var name in names)
            {
                var hit = sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (hit != null && IsSane(type, hit.Value!.Value)) return hit.Value.Value;
            }
            return null;
        }

        private static double? MaxValue(IHardware hw, SensorType type)
        {
            var values = AllSensors(hw)
                .Where(s => s.SensorType == type && s.Value.HasValue && IsSane(type, s.Value!.Value))
                .Select(s => (double)s.Value!.Value)
                .ToList();
            return values.Count > 0 ? values.Max() : null;
        }

        private static bool IsSane(SensorType type, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            return type switch
            {
                SensorType.Temperature => v > 0 && v < 150,
                SensorType.Load => v >= 0 && v <= 100.5,
                _ => v >= 0
            };
        }

        public bool IsOpen => _computer != null;

        public void Dispose()
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
            Available = false;
        }
    }
}
