﻿using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NotDefteri
{
    static class Theme
    {
        public static readonly string[] TabColors = { "#5BC8F5", "#F7A1C4", "#FBE15B", "#9BE15D", "#B69CF7", "#FFB86B", "#7EE0C3", "#F48A8A", "#A0B4FF", "#CFC6C2" };
        public static readonly string[] TabColorNames = { "Mavi", "Pembe", "Sarı", "Yeşil", "Mor", "Turuncu", "Mint", "Mercan", "Lavanta", "Gri" };
        public static readonly string[] PageColors = { "#FFF8F2", "#FFFFFF", "#FDEFF3", "#EAF6FC", "#FFF9E3", "#EFF8E8", "#F3EEFF", "#FCEEE9" };
        public static readonly string[] PageColorNames = { "Krem", "Beyaz", "Pembe", "Mavi", "Sarı", "Yeşil", "Mor", "Şeftali" };

        public const string Cover = "#F4B6A6";
        public const string Gold = "#C99B3B";
        public const string Ink = "#4A3A36";
        public const string InkSoft = "#A08E89";

        public static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        public static readonly FontFamily Font = new FontFamily("Segoe UI");
        public static readonly FontFamily SymbolFont = new FontFamily("Segoe UI, Segoe UI Symbol");

        public static Color C(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.LightGray; }
        }

        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        /// <summary>t=0 → a, t=1 → b</summary>
        public static SolidColorBrush Mix(string a, string b, double t)
        {
            Color ca = C(a), cb = C(b);
            var c = Color.FromRgb(
                (byte)(ca.R + (cb.R - ca.R) * t),
                (byte)(ca.G + (cb.G - ca.G) * t),
                (byte)(ca.B + (cb.B - ca.B) * t));
            var br = new SolidColorBrush(c);
            br.Freeze();
            return br;
        }

        public static SolidColorBrush Darken(string hex, double f)
        {
            var c = C(hex);
            var br = new SolidColorBrush(Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f)));
            br.Freeze();
            return br;
        }

        public static string Hex(byte r, byte g, byte b)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }

        public static string Upper(string s) { return (s ?? "").ToUpper(Tr); }

        public static DropShadowEffect Shadow(double blur, double depth, double opacity, double direction)
        {
            return new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Direction = direction, Color = Colors.Black };
        }

        const string NS = " xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' ";

        static Style Parse(string xaml) { return (Style)XamlReader.Parse(xaml); }

        static Style checkStyle, flatButton, pillButton, listItem, paragraph, list, thumb;

        /// <summary>Notların içindeki to-do kutusu. Renk, ayracın rengini (AccentBrush) kullanır.</summary>
        public static Style CheckBoxStyle
        {
            get
            {
                if (checkStyle == null) checkStyle = Parse(
@"<Style TargetType='CheckBox'" + NS + @">
  <Setter Property='Margin' Value='0,0,7,0'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='VerticalAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='CheckBox'>
      <Border x:Name='box' Width='16' Height='16' CornerRadius='4' BorderThickness='1.6' BorderBrush='#B9A39C' Background='#FFFFFF'>
        <Path x:Name='tick' Data='M 2.5 6.5 L 5.4 9.4 L 10.4 3.6' Stroke='#4A3A36' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed' Margin='0.4,0.4,0,0'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'>
          <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource AccentDarkBrush}'/>
        </Trigger>
        <Trigger Property='IsChecked' Value='True'>
          <Setter TargetName='box' Property='Background' Value='{DynamicResource AccentBrush}'/>
          <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource AccentDarkBrush}'/>
          <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
        </Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
                return checkStyle;
            }
        }

        public static Style FlatButton
        {
            get
            {
                if (flatButton == null) flatButton = Parse(
@"<Style TargetType='Button'" + NS + @">
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Foreground' Value='#4A3A36'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#1F4A3A36'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='#3A4A3A36'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
                return flatButton;
            }
        }

        public static Style PillButton
        {
            get
            {
                if (pillButton == null) pillButton = Parse(
@"<Style TargetType='Button'" + NS + @">
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Foreground' Value='#4A3A36'/>
  <Setter Property='Background' Value='#FFFFFF'/>
  <Setter Property='Padding' Value='14,6'/>
  <Setter Property='Margin' Value='6,0,0,0'/>
  <Setter Property='FontSize' Value='12.5'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='15' BorderBrush='#E8CFC6' BorderThickness='1' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#C99B3B'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='#FCEDE7'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
                return pillButton;
            }
        }

        public static Style ListItemStyle
        {
            get
            {
                if (listItem == null) listItem = Parse(
@"<Style TargetType='ListBoxItem'" + NS + @">
  <Setter Property='Padding' Value='10,7'/>
  <Setter Property='Margin' Value='0,0,0,4'/>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='ListBoxItem'>
      <Border x:Name='bd' CornerRadius='12' Background='Transparent' Padding='{TemplateBinding Padding}'>
        <ContentPresenter/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#80FFFFFF'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='Background' Value='#FFFFFFFF'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
                return listItem;
            }
        }

        public static Style ParagraphStyle
        {
            get
            {
                if (paragraph == null) paragraph = Parse(
@"<Style TargetType='Paragraph'" + NS + @"><Setter Property='Margin' Value='0,2,0,2'/></Style>");
                return paragraph;
            }
        }

        public static Style ListStyle
        {
            get
            {
                if (list == null) list = Parse(
@"<Style TargetType='List'" + NS + @"><Setter Property='Margin' Value='0,2,0,4'/><Setter Property='Padding' Value='22,0,0,0'/></Style>");
                return list;
            }
        }

        /// <summary>Boyutlandırma tutamağı. Tamamen şeffaf olursa Windows tıklamayı alttaki pencereye geçirir, o yüzden neredeyse şeffaf.</summary>
        public static Style ThumbStyle
        {
            get
            {
                if (thumb == null) thumb = Parse(
@"<Style TargetType='Thumb'" + NS + @"><Setter Property='Template'><Setter.Value>
<ControlTemplate TargetType='Thumb'><Border Background='#02000000'/></ControlTemplate>
</Setter.Value></Setter></Style>");
                return thumb;
            }
        }

        public static FrameworkElement ColorHeader(string hex, string name)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(4),
                Background = B(hex), BorderBrush = B("#33000000"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
    }
}
