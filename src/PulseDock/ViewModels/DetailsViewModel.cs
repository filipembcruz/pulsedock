using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PulseDock.Models;
using PulseDock.Services;

namespace PulseDock.ViewModels
{
    public sealed class ProcessRowViewModel
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public string ValueText { get; init; } = "";
        public double BarPercent { get; init; }
        public Brush Brush { get; init; } = Brushes.Gray;
    }

    /// <summary>"Top processes by …" window with end task.</summary>
    public sealed class DetailsViewModel : ObservableObject
    {
        private const int TopCount = 10;

        private readonly ProcessSampler _sampler = new();
        private readonly Func<Snapshot?> _snapshot;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _toastTimer;
        private bool _sampling;
        private List<ProcInfo> _last = new();

        private MetricKind _tab = MetricKind.Cpu;
        private ProcessRowViewModel? _selectedRow;
        private int? _selectedPid;
        private string _selectedName = "";
        private bool _confirming;
        private string _toast = "";
        private bool _toastIsError;
        private string _summary = "";

        public DetailsViewModel(Func<Snapshot?> snapshot, Action close)
        {
            _snapshot = snapshot;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await RefreshAsync();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = ""; };

            AskEndCommand = new RelayCommand(_ => Confirming = true, _ => _selectedPid.HasValue && !_confirming);
            CancelCommand = new RelayCommand(() => Confirming = false);
            ConfirmEndCommand = new RelayCommand(async () => await EndSelectedAsync());
            CloseCommand = new RelayCommand(close);

            // Language change: refresh titles and texts built here.
            _onLanguageChanged = () =>
            {
                Raise(nameof(Title)); Raise(nameof(ColumnLabel)); Raise(nameof(SelectionText));
                Rebuild();
            };
            Loc.Changed += _onLanguageChanged;
        }

        private readonly Action _onLanguageChanged;

        /// <summary>Unsubscribes from language changes (call when the window is discarded).</summary>
        public void Detach() => Loc.Changed -= _onLanguageChanged;

        public ObservableCollection<ProcessRowViewModel> Rows { get; } = new();

        public ICommand AskEndCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ConfirmEndCommand { get; }
        public ICommand CloseCommand { get; }

        public MetricKind Tab
        {
            get => _tab;
            set
            {
                if (value != MetricKind.Cpu && value != MetricKind.Memory && value != MetricKind.Disk) value = MetricKind.Cpu;
                if (Set(ref _tab, value))
                {
                    Raise(nameof(IsCpuTab)); Raise(nameof(IsMemoryTab)); Raise(nameof(IsDiskTab));
                    Raise(nameof(Title)); Raise(nameof(ColumnLabel));
                    Confirming = false;
                    Rebuild();
                }
            }
        }

        public bool IsCpuTab { get => _tab == MetricKind.Cpu; set { if (value) Tab = MetricKind.Cpu; } }
        public bool IsMemoryTab { get => _tab == MetricKind.Memory; set { if (value) Tab = MetricKind.Memory; } }
        public bool IsDiskTab { get => _tab == MetricKind.Disk; set { if (value) Tab = MetricKind.Disk; } }

        public string Title => _tab switch
        {
            MetricKind.Memory => Loc.T("TopMemory"),
            MetricKind.Disk => Loc.T("TopDisk"),
            _ => Loc.T("TopCpu")
        };

        public string ColumnLabel => _tab switch
        {
            MetricKind.Memory => Loc.T("ColMemory"),
            MetricKind.Disk => Loc.T("ColDisk"),
            _ => Loc.T("ColCpu")
        };

        public string Summary { get => _summary; private set => Set(ref _summary, value); }

        public ProcessRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (Set(ref _selectedRow, value) && value != null)
                {
                    _selectedPid = value.Pid;
                    _selectedName = value.Name;
                    Confirming = false;
                    Raise(nameof(SelectionText)); Raise(nameof(HasSelection)); Raise(nameof(SelectedName));
                }
            }
        }

        public bool HasSelection => _selectedPid.HasValue;
        public string SelectedName => _selectedName;

        public string SelectionText => _selectedPid.HasValue
            ? Loc.F("Selected", _selectedName, _selectedPid!)
            : Loc.T("SelectToEnd");

        public bool Confirming
        {
            get => _confirming;
            set { if (Set(ref _confirming, value)) Raise(nameof(NotConfirming)); }
        }

        public bool NotConfirming => !_confirming;

        public string Toast { get => _toast; private set { if (Set(ref _toast, value)) Raise(nameof(HasToast)); } }
        public bool HasToast => !string.IsNullOrEmpty(_toast);
        public bool ToastIsError { get => _toastIsError; private set => Set(ref _toastIsError, value); }

        public void Start(MetricKind tab)
        {
            Tab = tab;
            Raise(nameof(Tab));
            _ = RefreshAsync();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            Confirming = false;
        }

        private async Task RefreshAsync()
        {
            if (_sampling) return;
            _sampling = true;
            try
            {
                _last = await Task.Run(() => _sampler.Sample());
                Rebuild();
            }
            catch (Exception ex)
            {
                Log.Error("Details.Refresh", ex);
            }
            finally
            {
                _sampling = false;
            }
        }

        private void Rebuild()
        {
            UpdateSummary();

            IEnumerable<ProcInfo> ordered = _tab switch
            {
                MetricKind.Memory => _last.OrderByDescending(p => p.MemoryBytes),
                MetricKind.Disk => _last.OrderByDescending(p => p.DiskBps).ThenByDescending(p => p.MemoryBytes),
                _ => _last.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.MemoryBytes)
            };
            var top = ordered.Take(TopCount).ToList();

            double max = _tab switch
            {
                MetricKind.Memory => top.Count > 0 ? top.Max(p => (double)p.MemoryBytes) : 1,
                MetricKind.Disk => top.Count > 0 ? top.Max(p => p.DiskBps) : 1,
                _ => top.Count > 0 ? top.Max(p => p.CpuPercent) : 1
            };
            if (max <= 0) max = 1;

            var brush = _tab switch
            {
                MetricKind.Memory => new SolidColorBrush(Color.FromRgb(0xB4, 0x8C, 0xFF)),
                MetricKind.Disk => new SolidColorBrush(Color.FromRgb(0xF2, 0xA6, 0x5A)),
                _ => new SolidColorBrush(Color.FromRgb(0x5A, 0xB0, 0xFF))
            };
            brush.Freeze();

            Rows.Clear();
            ProcessRowViewModel? reselect = null;
            foreach (var p in top)
            {
                double v = _tab switch
                {
                    MetricKind.Memory => p.MemoryBytes,
                    MetricKind.Disk => p.DiskBps,
                    _ => p.CpuPercent
                };
                string text = _tab switch
                {
                    MetricKind.Memory => Format.Memory(p.MemoryBytes),
                    MetricKind.Disk => Format.MBps(p.DiskBps) + " MB/s",
                    _ => Format.Fixed1(p.CpuPercent) + "%"
                };
                var row = new ProcessRowViewModel
                {
                    Pid = p.Pid,
                    Name = p.Name,
                    Description = string.IsNullOrEmpty(p.Description) ? p.Name : p.Description,
                    ValueText = text,
                    BarPercent = v / max * 100,
                    Brush = brush
                };
                Rows.Add(row);
                if (row.Pid == _selectedPid) reselect = row;
            }

            // Keep the selection by PID across refreshes.
            _selectedRow = reselect;
            Raise(nameof(SelectedRow));
        }

        private void UpdateSummary()
        {
            var s = _snapshot();
            int count = _sampler.LastProcessCount;
            if (s == null) { Summary = Loc.F("ProcCount", count); return; }
            Summary = _tab switch
            {
                MetricKind.Memory => Loc.F("SumMemory", Format.Gb(s.MemoryUsedBytes), Format.GbTotal(s.MemoryTotalBytes), Format.Percent(s.MemoryLoad)),
                MetricKind.Disk => Loc.F("SumDisk", Format.Percent(s.DiskActive), Format.MBps(s.DiskReadBps), Format.MBps(s.DiskWriteBps)),
                _ => Loc.F("SumCpu", Format.Percent(s.CpuLoad), count)
            };
        }

        private async Task EndSelectedAsync()
        {
            if (!_selectedPid.HasValue) return;
            int pid = _selectedPid.Value;
            string name = _selectedName;
            Confirming = false;

            var (ok, message) = await ProcessSampler.KillAsync(pid, name);
            ToastIsError = !ok;
            Toast = message;
            _toastTimer.Stop();
            _toastTimer.Start();

            if (ok)
            {
                _selectedPid = null;
                _selectedName = "";
                _selectedRow = null;
                Raise(nameof(SelectedRow)); Raise(nameof(SelectionText)); Raise(nameof(HasSelection));
                _last.RemoveAll(p => p.Pid == pid);
                Rebuild();
            }
        }
    }
}
