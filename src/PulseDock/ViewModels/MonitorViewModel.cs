using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PulseDock.Models;
using PulseDock.Services;

namespace PulseDock.ViewModels
{
    public enum MetricKind { Cpu, Memory, Disk, Gpu0, Gpu1 }

    /// <summary>One widget row (CPU, Memory, Disk, GPU 0, GPU 1).</summary>
    public sealed class MetricViewModel : ObservableObject
    {
        private string _label = "";
        private string _suffix = "";
        private string _shortLabel = "";
        private string _valueText = "–";
        private string _detail = "";
        private string _detailShort = "";
        private double _percent;
        private bool _isVisible = true;
        private string? _toolTip;

        public MetricViewModel(MetricKind kind, Color color, Action<MetricKind>? openDetails)
        {
            Kind = kind;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
            HasDetails = openDetails != null;
            OpenDetailsCommand = new RelayCommand(_ => openDetails?.Invoke(kind), _ => openDetails != null);
        }

        public MetricKind Kind { get; }
        public Brush Brush { get; }
        public bool HasDetails { get; }
        public ICommand OpenDetailsCommand { get; }

        public string Label { get => _label; set { if (Set(ref _label, value)) Raise(nameof(CompactLabel)); } }
        public string Suffix { get => _suffix; set { if (Set(ref _suffix, value)) Raise(nameof(CompactLabel)); } }
        /// <summary>Label for the compact layouts ("Disk C:").</summary>
        public string CompactLabel => Kind == MetricKind.Disk ? _label + " " + _suffix : _label;
        public string ShortLabel { get => _shortLabel; set => Set(ref _shortLabel, value); }
        public string ValueText { get => _valueText; set => Set(ref _valueText, value); }
        public string Detail { get => _detail; set { if (Set(ref _detail, value)) Raise(nameof(DetailCompact)); } }
        /// <summary>Version for the compact vertical layout (without the "VRAM" prefix, to fit).</summary>
        public string DetailCompact => _detail.Replace("VRAM ", "");
        public string DetailShort { get => _detailShort; set => Set(ref _detailShort, value); }
        public double Percent { get => _percent; set => Set(ref _percent, value); }
        public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
        public string? ToolTip { get => _toolTip; set => Set(ref _toolTip, value); }
    }

    /// <summary>Widget state, shared by the main window and the tray panel.</summary>
    public sealed class MonitorViewModel : ObservableObject
    {
        private const int HistoryLength = 30;
        private readonly Queue<double> _downHistory = new();
        private readonly Queue<double> _upHistory = new();

        private LayoutKind _layout = LayoutKind.CompactVertical;
        private DisplayMode _mode = DisplayMode.Overlay;
        private string _modeText = "";
        private string _footerLeft = "";
        private string _footerRight = "";
        private Brush _background = Brushes.Black;
        private bool _netVisible = true;
        private string _netName = "";
        private string _netDown = "0";
        private string _netDownUnit = "Kb/s";
        private string _netUp = "0";
        private string _netUpUnit = "Kb/s";
        private PointCollection _downPoints = new();
        private PointCollection _upPoints = new();

        private Corner _corner = Corner.TopRight;
        private PositionMode _position = PositionMode.Corner;
        private bool _locked;
        private readonly Action<bool> _setLock;

        public MonitorViewModel(Action<MetricKind> openDetails, Action openSettings, Action togglePin, Action exit,
                                Action<LayoutKind> setLayout, Action<DisplayMode> setMode,
                                Action openMenu, Action<Corner> setCorner, Action<bool> setLock)
        {
            _setLock = setLock;
            Cpu = new MetricViewModel(MetricKind.Cpu, Color.FromRgb(0x5A, 0xB0, 0xFF), openDetails) { Label = "CPU", ShortLabel = "CPU" };
            Memory = new MetricViewModel(MetricKind.Memory, Color.FromRgb(0xB4, 0x8C, 0xFF), openDetails) { ShortLabel = "MEM" };
            Disk = new MetricViewModel(MetricKind.Disk, Color.FromRgb(0xF2, 0xA6, 0x5A), openDetails) { ShortLabel = "C:" };
            Gpu0 = new MetricViewModel(MetricKind.Gpu0, Color.FromRgb(0x59, 0xD4, 0x9B), null) { Label = "GPU 0", ShortLabel = "GPU0", IsVisible = false };
            Gpu1 = new MetricViewModel(MetricKind.Gpu1, Color.FromRgb(0x2F, 0xA6, 0x73), null) { Label = "GPU 1", ShortLabel = "GPU1", IsVisible = false };
            Metrics = new ObservableCollection<MetricViewModel> { Cpu, Memory, Disk, Gpu0, Gpu1 };

            OpenSettingsCommand = new RelayCommand(openSettings);
            OpenProcessesCommand = new RelayCommand(() => openDetails(MetricKind.Cpu));
            TogglePinCommand = new RelayCommand(togglePin);
            ExitCommand = new RelayCommand(exit);
            SetLayoutCommand = new RelayCommand(p => { if (p is LayoutKind k) setLayout(k); else if (p is string s && Enum.TryParse<LayoutKind>(s, out var k2)) setLayout(k2); });
            SetModeCommand = new RelayCommand(p => { if (p is DisplayMode m) setMode(m); else if (p is string s && Enum.TryParse<DisplayMode>(s, out var m2)) setMode(m2); });
            OpenMenuCommand = new RelayCommand(openMenu);
            SetCornerCommand = new RelayCommand(p => { if (p is Corner c) setCorner(c); else if (p is string s && Enum.TryParse<Corner>(s, out var c2)) setCorner(c2); });
            ToggleExpandCommand = new RelayCommand(() => setLayout(_layout == LayoutKind.Expanded ? LayoutKind.CompactVertical : LayoutKind.Expanded));
        }

        public ICommand OpenMenuCommand { get; }
        public ICommand SetCornerCommand { get; }
        public ICommand ToggleExpandCommand { get; }

        public string ExpandToggleText => Loc.T(_layout == LayoutKind.Expanded ? "BackToCompact" : "ExpandFull");

        public bool IsLayoutCompactV => _layout == LayoutKind.CompactVertical;
        public bool IsLayoutExpanded => _layout == LayoutKind.Expanded;
        public bool IsLayoutStrip => _layout == LayoutKind.CompactHorizontal;

        public bool IsModeOverlay => _mode == DisplayMode.Overlay;
        public bool IsModeDesktop => _mode == DisplayMode.Desktop;
        public bool IsModeTray => _mode == DisplayMode.Tray;

        public bool IsCornerTL => _position == PositionMode.Corner && _corner == Corner.TopLeft;
        public bool IsCornerTR => _position == PositionMode.Corner && _corner == Corner.TopRight;
        public bool IsCornerBL => _position == PositionMode.Corner && _corner == Corner.BottomLeft;
        public bool IsCornerBR => _position == PositionMode.Corner && _corner == Corner.BottomRight;
        public string PositionText => Loc.T(_position == PositionMode.Free ? "FreePositionDragged" : "PinnedToCorner");

        /// <summary>On = the widget cannot be dragged.</summary>
        public bool IsPositionLocked
        {
            get => _locked;
            set { if (_locked != value) { _locked = value; Raise(); _setLock(value); } }
        }

        public MetricViewModel Cpu { get; }
        public MetricViewModel Memory { get; }
        public MetricViewModel Disk { get; }
        public MetricViewModel Gpu0 { get; }
        public MetricViewModel Gpu1 { get; }
        public ObservableCollection<MetricViewModel> Metrics { get; }

        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenProcessesCommand { get; }
        public ICommand TogglePinCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand SetLayoutCommand { get; }
        public ICommand SetModeCommand { get; }

        public Snapshot? Last { get; private set; }

        public LayoutKind Layout
        {
            get => _layout;
            set
            {
                if (Set(ref _layout, value))
                {
                    Raise(nameof(IsExpandedLayout)); Raise(nameof(IsCompactLayout)); Raise(nameof(ExpandToggleText));
                    Raise(nameof(IsLayoutCompactV)); Raise(nameof(IsLayoutExpanded)); Raise(nameof(IsLayoutStrip));
                }
            }
        }

        public bool IsExpandedLayout => _layout == LayoutKind.Expanded;
        public bool IsCompactLayout => _layout != LayoutKind.Expanded;

        public DisplayMode Mode
        {
            get => _mode;
            set
            {
                if (Set(ref _mode, value))
                {
                    Raise(nameof(IsPinned));
                    Raise(nameof(IsModeOverlay)); Raise(nameof(IsModeDesktop)); Raise(nameof(IsModeTray));
                }
                ModeText = value switch
                {
                    DisplayMode.Overlay => Loc.T("ModeOverlay"),
                    DisplayMode.Desktop => Loc.T("ModeDesktop"),
                    _ => Loc.T("ModeTray")
                };
            }
        }

        public bool IsPinned => _mode == DisplayMode.Overlay;

        public string ModeText { get => _modeText; private set => Set(ref _modeText, value); }
        public string FooterLeft { get => _footerLeft; private set => Set(ref _footerLeft, value); }
        public string FooterRight { get => _footerRight; private set => Set(ref _footerRight, value); }
        public Brush Background { get => _background; private set => Set(ref _background, value); }

        public bool NetVisible { get => _netVisible; private set => Set(ref _netVisible, value); }
        public string NetName { get => _netName; private set => Set(ref _netName, value); }
        public string NetDown { get => _netDown; private set => Set(ref _netDown, value); }
        public string NetDownUnit { get => _netDownUnit; private set => Set(ref _netDownUnit, value); }
        public string NetUp { get => _netUp; private set => Set(ref _netUp, value); }
        private string _netDownCompact = "0";
        public string NetDownCompact { get => _netDownCompact; private set => Set(ref _netDownCompact, value); }
        public string NetUpUnit { get => _netUpUnit; private set => Set(ref _netUpUnit, value); }
        /// <summary>Points in a fixed 120×18 space (scaled by a Viewbox).</summary>
        public PointCollection DownPoints { get => _downPoints; private set => Set(ref _downPoints, value); }
        public PointCollection UpPoints { get => _upPoints; private set => Set(ref _upPoints, value); }

        private AppSettings _settings = new();

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            Memory.Label = Loc.T("Memory");
            Disk.Label = Loc.T("Disk");
            Raise(nameof(ExpandToggleText));
            Layout = s.Layout;
            Mode = s.Mode;
            _corner = s.Corner;
            _position = s.Position;
            Raise(nameof(IsCornerTL)); Raise(nameof(IsCornerTR)); Raise(nameof(IsCornerBL)); Raise(nameof(IsCornerBR)); Raise(nameof(PositionText));
            if (_locked != s.LockPosition) { _locked = s.LockPosition; Raise(nameof(IsPositionLocked)); }

            var bg = new SolidColorBrush(Color.FromArgb((byte)Math.Round(s.Opacity / 100.0 * 255), 0x16, 0x18, 0x1D));
            bg.Freeze();
            Background = bg;

            FooterLeft = Loc.F("UpdatesEvery", Format.Num(s.IntervalMs / 1000.0, 1));
            FooterRight = s.Mode == DisplayMode.Tray ? Loc.T("Tray") : s.Position == PositionMode.Free ? Loc.T("FreePosition") : s.Corner switch
            {
                Corner.TopLeft => Loc.T("CornerTL"),
                Corner.TopRight => Loc.T("CornerTR"),
                Corner.BottomLeft => Loc.T("CornerBL"),
                _ => Loc.T("CornerBR")
            };

            Disk.ShortLabel = s.DiskLetter + ":";
            Disk.Suffix = s.DiskLetter + ":";
            Cpu.IsVisible = s.ShowCpu;
            Memory.IsVisible = s.ShowMemory;
            Disk.IsVisible = s.ShowDisk;
            NetVisible = s.ShowNetwork;
            if (Last != null) Update(Last);
        }

        public void Update(Snapshot snap)
        {
            Last = snap;

            // CPU
            Cpu.Percent = snap.CpuLoad;
            Cpu.ValueText = Format.Percent(snap.CpuLoad);
            string ghz = snap.CpuGhz.HasValue ? Format.Fixed1(snap.CpuGhz.Value) + " GHz" : "";
            string ghzShort = snap.CpuGhz.HasValue ? Format.Fixed1(snap.CpuGhz.Value) + "GHz" : "";
            Cpu.Detail = Format.Join(ghz, Format.Temp(snap.CpuTemperature));
            Cpu.DetailShort = Format.JoinShort(ghzShort, Format.Temp(snap.CpuTemperature, true));
            Cpu.ToolTip = snap.CpuTemperature.HasValue || !snap.HardwareSensorsAvailable
                ? null
                : Loc.T("CpuTempNeedsAdmin");

            // Memory
            Memory.Percent = snap.MemoryLoad;
            Memory.ValueText = Format.Percent(snap.MemoryLoad);
            Memory.Detail = $"{Format.Gb(snap.MemoryUsedBytes)} / {Format.GbTotal(snap.MemoryTotalBytes)} GB";
            Memory.DetailShort = $"{Format.Gb(snap.MemoryUsedBytes)}/{Format.GbTotal(snap.MemoryTotalBytes)}G";

            // Disk
            Disk.Percent = snap.DiskActive;
            Disk.ValueText = Format.Percent(snap.DiskActive);
            string r = Format.MBps(snap.DiskReadBps), w = Format.MBps(snap.DiskWriteBps);
            Disk.Detail = Format.Join(Loc.F("ReadWrite", r, w), Format.Temp(snap.DiskTemperature));
            Disk.DetailShort = Format.JoinShort(Loc.F("ReadWriteShort", r, w), Format.Temp(snap.DiskTemperature, true));

            // GPUs
            var gpus = snap.Gpus;
            ApplyGpu(Gpu0, gpus.Count > 0 ? gpus[0] : null, gpus.Count == 1 ? "GPU" : "GPU 0", gpus.Count == 1 ? "GPU" : "GPU0", _settings.ShowGpu0);
            ApplyGpu(Gpu1, gpus.Count > 1 ? gpus[1] : null, "GPU 1", "GPU1", _settings.ShowGpu1);

            // Network
            (NetDown, NetDownUnit) = Format.NetRate(snap.NetDownBps);
            (NetUp, NetUpUnit) = Format.NetRate(snap.NetUpBps);
            // Compact layouts: a single unit only when both rates share it ("↓12,4 ↑1,3 Mb/s").
            NetDownCompact = NetDownUnit == NetUpUnit ? NetDown : NetDown + " " + NetDownUnit;
            NetName = snap.NetworkName;
            Push(_downHistory, snap.NetDownBps);
            Push(_upHistory, snap.NetUpBps);
            double max = Math.Max(1, Math.Max(_downHistory.DefaultIfEmpty(0).Max(), _upHistory.DefaultIfEmpty(0).Max()));
            DownPoints = ToPoints(_downHistory, max);
            UpPoints = ToPoints(_upHistory, max);
        }

        private static void ApplyGpu(MetricViewModel vm, GpuReading? g, string label, string shortLabel, bool show)
        {
            if (g == null)
            {
                vm.IsVisible = false;
                return;
            }
            vm.IsVisible = show;
            vm.Label = label;
            vm.ShortLabel = shortLabel;
            vm.Suffix = Loc.T(g.Integrated ? "Integrated" : "Dedicated");
            vm.ToolTip = g.Name;
            double load = g.Load ?? 0;
            vm.Percent = load;
            vm.ValueText = g.Load.HasValue ? Format.Percent(load) : "–";

            string mem = "", memShort = "";
            if (g.MemoryUsedMb.HasValue)
            {
                double used = g.MemoryUsedMb.Value / 1024.0;
                if (g.MemoryTotalMb.HasValue && g.MemoryTotalMb.Value > 0)
                {
                    string total = Format.GbTotal(g.MemoryTotalMb.Value * 1024 * 1024);
                    mem = $"VRAM {Format.Fixed1(used)}/{total} GB";
                    memShort = $"{Format.Fixed1(used)}/{total}G";
                }
                else
                {
                    mem = $"{Format.Fixed1(used)} GB";
                    memShort = $"{Format.Fixed1(used)}G";
                }
            }
            vm.Detail = Format.Join(mem, Format.Temp(g.Temperature));
            vm.DetailShort = Format.JoinShort(memShort, Format.Temp(g.Temperature, true));
        }

        private static void Push(Queue<double> q, double v)
        {
            q.Enqueue(v);
            while (q.Count > HistoryLength) q.Dequeue();
        }

        private static PointCollection ToPoints(Queue<double> q, double max)
        {
            var pts = new PointCollection();
            var arr = q.ToArray();
            int n = HistoryLength;
            int offset = n - arr.Length;
            for (int i = 0; i < arr.Length; i++)
            {
                double x = (offset + i) * 120.0 / (n - 1);
                double y = 17 - arr[i] / max * 15;
                pts.Add(new Point(x, y));
            }
            if (pts.Count == 1) pts.Add(new Point(120, pts[0].Y));
            pts.Freeze();
            return pts;
        }
    }
}
