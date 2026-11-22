using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NotDefteri
{
    /// <summary>Ekranın kenarına yapışık duran renkli ayraç şeridi.</summary>
    public class StripWindow : Window
    {
        public const double W = 48;
        const double TabH = 124;

        readonly AppController app;
        readonly StackPanel stack;
        public IntPtr Hwnd { get; private set; }

        public StripWindow(AppController a, IntPtr ownerHwnd)
        {
            app = a;
            Title = "Not Defteri";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            ShowActivated = false;
            Width = W;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.Font;
            var icon = app.AppIcon();
            if (icon != null) Icon = icon;

            // Masaüstüne "sahiplenince" Win+D ile kaybolmaz ve hep masaüstü simgelerinin üstünde kalır.
            if (ownerHwnd != IntPtr.Zero) new WindowInteropHelper(this).Owner = ownerHwnd;

            stack = new StackPanel();
            Content = stack;

            SourceInitialized += OnSourceInitialized;
            Activated += (s, e) => app.OnOurWindowActivated();
            Deactivated += (s, e) => app.OnOurWindowDeactivated();
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                    app.SendToDesktop();
                }
            };
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            long st = Native.GetLong(Hwnd, Native.GWL_STYLE);
            Native.SetLong(Hwnd, Native.GWL_STYLE, st | Native.WS_MINIMIZEBOX);
            long ex = Native.GetLong(Hwnd, Native.GWL_EXSTYLE);
            Native.SetLong(Hwnd, Native.GWL_EXSTYLE, (ex | Native.WS_EX_APPWINDOW) & ~Native.WS_EX_TOOLWINDOW);
            var src = HwndSource.FromHwnd(Hwnd);
            if (src != null) src.AddHook(WndProc);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Görev çubuğu butonuna, pencere öndeyken tıklanınca Windows "küçült" gönderir:
            // küçültmek yerine masaüstü katmanına geri gönderiyoruz.
            if (msg == Native.WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == Native.SC_MINIMIZE)
            {
                handled = true;
                app.TaskbarToggle();
            }
            return IntPtr.Zero;
        }

        public void Rebuild()
        {
            stack.Children.Clear();
            bool right = app.IsRightSide;
            stack.Children.Add(MakeHandle(right));
            stack.Children.Add(MakePin(right));
            if (System.Windows.Forms.Screen.AllScreens.Length > 1)
                stack.Children.Add(MakeScreenSwitch(right));
            var list = Store.Data.Dividers.Where(d => d.OnDesktop).OrderBy(d => d.Order).ToList();
            foreach (var d in list) stack.Children.Add(MakeTab(d, right));
            if (list.Count < Store.MaxOnDesktop) stack.Children.Add(MakePlus(right));
        }

        FrameworkElement MakeHandle(bool right)
        {
            var h = new Border
            {
                Width = 34,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Background = Theme.B(Theme.Cover),
                BorderBrush = Theme.B(Theme.Gold),
                BorderThickness = new Thickness(1.3),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = right ? new Thickness(0, 0, 5, 8) : new Thickness(5, 0, 0, 8),
                Cursor = Cursors.SizeAll,
                ToolTip = "Sürükle: başka kenara ya da monitöre taşı\nSağ tık: menü",
                Effect = Theme.Shadow(6, 1, 0.18, 270),
                Child = new TextBlock
                {
                    Text = "⋮⋮",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.B("#FFFFFF"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                }
            };
            h.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount > 1) return;
                app.HidePanelForDrag();
                try { DragMove(); } catch (InvalidOperationException) { }
                Snap();
                e.Handled = true;
            };
            h.ContextMenu = app.MakeHandleMenu();
            return h;
        }

        /// <summary>Çoklu monitörde şeridi tek tıkla diğer ekrana taşır.</summary>
        FrameworkElement MakeScreenSwitch(bool right)
        {
            string nextLabel = app.NextScreenLabel();
            var b = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = Theme.B("#CCFFF8F2"),
                BorderBrush = Theme.B(Theme.Gold),
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = right ? new Thickness(0, 0, 7, 8) : new Thickness(7, 0, 0, 8),
                Cursor = Cursors.Hand,
                ToolTip = "Diğer ekrana taşı (" + nextLabel + ")",
                Effect = Theme.Shadow(6, 1, 0.18, 270),
                Child = new TextBlock
                {
                    Text = "\uE7F4",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13.5,
                    Foreground = Theme.B(Theme.Gold),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += (s, e) => b.Background = Theme.B("#FFF3E4");
            b.MouseLeave += (s, e) => b.Background = Theme.B("#CCFFF8F2");
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; app.MoveToNextScreen(); };
            return b;
        }

        /// <summary>"Hep üstte" anahtarı: basınca ayraçlar diğer uygulamaların üstünde kalır.</summary>
        FrameworkElement MakePin(bool right)
        {
            bool on = app.AlwaysOnTop;
            var b = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = on ? Theme.B(Theme.Gold) : Theme.B("#CCFFF8F2"),
                BorderBrush = Theme.B(Theme.Gold),
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = right ? new Thickness(0, 0, 7, 8) : new Thickness(7, 0, 0, 8),
                Cursor = Cursors.Hand,
                ToolTip = on
                    ? "Hep üstte: AÇIK\nAyraçlar diğer uygulamaların üstünde kalıyor. Kapatmak için tıkla."
                    : "Hep üstte: KAPALI\nAyraçlar diğer uygulamaların arkasında kalıyor. Üstte tutmak için tıkla.",
                Effect = Theme.Shadow(6, 1, 0.18, 270),
                Child = new TextBlock
                {
                    Text = on ? "\uE840" : "\uE718",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = on ? Theme.B("#FFFFFF") : Theme.B(Theme.Gold),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; app.SetAlwaysOnTop(!app.AlwaysOnTop); };
            return b;
        }

        FrameworkElement MakeTab(DividerData d, bool right)
        {
            bool selected = app.OpenDivider == d;
            var text = new TextBlock
            {
                Text = Theme.Upper(d.Name),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Theme.B(Theme.Ink),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = TabH - 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new RotateTransform(right ? 90 : -90)
            };
            double baseW = selected ? W : W - 10;
            var bd = new Border
            {
                Width = baseW,
                Height = TabH,
                Background = Theme.B(d.TabColor),
                BorderBrush = Theme.Darken(d.TabColor, 0.85),
                BorderThickness = right ? new Thickness(1, 1, 0, 1) : new Thickness(0, 1, 1, 1),
                CornerRadius = right ? new CornerRadius(12, 0, 0, 12) : new CornerRadius(0, 12, 12, 0),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = d.Name,
                Effect = Theme.Shadow(6, 1, 0.18, right ? 180 : 0),
                Child = text
            };
            bd.MouseEnter += (s, e) => { if (!selected) bd.Width = W - 5; };
            bd.MouseLeave += (s, e) => { bd.Width = baseW; };
            bd.MouseLeftButtonUp += (s, e) => { e.Handled = true; app.ToggleDivider(d); };
            bd.ContextMenu = app.MakeDividerMenu(d);
            return bd;
        }

        FrameworkElement MakePlus(bool right)
        {
            var b = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = Theme.B("#CCFFF8F2"),
                BorderBrush = Theme.B(Theme.Gold),
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = right ? new Thickness(0, 2, 7, 0) : new Thickness(7, 2, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Yeni ayraç ekle",
                Opacity = 0.85,
                Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 17,
                    Foreground = Theme.B(Theme.Gold),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -3, 0, 0)
                }
            };
            b.MouseEnter += (s, e) => b.Opacity = 1;
            b.MouseLeave += (s, e) => b.Opacity = 0.85;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; app.NewDivider(true); };
            return b;
        }

        /// <summary>Kaydedilmiş konuma yerleştirir (monitör + kenar + yükseklik).</summary>
        public void Place()
        {
            Rect wa = app.WorkAreaDip();
            UpdateLayout();
            double h = ActualHeight > 0 ? ActualHeight : 400;
            Left = app.IsRightSide ? wa.Right - W : wa.Left;
            double top = wa.Top + (app.Dock.Top < 0 ? 90 : app.Dock.Top);
            if (top + h > wa.Bottom) top = wa.Bottom - h;
            if (top < wa.Top) top = wa.Top;
            Top = top;
        }

        /// <summary>Sürükleyip bırakınca: bırakılan monitörün en yakın kenarına yapışır, yüksekliği korur.</summary>
        void Snap()
        {
            Native.POINT pt;
            int px, py;
            if (Native.GetCursorPos(out pt))
            {
                px = pt.X;
                py = pt.Y;
            }
            else
            {
                double k = app.Scale;
                px = (int)((Left + ActualWidth / 2) * k);
                py = (int)((Top + ActualHeight / 2) * k);
            }
            app.SnapToPoint(px, py, Top, false);
        }
    }
}
