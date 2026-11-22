using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NotDefteri
{
    /// <summary>Tüm ayraçların (masaüstünde olsun olmasın) listelendiği ana pencere.</summary>
    public class LibraryWindow : Window
    {
        readonly AppController app;
        readonly ListBox list;
        readonly TextBox search;
        readonly TextBlock searchHint;
        readonly NotebookView nb;
        readonly TextBox nameBox;
        readonly Border swatch, card;
        readonly DockPanel headerRow;
        readonly TextBlock emptyText;
        readonly CheckBox autoStart, onTop;
        DividerData current;
        bool refreshing;

        public LibraryWindow(AppController a)
        {
            app = a;
            Title = "Not Defteri — Kütüphane";
            Width = 1020;
            Height = 680;
            MinWidth = 760;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.B("#FBF4F0");
            FontFamily = Theme.Font;
            Foreground = Theme.B(Theme.Ink);
            var icon = app.AppIcon();
            if (icon != null) Icon = icon;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---------------- sol: liste
            var left = new DockPanel { Background = Theme.B("#F8DCD2") };

            var head = new StackPanel { Margin = new Thickness(20, 20, 20, 12) };
            head.Children.Add(new TextBlock { Text = "KÜTÜPHANE", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.B(Theme.Gold) });
            head.Children.Add(new TextBlock { Text = "Tüm ayraçların ve notların", FontSize = 12, Foreground = Theme.B(Theme.InkSoft), Margin = new Thickness(0, 2, 0, 12) });
            var searchGrid = new Grid();
            search = new TextBox { FontSize = 13, Padding = new Thickness(8, 5, 8, 5), BorderBrush = Theme.B("#E8CFC6"), Background = Theme.B("#FFFFFF") };
            searchHint = new TextBlock { Text = "Notlarda ara…", Foreground = Theme.B(Theme.InkSoft), Margin = new Thickness(11, 6, 0, 0), IsHitTestVisible = false, FontSize = 13 };
            search.TextChanged += (s, e) =>
            {
                searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshList();
            };
            searchGrid.Children.Add(search);
            searchGrid.Children.Add(searchHint);
            head.Children.Add(searchGrid);
            DockPanel.SetDock(head, Dock.Top);
            left.Children.Add(head);

            var bottomPanel = new StackPanel { Margin = new Thickness(20, 10, 20, 18) };
            var newBtn = new Button { Content = "+  Yeni ayraç", Style = Theme.PillButton, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            newBtn.Click += (s, e) =>
            {
                var d = app.NewDivider(false);
                if (d != null) Select(d);
            };
            bottomPanel.Children.Add(newBtn);
            autoStart = new CheckBox
            {
                Content = "Windows açılınca başlat",
                Margin = new Thickness(2, 12, 0, 0),
                FontSize = 12,
                IsChecked = Store.Data.Settings.AutoStart
            };
            autoStart.Click += (s, e) => app.SetAutoStart(autoStart.IsChecked == true);
            bottomPanel.Children.Add(autoStart);
            onTop = new CheckBox
            {
                Content = "Ayraçlar hep üstte kalsın",
                Margin = new Thickness(2, 8, 0, 0),
                FontSize = 12,
                IsChecked = Store.Data.Settings.AlwaysOnTop,
                ToolTip = "Açıkken ayraçlar diğer uygulamaların üstünde durur.\nKapalıyken arkalarında, masaüstünde durur."
            };
            onTop.Click += (s, e) => app.SetAlwaysOnTop(onTop.IsChecked == true);
            bottomPanel.Children.Add(onTop);
            bottomPanel.Children.Add(new TextBlock
            {
                Text = "Masaüstünde en fazla " + Store.MaxOnDesktop + " ayraç durabilir.",
                FontSize = 11, Foreground = Theme.B(Theme.InkSoft), Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            DockPanel.SetDock(bottomPanel, Dock.Bottom);
            left.Children.Add(bottomPanel);

            list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(12, 0, 12, 0),
                ItemContainerStyle = Theme.ListItemStyle
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.SelectionChanged += OnSelect;
            left.Children.Add(list);

            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // ---------------- sağ: seçili ayraç
            var right = new Grid { Margin = new Thickness(24, 20, 24, 24) };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            headerRow = new DockPanel();
            swatch = new Border { Width = 14, Height = 40, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(swatch, Dock.Left);
            headerRow.Children.Add(swatch);

            var del = new Button { Content = "Sil", Style = Theme.PillButton, Foreground = Theme.B("#B4473A") };
            del.Click += (s, e) => { if (current != null) app.DeleteDivider(current); };
            var pageColor = new Button { Content = "Sayfa rengi ▾", Style = Theme.PillButton };
            pageColor.Click += (s, e) => OpenColorMenu(pageColor, false);
            var tabColor = new Button { Content = "Ayraç rengi ▾", Style = Theme.PillButton };
            tabColor.Click += (s, e) => OpenColorMenu(tabColor, true);
            foreach (var b in new[] { del, pageColor, tabColor })
            {
                DockPanel.SetDock(b, Dock.Right);
                headerRow.Children.Add(b);
            }

            nameBox = new TextBox
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Ayracın adı — tıklayıp değiştirebilirsin"
            };
            nameBox.GotKeyboardFocus += (s, e) => nameBox.BorderBrush = Theme.B(Theme.Gold);
            nameBox.LostKeyboardFocus += (s, e) => { nameBox.BorderBrush = Brushes.Transparent; CommitName(); };
            nameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { CommitName(); nb.FocusEditor(); e.Handled = true; }
                else if (e.Key == Key.Escape && current != null) { nameBox.Text = current.Name; nb.FocusEditor(); e.Handled = true; }
            };
            headerRow.Children.Add(nameBox);
            Grid.SetRow(headerRow, 0);
            right.Children.Add(headerRow);

            card = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.2),
                BorderBrush = Theme.B("#E4C98E"),
                Margin = new Thickness(0, 16, 0, 0),
                Effect = Theme.Shadow(16, 2, 0.12, 270)
            };
            nb = new NotebookView();
            nb.TabBarCorner = new CornerRadius(11, 11, 0, 0);
            card.Child = nb;
            Grid.SetRow(card, 1);
            right.Children.Add(card);

            emptyText = new TextBlock
            {
                Text = "Soldan bir ayraç seç ya da yeni bir ayraç oluştur.",
                FontSize = 14,
                Foreground = Theme.B(Theme.InkSoft),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRowSpan(emptyText, 2);
            right.Children.Add(emptyText);

            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            Content = root;

            Store.StructureChanged += OnStructure;
            Closed += (s, e) => { Store.StructureChanged -= OnStructure; nb.Detach(); };

            RefreshList();
            var first = Store.Data.Dividers.OrderBy(x => x.Order).FirstOrDefault();
            ShowDivider(first, null);
            if (first != null) SelectInList(first);
        }

        public void SaveNow() { nb.SaveNow(); }

        public void Select(DividerData d)
        {
            if (d == null) return;
            if (search.Text.Length > 0) search.Text = "";
            ShowDivider(d, null);
            RefreshList();
        }

        void SelectInList(DividerData d)
        {
            refreshing = true;
            foreach (ListBoxItem item in list.Items)
                if (item.Tag == d) { list.SelectedItem = item; break; }
            refreshing = false;
        }

        static bool Has(string s, string q)
        {
            return s != null && CultureInfo.CurrentCulture.CompareInfo.IndexOf(s, q, CompareOptions.IgnoreCase) >= 0;
        }

        static bool Matches(DividerData d, string q)
        {
            return Has(d.Name, q) || d.Pages.Any(p => Has(p.Title, q) || Has(p.Text, q));
        }

        void RefreshList()
        {
            refreshing = true;
            string q = search.Text.Trim();
            list.Items.Clear();
            ListBoxItem toSelect = null;
            foreach (var d in Store.Data.Dividers.OrderBy(x => x.Order))
            {
                if (q.Length > 0 && !Matches(d, q)) continue;
                var item = new ListBoxItem { Content = MakeRow(d, q), Tag = d };
                list.Items.Add(item);
                if (d == current) toSelect = item;
            }
            list.SelectedItem = toSelect;
            refreshing = false;
        }

        FrameworkElement MakeRow(DividerData d, string q)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sw = new Border { Width = 10, Height = 36, CornerRadius = new CornerRadius(5), Background = Theme.B(d.TabColor), Margin = new Thickness(0, 0, 10, 0) };
            g.Children.Add(sw);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            string sub = d.Pages.Count + " sayfa";
            if (q.Length > 0)
            {
                int hits = d.Pages.Count(p => Has(p.Title, q) || Has(p.Text, q));
                if (hits > 0) sub += " · " + hits + " eşleşme";
            }
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = Theme.B(Theme.InkSoft) });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            var cb = new CheckBox
            {
                Content = "Masaüstü",
                IsChecked = d.OnDesktop,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11.5,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Bu ayraç masaüstünde görünsün mü?\nKapatınca notlar silinmez, burada kalır."
            };
            cb.Click += (s, e) =>
            {
                bool want = cb.IsChecked == true;
                if (!app.SetOnDesktop(d, want)) cb.IsChecked = d.OnDesktop;
            };
            Grid.SetColumn(cb, 2);
            g.Children.Add(cb);
            return g;
        }

        void OnSelect(object sender, SelectionChangedEventArgs e)
        {
            if (refreshing) return;
            var item = list.SelectedItem as ListBoxItem;
            if (item == null) return;
            var d = item.Tag as DividerData;
            if (d == null) return;
            string pageId = null;
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                var mp = d.Pages.FirstOrDefault(p => Has(p.Title, q) || Has(p.Text, q));
                if (mp != null) pageId = mp.Id;
            }
            ShowDivider(d, pageId);
        }

        void ShowDivider(DividerData d, string pageId)
        {
            if (current != null && d != current) nb.SaveNow();
            current = d;
            bool has = d != null;
            headerRow.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            card.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            if (!has) { nb.SetDivider(null, null); return; }
            nameBox.Text = d.Name;
            swatch.Background = Theme.B(d.TabColor);
            card.Background = Theme.B(d.PageColor);
            nb.SetDivider(d, pageId);
        }

        void CommitName()
        {
            if (current == null) return;
            string t = (nameBox.Text ?? "").Trim();
            if (t.Length > 0 && t != current.Name)
            {
                current.Name = t;
                Store.NotifyStructure(this);
            }
            else nameBox.Text = current.Name;
        }

        void OpenColorMenu(Button anchor, bool tab)
        {
            if (current == null) return;
            var m = app.MakeColorMenu(current, tab);
            m.PlacementTarget = anchor;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }

        void OnStructure(object source)
        {
            RefreshList();
            autoStart.IsChecked = Store.Data.Settings.AutoStart;
            onTop.IsChecked = Store.Data.Settings.AlwaysOnTop;
            if (current == null) return;
            if (!Store.Data.Dividers.Contains(current)) { ShowDivider(null, null); return; }
            swatch.Background = Theme.B(current.TabColor);
            card.Background = Theme.B(current.PageColor);
            if (!nameBox.IsKeyboardFocused) nameBox.Text = current.Name;
        }
    }
}
