using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PulseDock.Models;
using PulseDock.ViewModels;
using PulseDock.Views;
using Forms = System.Windows.Forms;

namespace PulseDock.Services
{
    /// <summary>Wires up the sampler, windows, tray icon and settings.</summary>
    public sealed class AppHost : IDisposable
    {
        private AppSettings _settings;
        private readonly SystemSampler _sampler = new();
        private readonly MonitorViewModel _vm;
        private readonly MonitorWindow _widget;
        private TrayFlyoutWindow? _flyout;
        private DetailsWindow? _details;
        private DetailsViewModel? _detailsVm;
        private SettingsWindow? _settingsWindow;
        private QuickMenuWindow? _menu;
        private bool _repositionQueued;
        private bool _menuWasOpen;
        private Forms.NotifyIcon? _tray;
        private readonly DispatcherTimer _fastTimer;   // Ctrl for click-through
        private readonly DispatcherTimer _slowTimer;   // fullscreen + topmost reassertion
        private bool _hiddenForFullscreen;
        private bool _userHidden;
        private bool _disposed;

        public AppHost()
        {
            _settings = SettingsStore.Load();

            _vm = new MonitorViewModel(
                openDetails: OpenDetails,
                openSettings: OpenSettings,
                togglePin: TogglePin,
                exit: Exit,
                setLayout: k => Mutate(s => s.Layout = k),
                setMode: m => Mutate(s => s.Mode = m),
                openMenu: ShowQuickMenu,
                setCorner: c => Mutate(s => { s.Corner = c; s.Position = PositionMode.Corner; }),
                setLock: locked => Mutate(s => s.LockPosition = locked));

            _widget = new MonitorWindow { DataContext = _vm };
            _widget.NeedsReposition += QueueReposition;
            _widget.DragCompleted += OnDragCompleted;
            _widget.MenuRequested += ShowQuickMenu;
            // The widget never takes focus, so clicking it doesn't defocus the menu: close it manually.
            _widget.PreviewMouseDown += (_, _) => { _menuWasOpen = _menu?.IsVisible == true; _menu?.HideMenu(); };

            _fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _fastTimer.Tick += (_, _) => UpdateClickThrough();
            _slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _slowTimer.Tick += (_, _) => SlowTick();
        }

        public void Start()
        {
            // Language first, so the tray menu and shortcuts are created in the right one.
            _appliedLanguage = _settings.Language;
            Loc.Apply(_settings.Language);
            CreateTray();
            EnsureShortcuts();

            _sampler.IntervalMs = _settings.IntervalMs;
            _sampler.DiskLetter = _settings.DiskLetter;
            _sampler.Sampled += snap =>
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || _disposed) return;
                d.BeginInvoke(new Action(() => OnSample(snap)));
            };
            _sampler.Start();

            ApplySettings();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            _slowTimer.Start();
        }

        // ---------------- Data ----------------

        private void OnSample(Snapshot snap)
        {
            if (_disposed) return;
            _vm.Update(snap);
            if (_tray != null)
            {
                var text = $"PulseDock · CPU {Format.Percent(snap.CpuLoad)} · MEM {Format.Percent(snap.MemoryLoad)}";
                _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }

        // ---------------- Settings ----------------

        private void Mutate(Action<AppSettings> change)
        {
            var next = _settings.Clone();
            change(next);
            SaveAndApply(next);
        }

        private void SaveAndApply(AppSettings next)
        {
            bool autoStartChanged = next.AutoStart != _settings.AutoStart;
            _settings = next;
            SettingsStore.Save(_settings);
            if (autoStartChanged) SystemIntegration.SetAutoStart(_settings.AutoStart);
            ApplySettings();
        }

        private string? _appliedLanguage;

        private void ApplySettings()
        {
            if (_appliedLanguage != _settings.Language)
            {
                _appliedLanguage = _settings.Language;
                Loc.Apply(_settings.Language);
                if (_tray != null)
                {
                    var old = _tray.ContextMenuStrip;
                    _tray.ContextMenuStrip = BuildTrayMenu();
                    old?.Dispose();
                }
            }
            _vm.ApplySettings(_settings);
            _widget.CanDrag = !_settings.LockPosition;
            _sampler.IntervalMs = _settings.IntervalMs;
            _sampler.DiskLetter = _settings.DiskLetter;
            // Only makes sense as admin; without admin the driver isn't even extracted (avoids the antivirus alert).
            _sampler.AdvancedSensors = _settings.AdvancedSensors && SystemIntegration.IsAdministrator();

            if (_settings.Mode == DisplayMode.Tray)
            {
                _widget.Hide();
                _fastTimer.Stop();
            }
            else
            {
                _flyout?.HideFlyout();
                ShowWidgetIfAllowed();
                _widget.Controller.ApplyMode(_settings.Mode);
                PlaceWidget();
                if (_settings.ClickThrough && _settings.Mode == DisplayMode.Overlay) _fastTimer.Start();
                else { _fastTimer.Stop(); _widget.Controller.SetClickThrough(false); }
            }

            if (_details != null) _details.Topmost = _settings.Mode != DisplayMode.Desktop;
        }

        private void ShowWidgetIfAllowed()
        {
            if (_settings.Mode == DisplayMode.Tray || _userHidden || _hiddenForFullscreen) return;
            if (!_widget.IsVisible)
            {
                _widget.Opacity = 0;
                _widget.Show();
                _widget.Controller.ApplyMode(_settings.Mode);
                PlaceWidget();
                _widget.Opacity = 1;
            }
        }

        private void PlaceWidget()
        {
            if (!_widget.IsVisible || _widget.IsDragging) return;
            if (_settings.Position == PositionMode.Free)
                WindowHelper.PlaceFree(_widget, _settings.FreeX, _settings.FreeY);
            else
                WindowHelper.PlaceAtCorner(_widget, _settings.Corner, _settings.EdgeMargin, _settings.MonitorDevice);
        }

        /// <summary>Repositions after WPF finishes resizing the window (avoids using the old size).</summary>
        private void QueueReposition()
        {
            if (_repositionQueued) return;
            _repositionQueued = true;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _repositionQueued = false;
                PlaceWidget();
            }), DispatcherPriority.Loaded);
        }

        private void OnDragCompleted()
        {
            var (corner, device, x, y) = WindowHelper.ResolveDrop(_widget, _settings.EdgeMargin);
            Mutate(s =>
            {
                s.MonitorDevice = device;
                if (corner.HasValue)
                {
                    s.Position = PositionMode.Corner;
                    s.Corner = corner.Value;
                }
                else
                {
                    s.Position = PositionMode.Free;
                    s.FreeX = x;
                    s.FreeY = y;
                }
            });
        }

        private void ShowQuickMenu()
        {
            // A second click on ⋯ (or right-click) while the menu is open = close.
            if (_menuWasOpen) { _menuWasOpen = false; return; }
            _menu ??= new QuickMenuWindow { DataContext = _vm };
            _flyout?.HideFlyout();
            _menu.ShowAtCursor();
        }

        private void EnsureShortcuts()
        {
            Shortcuts.RemoveLegacy();
            SystemIntegration.RemoveLegacyAutoStart();
            Shortcuts.EnsureStartMenu();
            if (!_settings.DesktopShortcutCreated)
            {
                Shortcuts.CreateDesktop();
                _settings.DesktopShortcutCreated = true;
                SettingsStore.Save(_settings);
            }
            // Keep "Start with Windows" pointing to the current .exe (in case it moved folders).
            if (_settings.AutoStart) SystemIntegration.SetAutoStart(true);
        }

        private void OnDisplayChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(PlaceWidget));
        }

        private void TogglePin()
        {
            Mutate(s => s.Mode = s.Mode == DisplayMode.Overlay ? DisplayMode.Desktop : DisplayMode.Overlay);
        }

        // ---------------- Timers ----------------

        private void UpdateClickThrough()
        {
            bool on = _settings.ClickThrough && _settings.Mode == DisplayMode.Overlay && !Native.IsCtrlDown();
            _widget.Controller.SetClickThrough(on);
        }

        private void SlowTick()
        {
            if (_settings.Mode == DisplayMode.Tray) return;

            bool fullscreen = _settings.Mode == DisplayMode.Overlay && _settings.HideInFullscreen && Native.IsFullscreenAppRunning();
            if (fullscreen && !_hiddenForFullscreen)
            {
                _hiddenForFullscreen = true;
                _widget.Hide();
            }
            else if (!fullscreen && _hiddenForFullscreen)
            {
                _hiddenForFullscreen = false;
                ShowWidgetIfAllowed();
            }

            // Other "always on top" apps may get in front; reassert.
            // Skip this while the menu or details are open, so we don't cover them.
            bool popupOpen = (_menu != null && _menu.IsVisible) || (_details != null && _details.IsVisible);
            if (_settings.Mode == DisplayMode.Overlay && _widget.IsVisible && !popupOpen && !_widget.IsDragging)
                WindowHelper.BringToTopmost(_widget);
        }

        // ---------------- Windows ----------------

        private void OpenDetails(MetricKind kind)
        {
            if (_details == null)
            {
                _detailsVm = new DetailsViewModel(() => _vm.Last, () => _details?.Hide());
                _details = new DetailsWindow { DataContext = _detailsVm };
                _details.IsVisibleChanged += (_, e) =>
                {
                    if (!(bool)e.NewValue) _detailsVm?.Stop();
                };
                // Alt+F4 really closes it: recreate next time.
                _details.Closed += (_, _) => { _detailsVm?.Stop(); _detailsVm?.Detach(); _details = null; _detailsVm = null; };
            }

            _details.Topmost = _settings.Mode != DisplayMode.Desktop;
            _detailsVm!.Start(kind);

            if (!_details.IsVisible)
            {
                // Anchor chosen before Show(): showing the details closes the tray panel.
                Window anchor = _flyout != null && _flyout.IsVisible ? _flyout : _widget;
                _details.Opacity = 0;
                _details.Show();
                WindowHelper.PlaceBeside(_details, anchor, _settings.Corner, _settings.MonitorDevice);
                _details.Opacity = 1;
            }
            _flyout?.HideFlyout();
            _menu?.HideMenu();
            _details.Activate();
        }

        private void OpenSettings()
        {
            _flyout?.HideFlyout();
            _menu?.HideMenu();
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            var win = new SettingsWindow();
            SettingsViewModel? vm = null;
            vm = new SettingsViewModel(_settings,
                save: s =>
                {
                    // Keep what changed while the window was open (dragging, locking).
                    s.LockPosition = _settings.LockPosition;
                    s.DesktopShortcutCreated = _settings.DesktopShortcutCreated;
                    if (!vm!.PositionEdited)
                    {
                        s.Position = _settings.Position; s.Corner = _settings.Corner;
                        s.FreeX = _settings.FreeX; s.FreeY = _settings.FreeY; s.MonitorDevice = _settings.MonitorDevice;
                    }
                    SaveAndApply(s);
                    win.Close();
                },
                cancel: () => win.Close(),
                restartAsAdmin: RestartAsAdmin);
            win.DataContext = vm;
            win.Closed += (_, _) => _settingsWindow = null;
            win.Height = Math.Min(win.Height, SystemParameters.WorkArea.Height - 24);
            _settingsWindow = win;

            win.Opacity = 0;
            win.Show();
            WindowHelper.PlaceCentered(win, _settings.MonitorDevice);
            win.Opacity = 1;
            win.Activate();
        }

        private void RestartAsAdmin()
        {
            if (SystemIntegration.StartElevated())
                Exit();
        }

        // ---------------- Tray ----------------

        private void CreateTray()
        {
            System.Drawing.Icon? icon = null;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(SystemIntegration.ExePath); }
            catch (Exception ex) { Log.Error("Tray icon", ex); }

            _tray = new Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "PulseDock",
                ContextMenuStrip = BuildTrayMenu(),
                Visible = true
            };
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) OnTrayLeftClick();
            };
        }

        private Forms.ContextMenuStrip BuildTrayMenu()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Loc.T("TrayShowHide"), null, (_, _) => ToggleWidgetVisibility());
            menu.Items.Add(Loc.T("SettingsEllipsis"), null, (_, _) => OpenSettings());
            menu.Items.Add(Loc.T("TrayResetPosition"), null, (_, _) => Mutate(s => { s.Position = PositionMode.Corner; s.LockPosition = false; }));
            var layout = new Forms.ToolStripMenuItem(Loc.T("Layout"));
            layout.DropDownItems.Add(Loc.T("LayoutCompactV"), null, (_, _) => Mutate(s => s.Layout = LayoutKind.CompactVertical));
            layout.DropDownItems.Add(Loc.T("LayoutExpanded"), null, (_, _) => Mutate(s => s.Layout = LayoutKind.Expanded));
            layout.DropDownItems.Add(Loc.T("LayoutStripLong"), null, (_, _) => Mutate(s => s.Layout = LayoutKind.CompactHorizontal));
            menu.Items.Add(layout);
            var mode = new Forms.ToolStripMenuItem(Loc.T("Mode"));
            mode.DropDownItems.Add(Loc.T("ModeOverlay"), null, (_, _) => Mutate(s => s.Mode = DisplayMode.Overlay));
            mode.DropDownItems.Add(Loc.T("ModeDesktopLong"), null, (_, _) => Mutate(s => s.Mode = DisplayMode.Desktop));
            mode.DropDownItems.Add(Loc.T("ModeTrayLong"), null, (_, _) => Mutate(s => s.Mode = DisplayMode.Tray));
            menu.Items.Add(mode);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Loc.T("QuitShort"), null, (_, _) => Exit());
            return menu;
        }

        private void OnTrayLeftClick()
        {
            if (_settings.Mode == DisplayMode.Tray)
            {
                if (_flyout == null)
                {
                    _flyout = new TrayFlyoutWindow { DataContext = _vm };
                    _flyout.Closed += (_, _) => _flyout = null;
                }
                if (_flyout.IsVisible)
                {
                    _flyout.HideFlyout();
                }
                // Clicking the icon defocuses (closes) the panel before we get here: avoid reopening it immediately.
                else if ((DateTime.UtcNow - _flyout.LastHidden).TotalMilliseconds > 300)
                {
                    _flyout.ShowFlyout();
                }
            }
            else
            {
                ToggleWidgetVisibility();
            }
        }

        private void ToggleWidgetVisibility()
        {
            if (_settings.Mode == DisplayMode.Tray)
            {
                OnTrayLeftClick();
                return;
            }
            if (_widget.IsVisible)
            {
                _userHidden = true;
                _widget.Hide();
            }
            else
            {
                _userHidden = false;
                ShowWidgetIfAllowed();
            }
        }

        // ---------------- Shutdown ----------------

        private void Exit()
        {
            _menu?.CloseForShutdown();
            Application.Current?.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _fastTimer.Stop();
            _slowTimer.Stop();
            _detailsVm?.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _sampler.Dispose();
        }
    }
}
