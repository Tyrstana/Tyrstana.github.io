using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pardux
{
    internal static class Ui
    {
        /// <summary>Herhangi bir öğeye güvenilir tıklama davranışı ekler: fare öğe üzerinde bırakılırsa çalışır ve olay yukarı taşınmaz.</summary>
        public static void Click(FrameworkElement e, Action a)
        {
            bool pressed = false;
            e.PreviewMouseLeftButtonDown += (s, ev) => { pressed = true; ev.Handled = true; };
            e.MouseLeave += (s, ev) => pressed = false;
            e.PreviewMouseLeftButtonUp += (s, ev) =>
            {
                ev.Handled = true;
                bool inside = new Rect(0, 0, e.ActualWidth, e.ActualHeight).Contains(ev.GetPosition(e));
                if (pressed && inside) { pressed = false; a(); }
                pressed = false;
            };
        }

        /// <summary>Windows 11 tarzı başlık çubuğu düğmeleri: simge durumuna küçült ve kapat (sağ üst köşe).</summary>
        public const string CaptionButtons = @"
<StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top'>
  <Border x:Name='MinX' Width='46' Height='32' ToolTip='Simge durumuna küçült'>
    <Border.Style>
      <Style TargetType='Border'>
        <Setter Property='Background' Value='Transparent'/>
        <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource Hover}'/></Trigger></Style.Triggers>
      </Style>
    </Border.Style>
    <TextBlock Text='&#xE921;' FontFamily='{DynamicResource IconFont}' FontSize='10' Foreground='{DynamicResource Fg}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <Border x:Name='CloseX' Width='46' Height='32' ToolTip='Kapat'>
    <Border.Style>
      <Style TargetType='Border'>
        <Setter Property='Background' Value='Transparent'/>
        <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#C42B1C'/></Trigger></Style.Triggers>
      </Style>
    </Border.Style>
    <TextBlock Text='&#xE8BB;' FontFamily='{DynamicResource IconFont}' FontSize='10' HorizontalAlignment='Center' VerticalAlignment='Center'>
      <TextBlock.Style>
        <Style TargetType='TextBlock'>
          <Setter Property='Foreground' Value='{DynamicResource Fg}'/>
          <Style.Triggers>
            <DataTrigger Binding='{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Border}}' Value='True'><Setter Property='Foreground' Value='White'/></DataTrigger>
          </Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>
  </Border>
</StackPanel>";

        public const string CloseButton = @"
<Border x:Name='CloseX' Width='36' Height='30' CornerRadius='8' Cursor='Hand' VerticalAlignment='Top' HorizontalAlignment='Right' ToolTip='Kapat'>
  <Border.Style>
    <Style TargetType='Border'>
      <Setter Property='Background' Value='Transparent'/>
      <Style.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#E5484D'/></Trigger>
      </Style.Triggers>
    </Style>
  </Border.Style>
  <TextBlock Text='&#xE8BB;' FontFamily='{DynamicResource IconFont}' FontSize='10.5' HorizontalAlignment='Center' VerticalAlignment='Center'>
    <TextBlock.Style>
      <Style TargetType='TextBlock'>
        <Setter Property='Foreground' Value='{DynamicResource FgDim}'/>
        <Style.Triggers>
          <DataTrigger Binding='{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Border}}' Value='True'><Setter Property='Foreground' Value='White'/></DataTrigger>
        </Style.Triggers>
      </Style>
    </TextBlock.Style>
  </TextBlock>
</Border>";
    }

    /// <summary>Akrilik/mika arka planlı, kenarlıksız pencereler için ortak taban.</summary>
    public class DarkWindow : Window
    {
        protected FrameworkElement Body;

        protected void Init(string title, string xaml, double width, bool mica)
        {
            Title = title;
            Tag = mica ? "mica" : "acrylic";
            SizeToContent = SizeToContent.Height;
            Width = width;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            Fluent.Prepare(this);
            Resources.MergedDictionaries.Add(Theme.NewDict());
            Body = (FrameworkElement)XamlReader.Parse(xaml);
            Content = Body;
            try { Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(Controller.AppIcon.Handle, Int32Rect.Empty, null); }
            catch { }
        }

        protected T Find<T>(string name) where T : class { return Body.FindName(name) as T; }
    }
}
