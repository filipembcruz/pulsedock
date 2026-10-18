using System;
using System.Windows;
using System.Windows.Input;
using PulseDock.Services;

namespace PulseDock.Views
{
    /// <summary>Panel opened from the tray icon. Closes when it loses focus.</summary>
    public partial class TrayFlyoutWindow : Window
    {
        public DateTime LastHidden { get; private set; } = DateTime.MinValue;

        public TrayFlyoutWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => { WindowHelper.MakeToolWindow(this, noActivate: false); WindowHelper.ApplyDwmFrame(this); };
            Deactivated += (_, _) => HideFlyout();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        }

        public void ShowFlyout()
        {
            Opacity = 0;
            Show();
            WindowHelper.PlaceNearTray(this);
            Opacity = 1;
            Activate();
        }

        public void HideFlyout()
        {
            if (!IsVisible) return;
            Hide();
            LastHidden = DateTime.UtcNow;
        }
    }
}
