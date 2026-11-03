using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pardux
{
    /// <summary>Tema: renkler uygulama kaynaklarında tutulur, tüm pencereler DynamicResource ile anında güncellenir.</summary>
    internal static class Theme
    {
        public const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        public static bool IsDark { get; private set; }
        public static event Action Changed;

        /// <summary>Vurgu renkleri: ad → (ana renk, ikinci renk, görünen ad)</summary>
        public static readonly string[][] Accents =
        {
            new[] { "mor", "#8B6CFF", "#4F8DFF", "Mor" },
            new[] { "mavi", "#2F80FF", "#22C3E6", "Mavi" },
            new[] { "yesil", "#1FAE6B", "#21B5A8", "Yeşil" },
            new[] { "turuncu", "#FF7A2F", "#FF4F6D", "Turuncu" },
            new[] { "pembe", "#F2509A", "#A86BFF", "Pembe" },
            new[] { "kirmizi", "#E5404B", "#FF8A3D", "Kırmızı" },
        };

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return !(v is int && (int)v == 1);
                }
            }
            catch { return true; }
        }

        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static Color A(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static Brush B(string hex) { return B(C(hex)); }

        static string _sig;

        /// <summary>Ayarlara göre tema renklerini uygular. Değişiklik yoksa hiçbir şey yapmaz.</summary>
        public static void Apply(Settings s)
        {
            bool dark = s.Theme == "koyu" || (s.Theme != "acik" && SystemIsDark());
            bool glass = s.Backdrop != "duz" && Fluent.Supported;
            string sig = dark + "|" + s.Accent + "|" + s.Backdrop + "|" + Fluent.Supported;
            if (sig == _sig) return;
            _sig = sig;
            IsDark = dark;

            string[] acc = Accents[0];
            foreach (var a in Accents) if (a[0] == s.Accent) acc = a;
            Color main = C(acc[1]), second = C(acc[2]);

            Fill(Application.Current.Resources, dark, glass, main, second);
            // Ada her zaman siyah: kendi koyu paletini kullanır (vurgu rengi temadan gelir)
            Fill(Island, true, false, main, second);
            Island["IslandBg"] = B("#FF000000");
            Island["IslandEdge"] = B("#1EFFFFFF");
            Island["WinTint"] = B("#00000000");
            // macOS Spotlight gibi: seçili satır düz, dolgun bir vurgu rengi
            Island["PillBg"] = B(A(Mix(main, Colors.Black, 0.12), 0x8C));
            Island["Hover"] = B("#12FFFFFF");
            Island["Chip"] = B("#1AFFFFFF");
            Island["FooterBg"] = B("#00000000");
            Island["FieldBg"] = B("#1AFFFFFF");

            var h = Changed;
            if (h != null) h();
        }

        /// <summary>Adanın (dynamic island) her zaman koyu olan renk sözlüğü; ada penceresi bunu kendi kaynaklarına ekler.</summary>
        public static readonly ResourceDictionary Island = new ResourceDictionary();

        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        static void Fill(ResourceDictionary r, bool dark, bool glass, Color main, Color second)
        {
            Action<string, object> set = (k, v) => r[k] = v;

            // macOS'e yakın nötr gri tonlar
            if (dark)
            {
                set("WinTint", B(glass ? "#B81C1C1E" : "#FF1E1E20"));
                set("SideBg", B(glass ? "#40000000" : "#FF262628"));
                set("Fg", B("#F5F5F7")); set("FgDim", B("#A1A1A6")); set("FgMuted", B("#6E6E73"));
                set("Line", B("#14FFFFFF")); set("Hover", B("#10FFFFFF")); set("Chip", B("#1AFFFFFF"));
                set("FieldBg", B("#12FFFFFF")); set("FieldBorder", B("#1FFFFFFF")); set("FooterBg", B("#08FFFFFF"));
                set("BtnBg", B("#24FFFFFF")); set("BtnHover", B("#34FFFFFF")); set("Track", B("#3AFFFFFF")); set("Knob", B("#FFFFFF"));
                set("CardBg", B("#0DFFFFFF")); set("CardEdge", B("#12FFFFFF"));
                set("SegBg", B("#1FFFFFFF")); set("SegOn", B("#5C5C60"));
                set("ToastBg", B("#F2232325")); set("ToastEdge", B("#1FFFFFFF"));
                set("AccentText", B(Mix(main, Colors.White, 0.35)));
                set("AccentSoft", B(A(main, 0x30)));
            }
            else
            {
                set("WinTint", B(glass ? "#C8F5F5F7" : "#FFF5F5F7"));
                set("SideBg", B(glass ? "#30FFFFFF" : "#FFEBEBED"));
                set("Fg", B("#1D1D1F")); set("FgDim", B("#6E6E73")); set("FgMuted", B("#8E8E93"));
                set("Line", B("#12000000")); set("Hover", B("#0A000000")); set("Chip", B("#0E000000"));
                set("FieldBg", B("#FFFFFFFF")); set("FieldBorder", B("#1A000000")); set("FooterBg", B("#06000000"));
                set("BtnBg", B("#FFFFFFFF")); set("BtnHover", B("#F2F2F4")); set("Track", B("#26000000")); set("Knob", B("#FFFFFF"));
                set("CardBg", B("#B3FFFFFF")); set("CardEdge", B("#0F000000"));
                set("SegBg", B("#14000000")); set("SegOn", B("#FFFFFFFF"));
                set("ToastBg", B("#F5F6F6F8")); set("ToastEdge", B("#14000000"));
                set("AccentText", B(Mix(main, Colors.Black, 0.25)));
                set("AccentSoft", B(A(main, 0x26)));
            }
            set("BtnEdge", B(dark ? "#10FFFFFF" : "#1C000000"));
            set("Accent", B(main));
            set("AccentHover", B(Mix(main, dark ? Colors.White : Colors.Black, 0.1)));
            set("AccentBorder", B(A(main, 0x55)));
            set("OnAccent", B(Colors.White));
            set("LogoInk", B(Mix(main, Colors.Black, 0.55)));
            set("Danger", B("#E5484D"));
            set("Success", B("#3CC57A"));

            var grad = new LinearGradientBrush(main, second, new Point(0, 0), new Point(1, 1));
            grad.Freeze();
            set("AccentGrad", grad);
            var bar = new LinearGradientBrush(Mix(main, Colors.White, 0.3), second, new Point(0, 0), new Point(0, 1));
            bar.Freeze();
            set("AccentBar", bar);
            var pill = new LinearGradientBrush(A(main, (byte)(dark ? 0x38 : 0x2A)), A(second, (byte)(dark ? 0x14 : 0x10)), new Point(0, 0), new Point(1, 0));
            pill.Freeze();
            set("PillBg", pill);
            var glow = new RadialGradientBrush(A(main, (byte)(dark ? 0x30 : 0x22)), A(main, 0)) { Center = new Point(0.08, 0), GradientOrigin = new Point(0.08, 0), RadiusX = 0.55, RadiusY = 1.3 };
            glow.Freeze();
            set("Glow", glow);
        }

        public static Brush Brush(string key)
        {
            return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
        }

        /// <summary>
        /// Pardux logosu: grafit zemin üzerinde gümüş bir konuşma balonu yüz; yana taranmış perçem sol gözü örter.
        /// Gözler adlarıyla (…EyeL / …EyeR) canlandırılabilir; görünen göz sağdaki.
        /// </summary>
        public static string Logo(double size, double radius, string prefix)
        {
            return @"
<Grid Width='" + size + "' Height='" + size + @"'>
  <Border CornerRadius='" + radius + @"'>
    <Border.Background>
      <LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='#3A3A3E' Offset='0'/><GradientStop Color='#18181B' Offset='1'/></LinearGradientBrush>
    </Border.Background>
  </Border>
  <Border CornerRadius='" + radius + @"' BorderThickness='1' BorderBrush='#1AFFFFFF'/>
  <Viewbox>
    <Canvas Width='24' Height='24'>
      <Canvas RenderTransform='0.74,0,0,0.74,3.05,3.6'>
        <Path Fill='White' Data='M7,3 H17 A5,5 0 0 1 22,8 V13 A5,5 0 0 1 17,18 H11.4 C9.6,18 7.6,20.2 5.6,21.8 C6.2,20.4 6.6,19 6.4,18 C3.9,18 2,16.2 2,13 V8 A5,5 0 0 1 7,3 Z'/>
        <Path Fill='#232326' Data='M16.6,4.3 C15,5.2 13,6.6 11.4,8.6 C9.8,10.8 7.6,12.8 4.4,13.6 C3.4,12.2 3.3,10.2 3.3,8.4 L3.3,7.6 C3.3,5.4 5.2,4.3 7.4,4.3 Z'/>
        <Ellipse Canvas.Left='7.2' Canvas.Top='6.6' Width='2' Height='2' Fill='#232326' RenderTransformOrigin='0.5,0.5'>
          <Ellipse.RenderTransform><ScaleTransform x:Name='" + prefix + @"EyeL'/></Ellipse.RenderTransform>
        </Ellipse>
        <Ellipse Canvas.Left='14.9' Canvas.Top='7.4' Width='3.2' Height='3.2' Fill='#1E1E21' RenderTransformOrigin='0.5,0.5'>
          <Ellipse.RenderTransform><ScaleTransform x:Name='" + prefix + @"EyeR'/></Ellipse.RenderTransform>
        </Ellipse>
        <Path x:Name='" + prefix + @"Smile' Stroke='#1E1E21' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Data='M11.8,13.4 C13.4,15 15.6,15 17.4,13.2'/>
      </Canvas>
    </Canvas>
  </Viewbox>
</Grid>";
        }

        public const string Resources = @"
<ResourceDictionary " + Ns + @">
  <FontFamily x:Key='IconFont'>Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol</FontFamily>

  <Style x:Key='Field' TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='FontSize' Value='13.5'/>
    <Setter Property='Padding' Value='9,0'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='Bd' Background='{DynamicResource FieldBg}' CornerRadius='7' BorderThickness='1' BorderBrush='{DynamicResource FieldBorder}'>
            <Grid>
              <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
              <TextBlock x:Name='Wm' Text='{TemplateBinding Tag}' Foreground='{DynamicResource FgMuted}' Margin='{TemplateBinding Padding}' Padding='2,0,0,0'
                         IsHitTestVisible='False' Visibility='Collapsed' TextTrimming='CharacterEllipsis'
                         VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='Text' Value=''><Setter TargetName='Wm' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='Background' Value='{DynamicResource BtnBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource BtnEdge}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='14,6'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}' BorderThickness='1'
                  BorderBrush='{TemplateBinding BorderBrush}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.86'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.7'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='PrimaryBtn' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='{DynamicResource Accent}'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>
  <Style x:Key='DangerBtn' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='{DynamicResource Danger}'/>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>

  <Style x:Key='Ghost' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource FgDim}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='6' Padding='7,3'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/>
              <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Margin' Value='0,4,3,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='3' Background='{DynamicResource Track}'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- macOS segmentli denetim: SegHost içinde yan yana; seçili parça kabarık bir yüzey olur -->
  <Style x:Key='SegHost' TargetType='Border'>
    <Setter Property='Background' Value='{DynamicResource SegBg}'/>
    <Setter Property='CornerRadius' Value='8'/>
    <Setter Property='Padding' Value='2'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
  </Style>
  <Style x:Key='Seg' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='6' Padding='14,4' MinWidth='64'>
            <ContentPresenter HorizontalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SegOn}'/>
              <Setter TargetName='Bd' Property='Effect'>
                <Setter.Value><DropShadowEffect BlurRadius='3' ShadowDepth='1' Direction='270' Opacity='0.18' Color='Black'/></Setter.Value>
              </Setter>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- macOS Sistem Ayarları kenar çubuğu: renkli ikon karesi + ad; seçili satır vurgu renginde -->
  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='7' Padding='7,5' Margin='0,0,0,2'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Accent}'/>
              <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Gruplu ayar kartı ve satır ayırıcı -->
  <Style x:Key='Card' TargetType='Border'>
    <Setter Property='Background' Value='{DynamicResource CardBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource CardEdge}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='10'/>
  </Style>
  <Style x:Key='Sep' TargetType='Border'>
    <Setter Property='Height' Value='1'/>
    <Setter Property='Background' Value='{DynamicResource Line}'/>
    <Setter Property='Margin' Value='14,0'/>
  </Style>
  <Style x:Key='RowText' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
  <Style x:Key='GroupTitle' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Margin' Value='4,20,0,7'/>
  </Style>


  <Style x:Key='Swatch' TargetType='RadioButton'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid Width='28' Height='28' Margin='4,0,0,0'>
            <Ellipse x:Name='Ring' Stroke='{DynamicResource FgDim}' StrokeThickness='2' Opacity='0'/>
            <Ellipse Margin='4' Fill='{TemplateBinding Background}'/>
            <TextBlock x:Name='Tick' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' Foreground='White' FontSize='9'
                       HorizontalAlignment='Center' VerticalAlignment='Center' Opacity='0'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Ring' Property='Opacity' Value='0.35'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Ring' Property='Opacity' Value='1'/>
              <Setter TargetName='Tick' Property='Opacity' Value='1'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Label' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='{DynamicResource FgDim}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Margin' Value='2,14,0,6'/>
  </Style>
  <Style x:Key='Section' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='20'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Margin' Value='0,0,0,4'/>
  </Style>
  <Style x:Key='Hint' TargetType='TextBlock'>
    <Setter Property='Foreground' Value='{DynamicResource FgMuted}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='TextWrapping' Value='Wrap'/>
  </Style>

  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Padding' Value='14,9'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Background='Transparent'>
            <Grid Margin='{TemplateBinding Padding}'>
              <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
              <ContentPresenter VerticalAlignment='Center' Margin='0,0,14,0'/>
              <Border x:Name='Track' Grid.Column='1' Width='38' Height='22' CornerRadius='11' Background='{DynamicResource Track}' VerticalAlignment='Center'>
                <Ellipse x:Name='Knob' Width='18' Height='18' Fill='{DynamicResource Knob}' HorizontalAlignment='Left' Margin='2,0'>
                  <Ellipse.RenderTransform><TranslateTransform x:Name='KnobX'/></Ellipse.RenderTransform>
                  <Ellipse.Effect><DropShadowEffect BlurRadius='3' ShadowDepth='1' Direction='270' Opacity='0.3' Color='Black'/></Ellipse.Effect>
                </Ellipse>
              </Border>
            </Grid>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Track' Property='Background' Value='{DynamicResource Accent}'/>
              <Setter TargetName='Knob' Property='Fill' Value='White'/>
              <Trigger.EnterActions>
                <BeginStoryboard><Storyboard>
                  <DoubleAnimation Storyboard.TargetName='KnobX' Storyboard.TargetProperty='X' To='16' Duration='0:0:0.22'>
                    <DoubleAnimation.EasingFunction><BackEase EasingMode='EaseOut' Amplitude='0.3'/></DoubleAnimation.EasingFunction>
                  </DoubleAnimation>
                </Storyboard></BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard><Storyboard>
                  <DoubleAnimation Storyboard.TargetName='KnobX' Storyboard.TargetProperty='X' To='0' Duration='0:0:0.22'>
                    <DoubleAnimation.EasingFunction><BackEase EasingMode='EaseOut' Amplitude='0.3'/></DoubleAnimation.EasingFunction>
                  </DoubleAnimation>
                </Storyboard></BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        public static ResourceDictionary NewDict()
        {
            return (ResourceDictionary)XamlReader.Parse(Resources);
        }
    }
}
