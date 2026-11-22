using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NotDefteri
{
    /// <summary>Bir ayraca tıklayınca hemen yanında açılan küçük not sayfası.</summary>
    public class PanelWindow : Window
    {
        public const double S = 14; // gölge payı

        readonly AppController app;
        readonly NotebookView nb;
        readonly Border card, header;
        readonly TextBlock title;
        readonly Thumb edge, bottom, corner;
        bool right = true;

        public DividerData Divider { get; private set; }
        public IntPtr Hwnd { get { return new WindowInteropHelper(this).Handle; } }

        public PanelWindow(AppController a, Window owner)
        {
            app = a;
            Owner = owner;
            Title = "Not Defteri";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = Theme.Font;
            Foreground = Theme.B(Theme.Ink);

            var root = new Grid();

            card = new Border
            {
                BorderBrush = Theme.B(Theme.Gold),
                Effect = Theme.Shadow(18, 2, 0.22, 270)
            };
            var dock = new DockPanel();

            header = new Border { Height = 34, Cursor = Cursors.SizeAll, ToolTip = "Sürükle: başka kenara ya da monitöre taşı" };
            header.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount > 1 || e.OriginalSource is Button) return;
                try { DragMove(); } catch (InvalidOperationException) { }
                Native.POINT pt;
                int px, py;
                if (Native.GetCursorPos(out pt)) { px = pt.X; py = pt.Y; }
                else { double k = app.Scale; px = (int)((Left + ActualWidth / 2) * k); py = (int)((Top + 20) * k); }
                app.SnapToPoint(px, py, Top + S, true);
                e.Handled = true;
            };
            var hd = new DockPanel();
            var close = HeaderButton("×", "Kapat (Esc)", 16);
            close.Click += (s, e) => app.ClosePanel();
            var open = HeaderButton("↗", "Kütüphanede aç", 14);
            open.Click += (s, e) => { var d = Divider; app.ClosePanel(); app.ShowLibrary(d); };
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(open, Dock.Right);
            title = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            hd.Children.Add(close);
            hd.Children.Add(open);
            hd.Children.Add(title);
            header.Child = hd;
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            nb = new NotebookView();
            dock.Children.Add(nb);
            card.Child = dock;
            root.Children.Add(card);

            edge = MakeThumb(Cursors.SizeWE);
            edge.Width = 8;
            edge.DragDelta += (s, e) => ResizeBy(e.HorizontalChange, 0);
            edge.DragCompleted += (s, e) => Remember();
            root.Children.Add(edge);

            bottom = MakeThumb(Cursors.SizeNS);
            bottom.Height = 8;
            bottom.DragDelta += (s, e) => ResizeBy(0, e.VerticalChange);
            bottom.DragCompleted += (s, e) => Remember();
            root.Children.Add(bottom);

            corner = MakeThumb(Cursors.SizeNESW);
            corner.Width = 18;
            corner.Height = 18;
            corner.DragDelta += (s, e) => ResizeBy(e.HorizontalChange, e.VerticalChange);
            corner.DragCompleted += (s, e) => Remember();
            root.Children.Add(corner);

            Content = root;

            Activated += (s, e) => app.OnOurWindowActivated();
            Deactivated += (s, e) => app.OnOurWindowDeactivated();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; app.ClosePanel(); } };
            Closed += (s, e) => { nb.Detach(); app.OnPanelClosed(this); };
        }

        static Button HeaderButton(string text, string tip, double size)
        {
            return new Button
            {
                Content = new TextBlock { Text = text, FontSize = size, FontFamily = Theme.SymbolFont },
                ToolTip = tip,
                Style = Theme.FlatButton,
                Width = 30,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static Thumb MakeThumb(Cursor c)
        {
            return new Thumb { Style = Theme.ThumbStyle, Cursor = c };
        }

        void ApplySide(bool r)
        {
            right = r;
            card.Margin = r ? new Thickness(S, S, 0, S) : new Thickness(0, S, S, S);
            card.CornerRadius = r ? new CornerRadius(14, 0, 0, 14) : new CornerRadius(0, 14, 14, 0);
            card.BorderThickness = r ? new Thickness(1.2, 1.2, 0, 1.2) : new Thickness(0, 1.2, 1.2, 1.2);
            header.CornerRadius = r ? new CornerRadius(13, 0, 0, 0) : new CornerRadius(0, 13, 0, 0);

            edge.HorizontalAlignment = r ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            edge.VerticalAlignment = VerticalAlignment.Stretch;
            edge.Margin = r ? new Thickness(S - 4, S + 36, 0, S + 18) : new Thickness(0, S + 36, S - 4, S + 18);

            bottom.HorizontalAlignment = HorizontalAlignment.Stretch;
            bottom.VerticalAlignment = VerticalAlignment.Bottom;
            bottom.Margin = r ? new Thickness(S + 18, 0, 0, S - 4) : new Thickness(0, 0, S + 18, S - 4);

            corner.HorizontalAlignment = r ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            corner.VerticalAlignment = VerticalAlignment.Bottom;
            corner.Margin = r ? new Thickness(S - 4, 0, 0, S - 4) : new Thickness(0, 0, S - 4, S - 4);
            corner.Cursor = r ? Cursors.SizeNESW : Cursors.SizeNWSE;
        }

        public void Open(DividerData d, StripWindow strip)
        {
            if (Divider != null && Divider != d) nb.SaveNow();
            Divider = d;
            ApplySide(app.IsRightSide);
            RefreshHeader();
            nb.SetDivider(d, null);
            PlaceNear(strip);
            if (!IsVisible) Show();
            Activate();
            Dispatcher.BeginInvoke(new Action(nb.FocusEditor), DispatcherPriority.Input);
        }

        public void RefreshHeader()
        {
            if (Divider == null) return;
            header.Background = Theme.B(Divider.TabColor);
            card.Background = Theme.B(Divider.PageColor);
            title.Text = Theme.Upper(Divider.Name);
        }

        public void ApplySideAndPlace(StripWindow strip)
        {
            ApplySide(app.IsRightSide);
            PlaceNear(strip);
        }

        public void PlaceNear(StripWindow strip)
        {
            Rect wa = app.WorkAreaDip();
            double w = Math.Min(Math.Max(300, app.Dock.PanelWidth), Math.Max(300, wa.Width - StripWindow.W - 20)) + S;
            double h = Math.Min(Math.Max(260, app.Dock.PanelHeight), wa.Height) + 2 * S;
            Width = w;
            Height = h;
            Left = right ? strip.Left - w : strip.Left + StripWindow.W;
            double top = strip.Top - S;
            if (top + h > wa.Bottom + S) top = wa.Bottom + S - h;
            if (top < wa.Top - S) top = wa.Top - S;
            Top = top;
        }

        void ResizeBy(double dx, double dy)
        {
            Rect wa = app.WorkAreaDip();
            if (dx != 0)
            {
                double dw = right ? -dx : dx;
                double maxW = Math.Max(320, wa.Width - StripWindow.W - 10) + S;
                double nw = Math.Max(300 + S, Math.Min(maxW, Width + dw));
                if (right) Left -= (nw - Width);
                Width = nw;
            }
            if (dy != 0)
            {
                double maxH = wa.Bottom + S - Top;
                double nh = Math.Max(260 + 2 * S, Math.Min(maxH, Height + dy));
                Height = nh;
            }
        }

        void Remember()
        {
            app.Dock.PanelWidth = Math.Round(Width - S);
            app.Dock.PanelHeight = Math.Round(Height - 2 * S);
            Store.MarkDirty();
        }

        public void SaveNow() { nb.SaveNow(); }
    }
}
