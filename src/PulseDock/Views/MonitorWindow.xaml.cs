using System;
using System.Windows;
using System.Windows.Input;
using PulseDock.Services;

namespace PulseDock.Views
{
    /// <summary>The widget. Position and mode are controlled by AppHost.</summary>
    public partial class MonitorWindow : Window
    {
        private Point? _pressPoint;

        public WidgetWindowController Controller { get; }

        /// <summary>Allows dragging (off when "Lock position" is on).</summary>
        public bool CanDrag { get; set; } = true;

        /// <summary>True while the user is dragging: the host does not reposition in the meantime.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>Size changed (layout switch, hidden metrics, DPI).</summary>
        public event Action? NeedsReposition;

        /// <summary>The user dropped the widget after dragging.</summary>
        public event Action? DragCompleted;

        /// <summary>Right-click anywhere on the widget.</summary>
        public event Action? MenuRequested;

        public MonitorWindow(bool perPixelTransparency = false)
        {
            InitializeComponent();
            if (perPixelTransparency)
            {
                // Diagnostics only: the old per-pixel transparent window. Must be set before the HWND exists.
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
            }
            Controller = new WidgetWindowController(this) { PerPixelTransparency = perPixelTransparency };
            SourceInitialized += (_, _) => Controller.Attach();
            SizeChanged += (_, _) => { if (!IsDragging) NeedsReposition?.Invoke(); };
            DpiChanged += (_, _) => { if (!IsDragging) NeedsReposition?.Invoke(); };

            // Drag from any point (including the clickable rows):
            // it only becomes a drag after moving 5 px, so a plain click stays a click.
            PreviewMouseLeftButtonDown += (_, e) => _pressPoint = e.GetPosition(this);
            PreviewMouseLeftButtonUp += (_, _) => _pressPoint = null;
            PreviewMouseMove += OnPreviewMouseMove;
            PreviewMouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                MenuRequested?.Invoke();
            };
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressPoint == null || !CanDrag) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _pressPoint = null; return; }

            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _pressPoint.Value.X) < 5 && Math.Abs(p.Y - _pressPoint.Value.Y) < 5) return;

            _pressPoint = null;
            Mouse.Capture(null); // cancels the "click" on the button that was under the mouse
            IsDragging = true;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The button was released before DragMove started.
            }
            finally
            {
                IsDragging = false;
            }
            DragCompleted?.Invoke();
        }
    }
}
