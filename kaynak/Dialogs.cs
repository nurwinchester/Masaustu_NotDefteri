﻿using System;
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
    }
}
