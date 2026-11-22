﻿﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace NotDefteri
{
    public class PageData
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Xaml { get; set; }
        public string Text { get; set; }
    }

    public class DividerData
    {
        public DividerData() { Pages = new List<PageData>(); }
        public string Id { get; set; }
        public string Name { get; set; }
        public string TabColor { get; set; }
        public string PageColor { get; set; }
        public bool OnDesktop { get; set; }
        public int Order { get; set; }
        public string ActivePageId { get; set; }
        public List<PageData> Pages { get; set; }
    }

    public class DockData
    {
        public DockData() { Side = "Right"; Top = -1; PanelWidth = 400; PanelHeight = 500; }
        public string Side { get; set; }
        public double Top { get; set; }
        public string Screen { get; set; }
        public double PanelWidth { get; set; }
        public double PanelHeight { get; set; }
    }

    public class SettingsData
    {
        public SettingsData() { AutoStart = true; AiProvider = "Gemini"; AiModel = "gemini-3.5-flash-lite"; }
        public bool AutoStart { get; set; }
        /// <summary>Açıkken ayraçlar ve açık sayfa diğer uygulamaların üstünde kalır.</summary>
        public bool AlwaysOnTop { get; set; }
        /// <summary>İsteğe bağlı yapay zeka sağlayıcısı: Gemini, OpenAI veya Ollama.</summary>
        public string AiProvider { get; set; }
        public string AiApiKey { get; set; }
        public string AiModel { get; set; }
        public string AiEndpoint { get; set; }
    }

    public class AppData
    {
        public AppData() { Version = 1; Dividers = new List<DividerData>(); Dock = new DockData(); Settings = new SettingsData(); }
        public int Version { get; set; }
        public List<DividerData> Dividers { get; set; }
        public DockData Dock { get; set; }
        public SettingsData Settings { get; set; }
    }

    public static class Store
    {
        public const int MaxOnDesktop = 5;
        public static AppData Data;
        public static readonly string Dir;
        static readonly string FilePath;
        static DispatcherTimer timer;

        /// <summary>Bir sayfanın içeriği değişti (kaynak, sayfa).</summary>
        public static event Action<object, PageData> PageChanged;
        /// <summary>Ayraç/sayfa listesi, adlar, renkler ya da masaüstü durumu değişti.</summary>
        public static event Action<object> StructureChanged;

        static Store()
        {
            Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasaustuNotDefteri");
            FilePath = Path.Combine(Dir, "notlar.json");
        }

        static JavaScriptSerializer Ser()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 100;
            return s;
        }

        public static string NewId() { return Guid.NewGuid().ToString("N"); }

        public static void Load()
        {
            Directory.CreateDirectory(Dir);
            Data = TryRead(FilePath);
            if (Data == null && File.Exists(FilePath))
            {
                // Dosya bozulmuşsa kenara al ve en son yedekten dön.
                try { File.Copy(FilePath, FilePath + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
                catch { }
                Data = LatestBackup();
            }
            if (Data == null) Data = new AppData();
            Normalize();
        }

        static AppData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return Ser().Deserialize<AppData>(json);
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        static AppData LatestBackup()
        {
            try
            {
                string bdir = Path.Combine(Dir, "yedekler");
                if (!Directory.Exists(bdir)) return null;
                foreach (var f in Directory.GetFiles(bdir, "notlar-*.json").OrderByDescending(x => x))
                {
                    var d = TryRead(f);
                    if (d != null) return d;
                }
            }
            catch (Exception ex) { App.Log(ex); }
            return null;
        }

        static void Normalize()
        {
            if (Data.Dividers == null) Data.Dividers = new List<DividerData>();
            if (Data.Dock == null) Data.Dock = new DockData();
            if (Data.Settings == null) Data.Settings = new SettingsData();
            if (Data.Dock.PanelWidth < 300) Data.Dock.PanelWidth = 400;
            if (Data.Dock.PanelHeight < 260) Data.Dock.PanelHeight = 500;
            int i = 0;
            foreach (var d in Data.Dividers)
            {
                if (string.IsNullOrEmpty(d.Id)) d.Id = NewId();
                if (d.Name == null) d.Name = "AYRAÇ";
                if (string.IsNullOrEmpty(d.TabColor)) d.TabColor = Theme.TabColors[i % Theme.TabColors.Length];
                if (string.IsNullOrEmpty(d.PageColor)) d.PageColor = Theme.PageColors[0];
                if (d.Pages == null) d.Pages = new List<PageData>();
                foreach (var p in d.Pages)
                {
                    if (string.IsNullOrEmpty(p.Id)) p.Id = NewId();
                    if (string.IsNullOrEmpty(p.Title)) p.Title = "Sayfa";
                }
                if (d.Pages.Count == 0) d.Pages.Add(new PageData { Id = NewId(), Title = "Sayfa 1" });
                i++;
            }
            // Masaüstünde en fazla 5 ayraç.
            int on = 0;
            foreach (var d in Data.Dividers.OrderBy(x => x.Order))
            {
                if (d.OnDesktop) { on++; if (on > MaxOnDesktop) d.OnDesktop = false; }
            }
        }

        public static int OnDesktopCount() { return Data.Dividers.Count(d => d.OnDesktop); }

        public static void MarkDirty()
        {
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timer.Tick += (s, e) => { timer.Stop(); SaveNow(); };
            }
            timer.Stop();
            timer.Start();
        }

        public static void NotifyPage(object source, PageData p)
        {
            var h = PageChanged;
            if (h != null) h(source, p);
        }

        public static void NotifyStructure(object source)
        {
            MarkDirty();
            var h = StructureChanged;
            if (h != null) h(source);
        }

        public static void SaveNow()
        {
            if (timer != null) timer.Stop();
            if (Data == null) return;
            try
            {
                Directory.CreateDirectory(Dir);
                Backup();
                string json = Ser().Serialize(Data);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    try { File.Replace(tmp, FilePath, null); }
                    catch { File.Copy(tmp, FilePath, true); File.Delete(tmp); }
                }
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Günün ilk kaydından önce mevcut dosyayı yedekler; son 30 günü tutar.</summary>
        static void Backup()
        {
            if (!File.Exists(FilePath)) return;
            string bdir = Path.Combine(Dir, "yedekler");
            Directory.CreateDirectory(bdir);
            string today = Path.Combine(bdir, "notlar-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
            if (File.Exists(today)) return;
            File.Copy(FilePath, today);
            foreach (var f in Directory.GetFiles(bdir, "notlar-*.json").OrderByDescending(x => x).Skip(30))
            {
                try { File.Delete(f); } catch { }
            }
        }
    }
}
