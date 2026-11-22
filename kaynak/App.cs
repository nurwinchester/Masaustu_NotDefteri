using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace NotDefteri
{
    public static class App
    {
        static bool errorShown;

        [STAThread]
        public static void Main()
        {
            bool created;
            var mutex = new Mutex(true, "MasaustuNotDefteri_Tek", out created);
            if (!created)
            {
                // Zaten açık: açık olana "öne gel" de ve çık.
                try { EventWaitHandle.OpenExisting("MasaustuNotDefteri_Goster").Set(); } catch { }
                return;
            }
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "MasaustuNotDefteri_Goster");

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                e.Handled = true;
                if (!errorShown)
                {
                    errorShown = true;
                    MessageBox.Show("Beklenmeyen bir hata oldu ama uygulama çalışmaya devam ediyor.\n\nAyrıntılar şu dosyada:\n" +
                        Path.Combine(Store.Dir, "hata.log"), "Not Defteri", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

            var ctl = new AppController();
            app.Startup += (s, e) =>
            {
                ctl.Start();
                var t = new Thread(() =>
                {
                    while (showEvent.WaitOne())
                        app.Dispatcher.BeginInvoke(new Action(ctl.BringFront));
                });
                t.IsBackground = true;
                t.Start();
            };
            app.SessionEnding += (s, e) => ctl.SaveAll();
            app.Run();
            GC.KeepAlive(mutex);
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Store.Dir);
                File.AppendAllText(Path.Combine(Store.Dir, "hata.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }

    public class AppController
    {
        public StripWindow Strip;
        public PanelWindow Panel;
        LibraryWindow library;
        WF.NotifyIcon tray;
        public bool Exiting;
        ImageSource icon;
        bool iconTried;

        public DockData Dock { get { return Store.Data.Dock; } }
        public bool IsRightSide { get { return Dock.Side != "Left"; } }
        public bool AlwaysOnTop { get { return Store.Data.Settings.AlwaysOnTop; } }
        public DividerData OpenDivider { get { return Panel != null && Panel.IsVisible ? Panel.Divider : null; } }

        static string ExePath { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }
        static string IcoPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"); } }

        public void Start()
        {
            Store.Load();
            if (Store.Data.Dividers.Count == 0) CreateDefaults();
            ApplyAutoStart();
            CreateStrip();
            CreateTray();
            Store.StructureChanged += OnStructure;
            SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                if (Strip != null) Strip.Dispatcher.BeginInvoke(new Action(Replace));
            };
            Store.SaveNow();
        }

        // ------------------------------------------------------------ pencereler

        void CreateStrip()
        {
            Strip = new StripWindow(this, Native.DesktopHost());
            Strip.Closed += OnStripClosed;
            Strip.Left = -20000;
            Strip.Top = 0;
            Strip.Rebuild();
            Strip.Show();
            Strip.Place();
            ApplyTopmost();
        }

        void OnStripClosed(object sender, EventArgs e)
        {
            if (Exiting) return;
            // Explorer yeniden başlarsa masaüstü penceresi değişir; şeridi yeniden kuruyoruz.
            Panel = null;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (s, a) =>
            {
                t.Stop();
                try { CreateStrip(); } catch (Exception ex) { App.Log(ex); }
            };
            t.Start();
        }

        public void OnPanelClosed(PanelWindow p)
        {
            if (Panel == p) Panel = null;
            if (!Exiting && Strip != null && Strip.IsLoaded) Strip.Rebuild();
        }

        void Replace()
        {
            if (Strip == null) return;
            Strip.Place();
            if (Panel != null && Panel.IsVisible) Panel.PlaceNear(Strip);
        }

        public double Scale
        {
            get
            {
                if (Strip != null)
                {
                    var src = PresentationSource.FromVisual(Strip);
                    if (src != null && src.CompositionTarget != null) return src.CompositionTarget.TransformToDevice.M11;
                }
                using (var g = SD.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0;
            }
        }

        WF.Screen DockScreen()
        {
            foreach (var s in WF.Screen.AllScreens)
                if (s.DeviceName == Dock.Screen) return s;
            return WF.Screen.PrimaryScreen;
        }

        public Rect WorkAreaDip()
        {
            var wa = DockScreen().WorkingArea;
            double k = Scale;
            return new Rect(wa.Left / k, wa.Top / k, wa.Width / k, wa.Height / k);
        }

        public ImageSource AppIcon()
        {
            if (!iconTried)
            {
                iconTried = true;
                try { if (File.Exists(IcoPath)) icon = BitmapFrame.Create(new Uri(IcoPath)); }
                catch (Exception ex) { App.Log(ex); }
            }
            return icon;
        }

        // ------------------------------------------------------------ masaüstü katmanı

        public void OnOurWindowActivated() { }

        public void OnOurWindowDeactivated()
        {
            if (Strip == null) return;
            Strip.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Exiting || AlwaysOnTop) return;
                foreach (Window w in Application.Current.Windows)
                    if (w.IsActive && !(w is LibraryWindow)) return;
                SendToDesktop();
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Ayraçları ve açık sayfayı bütün pencerelerin arkasına gönderir.</summary>
        public void SendToDesktop()
        {
            if (AlwaysOnTop) return;
            if (Strip != null) Native.SendToBottom(Strip.Hwnd);
            if (Panel != null && Panel.IsVisible) Native.SendToBottom(Panel.Hwnd);
        }

        /// <summary>Görev çubuğu / tepsi: ayraçları geçici olarak öne getirir.</summary>
        public void BringFront()
        {
            if (Strip == null) return;
            if (!Strip.IsVisible) Strip.Show();
            Strip.Activate();
            if (AlwaysOnTop) return;
            Strip.Topmost = true;
            Strip.Topmost = false;
            if (Panel != null && Panel.IsVisible)
            {
                Panel.Topmost = true;
                Panel.Topmost = false;
            }
        }

        /// <summary>"Hep üstte" ayarını açar/kapatır.</summary>
        public void SetAlwaysOnTop(bool on)
        {
            Store.Data.Settings.AlwaysOnTop = on;
            ApplyTopmost();
            Store.NotifyStructure(this);
        }

        void ApplyTopmost()
        {
            bool on = AlwaysOnTop;
            if (Strip != null) Strip.Topmost = on;
            if (Panel != null) Panel.Topmost = on;
            if (on) return;
            // Kapatıldıysa ve şu an başka bir uygulamada çalışılıyorsa arkaya çekil.
            foreach (Window w in Application.Current.Windows)
                if (w.IsActive && !(w is LibraryWindow)) return;
            SendToDesktop();
        }

        public void TaskbarToggle()
        {
            if (AlwaysOnTop) return;
            SendToDesktop();
            Native.SetForegroundWindow(Native.GetShellWindow());
        }

        // ------------------------------------------------------------ açılır sayfa

        public void ToggleDivider(DividerData d)
        {
            if (OpenDivider == d) ClosePanel();
            else OpenPanel(d);
        }

        public void OpenPanel(DividerData d)
        {
            if (Strip == null) return;
            if (Panel == null) Panel = new PanelWindow(this, Strip);
            Panel.Topmost = AlwaysOnTop;
            Panel.Open(d, Strip);
            Strip.Rebuild();
        }

        public void ClosePanel()
        {
            if (Panel != null && Panel.IsVisible)
            {
                Panel.SaveNow();
                Panel.Hide();
            }
            if (Strip != null) Strip.Rebuild();
        }

        // ------------------------------------------------------------ ayraç işlemleri

        void OnStructure(object source)
        {
            if (Strip != null) Strip.Rebuild();
            if (Panel != null && Panel.IsVisible)
            {
                var d = Panel.Divider;
                if (d == null || !Store.Data.Dividers.Contains(d) || !d.OnDesktop) ClosePanel();
                else Panel.RefreshHeader();
            }
        }

        string NextColor()
        {
            foreach (var c in Theme.TabColors)
                if (!Store.Data.Dividers.Any(d => string.Equals(d.TabColor, c, StringComparison.OrdinalIgnoreCase)))
                    return c;
            return Theme.TabColors[Store.Data.Dividers.Count % Theme.TabColors.Length];
        }

        public DividerData NewDivider(bool fromStrip)
        {
            string name = Dialogs.Ask("Yeni ayraç", "Ayracın üzerinde yazacak isim (ör. UNREAL):", "");
            if (name == null) return null;
            name = name.Trim();
            if (name.Length == 0) name = "YENİ";
            var d = new DividerData
            {
                Id = Store.NewId(),
                Name = name,
                TabColor = NextColor(),
                PageColor = Theme.PageColors[0],
                OnDesktop = Store.OnDesktopCount() < Store.MaxOnDesktop,
                Order = Store.Data.Dividers.Count == 0 ? 0 : Store.Data.Dividers.Max(x => x.Order) + 1
            };
            d.Pages.Add(new PageData { Id = Store.NewId(), Title = "Sayfa 1" });
            Store.Data.Dividers.Add(d);
            Store.NotifyStructure(this);
            if (fromStrip && d.OnDesktop) OpenPanel(d);
            return d;
        }

        public void Rename(DividerData d)
        {
            string name = Dialogs.Ask("Ayracın adı", "Ayracın üzerinde yazacak isim:", d.Name);
            if (name == null || name.Trim().Length == 0) return;
            d.Name = name.Trim();
            Store.NotifyStructure(this);
        }

        public bool SetOnDesktop(DividerData d, bool on)
        {
            if (on && !d.OnDesktop && Store.OnDesktopCount() >= Store.MaxOnDesktop)
            {
                Dialogs.Info("Masaüstünde en fazla " + Store.MaxOnDesktop + " ayraç durabilir.\nÖnce başka bir ayracı masaüstünden kaldır.");
                return false;
            }
            if (d.OnDesktop == on) return true;
            d.OnDesktop = on;
            if (on) d.Order = Store.Data.Dividers.Max(x => x.Order) + 1; // masaüstünde en alta eklensin
            Store.NotifyStructure(this);
            return true;
        }

        public void DeleteDivider(DividerData d)
        {
            int n = d.Pages.Count;
            if (!Dialogs.Confirm("“" + d.Name + "” ayracı ve içindeki " + n + " sayfa kalıcı olarak silinsin mi?\n\n" +
                                 "Sadece masaüstünden kaldırmak istiyorsan “Masaüstü” kutusunun işaretini kaldırman yeterli.",
                                 "Ayracı sil"))
                return;
            if (OpenDivider == d) ClosePanel();
            Store.Data.Dividers.Remove(d);
            Store.NotifyStructure(this);
        }

        void SetColor(DividerData d, bool tab, string hex)
        {
            if (tab) d.TabColor = hex; else d.PageColor = hex;
            Store.NotifyStructure(this);
        }

        void FillColorItems(ItemCollection items, DividerData d, bool tab)
        {
            var colors = tab ? Theme.TabColors : Theme.PageColors;
            var names = tab ? Theme.TabColorNames : Theme.PageColorNames;
            string currentHex = tab ? d.TabColor : d.PageColor;
            for (int i = 0; i < colors.Length; i++)
            {
                string hex = colors[i];
                var mi = new MenuItem
                {
                    Header = Theme.ColorHeader(hex, names[i]),
                    IsChecked = string.Equals(currentHex, hex, StringComparison.OrdinalIgnoreCase)
                };
                mi.Click += (s, e) => SetColor(d, tab, hex);
                items.Add(mi);
            }
            items.Add(new Separator());
            var custom = new MenuItem { Header = "Özel renk…" };
            custom.Click += (s, e) =>
            {
                string h = Dialogs.PickColor(tab ? d.TabColor : d.PageColor);
                if (h != null) SetColor(d, tab, h);
            };
            items.Add(custom);
        }

        public ContextMenu MakeColorMenu(DividerData d, bool tab)
        {
            var m = new ContextMenu();
            FillColorItems(m.Items, d, tab);
            return m;
        }

        public ContextMenu MakeDividerMenu(DividerData d)
        {
            var m = new ContextMenu();
            var rn = new MenuItem { Header = "Adını değiştir…" };
            rn.Click += (s, e) => Rename(d);
            m.Items.Add(rn);
            var tc = new MenuItem { Header = "Ayraç rengi" };
            FillColorItems(tc.Items, d, true);
            m.Items.Add(tc);
            var pc = new MenuItem { Header = "Sayfa rengi" };
            FillColorItems(pc.Items, d, false);
            m.Items.Add(pc);
            m.Items.Add(new Separator());
            var hide = new MenuItem { Header = "Masaüstünden kaldır (notlar kütüphanede kalır)" };
            hide.Click += (s, e) => SetOnDesktop(d, false);
            m.Items.Add(hide);
            m.Items.Add(new Separator());
            var lib = new MenuItem { Header = "Kütüphanede aç" };
            lib.Click += (s, e) => ShowLibrary(d);
            m.Items.Add(lib);
            return m;
        }

        public ContextMenu MakeHandleMenu()
        {
            var m = new ContextMenu();
            var nd = new MenuItem { Header = "Yeni ayraç…" };
            nd.Click += (s, e) => NewDivider(true);
            m.Items.Add(nd);
            var lib = new MenuItem { Header = "Kütüphane" };
            lib.Click += (s, e) => ShowLibrary(null);
            m.Items.Add(lib);
            m.Items.Add(new Separator());
            var top = new MenuItem { Header = "Hep üstte tut" };
            top.Click += (s, e) => SetAlwaysOnTop(!AlwaysOnTop);
            m.Items.Add(top);
            var auto = new MenuItem { Header = "Windows açılınca başlat" };
            auto.Click += (s, e) => SetAutoStart(!Store.Data.Settings.AutoStart);
            m.Items.Add(auto);
            m.Items.Add(new Separator());
            var exit = new MenuItem { Header = "Çıkış" };
            exit.Click += (s, e) => Exit();
            m.Items.Add(exit);
            m.Opened += (s, e) =>
            {
                nd.IsEnabled = Store.OnDesktopCount() < Store.MaxOnDesktop;
                auto.IsChecked = Store.Data.Settings.AutoStart;
                top.IsChecked = AlwaysOnTop;
            };
            return m;
        }

        public void ShowLibrary(DividerData d)
        {
            if (library == null)
            {
                library = new LibraryWindow(this);
                library.Closed += (s, e) => { library = null; };
                library.Show();
            }
            else
            {
                if (library.WindowState == WindowState.Minimized) library.WindowState = WindowState.Normal;
                library.Activate();
            }
            if (d != null) library.Select(d);
        }

        // ------------------------------------------------------------ tepsi, başlangıç, çıkış

        void CreateTray()
        {
            tray = new WF.NotifyIcon();
            try
            {
                tray.Icon = File.Exists(IcoPath) ? new SD.Icon(IcoPath, 16, 16) : SD.Icon.ExtractAssociatedIcon(ExePath);
            }
            catch { tray.Icon = SD.SystemIcons.Application; }
            tray.Text = "Not Defteri";
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Ayraçları öne getir", null, (s, e) => BringFront());
            menu.Items.Add("Kütüphane", null, (s, e) => ShowLibrary(null));
            var topItem = new WF.ToolStripMenuItem("Hep üstte tut");
            topItem.Click += (s, e) => SetAlwaysOnTop(!AlwaysOnTop);
            menu.Items.Add(topItem);
            var auto = new WF.ToolStripMenuItem("Windows açılınca başlat");
            auto.Click += (s, e) => SetAutoStart(!Store.Data.Settings.AutoStart);
            menu.Items.Add(auto);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (s, e) => Exit());
            menu.Opening += (s, e) => { auto.Checked = Store.Data.Settings.AutoStart; topItem.Checked = AlwaysOnTop; };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) BringFront(); };
            tray.DoubleClick += (s, e) => ShowLibrary(null);
            tray.Visible = true;
        }

        public void SetAutoStart(bool on)
        {
            Store.Data.Settings.AutoStart = on;
            ApplyAutoStart();
            Store.NotifyStructure(this);
        }

        static void ApplyAutoStart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (Store.Data.Settings.AutoStart) k.SetValue("MasaustuNotDefteri", "\"" + ExePath + "\"");
                    else k.DeleteValue("MasaustuNotDefteri", false);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        public void SaveAll()
        {
            try
            {
                if (Panel != null) Panel.SaveNow();
                if (library != null) library.SaveNow();
            }
            catch (Exception ex) { App.Log(ex); }
            Store.SaveNow();
        }

        public void Exit()
        {
            SaveAll();
            Exiting = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ------------------------------------------------------------ ilk açılış

        void CreateDefaults()
        {
            string[] names = { "NOTLAR", "YAPILACAKLAR", "FİKİRLER" };
            for (int i = 0; i < names.Length; i++)
            {
                var d = new DividerData
                {
                    Id = Store.NewId(),
                    Name = names[i],
                    TabColor = Theme.TabColors[i],
                    PageColor = Theme.PageColors[0],
                    OnDesktop = true,
                    Order = i
                };
                var p = new PageData { Id = Store.NewId(), Title = i == 0 ? "Hoş geldin" : "Sayfa 1" };
                if (i == 0)
                {
                    try
                    {
                        var doc = WelcomeDoc();
                        p.Xaml = XamlWriter.Save(doc);
                        p.Text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
                    }
                    catch (Exception ex) { App.Log(ex); }
                }
                d.Pages.Add(p);
                Store.Data.Dividers.Add(d);
            }
        }

        static FlowDocument WelcomeDoc()
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("Hoş geldin!")) { FontSize = 22, FontWeight = FontWeights.Bold });
            doc.Blocks.Add(new Paragraph(new Run("Bu defter masaüstünde, açık pencerelerinin arkasında durur. Birkaç ipucu:")));
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            foreach (var s in new[]
            {
                "Bir ayraca tıkla: sayfası açılır. Tekrar tıkla: kapanır.",
                "Ayraca sağ tıkla: adını ve rengini değiştir ya da masaüstünden kaldır.",
                "Üstteki tutamaçtan sürükle: başka kenara ya da monitöre taşı.",
                "Sayfanın kenarından ya da alt köşesinden tutup boyutunu ayarla.",
                "Satır başında \"[] \" yaz: to-do kutusu. \"- \" yaz: madde. \"# \" yaz: başlık.",
                "Görev çubuğundaki simgeye tıklayınca ayraçlar öne gelir, tekrar tıklayınca geri çekilir.",
                "Tutamacın altındaki iğneye bas: ayraçlar diğer uygulamaların üstünde kalır. Tekrar bas: arkaya döner."
            })
                list.ListItems.Add(new ListItem(new Paragraph(new Run(s))));
            doc.Blocks.Add(list);
            doc.Blocks.Add(new Paragraph(new Run("İlk işler")) { FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 3) });
            foreach (var s in new[]
            {
                "Görev çubuğundaki simgeye sağ tıklayıp “Görev çubuğuna sabitle”yi seç",
                "İlk ayracının adını kendi konunla değiştir",
                "Bu kutuyu işaretle"
            })
            {
                var p = new Paragraph();
                p.Inlines.Add(new InlineUIContainer(new CheckBox()) { BaselineAlignment = BaselineAlignment.Center });
                p.Inlines.Add(new Run(s));
                doc.Blocks.Add(p);
            }
            return doc;
        }
    }
}
