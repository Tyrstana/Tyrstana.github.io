using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pardux
{
    /// <summary>
    /// Pardux Ada: ekranın üstünde duran siyah kapsül. Boşta küçük bir hap, müzik çalarken "şu an çalıyor",
    /// bildirimlerde uyarı kartı, Alt+Space ile yukarıdan aşağıya açılan arama paneli olur.
    ///
    /// Akıcılık için: adanın şekli (zemin, gölge, kırpma) içerikten bağımsız hafif bir katmandır. Animasyon sırasında
    /// içerik yeniden yerleşmez, yalnızca şekil büyür/küçülür ve içerik kırpılır. Hareket gerçek yay fiziğiyle yapılır.
    /// </summary>
    public class IslandWindow : Window, IHost
    {
        enum Mode { Hidden, Idle, Compact, Dot, Alert, Media, Watch, Search, Settings }

        // Boyutlar (WPF birimi)
        const double SearchW = 720, MediaW = 460, AlertW = 440, WatchW = 368, IdleW = 104, IdleH = 28, CompactH = 34, DotS = 26;
        const double WinW = 780, ShadowRoom = 46;

        /// <summary>Ayarlardaki ada stili: ekranın üst kenarına yapışık mı?</summary>
        bool Attached { get { return _c != null && _c.Settings != null && _c.Settings.IslandStyle == "yapisik"; } }

        /// <summary>Adanın ekranın üstüne uzaklığı (yapışık stilde sıfır).</summary>
        double Gap { get { return Attached ? 0 : 8; } }

        static string Xaml()
        {
            return @"
<Canvas x:Name='Root' " + Theme.Ns + @">
  <Canvas.RenderTransform><TranslateTransform x:Name='Drop'/></Canvas.RenderTransform>
  <Rectangle x:Name='Shadow' Fill='Black' IsHitTestVisible='False'>
    <Rectangle.Effect><DropShadowEffect BlurRadius='28' ShadowDepth='8' Direction='270' Opacity='0.45' Color='Black' RenderingBias='Performance'/></Rectangle.Effect>
  </Rectangle>
  <!-- Buzlu cam: panel açılırken arkadaki masaüstünün bulanık görüntüsü (zemin bunun üstünde yarı saydam olur) -->
  <Rectangle x:Name='Glass' IsHitTestVisible='False' Opacity='0' RenderOptions.BitmapScalingMode='Linear'/>
  <!-- Tazelenen cam görüntüsü eskisinin üstünde yumuşakça belirir (saniyelik yenilemede ani sıçrama olmasın) -->
  <Rectangle x:Name='GlassNext' IsHitTestVisible='False' Opacity='0' RenderOptions.BitmapScalingMode='Linear'/>
  <!-- Zemin: normalde siyah; müzik çalarken kapak renginin koyu tonuna bürünür -->
  <Rectangle x:Name='Bg' Stroke='{DynamicResource IslandEdge}' StrokeThickness='1'>
    <Rectangle.Fill>
      <LinearGradientBrush StartPoint='0,0' EndPoint='1,0.7'>
        <GradientStop x:Name='TintA' Color='Black' Offset='0'/>
        <GradientStop x:Name='TintB' Color='Black' Offset='1'/>
      </LinearGradientBrush>
    </Rectangle.Fill>
  </Rectangle>
  <Canvas x:Name='Stage'>

    <!-- Boşta: küçük göz kırpan Pardux yüzü -->
    <Grid x:Name='ViewIdle' Width='" + IdleW + "' Height='" + IdleH + @"' Background='Transparent' Cursor='Hand' ToolTip='Pardux’u aç'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center'>
        <Ellipse x:Name='EyeShapeL' Width='6' Height='8' Fill='White' Margin='0,0,9,0' RenderTransformOrigin='0.5,0.5'><Ellipse.RenderTransform><ScaleTransform x:Name='IdleEyeL'/></Ellipse.RenderTransform></Ellipse>
        <Ellipse x:Name='EyeShapeR' Width='6' Height='8' Fill='White' RenderTransformOrigin='0.5,0.5'><Ellipse.RenderTransform><ScaleTransform x:Name='IdleEyeR'/></Ellipse.RenderTransform></Ellipse>
      </StackPanel>
    </Grid>

    <!-- Kompakt: şu an çalan / zamanlayıcı -->
    <Grid x:Name='ViewCompact' Height='" + CompactH + @"' Background='Transparent'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/>
      </Grid.ColumnDefinitions>
      <Border Width='22' Height='22' CornerRadius='6' Margin='6,0,0,0' Background='#22FFFFFF' VerticalAlignment='Center'>
        <Grid>
          <TextBlock x:Name='CGlyph' FontFamily='{DynamicResource IconFont}' FontSize='11' Foreground='{DynamicResource AccentText}'
                     HorizontalAlignment='Center' VerticalAlignment='Center' Text='&#xE8D6;'/>
          <Border x:Name='CArt' CornerRadius='6'/>
        </Grid>
      </Border>
      <TextBlock x:Name='CTitle' Grid.Column='1' Margin='9,0,8,0' Foreground='{DynamicResource Fg}' FontSize='12' FontWeight='SemiBold'
                 VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
      <Border x:Name='CTimer' Grid.Column='2' CornerRadius='9' Padding='7,1' Margin='0,0,6,0' VerticalAlignment='Center' Background='{DynamicResource AccentSoft}'>
        <StackPanel Orientation='Horizontal'>
          <TextBlock x:Name='CTimerGlyph' Text='&#xE916;' FontFamily='{DynamicResource IconFont}' FontSize='10' Foreground='{DynamicResource AccentText}' VerticalAlignment='Center' Margin='0,0,4,0'/>
          <TextBlock x:Name='CTimerText' Foreground='{DynamicResource AccentText}' FontSize='11.5' FontWeight='SemiBold' VerticalAlignment='Center'/>
        </StackPanel>
      </Border>
      <Grid Grid.Column='3' MinWidth='30'>
        <StackPanel x:Name='CBars' Orientation='Horizontal' Height='14' Margin='0,0,12,0' VerticalAlignment='Center' HorizontalAlignment='Right'/>
        <!-- Üzerine gelince: adayı noktaya küçült -->
        <Border x:Name='CMin' Width='22' Height='22' CornerRadius='11' Margin='0,0,6,0' HorizontalAlignment='Right' VerticalAlignment='Center'
                Cursor='Hand' Opacity='0' IsHitTestVisible='False' ToolTip='Noktaya küçült'>
          <Border.Style>
            <Style TargetType='Border'>
              <Setter Property='Background' Value='#22FFFFFF'/>
              <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#3DFFFFFF'/></Trigger></Style.Triggers>
            </Style>
          </Border.Style>
          <Ellipse Width='6' Height='6' Fill='White' HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
      </Grid>
    </Grid>

    <!-- Nokta: adanın en küçük hali -->
    <Grid x:Name='ViewDot' Width='" + DotS + "' Height='" + DotS + @"' Background='Transparent' Cursor='Hand' ToolTip='Genişletmek için tıkla'>
      <Ellipse x:Name='DotCore' Width='9' Height='9' Fill='White' RenderTransformOrigin='0.5,0.5'>
        <Ellipse.RenderTransform><ScaleTransform x:Name='DotScale'/></Ellipse.RenderTransform>
      </Ellipse>
    </Grid>

    <!-- Kronometre / zamanlayıcı kartı -->
    <Grid x:Name='ViewWatch' Width='" + WatchW + @"' Background='Transparent'>
      <Grid Margin='18,12,12,12'>
        <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <StackPanel VerticalAlignment='Center'>
          <TextBlock x:Name='WLabel' Foreground='{DynamicResource FgMuted}' FontSize='11.5' TextTrimming='CharacterEllipsis'/>
          <TextBlock x:Name='WTime' Foreground='{DynamicResource Fg}' FontSize='30' FontWeight='SemiBold' FontFamily='Segoe UI Variable Display, Segoe UI'
                     Typography.NumeralAlignment='Tabular' Margin='0,-2,0,0'/>
        </StackPanel>
        <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
          <Border x:Name='WLeft' Style='{DynamicResource RoundBtn}' Width='42' Height='42' Margin='0,0,10,0'>
            <TextBlock x:Name='WLeftGlyph' Style='{DynamicResource RoundGlyph}'/>
          </Border>
          <Border x:Name='WRight' Style='{DynamicResource RoundBtn}' Width='42' Height='42'>
            <TextBlock x:Name='WRightGlyph' Style='{DynamicResource RoundGlyph}' FontSize='15'/>
          </Border>
          <!-- Kartı küçült: sayaç adada sayarken küçük kapsüle döner -->
          <Border x:Name='WMin' Width='28' Height='28' CornerRadius='14' Margin='10,0,0,0' Cursor='Hand' ToolTip='Küçült' VerticalAlignment='Center'>
            <Border.Style>
              <Style TargetType='Border'>
                <Setter Property='Background' Value='#16FFFFFF'/>
                <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#30FFFFFF'/></Trigger></Style.Triggers>
              </Style>
            </Border.Style>
            <TextBlock Text='&#xE70E;' FontFamily='{DynamicResource IconFont}' FontSize='10' Foreground='{DynamicResource FgDim}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
        </StackPanel>
      </Grid>
    </Grid>

    <!-- Uyarı / bildirim kartı -->
    <Grid x:Name='ViewAlert' Background='Transparent'>
      <Grid Margin='14,12,16,13'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
        <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
        <Border x:Name='AIconBox' Width='40' Height='40' CornerRadius='12' VerticalAlignment='Center'>
          <Grid>
            <TextBlock x:Name='AGlyph' FontFamily='{DynamicResource IconFont}' FontSize='17' HorizontalAlignment='Center' VerticalAlignment='Center'/>
            <ContentControl x:Name='ALogo' Focusable='False'>" + Theme.Logo(40, 12, "A") + @"</ContentControl>
          </Grid>
        </Border>
        <StackPanel Grid.Column='1' Margin='13,0,0,0' VerticalAlignment='Center'>
          <TextBlock x:Name='ATitle' Foreground='{DynamicResource Fg}' FontSize='14' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
          <TextBlock x:Name='ASub' Foreground='{DynamicResource FgDim}' FontSize='12.5' Margin='0,2,0,0' TextWrapping='Wrap' MaxHeight='36' TextTrimming='CharacterEllipsis'/>
        </StackPanel>
        <StackPanel x:Name='AButtons' Grid.Row='1' Grid.ColumnSpan='2' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,12,0,0'>
          <Button x:Name='ASnooze' Content='5 dk ertele' Style='{DynamicResource Btn}' Padding='14,6' Margin='0,0,8,0'/>
          <Button x:Name='AOk' Content='Tamam' Style='{DynamicResource PrimaryBtn}' Padding='18,6'/>
        </StackPanel>
      </Grid>
    </Grid>

    <!-- Medya kartı -->
    <Grid x:Name='ViewMedia' Width='" + MediaW + @"' Background='Transparent'>
      <StackPanel Margin='18,16,16,15'>
        <Grid>
          <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
          <Border Width='64' Height='64' CornerRadius='14' Background='#22FFFFFF'>
            <Grid>
              <TextBlock FontFamily='{DynamicResource IconFont}' FontSize='24' Foreground='{DynamicResource FgMuted}' Text='&#xE8D6;'
                         HorizontalAlignment='Center' VerticalAlignment='Center'/>
              <Border x:Name='MArt' CornerRadius='14'/>
            </Grid>
          </Border>
          <StackPanel Grid.Column='1' Margin='14,1,10,0' VerticalAlignment='Center'>
            <StackPanel Orientation='Horizontal'>
              <TextBlock x:Name='MApp' Foreground='{DynamicResource FgMuted}' FontSize='11.5' VerticalAlignment='Center'/>
              <StackPanel x:Name='MBars' Orientation='Horizontal' Height='11' Margin='8,0,0,0' VerticalAlignment='Center'/>
            </StackPanel>
            <TextBlock x:Name='MTitle' Foreground='{DynamicResource Fg}' FontSize='16' FontWeight='SemiBold' TextTrimming='CharacterEllipsis' Margin='0,3,0,0'/>
            <TextBlock x:Name='MArtist' Foreground='{DynamicResource FgDim}' FontSize='13' TextTrimming='CharacterEllipsis' Margin='0,1,0,0'/>
          </StackPanel>
          <Border x:Name='MClose' Grid.Column='2' Width='26' Height='26' CornerRadius='13' VerticalAlignment='Top' Cursor='Hand'
                  ToolTip='Adayı gizle (müzik duraklayana kadar)'>
            <Border.Style>
              <Style TargetType='Border'>
                <Setter Property='Background' Value='#16FFFFFF'/>
                <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#30FFFFFF'/></Trigger></Style.Triggers>
              </Style>
            </Border.Style>
            <TextBlock Text='&#xE8BB;' FontFamily='{DynamicResource IconFont}' FontSize='9' Foreground='{DynamicResource FgDim}'
                       HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
        </Grid>
        <Grid Margin='0,14,0,0'>
          <Grid.ColumnDefinitions><ColumnDefinition Width='44'/><ColumnDefinition/><ColumnDefinition Width='44'/></Grid.ColumnDefinitions>
          <TextBlock x:Name='MPos' Foreground='{DynamicResource FgMuted}' FontSize='11' VerticalAlignment='Center'/>
          <!-- Tıklanabilir ilerleme çubuğu: tıklanan yere sarar (geniş görünmez tıklama alanı) -->
          <Grid x:Name='MSeek' Grid.Column='1' Height='16' Background='Transparent' Cursor='Hand' ToolTip='Sarmak için tıkla'>
            <Grid x:Name='MTrack' Height='4' VerticalAlignment='Center' RenderTransformOrigin='0.5,0.5'>
              <Grid.RenderTransform><ScaleTransform x:Name='MTrackScale'/></Grid.RenderTransform>
              <Border CornerRadius='2' Background='#26FFFFFF'/>
              <Border x:Name='MProgress' CornerRadius='2' Background='White' RenderTransformOrigin='0,0.5'>
                <Border.RenderTransform><ScaleTransform x:Name='MProgressScale' ScaleX='0'/></Border.RenderTransform>
              </Border>
            </Grid>
          </Grid>
          <TextBlock x:Name='MDur' Grid.Column='2' Foreground='{DynamicResource FgMuted}' FontSize='11' HorizontalAlignment='Right' VerticalAlignment='Center'/>
        </Grid>
        <Grid Margin='0,10,0,0'>
        <!-- Döngü: kapalı → tüm liste → bu şarkı (sağ kenarda; açıkken vurgu renginde, bu şarkı modunda küçük 1 rozeti) -->
        <Border x:Name='MRepeat' Width='32' Height='32' CornerRadius='16' HorizontalAlignment='Right' VerticalAlignment='Center' Cursor='Hand'
                ToolTip='Döngü' Visibility='Collapsed'>
          <Border.Style>
            <Style TargetType='Border'>
              <Setter Property='Background' Value='Transparent'/>
              <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#1EFFFFFF'/></Trigger></Style.Triggers>
            </Style>
          </Border.Style>
          <Grid>
            <TextBlock x:Name='MRepeatGlyph' Text='&#xE8EE;' FontFamily='{DynamicResource IconFont}' FontSize='14' Foreground='{DynamicResource FgDim}'
                       HorizontalAlignment='Center' VerticalAlignment='Center'/>
            <Ellipse x:Name='MRepeatDot' Width='4' Height='4' Fill='{DynamicResource AccentText}' VerticalAlignment='Bottom' HorizontalAlignment='Center'
                     Margin='0,0,0,1' Opacity='0'/>
          </Grid>
        </Border>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Center'>
          <Border x:Name='MBack' Style='{DynamicResource RoundBtn}' Margin='0,0,14,0' Background='Transparent' ToolTip='10 sn geri sar'>
            <TextBlock Text='−10' Style='{DynamicResource RoundGlyph}' FontFamily='Segoe UI Variable Text, Segoe UI' FontSize='12' FontWeight='SemiBold' Opacity='0.8'/>
          </Border>
          <Border x:Name='MPrev' Style='{DynamicResource RoundBtn}' ToolTip='Önceki'><TextBlock Text='&#xE892;' Style='{DynamicResource RoundGlyph}'/></Border>
          <Border x:Name='MPlay' Style='{DynamicResource RoundBtn}' Width='46' Height='46' Margin='16,0' Background='White'>
            <TextBlock x:Name='MPlayGlyph' Text='&#xE769;' Style='{DynamicResource RoundGlyph}' Foreground='Black' FontSize='17'/>
          </Border>
          <Border x:Name='MNext' Style='{DynamicResource RoundBtn}' ToolTip='Sonraki'><TextBlock Text='&#xE893;' Style='{DynamicResource RoundGlyph}'/></Border>
          <Border x:Name='MFwd' Style='{DynamicResource RoundBtn}' Margin='14,0,0,0' Background='Transparent' ToolTip='10 sn ileri sar'>
            <TextBlock Text='+10' Style='{DynamicResource RoundGlyph}' FontFamily='Segoe UI Variable Text, Segoe UI' FontSize='12' FontWeight='SemiBold' Opacity='0.8'/>
          </Border>
        </StackPanel>
        </Grid>
      </StackPanel>
    </Grid>

    <!-- Arama paneli -->
    <StackPanel x:Name='ViewSearch' Width='" + SearchW + @"' Background='Transparent'>
      <Grid x:Name='Header' Margin='16,14,12,12'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <ContentControl x:Name='LogoHost' Focusable='False' RenderTransformOrigin='0.5,0.5'>
          <ContentControl.RenderTransform><ScaleTransform x:Name='LogoScale'/></ContentControl.RenderTransform>
          " + Theme.Logo(36, 11, "L") + @"
        </ContentControl>
        <Grid Grid.Column='1' Margin='12,0,8,0'>
          <TextBox x:Name='Input' FontSize='19' Background='Transparent' BorderThickness='0' Padding='0'
                   Foreground='{DynamicResource Fg}' CaretBrush='{DynamicResource Accent}' SelectionBrush='{DynamicResource Accent}'
                   VerticalContentAlignment='Center' FocusVisualStyle='{x:Null}'/>
          <TextBlock x:Name='Placeholder' IsHitTestVisible='False' FontSize='19' Foreground='{DynamicResource FgMuted}' Margin='3,0,0,0'
                     VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Text='Ne yapmak istersin?'/>
        </Grid>
        <Border x:Name='Gear' Grid.Column='2' Style='{DynamicResource IconBtn}' ToolTip='Ayarlar (Ctrl+,)'>
          <TextBlock Text='&#xE713;' Style='{DynamicResource IconGlyph}'/>
        </Border>
        <Border x:Name='CloseBtn' Grid.Column='3' Style='{DynamicResource IconBtn}' ToolTip='Kapat (Esc)'>
          <TextBlock Text='&#xE8BB;' Style='{DynamicResource IconGlyph}' FontSize='11'/>
        </Border>
      </Grid>
      <Border Height='1' Background='{DynamicResource Line}' Margin='14,0'/>

      <Grid x:Name='ResultsHost' Margin='8,6,8,6' ClipToBounds='True'>
        <Border x:Name='Pill' VerticalAlignment='Top' CornerRadius='10' Opacity='0' IsHitTestVisible='False' Background='{DynamicResource PillBg}'>
          <Border.RenderTransform><TranslateTransform x:Name='PillY'/></Border.RenderTransform>
        </Border>
        <ListBox x:Name='Results' Background='Transparent' BorderThickness='0' MaxHeight='470' Focusable='False'
                 ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Hidden'
                 VirtualizingPanel.IsVirtualizing='False'>
          <ListBox.ItemContainerStyle>
            <Style TargetType='ListBoxItem'>
              <Setter Property='Focusable' Value='False'/>
              <Setter Property='Cursor' Value='Hand'/>
              <Setter Property='Template'>
                <Setter.Value>
                  <ControlTemplate TargetType='ListBoxItem'>
                    <Border x:Name='Bd' CornerRadius='10' Padding='10,7' Margin='0,1' Background='Transparent'>
                      <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <MultiTrigger>
                        <MultiTrigger.Conditions>
                          <Condition Property='IsMouseOver' Value='True'/><Condition Property='IsSelected' Value='False'/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/>
                      </MultiTrigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </ListBox.ItemContainerStyle>
          <ListBox.ItemTemplate>
            <DataTemplate>
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
                <Grid Width='36' Height='36' VerticalAlignment='Center'>
                  <Border CornerRadius='9' Background='{Binding GlyphBackground}' Visibility='{Binding GlyphVisibility}'>
                    <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol' FontSize='16' Foreground='{Binding GlyphBrush}'
                               HorizontalAlignment='Center' VerticalAlignment='Center'/>
                  </Border>
                  <Border CornerRadius='8' Visibility='{Binding IconVisibility}' RenderOptions.BitmapScalingMode='HighQuality'>
                    <Border.Background><ImageBrush ImageSource='{Binding Icon}' Stretch='UniformToFill'/></Border.Background>
                  </Border>
                </Grid>
                <StackPanel Grid.Column='1' VerticalAlignment='Center' Margin='12,0,10,0'>
                  <TextBlock Text='{Binding Title}' Foreground='{DynamicResource Fg}' FontSize='14' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
                  <TextBlock Text='{Binding Subtitle}' Foreground='{DynamicResource FgDim}' FontSize='12'>
                    <TextBlock.Style>
                      <Style TargetType='TextBlock'>
                        <Setter Property='TextTrimming' Value='CharacterEllipsis'/>
                        <Setter Property='Margin' Value='0,1,0,0'/>
                        <Style.Triggers>
                          <!-- İnternetten gelen cevap birkaç satır olarak okunur -->
                          <DataTrigger Binding='{Binding Wrap}' Value='True'>
                            <Setter Property='TextWrapping' Value='Wrap'/>
                            <Setter Property='MaxHeight' Value='86'/>
                            <Setter Property='LineHeight' Value='17'/>
                            <Setter Property='Margin' Value='0,3,0,2'/>
                          </DataTrigger>
                        </Style.Triggers>
                      </Style>
                    </TextBlock.Style>
                  </TextBlock>
                </StackPanel>
                <Border x:Name='HintBd' Grid.Column='2' CornerRadius='6' Background='{DynamicResource Chip}' Padding='8,3' Margin='0,0,2,0' VerticalAlignment='Center' Opacity='0'>
                  <TextBlock Text='{Binding Hint}' Foreground='{DynamicResource Fg}' Opacity='0.8' FontSize='11'/>
                </Border>
              </Grid>
              <DataTemplate.Triggers>
                <DataTrigger Binding='{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}' Value='True'>
                  <DataTrigger.EnterActions>
                    <BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='HintBd' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.18'/></Storyboard></BeginStoryboard>
                  </DataTrigger.EnterActions>
                  <DataTrigger.ExitActions>
                    <BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='HintBd' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.12'/></Storyboard></BeginStoryboard>
                  </DataTrigger.ExitActions>
                </DataTrigger>
              </DataTemplate.Triggers>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </Grid>

      <!-- Arama kutusu boşken: takvim, pil, hava, gündem -->
      <ContentControl x:Name='HomeHost' Focusable='False' Visibility='Collapsed'/>

      <StackPanel x:Name='Compose' Visibility='Collapsed' Margin='22,4,22,14'>
        <Grid>
          <StackPanel Orientation='Horizontal' Margin='0,6,0,0'>
            <TextBlock x:Name='ComposeIcon' Text='&#xE715;' FontFamily='{DynamicResource IconFont}' Foreground='#FF7A7A' FontSize='15' VerticalAlignment='Center'/>
            <TextBlock x:Name='ComposeTitle' Text='Yeni mail' Foreground='{DynamicResource Fg}' FontSize='16' FontWeight='SemiBold' Margin='9,0,0,0'/>
          </StackPanel>
          <TextBlock x:Name='MailProviderLabel' HorizontalAlignment='Right' VerticalAlignment='Bottom' Foreground='{DynamicResource FgDim}' FontSize='12'/>
        </Grid>
        <TextBlock Text='Kime' Style='{DynamicResource Label}' Margin='2,12,0,6'/>
        <TextBox x:Name='MailTo' Style='{DynamicResource Field}' Tag='mail adresi ya da kayıtlı kişi adı (birden fazlaysa virgülle)'/>
        <StackPanel x:Name='SubjectRow'>
          <TextBlock Text='Konu' Style='{DynamicResource Label}' Margin='2,12,0,6'/>
          <TextBox x:Name='MailSubject' Style='{DynamicResource Field}'/>
        </StackPanel>
        <TextBlock Text='Mesaj' Style='{DynamicResource Label}' Margin='2,12,0,6'/>
        <TextBox x:Name='MailBody' Style='{DynamicResource Field}' AcceptsReturn='True' TextWrapping='Wrap' Height='150' Padding='10,8'
                 VerticalScrollBarVisibility='Auto' VerticalContentAlignment='Top'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,14,0,0'>
          <Button x:Name='MailCancel' Content='İptal' Style='{DynamicResource Btn}' Margin='0,0,8,0'/>
          <Button x:Name='MailSend' Content='Taslağı aç  ·  Ctrl+Enter' Style='{DynamicResource PrimaryBtn}'/>
        </StackPanel>
      </StackPanel>

      <Grid Margin='18,2,16,11'>
        <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <TextBlock x:Name='Status' Foreground='{DynamicResource FgMuted}' FontSize='11.5' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/>
        <Border Grid.Column='1' CornerRadius='5' Background='{DynamicResource Chip}' Padding='7,2' Margin='12,0,0,0' VerticalAlignment='Center'>
          <TextBlock x:Name='HotkeyText' Foreground='{DynamicResource FgDim}' FontSize='10.5'/>
        </Border>
      </Grid>
    </StackPanel>
  </Canvas>
</Canvas>";
        }

        internal const string LocalStyles = @"
<ResourceDictionary " + Theme.Ns + @">
  <Style x:Key='IconBtn' TargetType='Border'>
    <Setter Property='Width' Value='34'/>
    <Setter Property='Height' Value='34'/>
    <Setter Property='CornerRadius' Value='17'/>
    <Setter Property='Margin' Value='4,0,0,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Background' Value='Transparent'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource Chip}'/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key='IconGlyph' TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{DynamicResource IconFont}'/>
    <Setter Property='FontSize' Value='15'/>
    <Setter Property='Foreground' Value='{DynamicResource FgDim}'/>
    <Setter Property='HorizontalAlignment' Value='Center'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
  <Style x:Key='RoundBtn' TargetType='Border'>
    <Setter Property='Width' Value='38'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='CornerRadius' Value='23'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Background' Value='#14FFFFFF'/>
    <Setter Property='RenderTransformOrigin' Value='0.5,0.5'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.85'/></Trigger>
    </Style.Triggers>
  </Style>
  <Style x:Key='RoundGlyph' TargetType='TextBlock'>
    <Setter Property='FontFamily' Value='{DynamicResource IconFont}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='HorizontalAlignment' Value='Center'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
</ResourceDictionary>";

        const string SearchStatus = "↑↓ gezin  ·  Enter çalıştır  ·  Tab tamamla  ·  Esc kapat";

        static readonly string[] Examples =
        {
            "örn: discord aç", "örn: müzik aç", "örn: tarkan şımarık çal", "örn: ankara hava durumu", "örn: 100 dolar kaç tl",
            "örn: 15 dk sonra toplantıyı hatırlat", "örn: bul sunum", "örn: ses 40", "örn: pano", "örn: (1250*12)/7", "örn: karanlık mod"
        };

        // ---- Şekil durumları (animasyonla değişir; değişince yalnızca hafif şekil katmanı güncellenir) ----
        static readonly PropertyChangedCallback ShapeChanged = (d, e) => ((IslandWindow)d).ApplyGeometry();
        public static readonly DependencyProperty IWProperty = DependencyProperty.Register("IW", typeof(double), typeof(IslandWindow), new PropertyMetadata(IdleW, ShapeChanged));
        public static readonly DependencyProperty IHProperty = DependencyProperty.Register("IH", typeof(double), typeof(IslandWindow), new PropertyMetadata(IdleH, ShapeChanged));
        public static readonly DependencyProperty IRProperty = DependencyProperty.Register("IR", typeof(double), typeof(IslandWindow), new PropertyMetadata(IdleH / 2, ShapeChanged));

        double IW { get { return (double)GetValue(IWProperty); } set { SetValue(IWProperty, value); } }
        double IH { get { return (double)GetValue(IHProperty); } set { SetValue(IHProperty, value); } }
        double IR { get { return (double)GetValue(IRProperty); } set { SetValue(IRProperty, value); } }

        readonly Controller _c;
        readonly CommandEngine _engine;
        readonly Canvas Root, Stage;
        readonly Rectangle Shadow, Bg;
        readonly RectangleGeometry _clip = new RectangleGeometry();
        readonly TranslateTransform Drop, PillY;
        readonly Border CArt, CTimer, MArt, AIconBox, CMin;
        readonly Ellipse DotCore;
        readonly GradientStop TintA, TintB;
        Color _tintA = Colors.Black, _tintB = Colors.Black;
        readonly FrameworkElement ViewIdle, ViewCompact, ViewDot, ViewAlert, ViewMedia, ViewWatch, ViewSearch, LogoHost, ResultsHost, Compose, Pill, ALogo, AButtons;
        readonly SettingsPanel ViewSettings;
        readonly Rectangle Glass, GlassNext;
        readonly TextBlock WLabel, WTime, WLeftGlyph, WRightGlyph;
        readonly Border WRight;
        readonly DispatcherTimer _watchTick;
        /// <summary>Dosya seçme gibi bir iletişim kutusu açıkken odak kaybı paneli kapatmasın.</summary>
        public static bool ModalOpen;
        readonly TextBox Input, MailTo, MailSubject, MailBody;
        readonly TextBlock Placeholder, Status, HotkeyText, MailProviderLabel, CTitle, CTimerText, CTimerGlyph, CGlyph, ATitle, ASub, AGlyph, MApp, MTitle, MArtist, MPos, MDur, MPlayGlyph;
        readonly StackPanel CBars, MBars;
        readonly ListBox Results;
        readonly ScaleTransform EyeL, EyeR, LogoScale, IdleEyeL, IdleEyeR, MProgressScale, DotScale;
        readonly DispatcherTimer _statusTimer, _blinkTimer, _tick, _alertTimer, _mediaLeave;
        DispatcherTimer _hoverLeave;
        HomeView _home;
        readonly Random _rnd = new Random();

        Mode _mode = Mode.Hidden;
        int _exampleIndex, _version, _morphId;
        string _lastFirstTitle;
        bool _pillShown, _blocked, _hover, _dismissed, _dotted;
        string _dismissApp;
        IntPtr _prevForeground, _hwnd, _mouseHook;
        Native.LowLevelMouseProc _mouseProc;
        Native.WinEventProc _fgProc2; // temsilci çöp toplayıcıya gitmesin
        DateTime _lastPlaying = DateTime.MinValue;
        Action _alertOk, _alertSnooze;
        readonly Queue<Action> _pendingAlerts = new Queue<Action>();
        double _targetW = IdleW, _targetH = IdleH, _targetR = IdleH / 2;

        /// <summary>Yalnızca görsel testler için: pencereyi ekran dışında açar ve odak almaz.</summary>
        public static bool TestMode;

        public IslandWindow(Controller c)
        {
            _c = c;
            Title = "Pardux";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Resources.MergedDictionaries.Add(Theme.NewDict());
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(LocalStyles));
            Resources.MergedDictionaries.Add(Theme.Island);

            var root = (Canvas)XamlReader.Parse(Xaml());
            Content = root;
            Root = root;
            Func<string, object> f = root.FindName;
            Stage = (Canvas)f("Stage"); Shadow = (Rectangle)f("Shadow"); Bg = (Rectangle)f("Bg"); Drop = (TranslateTransform)f("Drop");
            ViewIdle = (FrameworkElement)f("ViewIdle"); ViewCompact = (FrameworkElement)f("ViewCompact"); ViewAlert = (FrameworkElement)f("ViewAlert");
            TintA = (GradientStop)f("TintA"); TintB = (GradientStop)f("TintB");
            ViewWatch = (FrameworkElement)f("ViewWatch"); Glass = (Rectangle)f("Glass"); GlassNext = (Rectangle)f("GlassNext");
            WLabel = (TextBlock)f("WLabel"); WTime = (TextBlock)f("WTime"); WLeftGlyph = (TextBlock)f("WLeftGlyph"); WRightGlyph = (TextBlock)f("WRightGlyph"); WRight = (Border)f("WRight");
            ViewSettings = new SettingsPanel(c);
            Stage.Children.Add(ViewSettings);
            ViewDot = (FrameworkElement)f("ViewDot"); DotCore = (Ellipse)f("DotCore"); DotScale = (ScaleTransform)f("DotScale"); CMin = (Border)f("CMin");
            ViewMedia = (FrameworkElement)f("ViewMedia"); ViewSearch = (FrameworkElement)f("ViewSearch");
            IdleEyeL = (ScaleTransform)f("IdleEyeL"); IdleEyeR = (ScaleTransform)f("IdleEyeR");
            CArt = (Border)f("CArt"); CGlyph = (TextBlock)f("CGlyph"); CTitle = (TextBlock)f("CTitle");
            CTimer = (Border)f("CTimer"); CTimerText = (TextBlock)f("CTimerText"); CTimerGlyph = (TextBlock)f("CTimerGlyph"); CBars = (StackPanel)f("CBars");
            AIconBox = (Border)f("AIconBox"); AGlyph = (TextBlock)f("AGlyph"); ALogo = (FrameworkElement)f("ALogo");
            ATitle = (TextBlock)f("ATitle"); ASub = (TextBlock)f("ASub"); AButtons = (FrameworkElement)f("AButtons");
            MArt = (Border)f("MArt"); MApp = (TextBlock)f("MApp"); MTitle = (TextBlock)f("MTitle"); MArtist = (TextBlock)f("MArtist");
            MPos = (TextBlock)f("MPos"); MDur = (TextBlock)f("MDur"); MProgressScale = (ScaleTransform)f("MProgressScale"); MBars = (StackPanel)f("MBars");
            MPlayGlyph = (TextBlock)f("MPlayGlyph");
            LogoHost = (FrameworkElement)f("LogoHost"); LogoScale = (ScaleTransform)f("LogoScale");
            EyeL = (ScaleTransform)f("LEyeL"); EyeR = (ScaleTransform)f("LEyeR");
            Input = (TextBox)f("Input"); Placeholder = (TextBlock)f("Placeholder");
            Status = (TextBlock)f("Status"); HotkeyText = (TextBlock)f("HotkeyText");
            Results = (ListBox)f("Results"); ResultsHost = (FrameworkElement)f("ResultsHost");
            Pill = (FrameworkElement)f("Pill"); PillY = (TranslateTransform)f("PillY");
            Compose = (FrameworkElement)f("Compose");
            MailTo = (TextBox)f("MailTo"); MailSubject = (TextBox)f("MailSubject"); MailBody = (TextBox)f("MailBody");
            MailProviderLabel = (TextBlock)f("MailProviderLabel");

            Stage.Clip = _clip;
            foreach (var v in Views)
            {
                v.Opacity = 0;
                v.IsHitTestVisible = false;
                v.RenderTransform = new TranslateTransform();
                Canvas.SetTop(v, Gap);
            }
            BuildBars(CBars, 4, 2.6, 14);
            BuildBars(MBars, 4, 2.5, 11);
            Root.Opacity = 0;
            ApplyGeometry();

            // Olaylar
            ((Button)f("MailSend")).Click += (s, e) => SendMail();
            ((Button)f("MailCancel")).Click += (s, e) => CloseCompose();
            ((Button)f("AOk")).Click += (s, e) => { var a = _alertOk; _alertOk = null; if (a != null) a(); EndAlert(); };
            ((Button)f("ASnooze")).Click += (s, e) => { var a = _alertSnooze; _alertSnooze = null; if (a != null) a(); EndAlert(); };
            Ui.Click((FrameworkElement)f("Gear"), OpenSettings);
            Ui.Click((FrameworkElement)f("CloseBtn"), () => CloseSearch(true));
            Ui.Click((FrameworkElement)f("MPrev"), () => { Press(f("MPrev")); MediaWatcher.Prev(); });
            Ui.Click((FrameworkElement)f("MNext"), () => { Press(f("MNext")); MediaWatcher.Next(); });
            Ui.Click((FrameworkElement)f("MPlay"), () => { Press(f("MPlay")); MediaWatcher.PlayPause(); });
            Ui.Click((FrameworkElement)f("MRepeat"), () =>
            {
                Press(f("MRepeat"));
                MediaWatcher.ToggleRepeat(mode => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (mode == null) { MApp.Text = "Döngü değiştirilemedi"; _seekMsgUntil = DateTime.Now.AddSeconds(2); return; }
                    _repeatShown = mode; _repeatHoldUntil = DateTime.Now.AddSeconds(2);
                    MApp.Text = mode == "track" ? "Döngü: bu şarkı" : mode == "list" ? "Döngü: tüm liste" : "Döngü kapalı";
                    _seekMsgUntil = DateTime.Now.AddSeconds(1.6);
                    UpdateRepeat(MediaWatcher.Current);
                })));
            });
            Ui.Click((FrameworkElement)f("MClose"), DismissMusic);
            Ui.Click((FrameworkElement)f("MBack"), () => { Press(f("MBack")); SeekBy(-10); });
            Ui.Click((FrameworkElement)f("MFwd"), () => { Press(f("MFwd")); SeekBy(10); });
            var seek = (FrameworkElement)f("MSeek");
            var trackScale = (ScaleTransform)f("MTrackScale");
            seek.MouseEnter += (s, e) => trackScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.6, 160));
            seek.MouseLeave += (s, e) => trackScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 200));
            seek.MouseLeftButtonDown += (s, e) =>
            {
                var np = MediaWatcher.Current;
                if (np == null || np.Duration.TotalSeconds < 1) return;
                double frac = Math.Max(0, Math.Min(1, e.GetPosition(seek).X / seek.ActualWidth));
                SeekTo(TimeSpan.FromSeconds(np.Duration.TotalSeconds * frac));
                e.Handled = true;
            };
            BuildBubbles();
            Ui.Click(CMin, () => { _dotted = true; GoRest(); });
            // Adanın kenarları da (fare üzerindeyken büyüyen kısım dahil) tıklanabilir
            Bg.MouseLeftButtonUp += (s, e) =>
            {
                if (_mode == Mode.Idle) OpenSearch();
                else if (_mode == Mode.Compact) OnCompactClick();
                else if (_mode == Mode.Dot) { _dotted = false; GoRest(); }
            };
            _home = new HomeView();
            _home.QueryRequested += q => SetQuery(q);
            _home.OpenRequested += uri =>
            {
                var parts = uri.Split('|');
                try { Shell.Open(parts[0]); }
                catch { if (parts.Length > 1) Shell.OpenUrl(parts[1]); }
                CloseSearch(false);
            };
            ((ContentControl)f("HomeHost")).Content = _home;
            ViewSettings.BackRequested += () => OpenSearch();
            ViewSettings.CloseRequested += () => CloseSettings(true);
            ViewSettings.Resized += () => { if (_mode == Mode.Settings) MorphHeight(Measure(ViewSettings, SearchW)); };
            Ui.Click((FrameworkElement)f("WLeft"), () => { Press(f("WLeft")); WatchLeft(); });
            Ui.Click(WRight, () => { Press(WRight); WatchRight(); });
            Ui.Click((FrameworkElement)f("WMin"), () => { if (_mode == Mode.Watch) GoRest(); });
            _watchTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _watchTick.Tick += (s, e) => { if (_mode == Mode.Watch) UpdateWatch(); else _watchTick.Stop(); };
            ViewDot.MouseLeftButtonUp += (s, e) => { if (_mode == Mode.Dot) { _dotted = false; GoRest(); } };
            ViewCompact.MouseLeftButtonUp += (s, e) => { if (_mode == Mode.Compact) OnCompactClick(); };
            ViewIdle.MouseLeftButtonUp += (s, e) => { if (_mode == Mode.Idle) OpenSearch(); };
            ViewAlert.MouseLeftButtonUp += (s, e) => { if (_mode == Mode.Alert && AButtons.Visibility != Visibility.Visible) EndAlert(); };
            // Fareyle ayrılış biraz geciktirilir: kapsülden baloncuklara geçerken arada kalan birkaç piksellik boşluk
            // adayı küçültüp baloncukları kapatmasın (titreme)
            _hoverLeave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
            _hoverLeave.Tick += (s, e) => { _hoverLeave.Stop(); if (!Root.IsMouseOver) Hover(false); };
            Root.MouseEnter += (s, e) => { _mediaLeave.Stop(); _hoverLeave.Stop(); Hover(true); };
            Root.MouseLeave += (s, e) => { _hoverLeave.Stop(); _hoverLeave.Start(); if (_mode == Mode.Media) { _mediaLeave.Stop(); _mediaLeave.Start(); } };
            WLabel.Cursor = Cursors.Hand;
            WLabel.MouseLeftButtonUp += (s, e) => { if (_mode == Mode.Watch) { e.Handled = true; CycleWatch(); } };
            LogoHost.MouseLeftButtonUp += (s, e) => { Blink(); Bounce(); };

            Input.TextChanged += (s, e) => OnInputChanged();
            PreviewKeyDown += OnKey;
            Results.PreviewMouseLeftButtonUp += OnResultClick;
            Results.SelectionChanged += (s, e) => QueuePill(true);
            Results.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => QueuePill(false)));
            Deactivated += (s, e) =>
            {
                if (_c.PinOpen || ModalOpen) return;
                if (_mode == Mode.Search) CloseSearch(false);
                else if (_mode == Mode.Settings) CloseSettings(false);
            };
            Closed += (s, e) => WatchOutsideClicks(false);

            _engine = new CommandEngine(this);
            Status.Text = SearchStatus;
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
            _statusTimer.Tick += (s, e) => { _statusTimer.Stop(); ResetStatus(); };
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _blinkTimer.Tick += (s, e) => { Blink(); _blinkTimer.Interval = TimeSpan.FromMilliseconds(2600 + _rnd.Next(3500)); };
            _blinkTimer.Start();
            _alertTimer = new DispatcherTimer();
            _alertTimer.Tick += (s, e) => { _alertTimer.Stop(); EndAlert(); };
            // Medya kartı asıl olarak dışarı tıklayınca küçülür; fare uzun süre uzak kalırsa da kendiliğinden döner
            _mediaLeave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _mediaLeave.Tick += (s, e) => { _mediaLeave.Stop(); if (_mode == Mode.Media) GoRest(); };
            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _tick.Tick += (s, e) => OnTick();
            _tick.Start();

            MediaWatcher.Changed += OnMediaChanged;
            Theme.Changed += () => { if (_mode == Mode.Search) QueuePill(false); };

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                // Alt+Tab'da görünmesin
                const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
                Native.SetWindowLong(_hwnd, GWL_EXSTYLE, Native.GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
                if (!TestMode)
                {
                    // Spotify / tarayıcı öne gelince ya da arkaya gidince adayı hemen yeniden değerlendir
                    _fgProc2 = (h, ev, w, o, ch, th, t) =>
                    {
                        // Başka bir "her zaman üstte" pencere öne geçmiş olabilir: ada yine en önde olsun
                        if (IsVisible) Native.BringToTopmost(_hwnd);
                        if (IsRest) { var m = RestMode(); if (m != _mode) GoRest(); }
                    };
                    Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _fgProc2, 0, 0, 0);
                }
            };
            Width = WinW;
            Height = 120;
            Left = (SystemParameters.PrimaryScreenWidth - WinW) / 2 + (TestMode ? -8000 : 0);
            Top = 0;
        }

        IEnumerable<FrameworkElement> Views { get { return new[] { ViewIdle, ViewCompact, ViewDot, ViewAlert, ViewMedia, ViewWatch, ViewSearch, (FrameworkElement)ViewSettings }; } }
        Settings S { get { return _c.Settings; } }
        bool Animated { get { return S.Animations; } }

        // =====================================================================
        //  Şekil motoru
        // =====================================================================

        /// <summary>Zemin, gölge ve kırpmayı mevcut boyuta göre günceller (her animasyon karesinde çağrılır; çok hafiftir).</summary>
        void ApplyGeometry()
        {
            if (Bg == null) return;
            double w = Math.Max(0, IW), h = Math.Max(0, IH);
            double r = Math.Min(IR, Math.Min(w, h) / 2);
            double left = (WinW - w) / 2;
            // Üste yapışık stil: şekil ekranın üst kenarının yarıçap kadar üstünden başlar; üst köşeler ekran dışında kalır
            // ve kare görünür, alt köşeler yuvarlak kalır (çentik görünümü)
            double top = Attached ? -r : Gap, hh = Attached ? h + r : h;
            Canvas.SetLeft(Bg, left); Canvas.SetTop(Bg, top);
            Bg.Width = w; Bg.Height = hh; Bg.RadiusX = r; Bg.RadiusY = r;
            Canvas.SetLeft(Shadow, left + 2); Canvas.SetTop(Shadow, top + 2);
            Shadow.Width = Math.Max(0, w - 4); Shadow.Height = Math.Max(0, hh - 4); Shadow.RadiusX = r; Shadow.RadiusY = r;
            _clip.Rect = new Rect(left, top, w, hh);
            _clip.RadiusX = r;
            _clip.RadiusY = r;
            PositionBubbles();
        }

        /// <summary>Pencere yüksekliği yalnızca adanın ihtiyacı kadar (saydam katmanın her karede kopyalanan alanı küçük kalsın).</summary>
        void FitWindow(double islandH, bool allowShrink)
        {
            double need = Math.Ceiling(Gap + islandH + ShadowRoom);
            if (need > Height || allowShrink) Height = need;
        }

        /// <summary>Adayı yeni boyuta yay fiziğiyle dönüştürür.</summary>
        void Morph(double w, double h, double r, bool open, Action done = null)
        {
            _animUntil = DateTime.Now.AddMilliseconds(750);
            int id = ++_morphId;
            _targetW = w; _targetH = h; _targetR = r;
            GrowGlass();
            FitWindow(Math.Max(h, IH), false);
            Action finish = () =>
            {
                if (id != _morphId) return;
                FitWindow(_targetH, true);
                if (done != null) done();
            };
            if (!Animated)
            {
                BeginAnimation(IWProperty, null); BeginAnimation(IHProperty, null); BeginAnimation(IRProperty, null);
                IW = w; IH = h; IR = r;
                Dispatcher.BeginInvoke(finish, DispatcherPriority.Background);
                return;
            }
            // Açılışta genişlik biraz önde gider, yükseklik ardından yaylanır (doğal "damla" hissi)
            int wMs = open ? 620 : 440, hMs = open ? 680 : 420;
            var aw = new DoubleAnimation(w, TimeSpan.FromMilliseconds(wMs)) { EasingFunction = open ? Anim.SpringOpen : Anim.SpringClose };
            var ah = new DoubleAnimation(h, TimeSpan.FromMilliseconds(hMs)) { EasingFunction = open ? Anim.SpringOpen : Anim.SpringClose };
            var ar = new DoubleAnimation(r, TimeSpan.FromMilliseconds(Math.Max(wMs, hMs))) { EasingFunction = Anim.SpringClose };
            (wMs > hMs ? aw : ah).Completed += (s, e) => finish();
            BeginAnimation(IWProperty, aw);
            BeginAnimation(IHProperty, ah);
            BeginAnimation(IRProperty, ar);
        }

        /// <summary>Yalnızca yüksekliği yumuşakça değiştirir (arama sonuçları değişince).</summary>
        void MorphHeight(double h)
        {
            _animUntil = DateTime.Now.AddMilliseconds(420);
            _targetH = h;
            GrowGlass();
            FitWindow(Math.Max(h, IH), false);
            if (!Animated) { BeginAnimation(IHProperty, null); IH = h; FitWindow(h, true); return; }
            var a = new DoubleAnimation(h, TimeSpan.FromMilliseconds(360)) { EasingFunction = Anim.SpringClose };
            int id = _morphId;
            a.Completed += (s, e) => { if (id == _morphId && Math.Abs(_targetH - h) < 0.5) FitWindow(h, true); };
            BeginAnimation(IHProperty, a);
        }

        /// <summary>Görünümler arası geçiş: eskisi hızla söner, yenisi hafifçe aşağı kayarak belirir.</summary>
        void SwapView(FrameworkElement next, int delay = 70)
        {
            foreach (var v in Views)
            {
                if (v == next) continue;
                v.IsHitTestVisible = false;
                // Görünmez görünen bir görünümün gecikmeli belirme animasyonu henüz başlamamış olabilir: onu da her zaman
                // iptal et, yoksa birkaç milisaniye sonra yeni görünümün üstünde belirir (üst üste binme)
                if (v.Opacity <= 0 && !v.HasAnimatedProperties) continue;
                if (Animated && v.Opacity > 0) v.BeginAnimation(OpacityProperty, Anim.To(0, 90));
                else { v.BeginAnimation(OpacityProperty, null); v.Opacity = 0; }
            }
            next.IsHitTestVisible = true;
            Canvas.SetTop(next, Gap); // stil (yüzen / yapışık) değişmiş olabilir
            if (next != ViewCompact) UpdateBubbles();
            UpdateTint();
            Canvas.SetLeft(next, (WinW - next.Width) / 2);
            if (!Animated) { next.BeginAnimation(OpacityProperty, null); next.Opacity = 1; return; }
            var tt = (TranslateTransform)next.RenderTransform;
            next.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 220, null, delay));
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-7, 0, TimeSpan.FromMilliseconds(520))
                { EasingFunction = Anim.SpringClose, BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, delay - 30)) });
        }

        /// <summary>Zemin rengi: müzik kartında ve şu an çalıyor kapsülünde kapak renginin koyu tonu, diğer hallerde siyah.</summary>
        void UpdateTint()
        {
            var np = MediaWatcher.Current;
            Color a = Colors.Black, b = Colors.Black;
            bool glassMode = GlassMode;
            if (glassMode && Glass.Fill == null) CaptureGlass(GlassArea());
            bool glass = glassMode && Glass.Fill != null;
            if (np != null && (_mode == Mode.Media || (_mode == Mode.Compact && HasMusic())))
            {
                Color c = np.ArtColor;
                if (_mode == Mode.Media) { a = Shade(c, 0.40); b = Shade(c, 0.13); }
                else { a = Shade(c, 0.30); b = Shade(c, 0.06); }
                // Buzlu camda kapak rengi yarı saydam: arkadaki bulanık görüntü renkli camın altından seçilir
                if (glass) { a.A = 0xC4; b.A = 0xD8; }
            }
            else if (glass)
            {
                // Buzlu cam: koyu ama yarı saydam zemin, altından bulanık masaüstü görünür
                a = Color.FromArgb(0xB4, 0x16, 0x16, 0x1A);
                b = Color.FromArgb(0xC8, 0x0E, 0x0E, 0x10);
            }
            // Cam görüntüsü hemen tam görünür olur; geçişi zeminin saydamlığı yapar. Kapanırken zemin yeniden koyulaşana kadar
            // cam yerinde kalır: aksi halde alttaki gölge katmanı bir anlığına koyu bir leke gibi görünür.
            if (glass)
            {
                Glass.BeginAnimation(OpacityProperty, null);
                Glass.Opacity = 1;
                _glassHiding = false;
            }
            else if (Glass.Opacity > 0.01 && !_glassHiding)
            {
                _glassHiding = true;
                GlassNext.BeginAnimation(OpacityProperty, null);
                GlassNext.Opacity = 0;
                var hide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(1)) { BeginTime = TimeSpan.FromMilliseconds(Animated ? 680 : 0) };
                hide.Completed += (s, e) => _glassHiding = false;
                Glass.BeginAnimation(OpacityProperty, hide);
            }
            if (a == _tintA && b == _tintB) return;
            _tintA = a; _tintB = b;
            if (Animated)
            {
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                TintA.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(a, TimeSpan.FromMilliseconds(650)) { EasingFunction = ease });
                TintB.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(b, TimeSpan.FromMilliseconds(650)) { EasingFunction = ease });
            }
            else
            {
                TintA.BeginAnimation(GradientStop.ColorProperty, null); TintA.Color = a;
                TintB.BeginAnimation(GradientStop.ColorProperty, null); TintB.Color = b;
            }
        }

        /// <summary>Rengin koyulaştırılmış hali (beyaz yazılar okunaklı kalsın diye doygunluk korunur, parlaklık düşer).</summary>
        static Color Shade(Color c, double k)
        {
            return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
        }

        double Measure(FrameworkElement v, double width)
        {
            v.Width = width;
            v.Measure(new Size(width, double.PositiveInfinity));
            return Math.Ceiling(v.DesiredSize.Height);
        }

        void EnsureShown()
        {
            if (IsVisible)
            {
                // Gizlenme animasyonu sürerken tekrar açılırsa: sönmeyi iptal et, adayı geri getir
                if (Root.Opacity < 1 || Drop.Y != 0)
                {
                    if (Animated)
                    {
                        Root.BeginAnimation(OpacityProperty, Anim.To(1, 120));
                        Drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = Anim.SpringClose });
                    }
                    else
                    {
                        Root.BeginAnimation(OpacityProperty, null); Root.Opacity = 1;
                        Drop.BeginAnimation(TranslateTransform.YProperty, null); Drop.Y = 0;
                    }
                }
                return;
            }
            // Gizliyken: ekranın tepesinde küçücük bir haptan doğar
            BeginAnimation(IWProperty, null); BeginAnimation(IHProperty, null); BeginAnimation(IRProperty, null);
            IW = 84; IH = 26; IR = 13;
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = 0;
            ShowActivated = false;
            Show();
            if (Animated)
            {
                Root.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 160));
                Drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = Anim.SpringOpen });
            }
            else { Root.Opacity = 1; Drop.Y = 0; }
        }

        /// <summary>Fare üstüne gelince kapsül hafifçe büyür (yalnızca dinlenme hallerinde).</summary>
        void Hover(bool on)
        {
            bool rest = _mode == Mode.Compact || _mode == Mode.Idle || _mode == Mode.Dot;
            ShowMinButton(on && _mode == Mode.Compact);
            HoverBubbles(on && _mode == Mode.Compact);
            if (!Animated || !rest || on == _hover) { _hover = on && rest; return; }
            _hover = on;
            bool dot = _mode == Mode.Dot;
            double w = _targetW + (on ? (dot ? 6 : 10) : 0), h = _targetH + (on ? (dot ? 6 : 3) : 0);
            BeginAnimation(IWProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(420)) { EasingFunction = Anim.SpringOpen });
            BeginAnimation(IHProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(420)) { EasingFunction = Anim.SpringOpen });
            BeginAnimation(IRProperty, new DoubleAnimation(h / 2, TimeSpan.FromMilliseconds(420)) { EasingFunction = Anim.SpringClose });
        }

        /// <summary>Kompakt adanın sağındaki "noktaya küçült" düğmesini (ses çubuklarının yerine) gösterir/gizler.</summary>
        void ShowMinButton(bool on)
        {
            if (CMin.IsHitTestVisible == on) return;
            CMin.IsHitTestVisible = on;
            if (Animated)
            {
                CMin.BeginAnimation(OpacityProperty, Anim.To(on ? 1 : 0, on ? 180 : 120));
                CBars.BeginAnimation(OpacityProperty, Anim.To(on ? 0 : 1, on ? 120 : 180));
            }
            else
            {
                CMin.BeginAnimation(OpacityProperty, null); CMin.Opacity = on ? 1 : 0;
                CBars.BeginAnimation(OpacityProperty, null); CBars.Opacity = on ? 0 : 1;
            }
        }

        // ---- Dışarı tıklama: medya kartı açıkken ekranın başka bir yerine tıklanınca ada küçülür ----
        void WatchOutsideClicks(bool on)
        {
            if (TestMode || on == (_mouseHook != IntPtr.Zero)) return;
            if (on)
            {
                _mouseProc = MouseHookProc; // temsilci çöp toplayıcıya gitmesin
                _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandle(null), 0);
            }
            else
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                {
                    var info = (Native.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if (!IslandContains(info.X, info.Y))
                        Dispatcher.BeginInvoke(new Action(() => { if (_mode == Mode.Media || _mode == Mode.Watch) GoRest(); }));
                }
            }
            return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        /// <summary>Ekran pikseli adanın görünen şeklinin içinde mi?</summary>
        bool IslandContains(int x, int y)
        {
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return true;
            var p = src.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
            double left = Left + (WinW - IW) / 2, top = Top + Gap;
            return p.X >= left - 4 && p.X <= left + IW + 4 && p.Y >= top - 4 && p.Y <= top + IH + 4;
        }

        static void Press(object o)
        {
            var e = o as FrameworkElement;
            if (e == null) return;
            var st = e.RenderTransform as ScaleTransform;
            if (st == null || st.IsFrozen) { st = new ScaleTransform(); e.RenderTransform = st; }
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), Anim.SpringOpen));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // =====================================================================
        //  Mod yönetimi
        // =====================================================================

        Mode RestMode()
        {
            if (_blocked) return Mode.Hidden;
            if (HasMusic() || HasCounter()) return _dotted ? Mode.Dot : Mode.Compact;
            _dotted = false; // gösterecek bir şey kalmayınca nokta hali unutulur; sonraki müzik yine tam görünür
            return S.IslandIdle ? Mode.Idle : Mode.Hidden;
        }

        bool HasMusic()
        {
            var np = MediaWatcher.Current;
            if (!S.IslandMusic || np == null) { _dismissed = false; return false; }
            if (np.IsPlaying) _lastPlaying = DateTime.Now;
            bool active = np.IsPlaying || (DateTime.Now - _lastPlaying).TotalSeconds < 8;
            if (_dismissed)
            {
                // Kullanıcı gizlediyse: müzik duraklayana ya da başka uygulamaya geçene kadar gizli kalır
                if (!active || np.App != _dismissApp) _dismissed = false;
                else return false;
            }
            // Spotify / tarayıcı ekranın önündeyken ada müziği göstermez; uygulama arkaya alınınca geri gelir
            if (active && S.IslandHideWhenAppFront && MediaAppInFront(np)) return false;
            return active;
        }

        IntPtr _fgHwnd;
        string _fgProc = "";

        /// <summary>Öndeki pencere, medyanın çaldığı uygulamaya mı ait?</summary>
        bool MediaAppInFront(NowPlaying np)
        {
            if (TestMode) return false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;
            if (fg != _fgHwnd)
            {
                _fgHwnd = fg;
                _fgProc = "";
                try
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(fg, out pid);
                    using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) _fgProc = p.ProcessName.ToLowerInvariant();
                }
                catch { }
            }
            if (_fgProc.Length < 3) return false;
            string aumid = (np.Aumid ?? "").ToLowerInvariant(), app = (np.App ?? "").ToLowerInvariant();
            return aumid.Contains(_fgProc) || app == _fgProc || (app == "edge" && _fgProc == "msedge") ||
                   (app == "medya oynatıcı" && _fgProc == "microsoft.media.player");
        }

        void DismissMusic()
        {
            var np = MediaWatcher.Current;
            _dismissed = true;
            _dismissApp = np != null ? np.App : null;
            GoRest();
        }

        /// <summary>Adada geri sayımı görünen zamanlayıcı (hatırlatıcılar geri sayım göstermez).</summary>
        Controller.Reminder NextTimer()
        {
            return _c.Reminders.Where(r => r.IsTimer).OrderBy(r => r.Due).FirstOrDefault();
        }

        /// <summary>Kompakt adada bir sayaç (zamanlayıcı ya da kronometre) gösterilecek mi?</summary>
        bool HasCounter() { return NextTimer() != null || _c.WatchActive; }

        static string WatchText(TimeSpan t, bool tenths)
        {
            string s = t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                         : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            return tenths ? s + "," + (t.Milliseconds / 100) : s;
        }

        /// <summary>Arama/medya/uyarı bittiğinde dinlenme haline (hap, şu an çalıyor ya da gizli) döner.</summary>
        void GoRest()
        {
            _hover = false;
            WatchOutsideClicks(false);
            ShowMinButton(false);
            if (_pendingAlerts.Count > 0) { _pendingAlerts.Dequeue()(); return; }
            var m = RestMode();
            if (m == Mode.Hidden) HideIsland();
            else if (m == Mode.Idle) ShowIdle();
            else if (m == Mode.Dot) ShowDot();
            else ShowCompact();
        }

        bool FromBig { get { return _mode == Mode.Search || _mode == Mode.Settings || _mode == Mode.Media || _mode == Mode.Watch || _mode == Mode.Alert; } }

        // ---- Nokta hali ----
        void ShowDot()
        {
            EnsureShown();
            UpdateDot();
            if (_mode == Mode.Dot) return;
            bool fromBig = FromBig;
            _mode = Mode.Dot;
            SwapView(ViewDot, fromBig ? 150 : 40);
            Morph(DotS, DotS, DotS / 2, false);
        }

        void UpdateDot()
        {
            var np = MediaWatcher.Current;
            bool music = HasMusic();
            Color c = music ? np.ArtColor : ((SolidColorBrush)Palette.Reminder).Color;
            bool pulse = Animated && (music ? np.IsPlaying : true);
            var old = DotCore.Tag as Tuple<bool, Color>;
            if (old != null && old.Item1 == pulse && old.Item2 == c) return;
            DotCore.Tag = Tuple.Create(pulse, c);
            var b = new SolidColorBrush(c);
            b.Freeze();
            DotCore.Fill = b;
            if (pulse)
            {
                var a = new DoubleAnimation(0.7, 1.05, TimeSpan.FromMilliseconds(900))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(a, 24);
                DotScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                DotScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                Anim.Stop(DotScale, ScaleTransform.ScaleXProperty, 0.8);
                Anim.Stop(DotScale, ScaleTransform.ScaleYProperty, 0.8);
            }
        }

        void HideIsland()
        {
            if (_mode == Mode.Hidden && !IsVisible) return;
            _mode = Mode.Hidden;
            UpdateTint();
            UpdateBubbles();
            foreach (var v in Views)
            {
                v.IsHitTestVisible = false;
                if (Animated) v.BeginAnimation(OpacityProperty, Anim.To(0, 90));
                else { v.BeginAnimation(OpacityProperty, null); v.Opacity = 0; }
            }
            if (!Animated) { Hide(); return; }
            Morph(84, 26, 13, false, () =>
            {
                if (_mode != Mode.Hidden) return;
                var a = Anim.To(0, 160);
                a.Completed += (s, e) => { if (_mode == Mode.Hidden) Hide(); };
                Root.BeginAnimation(OpacityProperty, a);
                Drop.BeginAnimation(TranslateTransform.YProperty, Anim.To(-10, 200));
            });
        }

        /// <summary>Boştaki adanın gözlerini ayarlara göre boyar ve boyutlandırır (renk, boyut, şekil).</summary>
        void ApplyEyes()
        {
            double w, h, gap;
            switch (S.EyeSize) { case "kucuk": w = 5; h = 6.5; gap = 8; break; case "buyuk": w = 8; h = 10.5; gap = 11; break; default: w = 6; h = 8; gap = 9; break; }
            if (S.EyeShape == "yuvarlak") h = w;
            else if (S.EyeShape == "cizgi") { w *= 1.7; h = Math.Max(2.5, h * 0.32); }
            Brush fill;
            if (S.EyeColor == "vurgu") fill = Theme.Brush("Accent");
            else
            {
                try { fill = Palette.Make(string.IsNullOrEmpty(S.EyeColor) ? "#FFFFFF" : S.EyeColor); }
                catch { fill = Brushes.White; }
            }
            foreach (var e in new[] { (Ellipse)Root.FindName("EyeShapeL"), (Ellipse)Root.FindName("EyeShapeR") })
            {
                e.Width = w; e.Height = h; e.Fill = fill;
            }
            ((Ellipse)Root.FindName("EyeShapeL")).Margin = new Thickness(0, 0, gap, 0);
        }

        void ShowIdle()
        {
            EnsureShown();
            ApplyEyes();
            if (_mode == Mode.Idle) return;
            bool fromBig = FromBig;
            _mode = Mode.Idle;
            SwapView(ViewIdle, fromBig ? 160 : 70);
            Morph(IdleW, IdleH, IdleH / 2, !fromBig);
        }

        // ---- Kompakt (şu an çalıyor / zamanlayıcı) ----
        void ShowCompact()
        {
            EnsureShown();
            bool changed = _mode != Mode.Compact;
            bool fromBig = FromBig;
            _mode = Mode.Compact;
            UpdateCompact();
            double w = CompactWidth();
            if (changed) { SwapView(ViewCompact, fromBig ? 170 : 80); Morph(w, CH, CH / 2, !fromBig); }
            else if ((Math.Abs(_targetW - w) > 1 || Math.Abs(_targetH - CH) > 0.5) && !_hover) Morph(w, CH, CH / 2, true);
        }

        /// <summary>Ayarlardaki ada boyutu: küçük, orta, büyük.</summary>
        double SizeK { get { return S.IslandSize == "kucuk" ? 0.9 : S.IslandSize == "buyuk" ? 1.14 : 1.0; } }
        double CH { get { return S.IslandSize == "kucuk" ? 30 : S.IslandSize == "buyuk" ? 38 : CompactH; } }

        double CompactWidth()
        {
            bool music = HasMusic();
            bool timer = CTimer.Visibility == Visibility.Visible;
            // Dengeli boyut: müzikte kapak + kısa başlık + dalga; sayaç varsa yanına küçük bir rozet
            double w = music ? 240 : 228;
            if (music && timer) w += 0; // sayaçlar baloncukta
            w = Math.Round(w * SizeK);
            if (Math.Abs(ViewCompact.Height - CH) > 0.5) ViewCompact.Height = CH;
            if (Math.Abs(ViewCompact.Width - w) > 0.5 || double.IsNaN(ViewCompact.Width))
            {
                ViewCompact.Width = w;
                Canvas.SetLeft(ViewCompact, (WinW - w) / 2);
            }
            return w;
        }

        void UpdateCompact()
        {
            var np = MediaWatcher.Current;
            bool music = HasMusic();
            var timer = NextTimer();
            bool watch = timer == null && _c.WatchActive;
            // Müzik çalarken sayaçlar ana kapsülde değil, yanındaki baloncuklarda gösterilir
            if (music) { timer = null; watch = false; }
            CTimer.Visibility = timer != null || watch ? Visibility.Visible : Visibility.Collapsed;
            if (timer != null) { CTimerText.Text = Countdown(timer.Due - DateTime.Now); CTimerGlyph.Text = ""; }
            else if (watch) { CTimerText.Text = WatchText(_c.Watch.Elapsed, false); CTimerGlyph.Text = ""; }
            if (music)
            {
                string title = np.Title;
                if (CTitle.Text != title) CTitle.Text = title;
                SetArt(CArt, np.Art);
                CGlyph.Text = G.Music;
                SetBars(CBars, np.IsPlaying, np.ArtColor);
                CBars.Visibility = Visibility.Visible;
            }
            else
            {
                SetArt(CArt, null);
                CGlyph.Text = timer != null ? G.Clock : "";
                CTitle.Text = timer != null ? timer.Text : _c.Watch.IsRunning ? "Kronometre" : "Duraklatıldı";
                CBars.Visibility = Visibility.Collapsed;
                SetBars(CBars, false, Colors.White);
            }
            if (_mode == Mode.Compact) { UpdateTint(); UpdateBubbles(); }
        }

        static string Countdown(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                     : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        static void SetArt(Border b, ImageSource img)
        {
            var cur = b.Background as ImageBrush;
            if (cur != null && cur.ImageSource == img) return;
            b.Background = img == null ? null : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }

        void OnCompactClick()
        {
            if (HasMusic()) ShowMedia();
            else if (HasCounter()) { _watchKind = null; _watchTimer = null; ShowWatch(); }
            else SetQueryAndOpen("hatırlatıcılar");
        }

        // ---- Kronometre / zamanlayıcı kartı ----
        void ShowWatch()
        {
            EnsureShown();
            _mode = Mode.Watch;
            _hover = false;
            ShowMinButton(false);
            WatchOutsideClicks(true);
            UpdateWatch();
            double h = Measure(ViewWatch, WatchW);
            SwapView(ViewWatch, 80);
            Morph(WatchW, h, Math.Min(32, h / 2), true);
            _watchTick.Start();
        }

        /// <summary>Kartta gösterilen zamanlayıcı (baloncuktan kronometre seçildiyse null).</summary>
        Controller.Reminder CurTimer()
        {
            if (_watchKind == "watch" && _c.WatchActive) return null;
            if (_watchKind == "timer" && _watchTimer != null && _c.Reminders.Contains(_watchTimer)) return _watchTimer;
            return NextTimer();
        }

        void UpdateWatch()
        {
            var timer = CurTimer();
            if (timer != null)
            {
                WLabel.Text = "Zamanlayıcı  ·  " + timer.Text + SwitchHint();
                WTime.Text = Countdown(timer.Due - DateTime.Now);
                WLeftGlyph.Text = "";     // iptal
                WRightGlyph.Text = "+1";
                WRightGlyph.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
                WRight.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x9F, 0x0A));
            }
            else if (_c.WatchActive)
            {
                bool run = _c.Watch.IsRunning;
                WLabel.Text = (run ? "Kronometre" : "Kronometre  ·  duraklatıldı") + SwitchHint();
                WTime.Text = WatchText(_c.Watch.Elapsed, true);
                WLeftGlyph.Text = "";     // sıfırla
                WRightGlyph.Text = run ? G.Pause : G.Play;
                WRightGlyph.FontFamily = (FontFamily)FindResource("IconFont");
                WRight.Background = new SolidColorBrush(run ? Color.FromArgb(0x38, 0xFF, 0x9F, 0x0A) : Color.FromArgb(0x38, 0x30, 0xD1, 0x58));
            }
            else GoRest();
        }

        /// <summary>Birden çok sayaç varsa başlıkta kaçıncısı olduğunu gösterir (başlığa tıklayınca sıradakine geçilir).</summary>
        string SwitchHint()
        {
            int n = CounterCount();
            if (n < 2) return "";
            var cur = CurTimer();
            var timers = _c.Reminders.Where(r => r.IsTimer).OrderBy(r => r.Due).ToList();
            int idx = cur == null ? timers.Count : timers.IndexOf(cur);
            return "   " + (idx + 1) + "/" + n + "  ›";
        }

        void WatchLeft()
        {
            var timer = CurTimer();
            if (timer != null) _c.CancelReminder(timer);
            else _c.ResetWatch();
            if (!HasCounter()) GoRest();
        }

        void WatchRight()
        {
            var timer = CurTimer();
            if (timer != null)
            {
                _c.CancelReminder(timer);
                _c.AddTimer((timer.Due - DateTime.Now).Add(TimeSpan.FromMinutes(1)), timer.Text);
            }
            else if (_c.Watch.IsRunning) _c.PauseWatch();
            else _c.StartWatch();
            if (_mode != Mode.Watch) ShowWatch(); else UpdateWatch();
        }

        // ---- Medya kartı ----
        void ShowMedia()
        {
            var np = MediaWatcher.Current;
            if (np == null) { GoRest(); return; }
            EnsureShown();
            _mode = Mode.Media;
            _hover = false;
            ShowMinButton(false);
            WatchOutsideClicks(true);
            UpdateMedia();
            double h = Measure(ViewMedia, MediaW);
            SwapView(ViewMedia, 90);
            Morph(MediaW, h, 34, true);
            Dispatcher.BeginInvoke(new Action(UpdateProgress), DispatcherPriority.Loaded);
        }

        void UpdateMedia()
        {
            var np = MediaWatcher.Current;
            if (np == null) return;
            if (DateTime.Now > _seekMsgUntil) MApp.Text = (np.IsPlaying ? "Çalıyor" : "Duraklatıldı") + "  ·  " + np.App;
            if (MTitle.Text != np.Title) MTitle.Text = np.Title;
            MArtist.Text = string.IsNullOrEmpty(np.Artist) ? " " : np.Artist;
            SetArt(MArt, np.Art);
            MPlayGlyph.Text = np.IsPlaying ? G.Pause : G.Play;
            SetBars(MBars, np.IsPlaying, np.ArtColor);
            UpdateProgress();
            UpdateRepeat(np);
            UpdateTint();
        }

        string _repeatShown;
        DateTime _repeatHoldUntil;

        /// <summary>Döngü düğmesi: kapalıyken soluk, açıkken vurgu renginde ve altında nokta; "bu şarkı" modunda tekli döngü simgesi.</summary>
        void UpdateRepeat(NowPlaying np)
        {
            var btn = (FrameworkElement)Root.FindName("MRepeat");
            var glyph = (TextBlock)Root.FindName("MRepeatGlyph");
            var dot = (UIElement)Root.FindName("MRepeatDot");
            bool can = np != null && np.CanRepeat;
            btn.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
            if (!can) return;
            // Az önce değiştirildiyse uygulamanın yeni durumu bildirmesini beklerken titremesin
            string mode = DateTime.Now < _repeatHoldUntil ? _repeatShown : (np.Repeat ?? _repeatShown ?? "none");
            bool on = mode == "list" || mode == "track";
            glyph.Text = mode == "track" ? "" : "";
            glyph.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentText" : "FgDim");
            dot.Opacity = on ? 1 : 0;
            btn.ToolTip = mode == "track" ? "Döngü: bu şarkı" : mode == "list" ? "Döngü: tüm liste" : "Döngü kapalı";
        }

        void UpdateProgress()
        {
            var np = MediaWatcher.Current;
            if (np == null) return;
            var pos = np.LivePosition;
            bool hasDur = np.Duration.TotalSeconds > 1;
            MPos.Text = hasDur ? Clock(pos) : "";
            MDur.Text = hasDur ? "-" + Clock(np.Duration - pos) : "";
            double frac = hasDur ? Math.Min(1, pos.TotalSeconds / np.Duration.TotalSeconds) : 0;
            double scale = frac;
            // İlerleme çubuğu ölçekle büyür (yerleşim hesabı yok, kayma yok)
            if (Animated) MProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(950)));
            else MProgressScale.ScaleX = scale;
        }

        DateTime _seekMsgUntil;

        void SeekBy(double seconds)
        {
            var np = MediaWatcher.Current;
            if (np == null) return;
            var t = np.LivePosition + TimeSpan.FromSeconds(seconds);
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (np.Duration.TotalSeconds > 1 && t > np.Duration) t = np.Duration - TimeSpan.FromSeconds(1);
            SeekTo(t);
        }

        void SeekTo(TimeSpan t)
        {
            var np = MediaWatcher.Current;
            if (np == null) return;
            var oldPos = np.Position;
            var oldAt = np.PositionAt;
            // Hemen göster (uygulamanın onayını beklemeden), desteklenmezse geri al
            np.Position = t;
            np.PositionAt = DateTime.Now;
            UpdateProgress();
            MediaWatcher.Seek(t, ok => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ok || _mode != Mode.Media) return;
                np.Position = oldPos;
                np.PositionAt = oldAt;
                UpdateProgress();
                MApp.Text = np.App + " sarmayı desteklemiyor";
                _seekMsgUntil = DateTime.Now.AddSeconds(3);
            })));
        }

        static string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        // ---- Ses dalgası çubukları ----
        static void BuildBars(StackPanel host, int n, double width, double height)
        {
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle { Width = width, Height = height, RadiusX = width / 2, RadiusY = width / 2, Margin = new Thickness(i == 0 ? 0 : 2.5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.White, RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 0.3) };
                host.Children.Add(r);
            }
        }

        void SetBars(StackPanel host, bool playing, Color color)
        {
            var old = host.Tag as Tuple<bool, Color>;
            bool animate = playing && Animated;
            if (old != null && old.Item1 == animate && old.Item2 == color) return; // değişiklik yoksa animasyonu yeniden başlatma
            host.Tag = Tuple.Create(animate, color);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            int i = 0;
            foreach (Rectangle r in host.Children)
            {
                r.Fill = brush;
                var st = (ScaleTransform)r.RenderTransform;
                if (animate)
                {
                    var a = new DoubleAnimation(0.25 + 0.1 * (i % 2), 0.95 - 0.12 * (i % 3), TimeSpan.FromMilliseconds(330 + 90 * ((i * 7) % 4)))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(70 * i),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    Timeline.SetDesiredFrameRate(a, 24);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else
                {
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    st.ScaleY = playing ? 0.6 : 0.25;
                }
                i++;
            }
        }

        // ---- Uyarılar ----
        public void ShowNotice(string title, string sub, string glyph, Brush color)
        {
            ShowAlertInternal(title, sub, glyph, color, false, null, null, TimeSpan.FromSeconds(3.6));
            Sounds.Play(Sound.Notify);
        }

        /// <summary>Kaçırılan bildirim hatırlatması: "Göster" listeyi açar, "Sonra" kapatır.</summary>
        internal void ShowMissed(List<Notifications.Item> items)
        {
            if (items.Count == 0 || _mode == Mode.Search || _mode == Mode.Settings) return;
            var last = items[items.Count - 1];
            string sub = last.App + (last.Title.Length > 0 ? ": " + last.Title : "") + (items.Count > 1 ? "  ·  +" + (items.Count - 1) + " diğer" : "");
            ((Button)Root.FindName("ASnooze")).Content = "Sonra";
            ((Button)Root.FindName("AOk")).Content = "Göster";
            ShowAlertInternal(items.Count == 1 ? "Kaçırdığın bir bildirim var" : "Kaçırdığın " + items.Count + " bildirim var", sub, G.Bell, Palette.Make("#FFD60A"), true,
                              () => SetQueryAndOpen("bildirimler"), () => { }, TimeSpan.FromSeconds(14));
            Sounds.Play(Sound.News);
        }

        public void ShowReminder(string text, Action snooze, string snoozeLabel = "5 dk ertele")
        {
            ((Button)Root.FindName("ASnooze")).Content = snoozeLabel;
            ((Button)Root.FindName("AOk")).Content = "Tamam";
            ShowAlertInternal("Hatırlatma", text, G.Clock, Palette.Reminder, true, null, snooze, TimeSpan.FromMinutes(2));
            Sounds.Play(Sound.Alarm);
        }

        // ---- Sesli komut ----
        bool _listening;

        /// <summary>"Hey Pardux" duyulunca: ada "Dinliyorum…" kartına dönüşür, söylenenler canlı yazılır.</summary>
        public void ShowListening()
        {
            _listening = true;
            if (_mode == Mode.Search) { Flash("Dinliyorum…"); return; }
            _pendingAlerts.Clear();
            if (_mode == Mode.Alert) _mode = Mode.Hidden; // açık bir bildirimin yerine geç
            ASub.Width = 300; // kart baştan geniş açılsın, yazdıkça büyüyüp kırpılmasın
            ShowAlertInternal("Dinliyorum…", "Ne yapmamı istersin?", "", Palette.Accent, false, null, null, TimeSpan.FromSeconds(12));
        }

        public void UpdateListening(string text)
        {
            if (!_listening) return;
            if (_mode == Mode.Search) { Input.Text = text; Input.CaretIndex = text.Length; return; }
            if (_mode == Mode.Alert) ASub.Text = "“" + text + "”";
        }

        /// <summary>Dinleme kartını sessizce kapatır (yanlış uyanma).</summary>
        public void CancelListening()
        {
            if (!_listening) return;
            _listening = false;
            ASub.Width = double.NaN;
            if (_mode == Mode.Alert) EndAlert();
        }

        public void EndListening(string title, string sub)
        {
            _listening = false;
            ASub.Width = double.NaN;
            if (_mode == Mode.Search) { Flash(title); return; }
            ShowAlertInternal(title, sub, "", Palette.Danger, false, null, null, TimeSpan.FromSeconds(3));
        }

        /// <summary>Söylenen komutu arama kutusuna yazar ve kısa bir an gösterdikten sonra ilk sonucu çalıştırır.</summary>
        public void RunVoiceCommand(string text)
        {
            _listening = false;
            ASub.Width = double.NaN;
            OpenSearch();
            SetQuery(text);
            // Kısa bir an gösterilir (ne anlaşıldığı görülsün), sonra çalıştırılır
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (_mode != Mode.Search || Input.Text != text) return;
                var it = Results.SelectedItem as ResultItem;
                if (it == null) return;
                if (it.Execute == null) { _voiceVersion = _version; return; } // cevap internetten/havadan gelecek: gelince okunur
                if (it.Info) { Speech.Say(it.Speak); return; }                 // bilgi satırı: tarayıcı açma, yalnızca söyle
                // Sesle yalnızca açıkça eşleşen komut kendiliğinden çalışır: yanlış duyulan bir cümle rastgele bir uygulamayı açmasın
                if (it.Score < VoiceMinScore)
                {
                    Flash("Bunu mu demek istedin? Seçip Enter'a bas");
                    Speech.Say("Tam anlayamadım, listeden seçebilirsin");
                    return;
                }
                // Riskli işler (bilgisayarı kapat, Pardux'tan çık…) sesle hiçbir zaman doğrudan yapılmaz: onay ister
                if (it.NeedsConfirm || it.GlyphBrush == Palette.Danger)
                {
                    it.NeedsConfirm = true;
                    Execute(); // ilk basış yalnızca "emin misin?" sorar
                    Speech.Say("Emin misin? Onaylamak için Enter'a bas");
                    return;
                }
                string say = Speech.Confirm(it);
                Execute();
                Speech.Say(say);
            };
            t.Start();
        }

        int _voiceVersion = -1;

        /// <summary>Sesli komutun kendiliğinden çalışması için gereken en düşük eşleşme puanı (tam komut, uygulama adı…).</summary>
        const int VoiceMinScore = 1050;

        /// <summary>"Anlıyorum…": söz bitti, Whisper yazıya döküyor.</summary>
        public void ShowThinking()
        {
            if (_mode == Mode.Alert && _listening) { ATitle.Text = "Anlıyorum…"; return; }
            if (_mode == Mode.Search) Flash("Anlıyorum…");
        }

        /// <summary>İki düğmeli soru kartı (ör. "İyi misin?"): ok ve other düğmeleri, süre sonunda kendiliğinden kapanır.</summary>
        public void ShowQuestion(string title, string sub, string okText, Action ok, string otherText, Action other, TimeSpan duration)
        {
            ((Button)Root.FindName("ASnooze")).Content = otherText;
            ((Button)Root.FindName("AOk")).Content = okText;
            ShowAlertInternal(title, sub, null, null, true, ok, other, duration);
            Sounds.Play(Sound.Notify);
        }

        void ShowAlertInternal(string title, string sub, string glyph, Brush color, bool buttons, Action ok, Action snooze, TimeSpan duration)
        {
            Action show = () =>
            {
                EnsureShown();
                _mode = Mode.Alert;
                _hover = false;
                WatchOutsideClicks(false);
                ShowMinButton(false);
                ATitle.Text = title;
                ASub.Text = sub ?? "";
                ASub.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
                bool logo = glyph == null;
                ALogo.Visibility = logo ? Visibility.Visible : Visibility.Collapsed;
                AGlyph.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
                AGlyph.Text = glyph ?? "";
                AGlyph.Foreground = color ?? Theme.Brush("AccentText");
                AIconBox.Background = logo || color == null ? null : Palette.Soft(color);
                AButtons.Visibility = buttons ? Visibility.Visible : Visibility.Collapsed;
                _alertOk = ok;
                _alertSnooze = snooze;
                // Genişlik içeriğe göre: kısa bildirimde ada gereksiz uzamaz
                ViewAlert.Width = double.NaN;
                ViewAlert.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = Math.Max(buttons ? 380 : 250, Math.Min(AlertW, Math.Ceiling(ViewAlert.DesiredSize.Width) + 14));
                double h = Measure(ViewAlert, w);
                SwapView(ViewAlert, 80);
                Morph(w, h, buttons ? 30 : Math.Min(32, h / 2), true);
                _alertTimer.Stop();
                _alertTimer.Interval = duration;
                _alertTimer.Start();
                if (Animated && logo) Dispatcher.BeginInvoke(new Action(() => BlinkEyes((ScaleTransform)Root.FindName("AEyeL"), (ScaleTransform)Root.FindName("AEyeR"))), DispatcherPriority.Background);
            };
            if (_mode == Mode.Search || _mode == Mode.Settings || (_mode == Mode.Alert && AButtons.Visibility == Visibility.Visible)) _pendingAlerts.Enqueue(show);
            else show();
        }

        void EndAlert()
        {
            _alertTimer.Stop();
            if (_mode == Mode.Alert) GoRest();
        }

        // ---- Zamanlayıcı / medya güncellemeleri ----
        void OnMediaChanged()
        {
            if (_mode == Mode.Media) { UpdateMedia(); if (MediaWatcher.Current == null) GoRest(); return; }
            if (_mode == Mode.Search || _mode == Mode.Alert) return;
            var rest = RestMode();
            if (rest == Mode.Compact) ShowCompact();
            else if (rest == Mode.Dot) { if (_mode == Mode.Dot) UpdateDot(); else GoRest(); }
            else if (_mode == Mode.Compact || _mode == Mode.Dot) GoRest();
        }

        void OnTick()
        {
            // Buzlu cam: arkadaki görüntü değişmiş olabilir, saniyede bir tazele
            if (IsVisible && !TestMode && _hwnd != IntPtr.Zero) Native.BringToTopmost(_hwnd); // sonradan "üstte" olan pencereler adayı örtmesin
            if (IsVisible && GlassMode && (!TestMode || TestBlurSource != null) && DateTime.Now > _animUntil) CaptureGlass(GlassArea(), false);
            // Tam ekran (oyun, video) açıkken adayı gizle
            bool blocked = S.IslandHideFullscreen && !TestMode && ForegroundIsFullscreen();
            if (blocked != _blocked)
            {
                _blocked = blocked;
                if (IsRest) GoRest();
            }
            if (_mode != Mode.Media) WatchOutsideClicks(false);
            if (_mode == Mode.Compact)
            {
                if (RestMode() != Mode.Compact) { GoRest(); return; }
                ShowCompact();
            }
            else if (_mode == Mode.Dot)
            {
                if (RestMode() != Mode.Dot) { GoRest(); return; }
                UpdateDot();
            }
            else if (_mode == Mode.Media) UpdateMedia();
            else if (_mode == Mode.Hidden && RestMode() != Mode.Hidden) GoRest();
        }

        public bool ForegroundIsFullscreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;
            var cls = new System.Text.StringBuilder(64);
            Native.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            var screen = System.Windows.Forms.Screen.FromHandle(fg).Bounds;
            return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        }

        /// <summary>Kurulu hatırlatıcılar ya da ayarlar değişince dışarıdan çağrılır.</summary>
        public void RefreshRest()
        {
            if (IsRest) { GoRest(); return; }
            // Açık panelde (ör. ayarlarda) stil değişmiş olabilir: görünümleri ve şekli yeniden yerleştir
            foreach (var v in Views) Canvas.SetTop(v, Gap);
            ApplyGeometry();
        }

        // =====================================================================
        //  Canlı etkinlik baloncukları: müzik çalarken kronometre / zamanlayıcı ana kapsülün yanında ayrı bir baloncukta durur.
        //  Fare üzerine gelince baloncuklar açılıp süreyi gösterir; tıklanan etkinliğin kartı açılır.
        // =====================================================================
        const int MaxBubbles = 2;
        int _moreCount;     // "+N" baloncuğunun saydığı etkinlik sayısı
        string _moreKind;   // "+N" baloncuğuna tıklanınca açılacak ilk etkinliğin türü
        readonly Border[] _bubbles = new Border[MaxBubbles];
        readonly Rectangle[] _bubGlass = new Rectangle[MaxBubbles], _bubFill = new Rectangle[MaxBubbles];
        readonly TextBlock[] _bubGlyph = new TextBlock[MaxBubbles], _bubText = new TextBlock[MaxBubbles];
        readonly string[] _bubKind = new string[MaxBubbles];
        readonly Controller.Reminder[] _bubTimer = new Controller.Reminder[MaxBubbles];
        Rectangle _bubBridge; // ada ile baloncuklar arasındaki boşluk: fare geçerken "ayrıldı" sayılmasın
        string _watchKind; // kronometre kartı hangi sayacı gösteriyor: "timer", "watch" ya da null (otomatik)
        Controller.Reminder _watchTimer; // kartta gösterilen zamanlayıcı (baloncuktan seçildiyse)

        void BuildBubbles()
        {
            _bubBridge = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Root.Children.Add(_bubBridge);
            for (int i = 0; i < MaxBubbles; i++)
            {
                int idx = i;
                var glyph = new TextBlock { FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                var text = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                                           HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0 };
                text.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
                // Zemin iki katman: buzlu cam (ayar açıksa arkadaki bulanık görüntü) ve onun üstünde koyu yarı saydam renk.
                // Yuvarlak köşeleri dikdörtgenin kendisi çizer; yapışık stilde üst yarısı ekranın dışına taşar (çentik gibi)
                var glass = new Rectangle { IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(glass, BitmapScalingMode.Linear);
                var fill = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xF4, 0, 0, 0)), Stroke = new SolidColorBrush(Color.FromArgb(0x1E, 255, 255, 255)), StrokeThickness = 1 };
                var grid = new Grid { Background = Brushes.Transparent };
                grid.Children.Add(glass);
                grid.Children.Add(fill);
                grid.Children.Add(glyph);
                grid.Children.Add(text);
                var b = new Border { Child = grid, Cursor = Cursors.Hand, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(0, 0.5) };
                var tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(1, 1));
                tg.Children.Add(new TranslateTransform());
                b.RenderTransform = tg;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; OnBubbleClick(idx); };
                Root.Children.Add(b);
                _bubbles[i] = b; _bubGlyph[i] = glyph; _bubText[i] = text; _bubGlass[i] = glass; _bubFill[i] = fill;
            }
        }

        /// <summary>
        /// Ana kapsülde gösterilmeyen canlı etkinlikler (sırayla): müzik çalarken tüm zamanlayıcılar ve kronometre;
        /// müzik yokken ana kapsül en yakın zamanlayıcıyı gösterir, diğer zamanlayıcılar ve kronometre baloncuğa geçer.
        /// </summary>
        List<KeyValuePair<string, Controller.Reminder>> BubbleItems()
        {
            var list = new List<KeyValuePair<string, Controller.Reminder>>();
            if (_mode != Mode.Compact) return list;
            var timers = _c.Reminders.Where(r => r.IsTimer).OrderBy(r => r.Due).ToList();
            if (!HasMusic())
            {
                if (timers.Count == 0) return list; // yalnızca kronometre: ana kapsülde
                timers.RemoveAt(0);                 // en yakın zamanlayıcı ana kapsülde
            }
            foreach (var t in timers) list.Add(new KeyValuePair<string, Controller.Reminder>("timer", t));
            if (_c.WatchActive) list.Add(new KeyValuePair<string, Controller.Reminder>("watch", null));
            // İkiden fazlası pencereye sığmaz: ikinci baloncuk "+N" olur, tıklanınca kart açılır, kartın başlığından
            // diğerlerine geçilir
            _moreCount = list.Count - 1;
            if (list.Count > MaxBubbles)
            {
                _moreKind = list[1].Key;
                list[1] = new KeyValuePair<string, Controller.Reminder>("more", list[1].Value);
                list.RemoveRange(MaxBubbles, list.Count - MaxBubbles);
            }
            return list;
        }

        /// <summary>Ana kapsülde gösterilmeyen canlı etkinlikleri baloncuklara dağıtır.</summary>
        void UpdateBubbles()
        {
            var items = BubbleItems();
            double ch = CH;
            bool appeared = false;
            for (int i = 0; i < MaxBubbles; i++)
            {
                var b = _bubbles[i];
                string k = i < items.Count ? items[i].Key : null;
                string was = _bubKind[i];
                _bubKind[i] = k;
                _bubTimer[i] = i < items.Count ? items[i].Value : null;
                if (k == null)
                {
                    if (was != null) HideBubble(b);
                    continue;
                }
                var timer = _bubTimer[i];
                bool more = k == "more";
                string txt = more ? "+" + _moreCount : timer != null ? Countdown(timer.Due - DateTime.Now) : WatchText(_c.Watch.Elapsed, false);
                if (_bubText[i].Text != txt) _bubText[i].Text = txt;
                // Görünüm (ikon, renk, boyut) yalnızca baloncuğun türü ya da ada boyutu/stili değişince ayarlanır
                string look = k + "|" + ch + "|" + Attached + "|" + (k == "watch" && !_c.Watch.IsRunning);
                if ((string)b.Tag != look)
                {
                    b.Tag = look;
                    _bubGlyph[i].Text = more ? "" : k == "timer" ? G.Clock : _c.Watch.IsRunning ? "" : G.Pause;
                    var fg = new SolidColorBrush(k == "timer" ? Color.FromRgb(0xFF, 0x86, 0xCF) : Color.FromRgb(0xFF, 0xB0, 0x4A));
                    fg.Freeze();
                    _bubGlyph[i].Foreground = fg;
                    _bubGlyph[i].Margin = new Thickness((ch - 14) / 2, 0, 0, 0);
                    // "+N" baloncuğu hep kapalı boyutta, sayı ortada ve her zaman görünür
                    _bubText[i].HorizontalAlignment = more ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                    _bubText[i].Margin = more ? new Thickness(0) : new Thickness(ch - 6, 0, 0, 0);
                    _bubText[i].BeginAnimation(OpacityProperty, null);
                    _bubText[i].Opacity = more || _hover ? 1 : 0;
                    if (was != null) { b.BeginAnimation(WidthProperty, null); b.Width = more || !_hover ? ch : BubbleOpenWidth(i); }
                    b.Height = ch;
                    double lift = Attached ? ch / 2 : 0;
                    foreach (var r in new[] { _bubGlass[i], _bubFill[i] })
                    {
                        r.RadiusX = r.RadiusY = ch / 2;
                        r.Margin = new Thickness(0, -lift, 0, 0);
                    }
                }
                // Açık genişlik metne göre (saatli süreler daha uzun)
                double open = BubbleOpenWidth(i);
                if (was == null) { ShowBubble(b, i, open); appeared = true; }
                else if (_hover && !b.HasAnimatedProperties && Math.Abs(b.Width - open) > 0.5) b.Width = open;
            }
            if (appeared) GrowGlass(); // yeni baloncuk camın yakalandığı alanın dışında kalmasın
            UpdateBubbleGlass();
            PositionBubbles();
        }

        double BubbleOpenWidth(int i)
        {
            if (_bubKind[i] == "more") return CH;
            string t = _bubText[i].Text ?? "";
            return CH + (t.Length > 5 ? 74 : 56);
        }

        void ShowBubble(Border b, int i, double open)
        {
            b.IsHitTestVisible = true;
            double ch = CH;
            b.BeginAnimation(WidthProperty, null);
            b.Width = _hover ? open : ch;
            _bubText[i].BeginAnimation(OpacityProperty, null);
            _bubText[i].Opacity = _hover || _bubKind[i] == "more" ? 1 : 0;
            var tg = (TransformGroup)b.RenderTransform;
            var sc = (ScaleTransform)tg.Children[0];
            var tr = (TranslateTransform)tg.Children[1];
            if (!Animated)
            {
                b.BeginAnimation(OpacityProperty, null); b.Opacity = 1;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                sc.ScaleX = sc.ScaleY = 1;
                return;
            }
            // Ana kapsülün kenarından "damla" gibi ayrılarak belirir
            b.BeginAnimation(OpacityProperty, Anim.FromTo(0, 1, 220, null, 60 + i * 70));
            var spring = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = Anim.SpringOpen, BeginTime = TimeSpan.FromMilliseconds(40 + i * 70) };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
            tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-22, 0, TimeSpan.FromMilliseconds(620)) { EasingFunction = Anim.SpringOpen, BeginTime = TimeSpan.FromMilliseconds(40 + i * 70) });
        }

        void HideBubble(Border b)
        {
            b.IsHitTestVisible = false;
            if (!Animated) { b.BeginAnimation(OpacityProperty, null); b.Opacity = 0; return; }
            b.BeginAnimation(OpacityProperty, Anim.To(0, 140));
            var sc = (ScaleTransform)((TransformGroup)b.RenderTransform).Children[0];
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(0.6, 200));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(0.6, 200));
        }

        /// <summary>Baloncukları ana şeklin sağ kenarına dizer (şekil büyüyüp küçülürken de takip ederler).</summary>
        void PositionBubbles()
        {
            if (_bubbles[0] == null) return;
            double start = (WinW + IW) / 2, x = start + 7;
            double top = Attached ? 0 : Gap;
            bool any = false;
            for (int i = 0; i < MaxBubbles; i++)
            {
                if (_bubKind[i] == null) continue;
                any = true;
                Canvas.SetLeft(_bubbles[i], x);
                Canvas.SetTop(_bubbles[i], top);
                MoveBubbleGlass(i, x, top);
                x += (double.IsNaN(_bubbles[i].Width) ? CH : _bubbles[i].Width) + 6;
            }
            // Görünmez köprü: kapsülden baloncuklara (ve baloncuklar arasına) geçerken fare adanın üstünde sayılır
            bool bridge = any && _mode == Mode.Compact;
            _bubBridge.Visibility = bridge ? Visibility.Visible : Visibility.Collapsed;
            _bubBridge.IsHitTestVisible = bridge;
            if (bridge)
            {
                Canvas.SetLeft(_bubBridge, start - 2);
                Canvas.SetTop(_bubBridge, top);
                _bubBridge.Width = Math.Max(0, x - start - 4);
                _bubBridge.Height = CH;
            }
        }

        /// <summary>Baloncukların açıkken kapladığı en sağ nokta (pencere içi): buzlu cam yakalaması bu genişliği de kapsamalı.</summary>
        double BubblesRight()
        {
            double x = (WinW + Math.Max(IW, _targetW)) / 2 + 7 + 10;
            for (int i = 0; i < MaxBubbles; i++)
                if (_bubKind[i] != null) x += BubbleOpenWidth(i) + 6;
            return x;
        }

        /// <summary>Buzlu cam açıksa baloncuklar da adanın bulanık arka planını kullanır (yoksa koyu düz zemin).</summary>
        void UpdateBubbleGlass()
        {
            if (_bubbles[0] == null) return;
            var main = Glass.Fill as ImageBrush;
            bool glass = main != null && GlassMode && _mode == Mode.Compact;
            for (int i = 0; i < MaxBubbles; i++)
            {
                if (_bubKind[i] == null && !glass) continue;
                var cur = _bubGlass[i].Fill as ImageBrush;
                if (!glass) { if (cur != null) { _bubGlass[i].Fill = null; _bubFill[i].Fill = new SolidColorBrush(Color.FromArgb(0xF4, 0, 0, 0)); } continue; }
                if (cur != null && cur.ImageSource == main.ImageSource && cur.Viewport == main.Viewport) continue;
                _bubGlass[i].Fill = new ImageBrush(main.ImageSource)
                {
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = main.Viewport, Stretch = Stretch.Fill, TileMode = TileMode.None,
                    Transform = new TranslateTransform()
                };
                if (cur == null) _bubFill[i].Fill = new SolidColorBrush(Color.FromArgb(0xB4, 0x16, 0x16, 0x1A));
                MoveBubbleGlass(i, Canvas.GetLeft(_bubbles[i]), Canvas.GetTop(_bubbles[i]));
            }
        }

        /// <summary>Cam görüntüsü pencereye göre sabit kalsın: baloncuk nerede ise görüntü o kadar ters kaydırılır.</summary>
        void MoveBubbleGlass(int i, double x, double y)
        {
            var br = _bubGlass[i].Fill as ImageBrush;
            if (br == null || double.IsNaN(x) || double.IsNaN(y)) return;
            var t = (TranslateTransform)br.Transform;
            t.X = -x;
            t.Y = -(y + _bubGlass[i].Margin.Top); // yapışık stilde dikdörtgen yukarı taşar
        }

        /// <summary>Fare adanın üzerindeyken baloncuklar açılıp süreleri gösterir.</summary>
        void HoverBubbles(bool on)
        {
            double ch = CH;
            for (int i = 0; i < MaxBubbles; i++)
            {
                if (_bubKind[i] == null || _bubKind[i] == "more") continue;
                var b = _bubbles[i];
                double to = on ? BubbleOpenWidth(i) : ch;
                if (!double.IsNaN(b.Width) && Math.Abs(b.Width - to) < 0.5 && !b.HasAnimatedProperties) continue;
                if (Animated)
                {
                    var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(460)) { EasingFunction = on ? Anim.SpringOpen : Anim.SpringClose, BeginTime = TimeSpan.FromMilliseconds(i * 50) };
                    a.CurrentTimeInvalidated += (s, e) => PositionBubbles();
                    int idx = i;
                    a.Completed += (s, e) =>
                    {
                        // Animasyonu bırak (sonraki genişlik güncellemeleri işlesin), son değeri yerel değer olarak tut
                        if (_bubbles[idx].Width != to) return;
                        _bubbles[idx].BeginAnimation(WidthProperty, null);
                        _bubbles[idx].Width = to;
                    };
                    b.BeginAnimation(WidthProperty, a);
                    b.Width = to;
                    _bubText[i].BeginAnimation(OpacityProperty, Anim.To(on ? 1 : 0, on ? 220 : 120, null, on ? 120 + i * 50 : 0));
                }
                else { b.BeginAnimation(WidthProperty, null); b.Width = to; _bubText[i].Opacity = on ? 1 : 0; }
            }
            PositionBubbles();
        }

        void OnBubbleClick(int i)
        {
            string k = _bubKind[i];
            if (k == null) return;
            if (k == "more") k = _moreKind;
            _watchKind = k;
            _watchTimer = _bubTimer[i];
            ShowWatch();
        }

        /// <summary>Kartta birden çok sayaç varsa başlığa tıklayınca sıradakine geçer.</summary>
        void CycleWatch()
        {
            var all = new List<KeyValuePair<string, Controller.Reminder>>();
            foreach (var t in _c.Reminders.Where(r => r.IsTimer).OrderBy(r => r.Due)) all.Add(new KeyValuePair<string, Controller.Reminder>("timer", t));
            if (_c.WatchActive) all.Add(new KeyValuePair<string, Controller.Reminder>("watch", null));
            if (all.Count < 2) return;
            var cur = CurTimer();
            int idx = cur == null ? all.FindIndex(p => p.Key == "watch") : all.FindIndex(p => p.Value == cur);
            var next = all[(idx + 1) % all.Count];
            _watchKind = next.Key;
            _watchTimer = next.Value;
            UpdateWatch();
            var tt = WTime.RenderTransform as TranslateTransform;
            if (Animated)
            {
                if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); WTime.RenderTransform = tt; }
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = Anim.SpringClose });
                WTime.BeginAnimation(OpacityProperty, Anim.FromTo(0.2, 1, 220));
            }
        }

        /// <summary>Kartta gösterilebilecek sayaç sayısı (zamanlayıcılar + kronometre).</summary>
        int CounterCount() { return _c.Reminders.Count(r => r.IsTimer) + (_c.WatchActive ? 1 : 0); }

        bool IsRest { get { return _mode == Mode.Compact || _mode == Mode.Idle || _mode == Mode.Dot || _mode == Mode.Hidden; } }

        // =====================================================================
        //  Arama paneli
        // =====================================================================

        public void Toggle()
        {
            if (_mode == Mode.Search && IsActive) CloseSearch(true);
            else if (_mode == Mode.Settings && IsActive) CloseSettings(true);
            else OpenSearch();
        }

        // ---------------- Ayarlar (adanın içinde) ----------------
        public void ShowSettings(string tab)
        {
            _c.ReloadSettingsIfChanged();
            _alertTimer.Stop();
            _mediaLeave.Stop();
            bool panelOpen = _mode == Mode.Search || _mode == Mode.Settings;
            if (!panelOpen)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != _hwnd) _prevForeground = fg;
                CaptureGlass();
            }
            ViewSettings.Reload();
            if (tab != null) ViewSettings.ShowTab(tab);
            EnsureShown();
            _mode = Mode.Settings;
            _hover = false;
            WatchOutsideClicks(false);
            ShowMinButton(false);
            if (!TestMode)
            {
                Activate();
                if (_hwnd != IntPtr.Zero) Native.ForceForeground(_hwnd);
            }
            Keyboard.ClearFocus();
            Focus();
            double h = Measure(ViewSettings, SearchW);
            SwapView(ViewSettings, 70);
            Morph(SearchW, h, 30, true);
        }

        void CloseSettings(bool restoreFocus)
        {
            if (_mode != Mode.Settings) return;
            ViewSettings.SaveNow();
            _mode = Mode.Alert; // geçici: GoRest bunu dinlenme moduna çevirir
            if (restoreFocus && _prevForeground != IntPtr.Zero) Native.SetForegroundWindow(_prevForeground);
            GoRest();
        }

        /// <summary>
        /// Buzlu cam: panel açılmadan hemen önce adanın arkasındaki ekran parçası alınır, küçültülüp büyütülerek bulanıklaştırılır
        /// ve yarı saydam zeminin altına konur. (Ekran görüntüsü katmanlı pencereleri, yani adanın kendisini içermez.)
        /// </summary>
        /// <summary>Şu anki halde buzlu cam kullanılacak mı? Paneller için bir ayar, adanın kendisi (kapsül, kartlar) için ayrı ayar.</summary>
        bool GlassMode
        {
            get
            {
                if (_mode == Mode.Hidden) return false;
                if (_mode == Mode.Search || _mode == Mode.Settings) return S.IslandGlass;
                return S.IslandBlur;
            }
        }

        /// <summary>
        /// Yakalanacak alan (pencere içi DIP): adanın hedef boyutunun çevresi. Yalnızca adanın kapladığı yer yakalanır;
        /// kapsül hâlindeyken bu küçücük bir şerittir (hem hızlı hem az bellek).
        /// </summary>
        Rect GlassArea()
        {
            double w = Math.Min(WinW, Math.Max(IW, _targetW) + 48), h = Gap + Math.Max(IH, _targetH) + 24;
            // Dinlenme hallerinde (kapsül, baloncuklar, medya/sayaç kartı) baştan geniş bir alan alınır: ada büyüyünce ya da
            // baloncuk belirince yeniden yakalamak gerekmez (yakalama anında animasyon takılmaz)
            if (_mode != Mode.Search && _mode != Mode.Settings) { w = WinW; h = Math.Max(h, Gap + 240); }
            return new Rect((WinW - w) / 2, 0, w, h);
        }

        static Rect PanelArea { get { return new Rect((WinW - SearchW - 48) / 2, 0, SearchW + 48, 640); } }

        /// <summary>Testlerde ekran yakalamak yerine kullanılacak görüntü.</summary>
        public static System.Windows.Media.Imaging.BitmapSource TestGlass;

        /// <summary>Testte gerçek bulanıklaştırma maliyetini ölçmek için: ekran yerine bu görüntüden kesilir.</summary>
        public static System.Drawing.Bitmap TestBlurSource;

        static System.Drawing.Bitmap TestCrop(int x, int y, int w, int h)
        {
            var b = new System.Drawing.Bitmap(w, h);
            lock (TestBlurSource) using (var g = System.Drawing.Graphics.FromImage(b)) g.DrawImage(TestBlurSource, new System.Drawing.Rectangle(0, 0, w, h), new System.Drawing.Rectangle(x, y, w, h), System.Drawing.GraphicsUnit.Pixel);
            return b;
        }

        /// <summary>Arama/ayar paneli açılmadan hemen önce (mod henüz değişmeden) çağrılır.</summary>
        void CaptureGlass()
        {
            if (S.IslandGlass) DoCapture(PanelArea);
            else ClearGlass();
        }

        /// <summary>Şu anki hal için (kapsül, kartlar) yakalar; buzlu cam kapalıysa görüntüyü bırakır.</summary>
        void CaptureGlass(Rect area, bool sync = true)
        {
            if (GlassMode) DoCapture(area, sync);
            else ClearGlass();
        }

        void ClearGlass()
        {
            Glass.Fill = null;
            GlassNext.BeginAnimation(OpacityProperty, null);
            GlassNext.Opacity = 0;
            GlassNext.Fill = null;
            UpdateBubbleGlass();
        }

        bool _capturing, _glassHiding;
        Rect _glassArea = Rect.Empty;

        /// <summary>Ada büyümeye başlarken: yeni boyut yakalanmış alanın dışına taşıyorsa camı hemen genişlet (alt kısım camsız kalmasın).</summary>
        void GrowGlass()
        {
            if (!GlassMode || Glass.Fill == null) return;
            var a = GlassArea();
            // Arka planda: yeni görüntü gelene kadar (birkaç kare) eski cam yerinde kalır, animasyon beklemez
            if (_glassArea.IsEmpty || !_glassArea.Contains(a)) DoCapture(a, false);
        }
        DateTime _animUntil; // şekil animasyonu sürerken cam tazelenmez (kare kaçmasın)

        /// <summary>
        /// Arkadaki ekranı yakalar. sync: hemen lazım (panel açılışı, ada büyümeye başlarken; küçük alan, birkaç ms).
        /// Aksi halde yakalama UI'da, bulanıklaştırma arka planda yapılır; animasyonlar takılmasın.
        /// </summary>
        void DoCapture(Rect area, bool sync = true)
        {
            if (TestMode && TestBlurSource == null)
            {
                if (TestGlass != null) SetGlass(TestGlass, new Rect(0, 0, WinW, 640));
                return;
            }
            if (_capturing && !sync) return;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src == null || src.CompositionTarget == null) return;
                var m = src.CompositionTarget.TransformToDevice;
                int x = (int)Math.Round((Left + area.X) * m.M11), y = (int)Math.Round((Top + area.Y) * m.M22);
                int w = Math.Max(8, (int)Math.Round(area.Width * m.M11)), h = Math.Max(8, (int)Math.Round(area.Height * m.M22));
                if (TestMode) { x = (int)area.X; y = (int)area.Y; w = (int)area.Width; h = (int)area.Height; }
                var exclude = new[] { _hwnd, NewsToast.Handle };
                // Adanın kendisi (ve sağ üstteki haber bildirimi) yakalamaya girmesin: yoksa her tazelemede koyulaşır
                Func<System.Drawing.Bitmap> grab = () => TestMode ? TestCrop(x, y, w, h) : GlassCapture.Grab(exclude, x, y, w, h);
                if (sync)
                {
                    using (var shot = grab()) SetGlass(GlassCapture.Blur(shot), area);
                    return;
                }
                // Yakalama da (ekran birleştirmesini beklediği için ~16 ms sürebilir) bulanıklaştırma da arka planda: animasyonlar takılmasın
                _capturing = true;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    System.Windows.Media.Imaging.BitmapSource bs = null;
                    try { using (var shot = grab()) bs = GlassCapture.Blur(shot); } catch { }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _capturing = false;
                        if (bs != null && GlassMode) SetGlass(bs, area, true);
                    }), DispatcherPriority.Background);
                });
            }
            catch { _capturing = false; }
        }

        /// <summary>Bulanık görüntüyü yakalandığı yere yerleştirir; cam katmanı adanın şekliyle kırpılır.</summary>
        /// <summary>fade: saniyelik tazeleme; yeni görüntü eskisinin üstünde yumuşakça belirir (ani sıçrama olmaz).</summary>
        void SetGlass(System.Windows.Media.ImageSource img, Rect area, bool fade = false)
        {
            var brush = new ImageBrush(img) { ViewportUnits = BrushMappingMode.Absolute, Viewport = area, Stretch = Stretch.Fill, TileMode = TileMode.None };
            brush.Freeze();
            foreach (var g in new[] { Glass, GlassNext })
                if (g.Clip != _clip)
                {
                    g.Width = WinW;
                    g.Height = 640;
                    Canvas.SetLeft(g, 0);
                    Canvas.SetTop(g, 0);
                    g.Clip = _clip;
                }
            _glassArea = area;
            // Alan değiştiyse (ada büyüdü) ya da cam görünmüyorsa geçişe gerek yok: doğrudan yerleştir
            var old = Glass.Fill as ImageBrush;
            bool crossfade = fade && Animated && old != null && old.Viewport == area && Glass.Opacity > 0.5 && !_glassHiding;
            if (!crossfade)
            {
                GlassNext.BeginAnimation(OpacityProperty, null);
                GlassNext.Opacity = 0;
                GlassNext.Fill = null;
                Glass.Fill = brush;
                UpdateBubbleGlass();
                return;
            }
            GlassNext.Fill = brush;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            a.Completed += (s, e) =>
            {
                if (GlassNext.Fill != brush) return; // bu arada başka bir görüntü geldi
                Glass.Fill = brush;
                GlassNext.BeginAnimation(OpacityProperty, null);
                GlassNext.Opacity = 0;
                GlassNext.Fill = null;
                UpdateBubbleGlass();
            };
            GlassNext.BeginAnimation(OpacityProperty, a);
        }

        public void ShowLauncher() { OpenSearch(); }

        void SetQueryAndOpen(string q)
        {
            OpenSearch();
            SetQuery(q);
        }

        public void OpenSearch()
        {
            _c.ReloadSettingsIfChanged();
            Theme.Apply(_c.Settings);
            HotkeyText.Text = S.Hotkey;
            _alertTimer.Stop();
            _mediaLeave.Stop();
            bool wasOpen = _mode == Mode.Search;
            if (_mode == Mode.Settings) ViewSettings.SaveNow();
            if (!wasOpen)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != _hwnd) _prevForeground = fg;
                if (_mode != Mode.Settings) CaptureGlass();
                Placeholder.Text = Greeting() + "  " + Examples[_exampleIndex++ % Examples.Length];
                if (Compose.Visibility == Visibility.Visible) { Compose.Visibility = Visibility.Collapsed; ResultsHost.Visibility = Visibility.Visible; }
            }
            EnsureShown();
            _mode = Mode.Search;
            _hover = false;
            WatchOutsideClicks(false);
            ShowMinButton(false);
            if (!TestMode)
            {
                Activate();
                if (_hwnd != IntPtr.Zero) Native.ForceForeground(_hwnd);
            }
            Input.Focus();
            Keyboard.Focus(Input);
            Input.SelectAll();
            AppIndex.RefreshIfStale();
            Refresh(true, false);
            if (!wasOpen)
            {
                ViewSearch.UpdateLayout();
                double h = Measure(ViewSearch, SearchW);
                SwapView(ViewSearch, 90);
                Morph(SearchW, h, 30, true);
                if (Animated)
                {
                    var once = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                    once.Tick += (s, e) => { once.Stop(); Bounce(); Blink(); };
                    once.Start();
                }
            }
        }

        /// <summary>Arama panelini kapatır; restoreFocus ise odağı önceki pencereye geri verir.</summary>
        void CloseSearch(bool restoreFocus)
        {
            if (_mode != Mode.Search) return;
            if (Compose.Visibility == Visibility.Visible) { Compose.Visibility = Visibility.Collapsed; ResultsHost.Visibility = Visibility.Visible; }
            _mode = Mode.Alert; // geçici: GoRest bunu dinlenme moduna çevirir
            _alertTimer.Stop();
            if (restoreFocus && _prevForeground != IntPtr.Zero) Native.SetForegroundWindow(_prevForeground);
            GoRest();
        }

        public void HideLauncher() { CloseSearch(false); }

        /// <summary>Odağı, ada açılmadan önceki pencereye geri verir (pano yapıştırma için).</summary>
        public void RestoreFocus()
        {
            if (_prevForeground != IntPtr.Zero) Native.SetForegroundWindow(_prevForeground);
        }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            if (h >= 5 && h < 12) return "Günaydın!";
            if (h >= 12 && h < 18) return "İyi günler!";
            if (h >= 18 && h < 23) return "İyi akşamlar!";
            return "İyi geceler!";
        }

        void Blink()
        {
            if (!Animated) return;
            if (_mode == Mode.Search) BlinkEyes(EyeL, EyeR);
            else if (_mode == Mode.Idle) BlinkEyes(IdleEyeL, IdleEyeR);
        }

        static void BlinkEyes(ScaleTransform l, ScaleTransform r)
        {
            if (l == null || r == null) return;
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
            l.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            r.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void Bounce()
        {
            if (!Animated) return;
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0.86, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), Anim.SpringOpen));
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ---------------- IHost ----------------
        public Settings Settings { get { return _c.Settings; } }

        public void SetQuery(string text)
        {
            if (Compose.Visibility == Visibility.Visible) CloseCompose();
            Input.Text = text;
            Input.CaretIndex = text.Length;
            Input.Focus();
            Refresh(true, true);
        }

        public void OpenSettings() { ShowSettings(null); }
        public void ExitApp() { _c.Exit(); }

        /// <summary>Ayar paneli açıksa içindeki kişi/komut satırlarını kaydeder.</summary>
        public void FlushSettings() { if (ViewSettings != null && _mode == Mode.Settings) ViewSettings.SaveNow(); }
        public void AddReminder(TimeSpan delay, string text) { _c.AddReminder(delay, text); }

        public void Flash(string message)
        {
            Status.Text = message;
            Status.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        void ResetStatus()
        {
            Status.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
            Status.Text = Compose.Visibility != Visibility.Visible ? SearchStatus
                        : _whatsApp ? "Ctrl+Enter gönderir  ·  Esc geri döner" : "Ctrl+Enter taslağı açar  ·  Esc geri döner";
        }

        public void AppendResults(int version, List<ResultItem> items, ResultItem placeholder)
        {
            if (version != _version || Compose.Visibility == Visibility.Visible) return;
            var list = (Results.ItemsSource as List<ResultItem>) ?? new List<ResultItem>();
            int at = placeholder != null ? list.IndexOf(placeholder) : -1;
            var merged = list.Where(x => x != placeholder).ToList();
            merged.InsertRange(at >= 0 ? at : 0, items);
            Results.ItemsSource = merged;
            Results.SelectedIndex = 0;
            AfterResults(true);
            // Sesle sorulmuştu: gelen cevabı (hava, internet) oku
            if (version == _voiceVersion)
            {
                _voiceVersion = -1;
                // "rapor dosyasını aç" gibi: bulunan en iyi dosya kendiliğinden açılır
                var auto = items.FirstOrDefault(x => x.AutoRun && x.Execute != null);
                if (auto != null && merged.IndexOf(auto) == 0)
                {
                    Results.SelectedItem = auto;
                    Speech.Say(auto.Speak);
                    Execute();
                    return;
                }
                var sp = items.FirstOrDefault(x => !string.IsNullOrEmpty(x.Speak));
                Speech.Say(sp != null ? sp.Speak : "Buna dair bir şey bulamadım");
            }
        }

        void OnInputChanged()
        {
            Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh(false, true);
        }

        void Refresh(bool force, bool fit)
        {
            if (Compose.Visibility == Visibility.Visible) return;
            _version++;
            List<ResultItem> items = _engine.Query(Input.Text, _version);
            Results.ItemsSource = items;
            bool any = items.Count > 0;
            ResultsHost.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            // Boş arama: "bugün" paneli (uygulama listesi yerine)
            bool home = Input.Text.Trim().Length == 0;
            var hh = (FrameworkElement)Root.FindName("HomeHost");
            if (home && (hh.Visibility != Visibility.Visible || force))
            {
                _home.Refresh(); // her açılışta güncel (saat, pil, gündem)
                if (hh.Visibility != Visibility.Visible)
                {
                    hh.Visibility = Visibility.Visible;
                    if (Animated) Anim.Enter(hh, 30, 6, 260);
                }
            }
            else if (!home) hh.Visibility = Visibility.Collapsed;
            if (any) Results.SelectedIndex = 0;
            string first = any ? items[0].Title : null;
            bool rows = force || first != _lastFirstTitle;
            _lastFirstTitle = first;
            if (fit) AfterResults(rows);
            else if (rows) AnimateRows(160);
        }

        /// <summary>Sonuçlar değişince: satırları kademeli belirt ve paneli yeni yüksekliğe yumuşakça uydur.</summary>
        void AfterResults(bool animateRows)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (animateRows) AnimateRows(0);
                if (_mode != Mode.Search) { UpdatePill(false); return; }
                double h = Measure(ViewSearch, SearchW);
                if (Math.Abs(h - _targetH) > 0.5) MorphHeight(h);
                UpdatePill(false);
            }), DispatcherPriority.Loaded);
        }

        void AnimateRows(int baseDelay)
        {
            if (!Animated) { QueuePill(false); return; }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < Results.Items.Count && i < 9; i++)
                {
                    var c = Results.ItemContainerGenerator.ContainerFromIndex(i) as UIElement;
                    if (c != null) Anim.Enter(c, baseDelay + i * 22, 5, 240);
                }
                UpdatePill(false);
            }), DispatcherPriority.Loaded);
        }

        void QueuePill(bool animate)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdatePill(animate)), DispatcherPriority.Loaded);
        }

        void UpdatePill(bool animate)
        {
            var c = Results.SelectedItem == null ? null : Results.ItemContainerGenerator.ContainerFromItem(Results.SelectedItem) as FrameworkElement;
            if (c == null || !c.IsVisible || ResultsHost.Visibility != Visibility.Visible)
            {
                Pill.BeginAnimation(OpacityProperty, null);
                Pill.Opacity = 0;
                _pillShown = false;
                return;
            }
            // Satırın kendi giriş kaydırması hesaba katılmasın: kalıcı konumu yerleşimden al
            var slot = LayoutInformation.GetLayoutSlot(c);
            double y = c.TransformToAncestor(ResultsHost).Transform(new Point(0, 0)).Y;
            var tt = c.RenderTransform as TranslateTransform;
            if (tt != null) y -= tt.Y;
            y += 1;
            double h = Math.Max(0, slot.Height - 2);
            if (animate && _pillShown && Animated)
            {
                PillY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.SpringClose });
                Pill.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.SpringClose });
            }
            else
            {
                PillY.BeginAnimation(TranslateTransform.YProperty, null);
                Pill.BeginAnimation(HeightProperty, null);
                PillY.Y = y;
                Pill.Height = h;
            }
            if (!_pillShown)
            {
                if (Animated) Pill.BeginAnimation(OpacityProperty, Anim.To(1, 160));
                else { Pill.BeginAnimation(OpacityProperty, null); Pill.Opacity = 1; }
            }
            _pillShown = true;
        }

        void Move(int d)
        {
            int n = Results.Items.Count;
            if (n == 0) return;
            Results.SelectedIndex = ((Results.SelectedIndex + d) % n + n) % n;
            Results.ScrollIntoView(Results.SelectedItem);
        }

        void OnResultClick(object sender, MouseButtonEventArgs e)
        {
            var c = ItemsControl.ContainerFromElement(Results, (DependencyObject)e.OriginalSource) as ListBoxItem;
            if (c == null) return;
            Results.SelectedItem = c.DataContext;
            Execute();
            if (_mode == Mode.Search) Input.Focus();
        }

        void Execute()
        {
            var it = Results.SelectedItem as ResultItem;
            if (it == null || it.Execute == null) return;
            if (it.NeedsConfirm && !it.Confirming)
            {
                it.BeginConfirm();
                QueuePill(true);
                return;
            }
            if (it.UsageKey != null) _c.Settings.AddUsage(it.UsageKey);
            Bounce();
            if (!it.KeepOpen) CloseSearch(false);
            try
            {
                it.Execute();
                if (!it.KeepOpen) Input.Text = "";
            }
            catch (Exception ex)
            {
                if (_mode != Mode.Search) OpenSearch();
                Flash("Olmadı: " + ex.Message);
                Status.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (_mode == Mode.Settings)
            {
                if (e.Key == Key.Escape && !ViewSettings.CapturingHotkey) { OpenSearch(); e.Handled = true; }
                return;
            }
            if (_mode != Mode.Search) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (Compose.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseCompose(); e.Handled = true; }
                else if (e.Key == Key.Enter && ctrl) { SendMail(); e.Handled = true; }
                return;
            }
            if (ctrl && e.Key == Key.OemComma) { OpenSettings(); e.Handled = true; return; }
            switch (e.Key)
            {
                case Key.Escape:
                    var sel = Results.SelectedItem as ResultItem;
                    if (sel != null && sel.Confirming) Refresh(true, true);
                    else if (Input.Text.Length > 0) Input.Text = "";
                    else CloseSearch(true);
                    e.Handled = true;
                    break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Enter: Execute(); e.Handled = true; break;
                case Key.Tab:
                    var it = Results.SelectedItem as ResultItem;
                    if (it != null && it.Complete != null) SetQuery(it.Complete);
                    e.Handled = true;
                    break;
            }
        }

        // ---------------- Mail paneli ----------------
        // Panel ya mail ya da WhatsApp mesajı için kullanılır
        bool _whatsApp;

        public void ShowCompose(string to, string subject, string body)
        {
            OpenCompose(false, to, subject, body);
        }

        public void ShowWhatsApp(string to, string text)
        {
            OpenCompose(true, to, "", text);
        }

        void OpenCompose(bool whatsApp, string to, string subject, string body)
        {
            if (_mode != Mode.Search) OpenSearch();
            _whatsApp = whatsApp;
            ResultsHost.Visibility = Visibility.Collapsed;
            Compose.Visibility = Visibility.Visible;
            var f = (Func<string, object>)Root.FindName;
            var icon = (TextBlock)f("ComposeIcon");
            icon.Text = whatsApp ? G.Chat : G.Mail;
            icon.Foreground = whatsApp ? Palette.Make("#25D366") : Palette.Make("#FF7A7A");
            ((TextBlock)f("ComposeTitle")).Text = whatsApp ? "WhatsApp mesajı" : "Yeni mail";
            ((FrameworkElement)f("SubjectRow")).Visibility = whatsApp ? Visibility.Collapsed : Visibility.Visible;
            MailTo.Tag = whatsApp ? "kayıtlı kişi adı ya da telefon numarası (boş bırakırsan WhatsApp'ta seçersin)" : "mail adresi ya da kayıtlı kişi adı (birden fazlaysa virgülle)";
            MailBody.Height = whatsApp ? 110 : 150;
            ((Button)f("MailSend")).Content = whatsApp ? (S.WhatsAppAutoSend ? "Gönder  ·  Ctrl+Enter" : "WhatsApp'ta aç  ·  Ctrl+Enter") : "Taslağı aç  ·  Ctrl+Enter";
            MailTo.Text = to ?? "";
            MailSubject.Text = subject ?? "";
            MailBody.Text = body ?? "";
            string p = _c.Settings.MailProvider;
            MailProviderLabel.Text = whatsApp ? (S.WhatsAppAutoSend ? "Sohbet açılınca otomatik gönderilir" : "WhatsApp'ta sohbet açılır")
                : p == "outlook" ? "Outlook'ta taslak olarak açılır" : p == "varsayilan" ? "Varsayılan mail uygulamasında açılır" : "Gmail'de taslak olarak açılır";
            ResetStatus();
            if (Animated) Anim.Enter(Compose, 60, 8, 260);
            AfterResults(false);
            TextBox focus = MailTo.Text.Length == 0 ? MailTo : MailSubject.Text.Length == 0 && !whatsApp ? MailSubject : MailBody;
            Dispatcher.BeginInvoke(new Action(() => { focus.Focus(); focus.CaretIndex = focus.Text.Length; }), DispatcherPriority.Input);
        }

        /// <summary>Kayıtlı Instagram değeri (sohbet linki ya da kullanıcı adı) → açılacak adres.</summary>
        static string InstagramUrl(string link)
        {
            link = (link ?? "").Trim();
            if (link.Length == 0) return "https://www.instagram.com/direct/inbox/";
            if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return link;
            if (link.IndexOf("instagram.com", StringComparison.OrdinalIgnoreCase) >= 0 || link.StartsWith("ig.me", StringComparison.OrdinalIgnoreCase)) return "https://" + link.TrimStart('/');
            return "https://ig.me/m/" + Uri.EscapeDataString(link.TrimStart('@'));
        }

        /// <summary>
        /// Instagram: kişinin ayarlarda kayıtlı sohbet linkini tarayıcıda açar, sayfadaki mesaj kutusunu bulup mesajı yazar ve gönderir.
        /// Mesaj kutusu bulunamazsa hiçbir şey yazmaz (yanlış yere yazılmasın), mesajı panoya koyup haber verir.
        /// </summary>
        public void SendDiscord(string who, string text, bool send)
        {
            var blurple = Palette.Make("#5865F2");
            ShowNotice("Discord'a yazılıyor…", who + " · “" + text + "”", G.Chat, blurple);
            string msg = text.Replace("\r", " ").Replace("\n", " ");
            var th = new System.Threading.Thread(() =>
            {
                string result = AppAutomation.Discord(who, msg, send);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (result == null) ShowNotice(send ? "Discord mesajı gönderildi" : "Discord mesajı yazıldı", who + " · “" + msg + "”" + (send ? "" : " · göndermek için Enter"), G.Check, blurple);
                    else
                    {
                        try { Clipboard.SetText(msg); } catch { }
                        ShowNotice("Discord mesajı gönderilemedi", result + " · mesaj panoda", G.Chat, Palette.Danger);
                    }
                }));
            }) { IsBackground = true };
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }

        public void AskGemini(string question)
        {
            var violet = Palette.Make("#8E75FF");
            ShowNotice("Gemini'ye soruluyor…", "“" + question + "”", G.Sparkle, violet);
            var th = new System.Threading.Thread(() =>
            {
                string result = AppAutomation.Gemini(question);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (result != null)
                    {
                        try { Clipboard.SetText(question); } catch { }
                        ShowNotice("Gemini'ye yazılamadı", result + " · soru panoda, Ctrl+V ile yapıştırabilirsin", G.Sparkle, Palette.Danger);
                    }
                }));
            }) { IsBackground = true };
            th.SetApartmentState(System.Threading.ApartmentState.STA);
            th.Start();
        }

        public void SendInstagram(string link, string text, string displayName)
        {
            var pink = Palette.Make("#E1306C");
            bool hasText = !string.IsNullOrWhiteSpace(text);
            bool hasLink = !string.IsNullOrWhiteSpace(link);
            Shell.OpenUrl(InstagramUrl(link));
            if (!hasLink)
            {
                if (hasText) { try { Clipboard.SetText(text); } catch { } }
                ShowNotice("Instagram açıldı", (displayName ?? "") + (string.IsNullOrEmpty(displayName) ? "" : " için sohbet linki kayıtlı değil · ") +
                           (hasText ? "mesaj panoda" : "Ayarlar → Kişiler'den link ekleyebilirsin"), G.Chat, pink);
                return;
            }
            if (!hasText) { ShowNotice("Instagram açıldı", (displayName ?? "") + " ile sohbet", G.Chat, pink); return; }
            ShowNotice("Instagram'a gönderiliyor…", (displayName ?? "") + " · “" + text + "”", G.Chat, pink);
            string msg = text.Replace("\r", " ").Replace("\n", " ");
            var th = new System.Threading.Thread(() =>
            {
                string result = InstagramType(msg);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (result == null) ShowNotice("Instagram mesajı gönderildi", (displayName ?? "") + " · “" + msg + "”", G.Check, pink);
                    else
                    {
                        try { Clipboard.SetText(msg); } catch { }
                        ShowNotice("Instagram mesajı gönderilemedi", result + " · mesaj panoda, Ctrl+V ile yapıştırabilirsin", G.Chat, Palette.Danger);
                    }
                }));
            }) { IsBackground = true };
            th.Start();
        }

        /// <summary>Instagram sohbet sayfası açılınca mesaj kutusunu bulur, yazar ve Enter'a basar. Başarılıysa null, değilse nedeni.</summary>
        static string InstagramType(string msg)
        {
            // 1) Tarayıcıda Instagram sekmesi öne gelsin
            IntPtr hwnd = IntPtr.Zero;
            var end = DateTime.Now.AddSeconds(25);
            while (DateTime.Now < end && hwnd == IntPtr.Zero)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (WindowTitleOf(fg).IndexOf("Instagram", StringComparison.OrdinalIgnoreCase) >= 0) hwnd = fg;
                else System.Threading.Thread.Sleep(300);
            }
            if (hwnd == IntPtr.Zero) return "Instagram sayfası açılmadı";

            // 2) Sayfa yüklenip mesaj kutusu görünene kadar ara
            System.Windows.Automation.AutomationElement box = null;
            end = DateTime.Now.AddSeconds(20);
            while (DateTime.Now < end && box == null)
            {
                try { box = FindMessageBox(hwnd); } catch { }
                if (box == null) System.Threading.Thread.Sleep(700);
            }
            if (box == null) return "mesaj kutusu bulunamadı (giriş yapılmamış ya da link bir sohbete ait değil)";

            // 3) Yaz ve gönder (yalnızca hâlâ aynı pencere öndeyse)
            try { box.SetFocus(); } catch { return "mesaj kutusuna odaklanılamadı"; }
            System.Threading.Thread.Sleep(250);
            if (Native.GetForegroundWindow() != hwnd) return "tarayıcı penceresi öne gelmedi";
            Native.TypeText(msg);
            System.Threading.Thread.Sleep(350);
            if (Native.GetForegroundWindow() != hwnd) return "tarayıcı penceresi değişti";
            Native.PressEnter();
            return null;
        }

        static System.Windows.Automation.AutomationElement FindMessageBox(IntPtr hwnd)
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            var edits = root.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit));
            System.Windows.Automation.AutomationElement best = null;
            foreach (System.Windows.Automation.AutomationElement e in edits)
            {
                string n = TextUtil.Norm(e.Current.Name ?? "");
                if (e.Current.IsOffscreen || !e.Current.IsKeyboardFocusable) continue;
                // Instagram'ın mesaj kutusu "Mesaj…" / "Message…" adını taşır; adres çubuğu ve arama kutusu atlanır
                if (n.StartsWith("mesaj") || n.StartsWith("message")) return e;
                if (n.Contains("adres") || n.Contains("address") || n.Contains("ara") || n.Contains("search")) continue;
                if (best == null || e.Current.BoundingRectangle.Bottom > best.Current.BoundingRectangle.Bottom) best = e;
            }
            // Ad bulunamazsa: sayfanın en altındaki yazı kutusu (sohbet ekranında mesaj kutusu en alttadır)
            return best != null && best.Current.BoundingRectangle.Top > 300 ? best : null;
        }

        static string WindowTitleOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, 256);
            return sb.ToString();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int max);

        /// <summary>WhatsApp'ta sohbeti açar; numara biliniyorsa ve ayar açıksa mesajı otomatik gönderir, sonucu adada bildirir.</summary>
        public void SendWhatsApp(string to, string text)
        {
            string phone = WhatsApp.Resolve(to, S);
            string who = string.IsNullOrWhiteSpace(to) ? "" : to.Trim();
            var green = Palette.Make("#25D366");
            bool auto = WhatsApp.Send(phone, text, S.WhatsAppAutoSend, sent => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (sent) ShowNotice("WhatsApp mesajı gönderildi", who + "  ·  " + text, G.Chat, green);
                else ShowNotice("WhatsApp açıldı", "Mesaj yazılı; göndermek için Enter'a bas", G.Chat, green);
            })));
            if (!auto)
            {
                string sub = phone.Length == 0 && who.Length > 0 ? who + " için kayıtlı numara yok, kişiyi WhatsApp'ta seç" :
                             phone.Length == 0 ? "Kişiyi WhatsApp'ta seç, mesaj hazır" : "Mesaj yazılı; göndermek için Enter'a bas";
                ShowNotice("WhatsApp açıldı", sub, G.Chat, green);
            }
        }

        void CloseCompose()
        {
            Compose.Visibility = Visibility.Collapsed;
            ResultsHost.Visibility = Visibility.Visible;
            ResetStatus();
            Input.Focus();
            Refresh(true, true);
        }

        void SendMail()
        {
            if (_whatsApp)
            {
                string who = MailTo.Text, msg = MailBody.Text;
                Compose.Visibility = Visibility.Collapsed;
                ResultsHost.Visibility = Visibility.Visible;
                Input.Text = "";
                CloseSearch(false);
                SendWhatsApp(who, msg);
                return;
            }
            var s = _c.Settings;
            string to = Contacts.Resolve(MailTo.Text, s);
            foreach (string r in to.Split(',').Select(x => x.Trim()).Where(x => x.Contains("@")))
            {
                s.RecentEmails.Remove(r);
                s.RecentEmails.Insert(0, r);
            }
            if (s.RecentEmails.Count > 20) s.RecentEmails.RemoveRange(20, s.RecentEmails.Count - 20);
            s.Save();
            string subj = MailSubject.Text, body = MailBody.Text;
            Compose.Visibility = Visibility.Collapsed;
            ResultsHost.Visibility = Visibility.Visible;
            Input.Text = "";
            CloseSearch(false);
            Mail.Compose(s.MailProvider, to.Replace(" ", ""), subj, body);
        }
    }
}
