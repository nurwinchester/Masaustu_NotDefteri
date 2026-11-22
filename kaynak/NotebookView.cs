using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace NotDefteri
{
    /// <summary>
    /// Bir ayracın içeriği: üstte Chrome benzeri sayfa sekmeleri, biçimlendirme çubuğu ve zengin metin alanı.
    /// Hem masaüstündeki açılır sayfada hem de Kütüphane penceresinde kullanılır.
    /// </summary>
    public class NotebookView : Grid
    {
        const double NormalSize = 14, H1Size = 22, H2Size = 17;

        DividerData div;
        PageData page;
        bool loading, dirty;

        readonly RichTextBox rtb;
        readonly StackPanel tabsPanel;
        readonly ScrollViewer tabsScroll;
        readonly Border tabBar;
        readonly Border toolBar;
        readonly Border assistBar;
        readonly TextBlock assistText;
        readonly Button assistActionBtn;
        readonly Button assistCloseBtn;
        readonly Button assistBtn;
        readonly DispatcherTimer saveTimer;
        readonly HashSet<CheckBox> wired = new HashSet<CheckBox>();
        readonly Dictionary<PageData, Border> tabBorders = new Dictionary<PageData, Border>();
        readonly Dictionary<PageData, TextBlock> tabTexts = new Dictionary<PageData, TextBlock>();
        readonly Dictionary<PageData, Button> tabCloses = new Dictionary<PageData, Button>();
        string undoXaml;
        int aiSeq;
        bool aiBusy;

        public DividerData Divider { get { return div; } }
        public CornerRadius TabBarCorner { set { tabBar.CornerRadius = value; } }

        public NotebookView()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // --- Sayfa sekmeleri ---
            tabsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            tabsScroll = new ScrollViewer
            {
                Content = tabsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            tabsScroll.PreviewMouseWheel += (s, e) =>
            {
                tabsScroll.ScrollToHorizontalOffset(tabsScroll.HorizontalOffset - e.Delta / 3.0);
                e.Handled = true;
            };
            tabBar = new Border { Padding = new Thickness(6, 6, 6, 0), Child = tabsScroll };
            Grid.SetRow(tabBar, 0);
            Children.Add(tabBar);

            // --- Biçimlendirme çubuğu ---
            var tools = new WrapPanel { Margin = new Thickness(6, 3, 6, 3) };
            tools.Children.Add(Tool("B", "Kalın (Ctrl+B)", () => EditingCommands.ToggleBold.Execute(null, rtb), t => t.FontWeight = FontWeights.Bold));
            tools.Children.Add(Tool("I", "İtalik (Ctrl+I)", () => EditingCommands.ToggleItalic.Execute(null, rtb), t => { t.FontStyle = FontStyles.Italic; t.FontFamily = new FontFamily("Georgia"); }));
            tools.Children.Add(Tool("U", "Altı çizili (Ctrl+U)", () => EditingCommands.ToggleUnderline.Execute(null, rtb), t => t.TextDecorations = TextDecorations.Underline));
            tools.Children.Add(Tool("S", "Üstü çizili (Ctrl+Shift+X)", ToggleStrike, t => t.TextDecorations = TextDecorations.Strikethrough));
            tools.Children.Add(Sep());
            tools.Children.Add(Tool("H1", "Başlık 1 (Ctrl+1) — ya da satır başında '# ' yaz", () => SetLevel(1), t => t.FontWeight = FontWeights.Bold));
            tools.Children.Add(Tool("H2", "Başlık 2 (Ctrl+2) — ya da satır başında '## ' yaz", () => SetLevel(2), t => t.FontWeight = FontWeights.SemiBold));
            tools.Children.Add(Tool("¶", "Normal metin (Ctrl+0)", () => SetLevel(0), null));
            tools.Children.Add(Sep());
            tools.Children.Add(Tool("•", "Madde işaretli liste (Ctrl+Shift+L) — ya da satır başında '- ' yaz", () => EditingCommands.ToggleBullets.Execute(null, rtb), t => t.FontSize = 16));
            tools.Children.Add(Tool("1.", "Numaralı liste (Ctrl+Shift+N) — ya da satır başında '1. ' yaz", () => EditingCommands.ToggleNumbering.Execute(null, rtb), null));
            tools.Children.Add(Tool("☐", "To-do kutusu (Ctrl+Shift+K) — ya da satır başında '[] ' yaz\nİşaretlemek için: kutuya tıkla ya da Ctrl+Enter", ToggleTodoSelection, t => t.FontSize = 15));
            tools.Children.Add(Sep());
            assistBtn = MakeAssistButton();
            tools.Children.Add(assistBtn);
            toolBar = new Border { Child = tools, BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetRow(toolBar, 1);
            Children.Add(toolBar);

            // --- Düzenleyici durum / geri al şeridi ---
            var assistRow = new DockPanel { Margin = new Thickness(10, 4, 8, 4) };
            assistCloseBtn = new Button
            {
                Content = "×",
                Style = Theme.FlatButton,
                FontSize = 13,
                Padding = new Thickness(6, 0, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Kapat"
            };
            assistCloseBtn.Click += (s, e) => { CancelAi(); HideAssistBar(); };
            DockPanel.SetDock(assistCloseBtn, Dock.Right);

            assistActionBtn = new Button
            {
                Content = "↩ Geri Al",
                Style = Theme.FlatButton,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            assistActionBtn.Click += (s, e) => { var act = assistActionBtn.Tag as Action; if (act != null) act(); };
            DockPanel.SetDock(assistActionBtn, Dock.Right);

            assistText = new TextBlock
            {
                FontSize = 12,
                Foreground = Theme.B(Theme.Ink),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            assistRow.Children.Add(assistCloseBtn);
            assistRow.Children.Add(assistActionBtn);
            assistRow.Children.Add(assistText);
            assistBar = new Border
            {
                Child = assistRow,
                Background = Theme.B("#FFF4D9"),
                BorderBrush = Theme.B("#E4C98E"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(assistBar, 2);
            Children.Add(assistBar);

            // --- Yazı alanı ---
            rtb = new RichTextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                IsDocumentEnabled = true,
                AcceptsTab = true,
                Padding = new Thickness(12, 10, 12, 12),
                FontFamily = Theme.Font,
                FontSize = NormalSize,
                Foreground = Theme.B(Theme.Ink),
                CaretBrush = Theme.B(Theme.Ink),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                AllowDrop = false
            };
            rtb.Resources.Add(typeof(CheckBox), Theme.CheckBoxStyle);
            rtb.Resources.Add(typeof(Paragraph), Theme.ParagraphStyle);
            rtb.Resources.Add(typeof(List), Theme.ListStyle);
            rtb.Resources["AccentBrush"] = Theme.B(Theme.TabColors[0]);
            rtb.Resources["AccentDarkBrush"] = Theme.Darken(Theme.TabColors[0], 0.7);
            rtb.TextChanged += (s, e) => ScheduleSave();
            rtb.PreviewKeyDown += OnKey;
            rtb.PreviewTextInput += OnTextInput;
            DataObject.AddPastingHandler(rtb, OnPaste);
            Grid.SetRow(rtb, 3);
            Children.Add(rtb);

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, e) => SaveNow();

            Store.PageChanged += OnExternalPage;
            Store.StructureChanged += OnStructure;
        }

        public void Detach()
        {
            CancelAi();
            SaveNow();
            Store.PageChanged -= OnExternalPage;
            Store.StructureChanged -= OnStructure;
        }

        public void FocusEditor()
        {
            rtb.Focus();
            Keyboard.Focus(rtb);
        }

        // ------------------------------------------------------------------ ayraç / sayfa

        public void SetDivider(DividerData d, string pageId)
        {
            SaveNow();
            div = d;
            page = null;
            if (d == null) { tabsPanel.Children.Clear(); return; }
            if (d.Pages.Count == 0) d.Pages.Add(new PageData { Id = Store.NewId(), Title = "Sayfa 1" });
            PageData p = null;
            if (pageId != null) p = d.Pages.FirstOrDefault(x => x.Id == pageId);
            if (p == null) p = d.Pages.FirstOrDefault(x => x.Id == d.ActivePageId);
            if (p == null) p = d.Pages[0];
            ApplyColors();
            RefreshTabs();
            ShowPage(p);
        }

        void ApplyColors()
        {
            if (div == null) return;
            tabBar.Background = Theme.Mix(div.TabColor, div.PageColor, 0.45);
            toolBar.Background = Theme.B(div.PageColor);
            toolBar.BorderBrush = Theme.Mix(div.TabColor, div.PageColor, 0.35);
            rtb.Resources["AccentBrush"] = Theme.B(div.TabColor);
            rtb.Resources["AccentDarkBrush"] = Theme.Darken(div.TabColor, 0.7);
            rtb.SelectionBrush = Theme.B(div.TabColor);
            UpdateTabVisuals();
        }

        void ShowPage(PageData p)
        {
            SaveNow();
            CancelAi();
            undoXaml = null;
            HideAssistBar();
            page = p;
            if (div != null && p != null) div.ActivePageId = p.Id;
            LoadDoc();
            UpdateTabVisuals();
            Store.MarkDirty();
            Border bd;
            if (p != null && tabBorders.TryGetValue(p, out bd)) bd.BringIntoView();
        }

        void LoadDoc()
        {
            loading = true;
            try
            {
                FlowDocument doc = null;
                if (page != null && !string.IsNullOrEmpty(page.Xaml))
                {
                    try { doc = XamlReader.Parse(page.Xaml) as FlowDocument; }
                    catch (Exception ex) { App.Log(ex); }
                    if (doc == null && !string.IsNullOrEmpty(page.Text))
                    {
                        // Biçim okunamazsa en azından düz metni kurtar.
                        doc = new FlowDocument();
                        foreach (var line in page.Text.Replace("\r", "").Split('\n'))
                            doc.Blocks.Add(new Paragraph(new Run(line)));
                    }
                }
                if (doc == null)
                {
                    doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph());
                }
                doc.PagePadding = new Thickness(2, 0, 2, 0);
                wired.Clear();
                rtb.Document = doc;
                WireTodos();
                rtb.CaretPosition = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? doc.ContentStart;
            }
            finally
            {
                loading = false;
                dirty = false;
                saveTimer.Stop();
            }
        }

        void ScheduleSave()
        {
            if (loading || page == null) return;
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void SaveNow()
        {
            saveTimer.Stop();
            if (!dirty || page == null) return;
            dirty = false;
            WireTodos();
            string xaml = null;
            try { xaml = XamlWriter.Save(rtb.Document); }
            catch (Exception ex)
            {
                // Yapıştırılan desteklenmeyen bir öğe varsa onu ayıklayıp tekrar dene.
                App.Log(ex);
                RemoveForeignUI();
                try { xaml = XamlWriter.Save(rtb.Document); }
                catch (Exception ex2) { App.Log(ex2); }
            }
            if (xaml != null) page.Xaml = xaml;
            page.Text = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
            Store.NotifyPage(this, page);
            Store.MarkDirty();
        }

        void OnExternalPage(object source, PageData p)
        {
            if (source == this || p != page || page == null) return;
            if (rtb.IsKeyboardFocusWithin) return;
            LoadDoc();
        }

        void OnStructure(object source)
        {
            if (source == this || div == null) return;
            if (!Store.Data.Dividers.Contains(div)) return;
            ApplyColors();
            if (page == null || !div.Pages.Contains(page))
            {
                dirty = false;
                page = null;
                if (div.Pages.Count == 0) div.Pages.Add(new PageData { Id = Store.NewId(), Title = "Sayfa 1" });
                RefreshTabs();
                ShowPage(div.Pages[0]);
            }
            else RefreshTabs();
        }

        // ------------------------------------------------------------------ sekmeler

        void RefreshTabs()
        {
            tabsPanel.Children.Clear();
            tabBorders.Clear();
            tabTexts.Clear();
            tabCloses.Clear();
            if (div == null) return;
            foreach (var p in div.Pages) tabsPanel.Children.Add(MakeTab(p));
            var add = new Button
            {
                Content = "+",
                Style = Theme.FlatButton,
                FontSize = 17,
                Padding = new Thickness(9, 0, 9, 3),
                Margin = new Thickness(2, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Yeni sayfa sekmesi (Ctrl+T)"
            };
            add.Click += (s, e) => AddPage();
            tabsPanel.Children.Add(add);
            UpdateTabVisuals();
        }

        FrameworkElement MakeTab(PageData p)
        {
            var tb = new TextBlock
            {
                Text = p.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5,
                MaxWidth = 140
            };
            var x = new Button
            {
                Content = "×",
                Style = Theme.FlatButton,
                FontSize = 13,
                Padding = new Thickness(5, 0, 5, 1),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Sayfayı sil"
            };
            x.Click += (s, e) => DeletePage(p);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(tb);
            sp.Children.Add(x);
            var bd = new Border
            {
                CornerRadius = new CornerRadius(9, 9, 0, 0),
                Padding = new Thickness(11, 5, 4, 6),
                Margin = new Thickness(0, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = sp
            };
            bd.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2) { StartRename(p); e.Handled = true; }
                else if (p != page) { ShowPage(p); e.Handled = true; }
            };

            var menu = new ContextMenu();
            var rn = new MenuItem { Header = "Yeniden adlandır" };
            rn.Click += (s, e) => StartRename(p);
            var ml = new MenuItem { Header = "Sola taşı" };
            ml.Click += (s, e) => MovePage(p, -1);
            var mr = new MenuItem { Header = "Sağa taşı" };
            mr.Click += (s, e) => MovePage(p, 1);
            var del = new MenuItem { Header = "Sayfayı sil" };
            del.Click += (s, e) => DeletePage(p);
            menu.Items.Add(rn);
            menu.Items.Add(ml);
            menu.Items.Add(mr);
            menu.Items.Add(new Separator());
            menu.Items.Add(del);
            menu.Opened += (s, e) => del.IsEnabled = div != null && div.Pages.Count > 1;
            bd.ContextMenu = menu;

            tabBorders[p] = bd;
            tabTexts[p] = tb;
            tabCloses[p] = x;
            return bd;
        }

        void UpdateTabVisuals()
        {
            if (div == null) return;
            var activeBg = Theme.B(div.PageColor);
            var idleBg = Theme.B("#50FFFFFF");
            foreach (var kv in tabBorders)
            {
                bool a = kv.Key == page;
                kv.Value.Background = a ? activeBg : idleBg;
                var t = tabTexts[kv.Key];
                t.Text = kv.Key.Title;
                t.FontWeight = a ? FontWeights.SemiBold : FontWeights.Normal;
                t.Foreground = Theme.B(Theme.Ink);
                tabCloses[kv.Key].Visibility = div.Pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void StartRename(PageData p)
        {
            Border bd;
            if (!tabBorders.TryGetValue(p, out bd)) return;
            var sp = bd.Child as StackPanel;
            var tb = tabTexts[p];
            if (sp == null || !sp.Children.Contains(tb)) return;
            var box = new TextBox
            {
                Text = p.Title,
                MinWidth = 90,
                FontSize = 12.5,
                Padding = new Thickness(2, 0, 2, 0),
                BorderBrush = Theme.B(Theme.Gold)
            };
            int idx = sp.Children.IndexOf(tb);
            sp.Children.RemoveAt(idx);
            sp.Children.Insert(idx, box);
            bool done = false;
            Action<bool> finish = commit =>
            {
                if (done) return;
                done = true;
                if (commit)
                {
                    string t = (box.Text ?? "").Trim();
                    if (t.Length > 0 && t != p.Title) { p.Title = t; Store.NotifyStructure(this); }
                }
                sp.Children.Remove(box);
                if (!sp.Children.Contains(tb)) sp.Children.Insert(Math.Min(idx, sp.Children.Count), tb);
                tb.Text = p.Title;
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { finish(true); e.Handled = true; FocusEditor(); }
                else if (e.Key == Key.Escape) { finish(false); e.Handled = true; FocusEditor(); }
            };
            box.LostKeyboardFocus += (s, e) => finish(true);
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); Keyboard.Focus(box); box.SelectAll(); }), DispatcherPriority.Input);
        }

        void AddPage()
        {
            if (div == null) return;
            SaveNow();
            var p = new PageData { Id = Store.NewId(), Title = "Sayfa " + (div.Pages.Count + 1) };
            div.Pages.Add(p);
            RefreshTabs();
            ShowPage(p);
            Store.NotifyStructure(this);
            Dispatcher.BeginInvoke(new Action(() => { tabsScroll.ScrollToRightEnd(); StartRename(p); }), DispatcherPriority.Loaded);
        }

        void DeletePage(PageData p)
        {
            if (div == null || div.Pages.Count <= 1) return;
            string text = p == page
                ? new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text
                : p.Text;
            if (!string.IsNullOrWhiteSpace(text) &&
                !Dialogs.Confirm("“" + p.Title + "” sayfası silinsin mi?\n\nSilinen sayfa günlük yedeklerde durmaya devam eder.", "Sayfayı sil"))
                return;
            int i = div.Pages.IndexOf(p);
            bool wasActive = p == page;
            if (wasActive) { saveTimer.Stop(); dirty = false; page = null; }
            div.Pages.Remove(p);
            RefreshTabs();
            if (wasActive) ShowPage(div.Pages[Math.Max(0, Math.Min(i - 1, div.Pages.Count - 1))]);
            Store.NotifyStructure(this);
        }

        void MovePage(PageData p, int dir)
        {
            if (div == null) return;
            int i = div.Pages.IndexOf(p), j = i + dir;
            if (i < 0 || j < 0 || j >= div.Pages.Count) return;
            div.Pages[i] = div.Pages[j];
            div.Pages[j] = p;
            RefreshTabs();
            Store.NotifyStructure(this);
        }

        void NextPage(int dir)
        {
            if (div == null || page == null || div.Pages.Count < 2) return;
            int i = div.Pages.IndexOf(page);
            int j = (i + dir + div.Pages.Count) % div.Pages.Count;
            ShowPage(div.Pages[j]);
            FocusEditor();
        }

        // ------------------------------------------------------------------ araç çubuğu

        Button Tool(string text, string tip, Action act, Action<TextBlock> fmt)
        {
            var tb = new TextBlock { Text = text, FontSize = 13.5, FontFamily = Theme.SymbolFont };
            if (fmt != null) fmt(tb);
            var b = new Button
            {
                Content = tb,
                ToolTip = tip,
                Style = Theme.FlatButton,
                Padding = new Thickness(7, 2, 7, 3),
                MinWidth = 28,
                Margin = new Thickness(0, 0, 1, 0)
            };
            b.Click += (s, e) =>
            {
                FocusEditor();
                act();
                ScheduleSave();
                FocusEditor();
            };
            return b;
        }

        static FrameworkElement Sep()
        {
            return new Border { Width = 1, Margin = new Thickness(5, 5, 5, 5), Background = Theme.B("#26000000") };
        }

        // ------------------------------------------------------------------ akıllı / yapay zeka düzenleyici

        Button MakeAssistButton()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = "✦",
                FontSize = 13,
                Foreground = Theme.B(Theme.Gold),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Düzenle ▾",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.B(Theme.Ink),
                VerticalAlignment = VerticalAlignment.Center
            });
            var b = new Button
            {
                Content = sp,
                ToolTip = "Yazıyı akıllıca düzenle (Ctrl+Shift+D)\nÇevrimdışı otomatik düzenleme ve isteğe bağlı Yapay Zeka seçenekleri",
                Style = Theme.FlatButton,
                Background = Theme.B("#22C99B3B"),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(1, 0, 0, 0)
            };
            b.Click += (s, e) => OpenAssistMenu();
            return b;
        }

        void OpenAssistMenu()
        {
            var m = new ContextMenu();
            bool hasSel = !rtb.Selection.IsEmpty;
            string scopeHint = hasSel ? " (seçili metin)" : "";

            AddAssistItem(m, "✦ Akıllı Düzenle" + scopeHint, "Ctrl+Shift+D", () => ApplyOfflineTransform(TextAssistant.SmartFormat, "✦ Metin akıllıca düzenlendi."));
            AddAssistItem(m, "Aa Yazım ve Boşlukları Düzelt" + scopeHint, null, () => ApplyOfflineTransform(TextAssistant.FixSpellingAndSpacing, "✦ Yazım ve boşluklar düzeltildi."));
            AddAssistItem(m, "• Maddelere Ayır" + scopeHint, null, () => ApplyOfflineTransform(TextAssistant.ConvertToBullets, "✦ Maddelere ayrıldı."));
            AddAssistItem(m, "☐ Yapılacaklara Çevir" + scopeHint, null, () => ApplyOfflineTransform(TextAssistant.ConvertToTodos, "✦ Yapılacaklar listesine çevrildi."));
            AddAssistItem(m, "↕ Tamamlananları Alta Al", null, () => ApplyOfflineTransform(TextAssistant.SortCompletedTodosBottom, "✦ Tamamlanan maddeler alta alındı.", true));
            AddAssistItem(m, "Biçimi Sadeleştir" + scopeHint, null, () => ApplyOfflineTransform(TextAssistant.SimplifyPlain, "✦ Biçim sadeleştirildi."));

            m.Items.Add(new Separator());

            bool aiReady = TextAssistant.IsAiConfigured();
            string provName = string.IsNullOrWhiteSpace(Store.Data.Settings.AiProvider) ? "Gemini" : Store.Data.Settings.AiProvider;
            string aiTag = aiReady ? provName : "Ayarla";

            AddAssistItem(m, "★ Yapay Zeka: Düzenle ve Toparla" + scopeHint, aiTag, () => StartAiAction(AiAction.Polish));
            AddAssistItem(m, "★ Yapay Zeka: Özetle" + scopeHint, null, () => StartAiAction(AiAction.Summarize));
            AddAssistItem(m, "★ Yapay Zeka: Görev Listesi Çıkar" + scopeHint, null, () => StartAiAction(AiAction.ExtractTodos));
            AddAssistItem(m, "★ Yapay Zeka: Daha Akıcı / Profesyonel Yap" + scopeHint, null, () => StartAiAction(AiAction.Professional));
            AddAssistItem(m, "★ Yapay Zeka: Özel İstek…" + scopeHint, null, () => StartAiAction(AiAction.Custom));
            AddAssistItem(m, "⚙ Yapay Zeka Ayarları…", null, () => Dialogs.ShowAiSettings());

            if (!string.IsNullOrEmpty(undoXaml))
            {
                m.Items.Add(new Separator());
                AddAssistItem(m, "↩ Son Düzenlemeyi Geri Al", null, UndoLastAssist);
            }

            m.PlacementTarget = assistBtn;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }

        static void AddAssistItem(ContextMenu menu, string title, string shortcut, Action act)
        {
            var mi = new MenuItem { Header = title };
            if (!string.IsNullOrEmpty(shortcut)) mi.InputGestureText = shortcut;
            mi.Click += (s, e) => act();
            menu.Items.Add(mi);
        }

        string CaptureDocXaml()
        {
            try { return XamlWriter.Save(rtb.Document); }
            catch { return null; }
        }

        string ExtractSourceText(bool forceFullDoc, out bool isSelection)
        {
            if (!forceFullDoc && !rtb.Selection.IsEmpty)
            {
                string sel = rtb.Selection.Text;
                if (!string.IsNullOrWhiteSpace(sel))
                {
                    isSelection = true;
                    return sel;
                }
            }
            isSelection = false;
            return TextAssistant.DocumentToMarkdown(rtb.Document);
        }

        void ApplyOfflineTransform(Func<string, string> transform, string statusMsg, bool forceFullDoc = false)
        {
            if (aiBusy) return;
            bool isSel;
            string source = ExtractSourceText(forceFullDoc, out isSel);
            if (string.IsNullOrWhiteSpace(source))
            {
                ShowAssistBar("Düzenlenecek bir yazı bulunamadı.", null, null, false);
                return;
            }
            string output = transform(source);
            if (string.IsNullOrWhiteSpace(output)) return;
            ApplyTransformedMarkdown(output, isSel, statusMsg);
        }

        void StartAiAction(AiAction action)
        {
            if (aiBusy) return;
            bool isSel;
            string source = ExtractSourceText(false, out isSel);
            if (string.IsNullOrWhiteSpace(source))
            {
                ShowAssistBar("Düzenlenecek bir yazı bulunamadı.", null, null, false);
                return;
            }
            if (!TextAssistant.IsAiConfigured())
            {
                if (!Dialogs.ShowAiSettings() || !TextAssistant.IsAiConfigured()) return;
            }
            string custom = null;
            if (action == AiAction.Custom)
            {
                custom = Dialogs.Ask("Yapay Zeka — Özel İstek", "Yazıya ne yapılmasını istersin? (ör. İngilizceye çevir, daha kısa yaz, toplantı notuna çevir):", "");
                if (string.IsNullOrWhiteSpace(custom)) return;
            }

            int seq = ++aiSeq;
            aiBusy = true;
            assistBtn.IsEnabled = false;
            PageData targetPage = page;
            ShowAssistBar("✦ Yapay zeka yazını düzenliyor…", "İptal", () => { CancelAi(); HideAssistBar(); }, false);

            TextAssistant.RunAiAsync(source, action, custom, (result, err) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq != aiSeq) return;
                    aiBusy = false;
                    assistBtn.IsEnabled = true;
                    if (targetPage != page) { HideAssistBar(); return; }
                    if (!string.IsNullOrEmpty(err))
                    {
                        ShowAssistBar("Yapay zeka hatası: " + err, "Ayarlar", () => Dialogs.ShowAiSettings(), true);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        ShowAssistBar("Yapay zeka boş yanıt döndürdü.", null, null, true);
                        return;
                    }
                    ApplyTransformedMarkdown(result, isSel, "✦ Yapay zeka ile düzenlendi.");
                }));
            });
        }

        void CancelAi()
        {
            aiSeq++;
            if (aiBusy)
            {
                aiBusy = false;
                if (assistBtn != null) assistBtn.IsEnabled = true;
            }
        }

        void ApplyTransformedMarkdown(string markdown, bool replaceSelection, string statusMsg)
        {
            string snap = CaptureDocXaml();
            if (!string.IsNullOrEmpty(snap)) undoXaml = snap;

            loading = true;
            try
            {
                if (replaceSelection && !rtb.Selection.IsEmpty)
                {
                    bool singlePlain = !markdown.Contains("\n") &&
                                       !markdown.StartsWith("#") &&
                                       !markdown.StartsWith("- ") &&
                                       !markdown.StartsWith("1. ") &&
                                       !markdown.Contains("**") &&
                                       !markdown.Contains("*") &&
                                       !markdown.Contains("~~") &&
                                       !markdown.Contains("`");
                    if (singlePlain)
                    {
                        rtb.Selection.Text = markdown;
                    }
                    else
                    {
                        ReplaceSelectedBlocks(markdown);
                    }
                }
                else
                {
                    var blocks = TextAssistant.MarkdownToBlocks(markdown);
                    var doc = new FlowDocument { PagePadding = new Thickness(2, 0, 2, 0) };
                    foreach (var b in blocks) doc.Blocks.Add(b);
                    wired.Clear();
                    rtb.Document = doc;
                    rtb.CaretPosition = doc.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? doc.ContentStart;
                }
                WireTodos();
            }
            finally
            {
                loading = false;
            }
            ScheduleSave();
            FocusEditor();
            ShowAssistBar(statusMsg, "↩ Geri Al", UndoLastAssist, false);
        }

        void ReplaceSelectedBlocks(string markdown)
        {
            var newBlocks = TextAssistant.MarkdownToBlocks(markdown);
            var startP = rtb.Selection.Start.Paragraph;
            var endP = rtb.Selection.End.Paragraph;
            Block startTop = TopLevelBlock(startP);
            Block endTop = TopLevelBlock(endP);

            if (startTop == null || endTop == null || startTop.Parent != rtb.Document || endTop.Parent != rtb.Document)
            {
                rtb.Selection.Text = markdown;
                return;
            }

            var toRemove = new List<Block>();
            bool inRange = false;
            foreach (Block b in rtb.Document.Blocks)
            {
                if (b == startTop) inRange = true;
                if (inRange) toRemove.Add(b);
                if (b == endTop) break;
            }
            if (toRemove.Count == 0)
            {
                rtb.Selection.Text = markdown;
                return;
            }

            Block anchor = toRemove[0];
            foreach (var nb in newBlocks)
                rtb.Document.Blocks.InsertBefore(anchor, nb);
            foreach (var old in toRemove)
                rtb.Document.Blocks.Remove(old);
            if (newBlocks.Count > 0)
            {
                var pos = newBlocks[0].ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                if (pos != null) rtb.CaretPosition = pos;
            }
        }

        Block TopLevelBlock(Paragraph p)
        {
            if (p == null) return null;
            DependencyObject cur = p;
            while (cur != null)
            {
                var blk = cur as Block;
                if (blk != null && blk.Parent == rtb.Document) return blk;
                cur = LogicalTreeHelper.GetParent(cur);
            }
            return null;
        }

        void UndoLastAssist()
        {
            if (string.IsNullOrEmpty(undoXaml)) return;
            string restore = undoXaml;
            undoXaml = null;
            loading = true;
            try
            {
                var doc = XamlReader.Parse(restore) as FlowDocument;
                if (doc != null)
                {
                    doc.PagePadding = new Thickness(2, 0, 2, 0);
                    wired.Clear();
                    rtb.Document = doc;
                    WireTodos();
                }
            }
            catch (Exception ex) { App.Log(ex); }
            finally { loading = false; }
            ScheduleSave();
            HideAssistBar();
            FocusEditor();
        }

        void ShowAssistBar(string text, string actionLabel, Action actionHandler, bool isError)
        {
            assistText.Text = text ?? "";
            assistBar.Background = isError ? Theme.B("#FDECEB") : Theme.B("#FFF4D9");
            assistBar.BorderBrush = isError ? Theme.B("#E2A49E") : Theme.B("#E4C98E");
            assistActionBtn.Tag = actionHandler;
            if (!string.IsNullOrEmpty(actionLabel) && actionHandler != null)
            {
                assistActionBtn.Content = actionLabel;
                assistActionBtn.Visibility = Visibility.Visible;
            }
            else
            {
                assistActionBtn.Visibility = Visibility.Collapsed;
            }
            assistBar.Visibility = Visibility.Visible;
        }

        void HideAssistBar()
        {
            assistBar.Visibility = Visibility.Collapsed;
            assistActionBtn.Tag = null;
        }

        void ToggleStrike()
        {
            var sel = rtb.Selection;
            var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool has = cur != null && cur.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        }

        // ------------------------------------------------------------------ başlıklar

        static int Level(Paragraph p)
        {
            object fs = p.ReadLocalValue(TextElement.FontSizeProperty);
            if (fs is double)
            {
                double v = (double)fs;
                if (v >= 20) return 1;
                if (v >= 16) return 2;
            }
            return 0;
        }

        static void ApplyLevel(Paragraph p, int level)
        {
            var r = new TextRange(p.ContentStart, p.ContentEnd);
            if (level == 0)
            {
                p.ClearValue(TextElement.FontSizeProperty);
                p.ClearValue(TextElement.FontWeightProperty);
                p.ClearValue(Block.MarginProperty);
                if (!r.IsEmpty)
                {
                    r.ApplyPropertyValue(TextElement.FontSizeProperty, NormalSize);
                    r.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                }
            }
            else
            {
                double size = level == 1 ? H1Size : H2Size;
                FontWeight w = level == 1 ? FontWeights.Bold : FontWeights.SemiBold;
                p.FontSize = size;
                p.FontWeight = w;
                p.Margin = new Thickness(0, level == 1 ? 10 : 6, 0, 3);
                if (!r.IsEmpty)
                {
                    r.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    r.ApplyPropertyValue(TextElement.FontWeightProperty, w);
                }
            }
        }

        void SetLevel(int level)
        {
            var ps = SelectedParagraphs();
            if (ps.Count == 0) return;
            bool allSame = ps.All(p => Level(p) == level);
            int target = (allSame && level != 0) ? 0 : level;
            foreach (var p in ps) ApplyLevel(p, target);
            ScheduleSave();
        }

        static IEnumerable<Paragraph> AllParagraphs(BlockCollection blocks)
        {
            foreach (Block b in blocks)
            {
                var p = b as Paragraph;
                if (p != null) { yield return p; continue; }
                var l = b as List;
                if (l != null)
                {
                    foreach (ListItem li in l.ListItems)
                        foreach (var x in AllParagraphs(li.Blocks)) yield return x;
                    continue;
                }
                var s = b as Section;
                if (s != null)
                    foreach (var x in AllParagraphs(s.Blocks)) yield return x;
            }
        }

        List<Paragraph> SelectedParagraphs()
        {
            var res = new List<Paragraph>();
            var start = rtb.Selection.Start.Paragraph;
            var end = rtb.Selection.End.Paragraph;
            if (start == null) return res;
            bool inRange = false;
            foreach (var p in AllParagraphs(rtb.Document.Blocks))
            {
                if (p == start) inRange = true;
                if (inRange) res.Add(p);
                if (inRange && (p == end || end == null)) break;
            }
            if (res.Count == 0) res.Add(start);
            return res;
        }

        // ------------------------------------------------------------------ to-do kutuları

        static InlineUIContainer TodoContainer(Paragraph p)
        {
            if (p == null) return null;
            foreach (Inline i in p.Inlines)
            {
                var c = i as InlineUIContainer;
                if (c != null) return c.Child is CheckBox ? c : null;
                var r = i as Run;
                if (r != null && string.IsNullOrEmpty(r.Text)) continue;
                return null;
            }
            return null;
        }

        static bool IsTodo(Paragraph p) { return TodoContainer(p) != null; }

        static string TodoText(Paragraph p)
        {
            var c = TodoContainer(p);
            if (c == null) return "";
            return new TextRange(c.ElementEnd, p.ContentEnd).Text;
        }

        void AddTodo(Paragraph p, bool moveCaret)
        {
            if (p == null || IsTodo(p)) return;
            var cb = new CheckBox();
            var c = new InlineUIContainer(cb) { BaselineAlignment = BaselineAlignment.Center };
            var first = p.Inlines.FirstInline;
            if (first != null) p.Inlines.InsertBefore(first, c);
            else p.Inlines.Add(c);
            Wire(cb);
            if (moveCaret)
            {
                var pos = c.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
                if (pos != null) rtb.CaretPosition = pos;
            }
            ScheduleSave();
        }

        void RemoveTodo(Paragraph p)
        {
            var c = TodoContainer(p);
            if (c == null) return;
            StyleTodo(p, false);
            p.Inlines.Remove(c);
            ScheduleSave();
        }

        static void StyleTodo(Paragraph p, bool done)
        {
            var c = TodoContainer(p);
            if (c == null) return;
            var r = new TextRange(c.ElementEnd, p.ContentEnd);
            if (r.IsEmpty) return;
            r.ApplyPropertyValue(Inline.TextDecorationsProperty, done ? TextDecorations.Strikethrough : new TextDecorationCollection());
            r.ApplyPropertyValue(TextElement.ForegroundProperty, done ? Theme.B(Theme.InkSoft) : Theme.B(Theme.Ink));
        }

        void ToggleTodoSelection()
        {
            var ps = SelectedParagraphs();
            if (ps.Count == 0) return;
            bool all = ps.All(IsTodo);
            foreach (var p in ps)
            {
                if (all) RemoveTodo(p);
                else AddTodo(p, ps.Count == 1);
            }
        }

        void ToggleCheckAtCaret()
        {
            var c = TodoContainer(rtb.CaretPosition.Paragraph);
            if (c == null) return;
            var cb = (CheckBox)c.Child;
            cb.IsChecked = cb.IsChecked != true;
        }

        void Wire(CheckBox cb)
        {
            if (!wired.Add(cb)) return;
            cb.Checked += OnCheck;
            cb.Unchecked += OnCheck;
        }

        void WireTodos()
        {
            if (rtb.Document == null) return;
            foreach (var p in AllParagraphs(rtb.Document.Blocks))
            {
                var c = TodoContainer(p);
                if (c != null) Wire((CheckBox)c.Child);
            }
        }

        void OnCheck(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            var cb = sender as CheckBox;
            if (cb == null) return;
            var c = cb.Parent as InlineUIContainer;
            var p = c != null ? c.Parent as Paragraph : null;
            if (p == null && rtb.Document != null)
            {
                foreach (var x in AllParagraphs(rtb.Document.Blocks))
                {
                    var xc = TodoContainer(x);
                    if (xc != null && xc.Child == cb) { p = x; break; }
                }
            }
            if (p != null) StyleTodo(p, cb.IsChecked == true);
            ScheduleSave();
        }

        /// <summary>Kaydedilemeyen yapıştırılmış öğeleri (resim vb.) belgeden çıkarır; to-do kutularına dokunmaz.</summary>
        void RemoveForeignUI()
        {
            var doc = rtb.Document;
            foreach (var p in AllParagraphs(doc.Blocks).ToList())
            {
                foreach (var c in p.Inlines.OfType<InlineUIContainer>().ToList())
                    if (!(c.Child is CheckBox)) p.Inlines.Remove(c);
            }
            RemoveBlockUI(doc.Blocks);
        }

        static void RemoveBlockUI(BlockCollection blocks)
        {
            foreach (Block b in blocks.ToList())
            {
                if (b is BlockUIContainer) { blocks.Remove(b); continue; }
                var l = b as List;
                if (l != null) foreach (ListItem li in l.ListItems) RemoveBlockUI(li.Blocks);
                var s = b as Section;
                if (s != null) RemoveBlockUI(s.Blocks);
            }
        }

        // ------------------------------------------------------------------ klavye

        void OnKey(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            bool alt = (mods & ModifierKeys.Alt) != 0;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (ctrl && !alt && key == Key.Tab) { NextPage(shift ? -1 : 1); e.Handled = true; return; }

            if (ctrl && !shift && !alt)
            {
                switch (key)
                {
                    case Key.D1: case Key.NumPad1: SetLevel(1); e.Handled = true; return;
                    case Key.D2: case Key.NumPad2: SetLevel(2); e.Handled = true; return;
                    case Key.D0: case Key.NumPad0: SetLevel(0); e.Handled = true; return;
                    case Key.T: AddPage(); e.Handled = true; return;
                    case Key.Enter: ToggleCheckAtCaret(); e.Handled = true; return;
                }
            }
            if (ctrl && shift && !alt)
            {
                if (key == Key.X) { ToggleStrike(); ScheduleSave(); e.Handled = true; return; }
                if (key == Key.K) { ToggleTodoSelection(); e.Handled = true; return; }
                if (key == Key.D) { ApplyOfflineTransform(TextAssistant.SmartFormat, "✦ Metin akıllıca düzenlendi."); e.Handled = true; return; }
            }
            if (mods == ModifierKeys.None && key == Key.Enter) { HandleEnter(e); return; }
            if (mods == ModifierKeys.None && key == Key.Back) { HandleBackspace(e); return; }
        }

        void HandleEnter(KeyEventArgs e)
        {
            if (!rtb.Selection.IsEmpty) return;
            var p = rtb.CaretPosition.Paragraph;
            if (p == null) return;
            bool todo = IsTodo(p);
            int level = Level(p);

            // Boş bir to-do satırında Enter: listeden çık.
            if (todo && TodoText(p).Trim().Length == 0)
            {
                RemoveTodo(p);
                e.Handled = true;
                return;
            }
            if (!todo && level == 0) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var np = rtb.CaretPosition.Paragraph;
                if (np == null || np == p) return;
                if (level > 0 && new TextRange(np.ContentStart, np.ContentEnd).IsEmpty) ApplyLevel(np, 0);
                if (todo)
                {
                    if (!IsTodo(np)) AddTodo(np, true);
                    else
                    {
                        var cb = TodoContainer(np).Child as CheckBox;
                        if (cb != null && cb.IsChecked == true && TodoText(np).Trim().Length == 0) cb.IsChecked = false;
                    }
                    StyleTodo(np, false);
                }
                ScheduleSave();
            }), DispatcherPriority.Input);
        }

        void HandleBackspace(KeyEventArgs e)
        {
            if (!rtb.Selection.IsEmpty) return;
            var p = rtb.CaretPosition.Paragraph;
            var c = TodoContainer(p);
            if (c == null) return;
            var caret = rtb.CaretPosition;
            bool atStart = caret.CompareTo(c.ElementEnd) <= 0 || new TextRange(c.ElementEnd, caret).Text.Length == 0;
            if (!atStart) return;
            RemoveTodo(p);
            e.Handled = true;
        }

        /// <summary>Satır başı kısayolları: "[] " to-do, "- " madde, "1. " numara, "# " ve "## " başlık.</summary>
        void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text != " " || !rtb.Selection.IsEmpty) return;
            var caret = rtb.CaretPosition;
            var p = caret.Paragraph;
            if (p == null || IsTodo(p)) return;
            string before = new TextRange(p.ContentStart, caret).Text;
            Action act = null;
            if (before == "[]" || before == "[ ]") act = () => AddTodo(p, true);
            else if ((before == "-" || before == "*") && !(p.Parent is ListItem)) act = () => EditingCommands.ToggleBullets.Execute(null, rtb);
            else if (before == "1." && !(p.Parent is ListItem)) act = () => EditingCommands.ToggleNumbering.Execute(null, rtb);
            else if (before == "#") act = () => ApplyLevel(p, 1);
            else if (before == "##") act = () => ApplyLevel(p, 2);
            if (act == null) return;
            e.Handled = true;
            new TextRange(p.ContentStart, caret).Text = "";
            act();
            ScheduleSave();
        }

        /// <summary>Dışarıdan yapıştırırken sadece metni al; böylece resim vb. kaydı bozamaz.</summary>
        void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var data = e.DataObject;
            if (data.GetDataPresent(DataFormats.Xaml)) return; // kendi notlarımızdan kopyalama
            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                string text = data.GetData(DataFormats.UnicodeText) as string;
                var d = new DataObject();
                d.SetData(DataFormats.UnicodeText, text ?? "");
                e.DataObject = d;
                e.FormatToApply = DataFormats.UnicodeText;
            }
            else e.CancelCommand();
        }
    }
}
