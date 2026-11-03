using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pardux
{
    /// <summary>
    /// Ayarlar: ayrı pencere yerine adanın içinde açılan panel. Her değişiklik anında uygulanır ve kaydedilir
    /// (kaydet düğmesi yok, kapatınca hiçbir şey kaybolmaz).
    /// </summary>
    public class SettingsPanel : Grid
    {
        public const double PanelW = 720;

        static string Tab(string name, string text, bool chk = false)
        {
            return "<RadioButton x:Name='Tab" + name + "' GroupName='tabs' Style='{DynamicResource Seg}' Content='" + text + "'" + (chk ? " IsChecked='True'" : "") + "/>";
        }

        static string Row(string title, string hint, string control)
        {
            return @"
                <Grid Margin='14,8'>
                  <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
                  <StackPanel VerticalAlignment='Center' Margin='0,0,14,0'>
                    <TextBlock Text='" + title + @"' Style='{DynamicResource RowText}'/>" +
                    (hint == null ? "" : "<TextBlock Text='" + hint + "' Foreground='{DynamicResource FgMuted}' FontSize='11.5' Margin='0,2,0,0' TextWrapping='Wrap'/>") + @"
                  </StackPanel>
                  <ContentControl Grid.Column='1' Focusable='False' VerticalAlignment='Center'>" + control + @"</ContentControl>
                </Grid>";
        }

        static string Seg(string group, params string[] items)
        {
            // items: "Ad|Metin"
            return "<Border Style='{DynamicResource SegHost}'><StackPanel Orientation='Horizontal'>" +
                   string.Concat(items.Select(i => "<RadioButton x:Name='" + i.Split('|')[0] + "' Content='" + i.Split('|')[1] + "' GroupName='" + group + "' Style='{DynamicResource Seg}'/>")) +
                   "</StackPanel></Border>";
        }

        static string Check(string name, string text) { return "<CheckBox x:Name='" + name + "' Content='" + text + "' Style='{DynamicResource Check}'/>"; }

        const string Sep = "<Border Style='{DynamicResource Sep}'/>";

        static string Card(params string[] rows) { return "<Border Style='{DynamicResource Card}'><StackPanel>" + string.Join(Sep, rows) + "</StackPanel></Border>"; }

        static string Group(string text, string hint = null)
        {
            return "<TextBlock Text='" + text + "' Style='{DynamicResource GroupTitle}'/>" +
                   (hint == null ? "" : "<TextBlock Text='" + hint + "' Style='{DynamicResource Hint}' Margin='4,-4,0,8'/>");
        }

        static string Xaml()
        {
            return @"
<Grid " + Theme.Ns + @">
  <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
  <Grid Margin='14,12,12,8'>
    <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
    <Border x:Name='Back' Style='{DynamicResource IconBtn}' Margin='0' ToolTip='Geri (Esc)'><TextBlock Text='&#xE76B;' Style='{DynamicResource IconGlyph}' FontSize='13'/></Border>
    <StackPanel Grid.Column='1' Orientation='Horizontal' Margin='8,0,0,0' VerticalAlignment='Center'>
      " + Theme.Logo(26, 7, "P") + @"
      <TextBlock Text='Ayarlar' Foreground='{DynamicResource Fg}' FontSize='17' FontWeight='SemiBold' Margin='10,0,0,0' VerticalAlignment='Center'/>
      <TextBlock x:Name='Saved' Text='Kaydedildi' Foreground='{DynamicResource FgMuted}' FontSize='11.5' Margin='12,2,0,0' VerticalAlignment='Center' Opacity='0'/>
    </StackPanel>
    <Border x:Name='Close' Grid.Column='2' Style='{DynamicResource IconBtn}' ToolTip='Kapat'><TextBlock Text='&#xE8BB;' Style='{DynamicResource IconGlyph}' FontSize='11'/></Border>
  </Grid>
  <Border Grid.Row='1' Style='{DynamicResource SegHost}' HorizontalAlignment='Center' Margin='0,0,0,6'>
    <StackPanel Orientation='Horizontal'>" +
      Tab("Genel", "Genel", true) + Tab("Gorunum", "Görünüm") + Tab("Ada", "Ada") + Tab("Ses", "Ses") + Tab("Bildirim", "Bildirimler") +
      Tab("Kisi", "Kişiler") + Tab("Komut", "Komutlar") + Tab("Hakkinda", "Hakkında") + @"
    </StackPanel>
  </Border>
  <ScrollViewer x:Name='Scroll' Grid.Row='2' MaxHeight='440' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
    <Grid x:Name='Pages' Margin='20,0,20,6'>

      <StackPanel x:Name='PageGenel'>" +
        Group("Kısayol ve mail") +
        Card(Row("Açma kısayolu", "Kutuya tıkla, sonra yeni tuş birleşimine bas",
                 "<TextBox x:Name='HotkeyBox' Style='{DynamicResource Field}' IsReadOnly='True' Cursor='Hand' FontWeight='SemiBold' Width='180' HorizontalContentAlignment='Center'/>"),
             Row("Mail nerede açılsın?", null, Seg("mail", "MailGmail|Gmail", "MailOutlook|Outlook", "MailDefault|Mail uygulaması"))) +
        Group("Davranış") +
        Card(Check("Startup", "Windows açılınca arka planda başlat"), Check("Frequent", "Boş aramada sık kullandıklarımı göster"),
             Check("Clips", "Pano geçmişini tut (yalnızca bellekte)"), Check("AutoPlay", "Spotify: aranan şarkıyı otomatik başlat"),
             Check("WaAuto", "WhatsApp ve Discord: mesajı yazınca otomatik gönder"),
             Check("WebAns", "Bilmediğim soruları internette araştırıp cevabı burada göster")) + @"
      </StackPanel>

      <StackPanel x:Name='PageGorunum' Visibility='Collapsed'>" +
        Group("Renkler") +
        Card(Row("Tema", "Haber penceresi ve bildirimler için", Seg("theme", "ThSys|Sistem", "ThDark|Koyu", "ThLight|Açık")),
             Row("Vurgu rengi", null, "<StackPanel x:Name='Swatches' Orientation='Horizontal'/>")) +
        Group("Efektler") +
        Card(Check("Blur", "Ada buzlu cam olsun (arkası bulanık görünür)"),
             Check("Glass", "Arama ve ayar panelinde de buzlu cam"),
             Row("Pencere arka planı", "Haber penceresinde", Seg("backdrop", "BdAcrylic|Bulanık", "BdMica|Mika", "BdFlat|Düz")),
             Check("Anims", "Animasyonlar")) + @"
        <TextBlock x:Name='GlassNote' Style='{DynamicResource Hint}' Margin='4,8,0,0' Visibility='Collapsed' Text='Bulanık ve mika arka plan Windows 11 (22H2 ve sonrası) gerektirir.'/>
      </StackPanel>

      <StackPanel x:Name='PageAda' Visibility='Collapsed'>" +
        Group("Pardux Ada", "Ekranın üstündeki siyah ada: müzik, zamanlayıcı ve bildirimler burada görünür.") +
        Card(Row("Stil", "Yüzen ya da ekranın üst kenarına yapışık (çentik gibi)", Seg("style", "StFloat|Yüzen", "StAttach|Üste yapışık")),
             Row("Boyut", "Hep görünen küçük adanın boyutu", Seg("size", "SzS|Küçük", "SzM|Orta", "SzL|Büyük")),
             Check("IsMusic", "Çalan müziği ve videoyu adada göster"),
             Check("IsFront", "Spotify / tarayıcı ekranın önündeyken adayı gizle"),
             Check("IsIdle", "Boştayken küçük adayı hep göster"),
             Check("IsFull", "Oyun ve tam ekran uygulamalarda gizle"),
             Check("IsCharge", "Şarj takılınca / çıkarılınca bildir")) +
        Group("Gözler", "Boştayken görünen küçük adadaki Pardux gözleri.") +
        Card(Row("Önizleme", null, @"<Border Width='104' Height='28' CornerRadius='14' Background='Black' BorderBrush='#1EFFFFFF' BorderThickness='1'>
                    <StackPanel Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center'>
                      <Ellipse x:Name='PrevEyeL' Fill='White' Width='6' Height='8' Margin='0,0,9,0'/><Ellipse x:Name='PrevEyeR' Fill='White' Width='6' Height='8'/>
                    </StackPanel></Border>"),
             Row("Renk", null, "<StackPanel x:Name='EyeSwatches' Orientation='Horizontal'/>"),
             Row("Boyut", null, Seg("eyesize", "EyS|Küçük", "EyM|Orta", "EyL|Büyük")),
             Row("Şekil", null, Seg("eyeshape", "EyOval|Oval", "EyRound|Yuvarlak", "EyLine|Uykulu"))) + @"
      </StackPanel>

      <StackPanel x:Name='PageSes' Visibility='Collapsed'>" +
        Group("Sesli komut", "“Hey Pardux” de, sesi duyunca ne istediğini söyle: “müzik aç”, “10 dakika sonra çayı hatırlat”, “annem’e whatsapp’tan geliyorum yaz”. Ses tamamen bilgisayarında işlenir, internete gönderilmez.") +
        Card(Check("Wake", "“Hey Pardux” ile uyandır"),
             Check("Replies", "Sesle sorunca sesle cevap ver (“Dinliyorum efendim”, “Müziği açıyorum”)"),
             Row("Ses", "Doğal: çevrimdışı yapay zekâ sesi, daha akıcı telaffuz", Seg("veng", "VeNat|Doğal", "VeWin|Windows")),
             Row("Durum", null, "<TextBlock x:Name='VoiceState' Foreground='{DynamicResource FgDim}' FontSize='12.5'/>")) +
        Group("Ses efektleri") +
        Card(Check("SoundsOn", "Pardux sesleri (uyanma, bildirim, alarm)"),
             Row("Ses düzeyi", null, Seg("vol", "VolLow|Kısık", "VolMid|Orta", "VolHigh|Yüksek")),
             Row("Dinle", null, @"<StackPanel Orientation='Horizontal'>
                    <Button x:Name='PlayWake' Content='Uyanma' Style='{DynamicResource Btn}' Margin='0,0,6,0'/>
                    <Button x:Name='PlayNotify' Content='Bildirim' Style='{DynamicResource Btn}' Margin='0,0,6,0'/>
                    <Button x:Name='PlayAlarm' Content='Alarm' Style='{DynamicResource Btn}' Margin='0,0,6,0'/>
                    <Button x:Name='PlayVoice' Content='Konuşma' Style='{DynamicResource Btn}'/>
                  </StackPanel>")) + @"
      </StackPanel>

      <StackPanel x:Name='PageBildirim' Visibility='Collapsed'>" +
        Group("Gündem haberleri", "Öne çıkan önemli haberler sağ üstte kısa bir bildirimle gelir; tıklayınca ayrıntısı açılır. Gece 00:00–08:00 arası sessizdir.") +
        Card(Check("NewsOn", "Önemli haberleri bildir"),
             Check("MissedOn", "Kaçırdığım bildirimleri sonra hatırlat (bildirim merkezinde 10 dk açılmadan duranlar)"),
             Row("Örnek bildirim", "Şu anki en önemli haberi hemen gösterir", "<Button x:Name='NewsTest' Content='Şimdi göster' Style='{DynamicResource Btn}'/>")) +
        Group("İyi misin?", "Bilgisayarı uzun süre hiç ellemezsen Pardux yukarıdan hâlini hatırını sorar (film izlerken ya da oyundayken sormaz).") +
        Card(Check("CheckIn", "Uzun süre hareketsiz kalınca sor"),
             Row("Ne kadar sonra?", null, Seg("idle", "Idle30|30 dk", "Idle60|1 saat", "Idle120|2 saat")),
             Row("Örnek", null, "<Button x:Name='CheckTest' Content='Şimdi sor' Style='{DynamicResource Btn}'/>")) + @"
      </StackPanel>

      <StackPanel x:Name='PageKisi' Visibility='Collapsed'>
        <TextBlock Text='Kayıtlı kişilere adıyla mail, WhatsApp ya da Instagram mesajı gönderebilirsin. Instagram için sohbeti tarayıcıda açıp adres çubuğundaki linki yapıştır. Ör: &quot;ayşe&apos;ye instagramdan selam yaz&quot;' Style='{DynamicResource Hint}' Margin='4,4,0,10'/>
        <Grid Margin='4,0,0,6'>
          <Grid.ColumnDefinitions><ColumnDefinition Width='100'/><ColumnDefinition/><ColumnDefinition Width='146'/><ColumnDefinition Width='160'/><ColumnDefinition Width='44'/></Grid.ColumnDefinitions>
          <TextBlock Text='Ad' Style='{DynamicResource Label}' Margin='0'/>
          <TextBlock Grid.Column='1' Text='Mail' Style='{DynamicResource Label}' Margin='8,0,0,0'/>
          <TextBlock Grid.Column='2' Text='Telefon (WhatsApp)' Style='{DynamicResource Label}' Margin='8,0,0,0'/>
          <TextBlock Grid.Column='3' Text='Instagram sohbet linki' Style='{DynamicResource Label}' Margin='8,0,0,0'/>
        </Grid>
        <StackPanel x:Name='ContactRows'/>
        <Button x:Name='AddContact' Content='+  Kişi ekle' Style='{DynamicResource Btn}' HorizontalAlignment='Left' Margin='0,4,0,0'/>
      </StackPanel>

      <StackPanel x:Name='PageKomut' Visibility='Collapsed'>
        <TextBlock Text='Bir ad yazınca açılacak klasör, dosya, program veya site. Ör: &quot;proje&quot; → C:\Projeler' Style='{DynamicResource Hint}' Margin='4,4,0,10'/>
        <StackPanel x:Name='CmdRows'/>
        <Button x:Name='AddCmd' Content='+  Komut ekle' Style='{DynamicResource Btn}' HorizontalAlignment='Left' Margin='0,4,0,0'/>
      </StackPanel>

      <StackPanel x:Name='PageHakkinda' Visibility='Collapsed'>
        <Border Style='{DynamicResource Card}' Padding='16' Margin='0,4,0,0'>
          <StackPanel Orientation='Horizontal'>
            " + Theme.Logo(54, 14, "H") + @"
            <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
              <TextBlock Text='Pardux' Foreground='{DynamicResource Fg}' FontSize='18' FontWeight='SemiBold'/>
              <TextBlock x:Name='AboutVersion' Foreground='{DynamicResource FgDim}' FontSize='12' Margin='0,2,0,0'/>
            </StackPanel>
          </StackPanel>
        </Border>" + Group("Araçlar") + @"
        <WrapPanel>
          <Button x:Name='Shortcut' Content='Masaüstü kısayolu oluştur' Style='{DynamicResource Btn}' Margin='0,0,8,8'/>
          <Button x:Name='Reindex' Content='Uygulama listesini yenile' Style='{DynamicResource Btn}' Margin='0,0,8,8'/>
          <Button x:Name='OpenDir' Content='Ayar klasörünü aç' Style='{DynamicResource Btn}' Margin='0,0,8,8'/>
          <Button x:Name='ClearUsage' Content='Kullanım geçmişini sıfırla' Style='{DynamicResource Btn}' Margin='0,0,8,8'/>
          <Button x:Name='ClearClips' Content='Pano geçmişini temizle' Style='{DynamicResource Btn}' Margin='0,0,8,8'/>
        </WrapPanel>
      </StackPanel>
    </Grid>
  </ScrollViewer>
  <TextBlock x:Name='Info' Grid.Row='3' Foreground='{DynamicResource FgDim}' FontSize='12' TextWrapping='Wrap' Margin='22,4,22,12' MinHeight='16'/>
</Grid>";
        }

        static readonly string[] Pages = { "Genel", "Gorunum", "Ada", "Ses", "Bildirim", "Kisi", "Komut", "Hakkinda" };

        readonly Controller _c;
        readonly FrameworkElement _body;
        readonly TextBox _hotkey;
        readonly DispatcherTimer _saveTimer, _savedFade;
        string _hotkeyValue;
        bool _loading;

        /// <summary>Panel yüksekliği değişince (sekme değişimi, satır ekleme) ada yeni boyuta uyar.</summary>
        public event Action Resized;
        public event Action BackRequested, CloseRequested;

        /// <summary>Kısayol kutusu tuş beklerken Esc paneli kapatmasın.</summary>
        public bool CapturingHotkey { get { return _hotkey.IsKeyboardFocused; } }

        Settings S { get { return _c.Settings; } }

        T Find<T>(string n) where T : class { return _body.FindName(n) as T; }

        public SettingsPanel(Controller c)
        {
            _c = c;
            Width = PanelW;
            Background = Brushes.Transparent;
            // Stiller panelin kendisinde: kod ile üretilen satırlar da (adaya eklenmeden önce) bulabilsin; renkler adanın koyu paletinden
            Resources.MergedDictionaries.Add(Theme.NewDict());
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(IslandWindow.LocalStyles));
            Resources.MergedDictionaries.Add(Theme.Island);
            _body = (FrameworkElement)XamlReader.Parse(Xaml());
            Children.Add(_body);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _saveTimer.Tick += (o, e) => { _saveTimer.Stop(); SaveNow(); };
            _savedFade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            _savedFade.Tick += (o, e) => { _savedFade.Stop(); Find<UIElement>("Saved").BeginAnimation(OpacityProperty, Anim.To(0, 300)); };

            foreach (string p in Pages)
            {
                string page = p;
                Find<RadioButton>("Tab" + p).Checked += (o, e) => ShowPage(page);
            }
            Ui.Click(Find<FrameworkElement>("Back"), () => { var h = BackRequested; if (h != null) h(); });
            Ui.Click(Find<FrameworkElement>("Close"), () => { var h = CloseRequested; if (h != null) h(); });

            _hotkey = Find<TextBox>("HotkeyBox");
            _hotkey.PreviewKeyDown += CaptureHotkey;
            _hotkey.GotKeyboardFocus += (o, e) => { _c.SuspendHotkey(); _hotkey.Text = "Tuşlara bas…"; };
            _hotkey.LostKeyboardFocus += (o, e) => { _hotkey.Text = S.Hotkey; _c.RegisterHotkey(S.Hotkey, false); };

            // Görünüm: renk örnekleri
            var sw = Find<StackPanel>("Swatches");
            foreach (var a in Theme.Accents)
            {
                string key = a[0];
                var rb = new RadioButton
                {
                    GroupName = "accent", Style = (Style)FindResource("Swatch"), ToolTip = a[3], Tag = key,
                    Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a[1]), (Color)ColorConverter.ConvertFromString(a[2]), 45)
                };
                rb.Checked += (o, e) => Change(() => S.Accent = key);
                sw.Children.Add(rb);
            }

            // Göz renkleri
            var eyes = Find<StackPanel>("EyeSwatches");
            foreach (var ec in new[] { new[] { "#FFFFFF", "Beyaz" }, new[] { "vurgu", "Vurgu rengi" }, new[] { "#5EEAD4", "Turkuaz" }, new[] { "#60A5FA", "Mavi" },
                                      new[] { "#4ADE80", "Yeşil" }, new[] { "#F472B6", "Pembe" }, new[] { "#FB923C", "Turuncu" }, new[] { "#FACC15", "Sarı" } })
            {
                string key = ec[0];
                Brush bg = key == "vurgu" ? (Brush)FindResource("AccentGrad") : new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));
                var rb = new RadioButton { GroupName = "eyecolor", Style = (Style)FindResource("Swatch"), ToolTip = ec[1], Tag = key, Background = bg };
                rb.Checked += (o, e) => { if (!_loading) Change(() => { S.EyeColor = key; UpdateEyePreview(); }); };
                eyes.Children.Add(rb);
            }

            // Sesler
            Find<Button>("PlayWake").Click += (o, e) => PlayPreview(Sound.Wake);
            Find<Button>("PlayNotify").Click += (o, e) => PlayPreview(Sound.Notify);
            Find<Button>("PlayAlarm").Click += (o, e) => PlayPreview(Sound.Alarm);
            Find<Button>("PlayVoice").Click += (o, e) =>
            {
                if (!S.VoiceReplies) { Info("Sesli cevap kapalı; önce \"Sesle sorunca sesle cevap ver\"i aç."); return; }
                Speech.Say("Merhaba, ben Pardux. Sana nasıl yardımcı olabilirim?");
            };
            Find<Button>("NewsTest").Click += (o, e) => { Info("En önemli haber getiriliyor…"); _c.ShowTopNews(); };
            Find<Button>("CheckTest").Click += (o, e) => _c.AskCheckIn(true);

            // Kişiler / komutlar
            Find<Button>("AddContact").Click += (o, e) => FocusFirst(AddRow(Find<StackPanel>("ContactRows"), true, "", "", "", ""));
            Find<Button>("AddCmd").Click += (o, e) => FocusFirst(AddRow(Find<StackPanel>("CmdRows"), false, "", "", null, null));

            // Araçlar
            Find<Button>("Reindex").Click += (o, e) => { AppIndex.RefreshAsync(); Info("Uygulama listesi yenileniyor…"); };
            Find<Button>("OpenDir").Click += (o, e) => Shell.Open(Settings.Dir);
            Find<Button>("ClearUsage").Click += (o, e) => { S.Usage.Clear(); S.Save(); Info("Kullanım geçmişi sıfırlandı."); };
            Find<Button>("ClearClips").Click += (o, e) => { _c.Clips.Clear(); Info("Pano geçmişi temizlendi."); };
            Find<Button>("Shortcut").Click += (o, e) =>
            {
                try { Shell.CreateDesktopShortcut(); Info("Masaüstüne \"Pardux\" kısayolu eklendi (yeni simgeyle)."); }
                catch (Exception ex) { Info("Kısayol oluşturulamadı: " + ex.Message, true); }
            };
            Find<TextBlock>("AboutVersion").Text = "Sürüm " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(2) + " · " + Settings.Dir;
            if (!Fluent.Supported) Find<UIElement>("GlassNote").Visibility = Visibility.Visible;

            BindAll();
        }

        // ---------------- Bağlama ----------------
        void Bind(string name, Func<bool> get, Action<bool> set)
        {
            var cb = Find<CheckBox>(name);
            cb.IsChecked = get();
            cb.Click += (o, e) => Change(() => set(cb.IsChecked == true));
        }

        void BindRadio(string name, Func<bool> isOn, Action select)
        {
            var rb = Find<RadioButton>(name);
            rb.IsChecked = isOn();
            rb.Checked += (o, e) => { if (!_loading) Change(select); };
        }

        void BindAll()
        {
            _loading = true;
            _hotkeyValue = S.Hotkey;
            _hotkey.Text = S.Hotkey;
            Bind("Startup", () => S.StartWithWindows, v => { S.StartWithWindows = v; S.ApplyStartup(); });
            Bind("Frequent", () => S.ShowFrequent, v => S.ShowFrequent = v);
            Bind("Clips", () => S.ClipboardHistory, v => { S.ClipboardHistory = v; if (!v) _c.Clips.Clear(); });
            Bind("AutoPlay", () => S.SpotifyAutoPlay, v => S.SpotifyAutoPlay = v);
            Bind("WaAuto", () => S.WhatsAppAutoSend, v => S.WhatsAppAutoSend = v);
            BindRadio("MailGmail", () => S.MailProvider == "gmail", () => S.MailProvider = "gmail");
            BindRadio("MailOutlook", () => S.MailProvider == "outlook", () => S.MailProvider = "outlook");
            BindRadio("MailDefault", () => S.MailProvider == "varsayilan", () => S.MailProvider = "varsayilan");

            BindRadio("ThSys", () => S.Theme == "sistem", () => S.Theme = "sistem");
            BindRadio("ThDark", () => S.Theme == "koyu", () => S.Theme = "koyu");
            BindRadio("ThLight", () => S.Theme == "acik", () => S.Theme = "acik");
            foreach (RadioButton rb in Find<StackPanel>("Swatches").Children) rb.IsChecked = (string)rb.Tag == S.Accent;
            Bind("Glass", () => S.IslandGlass, v => S.IslandGlass = v);
            Bind("Blur", () => S.IslandBlur, v => S.IslandBlur = v);
            Bind("Anims", () => S.Animations, v => S.Animations = v);
            BindRadio("BdAcrylic", () => S.Backdrop == "akrilik", () => S.Backdrop = "akrilik");
            BindRadio("BdMica", () => S.Backdrop == "mika", () => S.Backdrop = "mika");
            BindRadio("BdFlat", () => S.Backdrop == "duz", () => S.Backdrop = "duz");

            BindRadio("StFloat", () => S.IslandStyle != "yapisik", () => S.IslandStyle = "yuzen");
            BindRadio("StAttach", () => S.IslandStyle == "yapisik", () => S.IslandStyle = "yapisik");
            BindRadio("VeNat", () => S.VoiceEngine != "windows", () => S.VoiceEngine = "dogal");
            BindRadio("VeWin", () => S.VoiceEngine == "windows", () => S.VoiceEngine = "windows");
            BindRadio("EyS", () => S.EyeSize == "kucuk", () => { S.EyeSize = "kucuk"; UpdateEyePreview(); });
            BindRadio("EyM", () => S.EyeSize == "orta", () => { S.EyeSize = "orta"; UpdateEyePreview(); });
            BindRadio("EyL", () => S.EyeSize == "buyuk", () => { S.EyeSize = "buyuk"; UpdateEyePreview(); });
            BindRadio("EyOval", () => S.EyeShape == "oval", () => { S.EyeShape = "oval"; UpdateEyePreview(); });
            BindRadio("EyRound", () => S.EyeShape == "yuvarlak", () => { S.EyeShape = "yuvarlak"; UpdateEyePreview(); });
            BindRadio("EyLine", () => S.EyeShape == "cizgi", () => { S.EyeShape = "cizgi"; UpdateEyePreview(); });
            foreach (RadioButton rb in Find<StackPanel>("EyeSwatches").Children) rb.IsChecked = (string)rb.Tag == S.EyeColor;
            UpdateEyePreview();
            BindRadio("SzS", () => S.IslandSize == "kucuk", () => S.IslandSize = "kucuk");
            BindRadio("SzM", () => S.IslandSize == "orta", () => S.IslandSize = "orta");
            BindRadio("SzL", () => S.IslandSize == "buyuk", () => S.IslandSize = "buyuk");
            Bind("IsMusic", () => S.IslandMusic, v => S.IslandMusic = v);
            Bind("IsFront", () => S.IslandHideWhenAppFront, v => S.IslandHideWhenAppFront = v);
            Bind("IsIdle", () => S.IslandIdle, v => S.IslandIdle = v);
            Bind("IsFull", () => S.IslandHideFullscreen, v => S.IslandHideFullscreen = v);
            Bind("IsCharge", () => S.IslandCharging, v => S.IslandCharging = v);

            Bind("Wake", () => S.WakeWord, v => { S.WakeWord = v; _c.ApplyVoice(); });
            Bind("Replies", () => S.VoiceReplies, v => S.VoiceReplies = v);
            Bind("WebAns", () => S.WebAnswers, v => S.WebAnswers = v);
            Bind("SoundsOn", () => S.Sounds, v => S.Sounds = v);
            BindRadio("VolLow", () => S.SoundVolume <= 45, () => S.SoundVolume = 35);
            BindRadio("VolMid", () => S.SoundVolume > 45 && S.SoundVolume <= 80, () => S.SoundVolume = 65);
            BindRadio("VolHigh", () => S.SoundVolume > 80, () => S.SoundVolume = 100);

            Bind("NewsOn", () => S.NewsAlerts, v => S.NewsAlerts = v);
            Bind("MissedOn", () => S.MissedReminders, v => S.MissedReminders = v);
            Bind("CheckIn", () => S.CheckIn, v => S.CheckIn = v);
            BindRadio("Idle30", () => S.CheckInMinutes <= 30, () => S.CheckInMinutes = 30);
            BindRadio("Idle60", () => S.CheckInMinutes > 30 && S.CheckInMinutes <= 60, () => S.CheckInMinutes = 60);
            BindRadio("Idle120", () => S.CheckInMinutes > 60, () => S.CheckInMinutes = 120);

            foreach (string name in S.Contacts.Keys.Concat(S.Phones.Keys).Concat(S.Instagram.Keys).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                string mail, tel, ig;
                S.Contacts.TryGetValue(name, out mail);
                S.Phones.TryGetValue(name, out tel);
                S.Instagram.TryGetValue(name, out ig);
                AddRow(Find<StackPanel>("ContactRows"), true, name, mail ?? "", tel ?? "", ig ?? "", false);
            }
            foreach (var cmd in S.CustomCommands.Where(x => x != null)) AddRow(Find<StackPanel>("CmdRows"), false, cmd.Name, cmd.Target, null, null, false);
            _loading = false;
            UpdateVoiceState();
        }

        /// <summary>Panel her açılışta güncel ayarları gösterir.</summary>
        public void Reload()
        {
            Find<StackPanel>("ContactRows").Children.Clear();
            Find<StackPanel>("CmdRows").Children.Clear();
            // Olay işleyicileri tekrar bağlanmasın diye yalnızca değerleri tazele
            _loading = true;
            foreach (var cb in new[] { "Startup", "Frequent", "Clips", "AutoPlay", "WaAuto", "Glass", "Blur", "Anims", "IsMusic", "IsFront", "IsIdle", "IsFull", "IsCharge", "Wake", "Replies", "WebAns", "SoundsOn", "NewsOn", "MissedOn", "CheckIn" })
                Find<CheckBox>(cb).IsChecked = CheckValue(cb);
            _hotkey.Text = S.Hotkey;
            foreach (string name in S.Contacts.Keys.Concat(S.Phones.Keys).Concat(S.Instagram.Keys).Distinct(StringComparer.CurrentCultureIgnoreCase))
            {
                string mail, tel, ig;
                S.Contacts.TryGetValue(name, out mail);
                S.Phones.TryGetValue(name, out tel);
                S.Instagram.TryGetValue(name, out ig);
                AddRow(Find<StackPanel>("ContactRows"), true, name, mail ?? "", tel ?? "", ig ?? "", false);
            }
            foreach (var cmd in S.CustomCommands.Where(x => x != null)) AddRow(Find<StackPanel>("CmdRows"), false, cmd.Name, cmd.Target, null, null, false);
            _loading = false;
            UpdateVoiceState();
            Find<TextBlock>("Info").Text = "";
        }

        bool CheckValue(string name)
        {
            switch (name)
            {
                case "Startup": return S.StartWithWindows;
                case "Frequent": return S.ShowFrequent;
                case "Clips": return S.ClipboardHistory;
                case "AutoPlay": return S.SpotifyAutoPlay;
                case "WaAuto": return S.WhatsAppAutoSend;
                case "Glass": return S.IslandGlass;
                case "Blur": return S.IslandBlur;
                case "Anims": return S.Animations;
                case "IsMusic": return S.IslandMusic;
                case "IsFront": return S.IslandHideWhenAppFront;
                case "IsIdle": return S.IslandIdle;
                case "IsFull": return S.IslandHideFullscreen;
                case "IsCharge": return S.IslandCharging;
                case "Wake": return S.WakeWord;
                case "Replies": return S.VoiceReplies;
                case "WebAns": return S.WebAnswers;
                case "SoundsOn": return S.Sounds;
                case "NewsOn": return S.NewsAlerts;
                case "MissedOn": return S.MissedReminders;
                default: return S.CheckIn;
            }
        }

        /// <summary>Ayarlardaki küçük ada önizlemesindeki gözleri seçilen renk, boyut ve şekle göre çizer.</summary>
        void UpdateEyePreview()
        {
            double w, h, gap;
            switch (S.EyeSize) { case "kucuk": w = 5; h = 6.5; gap = 8; break; case "buyuk": w = 8; h = 10.5; gap = 11; break; default: w = 6; h = 8; gap = 9; break; }
            if (S.EyeShape == "yuvarlak") h = w;
            else if (S.EyeShape == "cizgi") { w *= 1.7; h = Math.Max(2.5, h * 0.32); }
            Brush fill;
            try { fill = S.EyeColor == "vurgu" ? (Brush)FindResource("Accent") : new SolidColorBrush((Color)ColorConverter.ConvertFromString(S.EyeColor)); }
            catch { fill = Brushes.White; }
            foreach (var n in new[] { "PrevEyeL", "PrevEyeR" })
            {
                var e = Find<System.Windows.Shapes.Ellipse>(n);
                e.Width = w; e.Height = h; e.Fill = fill;
            }
            Find<System.Windows.Shapes.Ellipse>("PrevEyeL").Margin = new Thickness(0, 0, gap, 0);
        }

        public void UpdateVoiceState()
        {
            string t;
            if (!Voice.Available) t = "Ses motoru bulunamadı (vosk klasörü eksik)";
            else if (!S.WakeWord) t = "Kapalı";
            else if (Voice.Running) t = "Dinliyor · “Hey Pardux” de" + (Whisper.Available ? " · Whisper ile gelişmiş Türkçe" : "");
            else t = Voice.LastError ?? "Başlatılıyor…";
            Find<TextBlock>("VoiceState").Text = t;
        }

        void PlayPreview(Sound s)
        {
            if (!S.Sounds) { Info("Sesler kapalı; önce \"Pardux sesleri\"ni aç."); return; }
            Sounds.Play(s);
        }

        // ---------------- Değişiklik ve kayıt ----------------
        void Change(Action apply)
        {
            if (_loading) return;
            apply();
            Theme.Apply(S);
            _c.RefreshIsland();
            QueueSave();
        }

        void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Kişi ve komut satırlarını ayarlara aktarıp diske yazar.</summary>
        public void SaveNow()
        {
            _saveTimer.Stop();
            if (S.DeletedContacts == null) S.DeletedContacts = new List<string>();
            if (S.DeletedCommands == null) S.DeletedCommands = new List<string>();
            // Adı değiştirilen satırların eski adı "silindi" sayılır (yedeklerden eski adla ikinci bir kopya gelmesin)
            NoteRenames(Find<StackPanel>("ContactRows"), S.DeletedContacts);
            NoteRenames(Find<StackPanel>("CmdRows"), S.DeletedCommands);

            var oldContacts = S.Contacts; var oldPhones = S.Phones; var oldIg = S.Instagram; var oldCmds = S.CustomCommands;
            S.CustomCommands = Rows(Find<StackPanel>("CmdRows")).Where(t => t[1].Length > 0).Select(t => new CustomCommand { Name = t[0], Target = t[1] }).ToList();
            S.Contacts = new Dictionary<string, string>();
            S.Phones = new Dictionary<string, string>();
            S.Instagram = new Dictionary<string, string>();
            foreach (var t in Rows(Find<StackPanel>("ContactRows")))
            {
                if (t[1].Length > 0) S.Contacts[t[0]] = t[1];
                if (t[2].Length > 0) S.Phones[t[0]] = t[2];
                if (t[3].Length > 0) S.Instagram[t[0]] = t[3].Contains("/") ? t[3] : t[3].TrimStart('@');
                UnmarkDeleted(S.DeletedContacts, t[0]);
            }
            foreach (var c in S.CustomCommands) UnmarkDeleted(S.DeletedCommands, c.Name);
            // Güvenlik: panelde görünmeyen ama silinmemiş kişi/komutlar korunur (panel eski bir listeyi gösteriyor olsa bile hiçbir şey kaybolmaz)
            Action<Dictionary<string, string>, Dictionary<string, string>> keep = (from, to) =>
            {
                if (from == null) return;
                foreach (var kv in from)
                    if (!IsListed(kv.Key) && !IsMarked(S.DeletedContacts, kv.Key)) to[kv.Key] = kv.Value;
            };
            keep(oldContacts, S.Contacts); keep(oldPhones, S.Phones); keep(oldIg, S.Instagram);
            if (oldCmds != null)
                foreach (var c in oldCmds)
                    if (c != null && !S.CustomCommands.Any(x => string.Equals(x.Name, c.Name, StringComparison.CurrentCultureIgnoreCase)) && !IsMarked(S.DeletedCommands, c.Name))
                        S.CustomCommands.Add(c);
            S.Save();
            var saved = Find<UIElement>("Saved");
            saved.BeginAnimation(OpacityProperty, Anim.To(1, 160));
            _savedFade.Stop();
            _savedFade.Start();
        }

        static bool IsMarked(List<string> list, string name)
        {
            return list != null && list.Any(x => string.Equals(x, name, StringComparison.CurrentCultureIgnoreCase));
        }

        static void MarkDeleted(List<string> list, string name)
        {
            if (list != null && !IsMarked(list, name)) list.Add(name);
        }

        static void UnmarkDeleted(List<string> list, string name)
        {
            if (list != null) list.RemoveAll(x => string.Equals(x, name, StringComparison.CurrentCultureIgnoreCase));
        }

        bool IsListed(string name)
        {
            return Rows(Find<StackPanel>("ContactRows")).Any(t => string.Equals(t[0], name, StringComparison.CurrentCultureIgnoreCase));
        }

        static void NoteRenames(StackPanel host, List<string> deleted)
        {
            foreach (var g in host.Children.OfType<Grid>())
            {
                var box = g.Children.OfType<TextBox>().FirstOrDefault();
                string orig = g.Tag as string, now = box != null ? box.Text.Trim() : "";
                if (!string.IsNullOrWhiteSpace(orig) && now.Length > 0 && !string.Equals(orig, now, StringComparison.CurrentCultureIgnoreCase))
                    MarkDeleted(deleted, orig);
                if (now.Length > 0) g.Tag = now;
            }
        }

        void ShowPage(string page)
        {
            foreach (string q in Pages) Find<FrameworkElement>("Page" + q).Visibility = q == page ? Visibility.Visible : Visibility.Collapsed;
            if (page == "Ses") UpdateVoiceState();
            Find<ScrollViewer>("Scroll").ScrollToTop();
            if (S.Animations) Anim.Enter(Find<FrameworkElement>("Page" + page), 0, 6, 220);
            RaiseResized();
        }

        void RaiseResized()
        {
            Dispatcher.BeginInvoke(new Action(() => { var h = Resized; if (h != null) h(); }), DispatcherPriority.Loaded);
        }

        public void ShowTab(string page) { Find<RadioButton>("Tab" + page).IsChecked = true; ShowPage(page); }

        // ---------------- Satırlar ----------------
        static void FocusFirst(FrameworkElement row)
        {
            var tb = ((Panel)row).Children.OfType<TextBox>().FirstOrDefault();
            if (tb != null) tb.Dispatcher.BeginInvoke(new Action(() => tb.Focus()), DispatcherPriority.Input);
        }

        FrameworkElement AddRow(StackPanel host, bool contact, string v1, string v2, string v3, string v4, bool animate = true)
        {
            // Tag: satırın kayıttaki adı (ad değiştirilirse eski ad "silindi" sayılır, iki kopya oluşmaz)
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7), Tag = v1 ?? "" };
            if (contact)
            {
                foreach (double w in new double[] { 100, -1, 146, 160 })
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            }
            else
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var vals = contact ? new[] { v1, v2, v3, v4 } : new[] { v1, v2 };
            var hints = contact ? new[] { "Ad", "mail@ornek.com", "0555 111 22 33", "instagram.com/direct/t/…" } : new[] { "Ad", "Klasör, dosya, program veya site" };
            TextBox second = null;
            for (int i = 0; i < vals.Length; i++)
            {
                var tb = new TextBox { Style = (Style)FindResource("Field"), Text = vals[i] ?? "", Tag = hints[i], ToolTip = hints[i], Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
                tb.LostKeyboardFocus += (o, e) => QueueSave();
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
                if (i == 1) second = tb;
            }
            int col = vals.Length;
            if (!contact)
            {
                var target = second;
                var br = new Button { Content = "…", Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 0, 12, 0), Height = 32, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Dosya seç" };
                br.Click += (o, e) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Açılacak dosya veya programı seç", CheckFileExists = true };
                    IslandWindow.ModalOpen = true;
                    try { if (dlg.ShowDialog(Window.GetWindow(this)) == true) { target.Text = dlg.FileName; QueueSave(); } }
                    finally { IslandWindow.ModalOpen = false; }
                };
                Grid.SetColumn(br, col++);
                g.Children.Add(br);
            }
            var del = new Button { Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 0, 10, 0), Height = 32, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Sil",
                Content = new TextBlock { Text = "\uE74D", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 } };
            del.Click += (o, e) =>
            {
                host.Children.Remove(g);
                // Bilerek silinen kişi/komut not edilir: diğer kopyalardan (yedekler) geri getirilmez
                var nameBox = g.Children.OfType<TextBox>().FirstOrDefault();
                foreach (string n in new[] { (string)g.Tag, nameBox != null ? nameBox.Text.Trim() : "" })
                    if (!string.IsNullOrWhiteSpace(n)) MarkDeleted(contact ? S.DeletedContacts : S.DeletedCommands, n);
                QueueSave();
                RaiseResized();
            };
            Grid.SetColumn(del, col);
            g.Children.Add(del);
            host.Children.Add(g);
            if (animate && S.Animations) Anim.Enter(g, 0, 6, 200);
            if (animate) RaiseResized();
            return g;
        }

        static IEnumerable<string[]> Rows(StackPanel host)
        {
            foreach (var g in host.Children.OfType<Grid>())
            {
                var v = g.Children.OfType<TextBox>().Select(t => t.Text.Trim()).ToList();
                while (v.Count < 4) v.Add("");
                if (v[0].Length > 0) yield return v.ToArray();
            }
        }

        void Info(string msg, bool error = false)
        {
            var t = Find<TextBlock>("Info");
            t.Text = msg;
            t.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "AccentText");
            if (S.Animations) Anim.Enter(t, 0, 4, 200);
        }

        // ---------------- Kısayol yakalama ----------------
        void CaptureHotkey(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
            var mods = Keyboard.Modifiers;
            bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
            if (mods == ModifierKeys.None && !win && !(key >= Key.F1 && key <= Key.F24))
            {
                _hotkey.Text = "En az bir değiştirici tuş (Ctrl, Alt, Shift, Win) kullan";
                return;
            }
            string spec = "";
            if ((mods & ModifierKeys.Control) != 0) spec += "Ctrl+";
            if ((mods & ModifierKeys.Alt) != 0) spec += "Alt+";
            if ((mods & ModifierKeys.Shift) != 0) spec += "Shift+";
            if (win) spec += "Win+";
            spec += key.ToString();
            if (_c.RegisterHotkey(spec, false))
            {
                S.Hotkey = spec;
                _hotkeyValue = spec;
                _hotkey.Text = spec;
                QueueSave();
                Info("Kısayol " + spec + " olarak ayarlandı.");
                Keyboard.ClearFocus();
            }
            else
            {
                Info(spec + " başka bir uygulama tarafından kullanılıyor, başka bir kombinasyon dene.", true);
                _hotkey.Text = "Tuşlara bas…";
            }
        }
    }
}
