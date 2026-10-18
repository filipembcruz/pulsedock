using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PulseDock.Models;
using PulseDock.Services;
using Forms = System.Windows.Forms;

namespace PulseDock.ViewModels
{
    public sealed class ChoiceOption : ObservableObject
    {
        private bool _isSelected;
        public ChoiceOption(string label, string value) { Label = label; Value = value; }
        public string Label { get; }
        public string Value { get; }
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    }

    /// <summary>Edits a copy of the settings; applies only on save.</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly AppSettings _s;

        public SettingsViewModel(AppSettings current, Action<AppSettings> save, Action cancel, Action restartAsAdmin)
        {
            _s = current.Clone();

            Monitors = new ObservableCollection<ChoiceOption>();
            var screens = Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var sc = screens[i];
                var label = Loc.F("MonitorN", i + 1) + (sc.Primary ? Loc.T("Primary") : "");
                var opt = new ChoiceOption(label, sc.DeviceName);
                bool selected = _s.MonitorDevice == null ? sc.Primary : _s.MonitorDevice == sc.DeviceName;
                opt.IsSelected = selected;
                opt.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ChoiceOption.IsSelected) && opt.IsSelected && _initialized)
                    {
                        _s.MonitorDevice = sc.Primary ? null : sc.DeviceName;
                        _s.Position = PositionMode.Corner;
                        PositionEdited = true;
                    }
                };
                Monitors.Add(opt);
            }
            if (!Monitors.Any(m => m.IsSelected) && Monitors.Count > 0) Monitors[0].IsSelected = true;

            Disks = new ObservableCollection<ChoiceOption>();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                {
                    var letter = d.Name.Substring(0, 1).ToUpperInvariant();
                    var opt = new ChoiceOption(letter + ":", letter) { IsSelected = letter == _s.DiskLetter };
                    opt.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChoiceOption.IsSelected) && opt.IsSelected) _s.DiskLetter = letter; };
                    Disks.Add(opt);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Drives", ex);
            }

            _initialized = true;
            SaveCommand = new RelayCommand(() => save(_s));
            CancelCommand = new RelayCommand(cancel);
            RestartAsAdminCommand = new RelayCommand(restartAsAdmin);
            IsAdmin = SystemIntegration.IsAdministrator();
        }

        private readonly bool _initialized;

        /// <summary>The user changed corner, distance or monitor in this window.</summary>
        public bool PositionEdited { get; private set; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RestartAsAdminCommand { get; }

        public ObservableCollection<ChoiceOption> Monitors { get; }
        public ObservableCollection<ChoiceOption> Disks { get; }

        public bool IsAdmin { get; }
        public bool IsNotAdmin => !IsAdmin;

        // ---- Mode ----
        public bool IsOverlay { get => _s.Mode == DisplayMode.Overlay; set { if (value) SetMode(DisplayMode.Overlay); } }
        public bool IsDesktop { get => _s.Mode == DisplayMode.Desktop; set { if (value) SetMode(DisplayMode.Desktop); } }
        public bool IsTray { get => _s.Mode == DisplayMode.Tray; set { if (value) SetMode(DisplayMode.Tray); } }
        public bool ShowsCorner => _s.Mode != DisplayMode.Tray;

        private void SetMode(DisplayMode m)
        {
            _s.Mode = m;
            Raise(nameof(IsOverlay)); Raise(nameof(IsDesktop)); Raise(nameof(IsTray)); Raise(nameof(ShowsCorner));
        }

        // ---- Layout ----
        public bool IsCompactV { get => _s.Layout == LayoutKind.CompactVertical; set { if (value) SetLayout(LayoutKind.CompactVertical); } }
        public bool IsExpanded { get => _s.Layout == LayoutKind.Expanded; set { if (value) SetLayout(LayoutKind.Expanded); } }
        public bool IsCompactH { get => _s.Layout == LayoutKind.CompactHorizontal; set { if (value) SetLayout(LayoutKind.CompactHorizontal); } }

        private void SetLayout(LayoutKind k)
        {
            _s.Layout = k;
            Raise(nameof(IsCompactV)); Raise(nameof(IsExpanded)); Raise(nameof(IsCompactH));
        }

        // ---- Corner ----
        public bool IsTopLeft { get => _s.Corner == Corner.TopLeft; set { if (value) SetCorner(Corner.TopLeft); } }
        public bool IsTopRight { get => _s.Corner == Corner.TopRight; set { if (value) SetCorner(Corner.TopRight); } }
        public bool IsBottomLeft { get => _s.Corner == Corner.BottomLeft; set { if (value) SetCorner(Corner.BottomLeft); } }
        public bool IsBottomRight { get => _s.Corner == Corner.BottomRight; set { if (value) SetCorner(Corner.BottomRight); } }

        public string CornerName => _s.Corner switch
        {
            Corner.TopLeft => Loc.T("CornerTL"),
            Corner.TopRight => Loc.T("CornerTR"),
            Corner.BottomLeft => Loc.T("CornerBL"),
            _ => Loc.T("CornerBR")
        };

        private void SetCorner(Corner c)
        {
            _s.Corner = c;
            _s.Position = PositionMode.Corner; // picking a corner leaves free positioning
            PositionEdited = true;
            Raise(nameof(IsTopLeft)); Raise(nameof(IsTopRight)); Raise(nameof(IsBottomLeft)); Raise(nameof(IsBottomRight)); Raise(nameof(CornerName));
        }

        public int EdgeMargin
        {
            get => _s.EdgeMargin;
            set { _s.EdgeMargin = Math.Clamp(value, 0, 400); PositionEdited = true; Raise(); }
        }

        // ---- Metrics ----
        public bool ShowCpu { get => _s.ShowCpu; set { _s.ShowCpu = value; Raise(); } }
        public bool ShowMemory { get => _s.ShowMemory; set { _s.ShowMemory = value; Raise(); } }
        public bool ShowDisk { get => _s.ShowDisk; set { _s.ShowDisk = value; Raise(); } }
        public bool ShowGpu0 { get => _s.ShowGpu0; set { _s.ShowGpu0 = value; Raise(); } }
        public bool ShowGpu1 { get => _s.ShowGpu1; set { _s.ShowGpu1 = value; Raise(); } }
        public bool ShowNetwork { get => _s.ShowNetwork; set { _s.ShowNetwork = value; Raise(); } }

        // ---- Behavior ----
        public int Opacity
        {
            get => _s.Opacity;
            set { _s.Opacity = Math.Clamp(value, 40, 100); Raise(); Raise(nameof(OpacityText)); }
        }

        public string OpacityText => _s.Opacity + "%";

        public bool Is500 { get => _s.IntervalMs == 500; set { if (value) SetInterval(500); } }
        public bool Is1000 { get => _s.IntervalMs == 1000; set { if (value) SetInterval(1000); } }
        public bool Is2000 { get => _s.IntervalMs == 2000; set { if (value) SetInterval(2000); } }
        public bool Is5000 { get => _s.IntervalMs == 5000; set { if (value) SetInterval(5000); } }

        private void SetInterval(int ms)
        {
            _s.IntervalMs = ms;
            Raise(nameof(Is500)); Raise(nameof(Is1000)); Raise(nameof(Is2000)); Raise(nameof(Is5000));
        }

        public bool AutoStart { get => _s.AutoStart; set { _s.AutoStart = value; Raise(); } }
        public bool ClickThrough { get => _s.ClickThrough; set { _s.ClickThrough = value; Raise(); } }
        public bool HideInFullscreen { get => _s.HideInFullscreen; set { _s.HideInFullscreen = value; Raise(); } }
        public bool AdvancedSensors { get => _s.AdvancedSensors; set { _s.AdvancedSensors = value; Raise(); } }

        // ---- Language (applied on save) ----
        public bool IsLangAuto { get => _s.Language == "auto"; set { if (value) SetLanguage("auto"); } }
        public bool IsLangEn { get => _s.Language == "en"; set { if (value) SetLanguage("en"); } }
        public bool IsLangPt { get => _s.Language == "pt"; set { if (value) SetLanguage("pt"); } }

        private void SetLanguage(string code)
        {
            _s.Language = code;
            Raise(nameof(IsLangAuto)); Raise(nameof(IsLangEn)); Raise(nameof(IsLangPt));
        }
    }
}
