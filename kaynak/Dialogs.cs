using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace NotDefteri
{
    static class Dialogs
    {
        public static string Ask(string title, string prompt, string initial)
        {
            var w = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Theme.B("#FFF8F2"),
                FontFamily = Theme.Font,
                Foreground = Theme.B(Theme.Ink)
            };
            var sp = new StackPanel { Margin = new Thickness(20), Width = 330 };
            sp.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            var tb = new TextBox { Text = initial ?? "", FontSize = 14, Padding = new Thickness(6, 4, 6, 4) };
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Tamam", IsDefault = true, Style = Theme.PillButton };
            var cancel = new Button { Content = "İptal", IsCancel = true, Style = Theme.PillButton };
            string result = null;
            ok.Click += (s, e) => { result = tb.Text; w.DialogResult = true; };
            row.Children.Add(ok);
            row.Children.Add(cancel);
            sp.Children.Add(row);
            w.Content = sp;
            w.Loaded += (s, e) => { tb.Focus(); Keyboard.Focus(tb); tb.SelectAll(); };
            w.ShowDialog();
            return result;
        }

        public static bool Confirm(string text, string title)
        {
            return MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public static void Info(string text)
        {
            MessageBox.Show(text, "Not Defteri", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Windows renk seçicisi. İptal edilirse null döner.</summary>
        public static string PickColor(string current)
        {
            try
            {
                using (var cd = new WF.ColorDialog())
                {
                    cd.FullOpen = true;
                    try { cd.Color = SD.ColorTranslator.FromHtml(current); } catch { }
                    if (cd.ShowDialog() == WF.DialogResult.OK)
                        return Theme.Hex(cd.Color.R, cd.Color.G, cd.Color.B);
                }
            }
            catch (Exception ex) { App.Log(ex); }
            return null;
        }

        /// <summary>İsteğe bağlı Yapay Zeka (Gemini / OpenAI / Ollama) ayar penceresi.</summary>
        public static bool ShowAiSettings()
        {
            var st = Store.Data.Settings;
            var w = new Window
            {
                Title = "Yapay Zeka Ayarları",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                ShowInTaskbar = false,
                Background = Theme.B("#FFF8F2"),
                FontFamily = Theme.Font,
                Foreground = Theme.B(Theme.Ink)
            };

            var sp = new StackPanel { Margin = new Thickness(20), Width = 400 };
            sp.Children.Add(new TextBlock
            {
                Text = "Çevrimdışı \"Akıllı Düzenle\" her zaman ücretsiz ve anahtarsız çalışır.\nGerçek yapay zeka (özetleme, yeniden yazma, özel istek) için aşağıdan bir sağlayıcı seçebilirsin:",
                FontSize = 12,
                Foreground = Theme.B(Theme.InkSoft),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            sp.Children.Add(new TextBlock { Text = "Sağlayıcı", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var prov = new ComboBox { FontSize = 13, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 10) };
            prov.Items.Add("Gemini");
            prov.Items.Add("OpenAI");
            prov.Items.Add("Ollama");
            string curProv = string.IsNullOrWhiteSpace(st.AiProvider) ? "Gemini" : st.AiProvider;
            prov.SelectedItem = prov.Items.Contains(curProv) ? curProv : "Gemini";
            sp.Children.Add(prov);

            var keyLabel = new TextBlock { Text = "API Anahtarı", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(keyLabel);
            var keyBox = new PasswordBox { Password = st.AiApiKey ?? "", FontSize = 13, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(keyBox);

            sp.Children.Add(new TextBlock { Text = "Model", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var modelBox = new ComboBox
            {
                IsEditable = true,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(modelBox);

            var epLabel = new TextBlock { Text = "API Adresi (Ollama / özel uç nokta)", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(epLabel);
            var epBox = new TextBox
            {
                Text = st.AiEndpoint ?? "",
                FontSize = 12.5,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(epBox);

            var hint = new TextBlock
            {
                FontSize = 11.5,
                Foreground = Theme.B(Theme.InkSoft),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            sp.Children.Add(hint);

            var testStatus = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            sp.Children.Add(testStatus);

            Action<bool> syncUi = resetDefaults =>
            {
                string p = (prov.SelectedItem as string) ?? "Gemini";
                bool isGemini = p == "Gemini";
                bool isOllama = p == "Ollama";
                epLabel.Visibility = isGemini ? Visibility.Collapsed : Visibility.Visible;
                epBox.Visibility = isGemini ? Visibility.Collapsed : Visibility.Visible;
                keyLabel.Text = isOllama ? "API Anahtarı (Ollama için boş bırakılabilir)" : "API Anahtarı (AIza… ile başlar)";

                string prevModel = resetDefaults ? "" : (string.IsNullOrWhiteSpace(st.AiModel) ? TextAssistant.DefaultModelFor(p) : st.AiModel);
                modelBox.Items.Clear();
                foreach (var m in TextAssistant.SuggestedModelsFor(p)) modelBox.Items.Add(m);
                modelBox.Text = string.IsNullOrWhiteSpace(prevModel) ? TextAssistant.DefaultModelFor(p) : prevModel;

                if (resetDefaults)
                {
                    epBox.Text = TextAssistant.DefaultEndpointFor(p);
                }
                else if (!isGemini && string.IsNullOrWhiteSpace(epBox.Text))
                {
                    epBox.Text = TextAssistant.DefaultEndpointFor(p);
                }
                if (isGemini)
                    hint.Text = "Google AI Studio (aistudio.google.com) üzerinden ücretsiz Gemini API anahtarı alabilirsin. Varsayılan model: gemini-3.5-flash-lite.";
                else if (isOllama)
                    hint.Text = "Bilgisayarında çalışan Ollama ile tamamen yerel ve internetsiz yapay zeka kullanabilirsin.";
                else
                    hint.Text = "OpenAI veya OpenAI uyumlu herhangi bir API adresiyle çalışır.";
            };
            syncUi(false);
            prov.SelectionChanged += (s, e) => syncUi(true);

            var row = new DockPanel { LastChildFill = false };
            var testBtn = new Button { Content = "Bağlantıyı Test Et", Style = Theme.PillButton, Margin = new Thickness(0) };
            DockPanel.SetDock(testBtn, Dock.Left);
            row.Children.Add(testBtn);

            var ok = new Button { Content = "Kaydet", IsDefault = true, Style = Theme.PillButton };
            var cancel = new Button { Content = "İptal", IsCancel = true, Style = Theme.PillButton };
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(ok, Dock.Right);
            row.Children.Add(cancel);
            row.Children.Add(ok);

            testBtn.Click += (s, e) =>
            {
                string p = (prov.SelectedItem as string) ?? "Gemini";
                testBtn.IsEnabled = false;
                testStatus.Foreground = Theme.B(Theme.InkSoft);
                testStatus.Text = "Bağlantı test ediliyor…";
                testStatus.Visibility = Visibility.Visible;
                TextAssistant.TestConnectionAsync(p, keyBox.Password, modelBox.Text, epBox.Text, (okRes, msg) =>
                {
                    w.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        testBtn.IsEnabled = true;
                        testStatus.Foreground = okRes ? Theme.B("#2D7A46") : Theme.B("#B4473A");
                        testStatus.Text = msg;
                    }));
                });
            };

            bool saved = false;
            ok.Click += (s, e) =>
            {
                string p = (prov.SelectedItem as string) ?? "Gemini";
                st.AiProvider = p;
                st.AiApiKey = TextAssistant.CleanApiKey(keyBox.Password);
                st.AiModel = string.IsNullOrWhiteSpace(modelBox.Text) ? TextAssistant.DefaultModelFor(p) : modelBox.Text.Trim();
                st.AiEndpoint = (epBox.Text ?? "").Trim();
                Store.SaveNow();
                saved = true;
                w.DialogResult = true;
            };
            sp.Children.Add(row);

            w.Content = sp;
            w.ShowDialog();
            return saved;
        }
    }
}
