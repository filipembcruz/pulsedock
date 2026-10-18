using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PulseDock.Services;

namespace PulseDock.Views
{
    /// <summary>
    /// Quick menu (⋯ button or right-click). It is a separate, activatable window instead of a ContextMenu:
    /// the widget cannot take focus, and native menus attached to it closed on their own.
    /// </summary>
    public partial class QuickMenuWindow : Window
    {
        public QuickMenuWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => { WindowHelper.MakeToolWindow(this, noActivate: false); WindowHelper.ApplyDwmFrame(this); };
            Deactivated += (_, _) => HideMenu();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideMenu(); };
            // Alt+F4: just hide, so it can be reopened.
            Closing += (_, e) =>
            {
                if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted && !_shuttingDown)
                {
                    e.Cancel = true;
                    HideMenu();
                }
            };
        }

        private bool _shuttingDown;

        public void CloseForShutdown()
        {
            _shuttingDown = true;
            Close();
        }

        public void ShowAtCursor()
        {
            Opacity = 0;
            Show();
            WindowHelper.PlaceNearCursor(this);
            Opacity = 1;
            Activate();
        }

        public void HideMenu()
        {
            if (IsVisible) Hide();
        }

        private void AnyButton_Click(object sender, RoutedEventArgs e)
        {
            // The "Lock position" toggle does not close the menu.
            if (e.OriginalSource is CheckBox) return;
            // Close after the button's command has run.
            Dispatcher.BeginInvoke(new Action(HideMenu));
        }
    }
}
