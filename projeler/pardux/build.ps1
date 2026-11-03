# Pardux derleme betiği — Windows'ta yerleşik .NET Framework derleyicisini kullanır, ek kurulum gerekmez.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf  = Join-Path $fw 'WPF'
$dist = Join-Path $root 'dist'
$icon = Join-Path $root 'assets\pardux.ico'
New-Item -ItemType Directory -Force $dist | Out-Null
New-Item -ItemType Directory -Force (Split-Path $icon) | Out-Null

if (-not (Test-Path $icon)) {
    Write-Host 'Simge olusturuluyor...'
    & (Join-Path $root 'tools\make-icon.ps1') -Out $icon
}

$refs = @(
    "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
    "$wpf\UIAutomationClient.dll", "$wpf\UIAutomationTypes.dll",
    "$fw\System.Xaml.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Data.dll",
    "$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Web.Extensions.dll",
    "$fw\Microsoft.CSharp.dll", "$fw\System.Xml.dll", "$fw\System.Security.dll", "$fw\System.Runtime.dll",
    # Windows medya oturumları (şu an çalan) için WinRT meta verileri
    "$env:WINDIR\System32\WinMetadata\Windows.Media.winmd", "$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd",
    "$env:WINDIR\System32\WinMetadata\Windows.Storage.winmd",
    # Wi-Fi/Bluetooth (Radyo API'si), kaçırılan bildirimler, parlaklık (WMI)
    "$env:WINDIR\System32\WinMetadata\Windows.Devices.winmd", "$env:WINDIR\System32\WinMetadata\Windows.UI.winmd",
    "$env:WINDIR\System32\WinMetadata\Windows.ApplicationModel.winmd", "$fw\System.Management.dll"
) | ForEach-Object { "/r:$_" }

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $dist 'Pardux.exe'
Remove-Item (Join-Path $dist 'EmoAsistan.exe') -ErrorAction SilentlyContinue  # eski adla derlenmiş exe

& "$fw\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$out" "/win32icon:$icon" "/resource:$icon,pardux.ico" "/win32manifest:$(Join-Path $root 'src\app.manifest')" $refs $sources
if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz" }

# Uyanış ve bildirim sesleri
$sounds = Join-Path $root 'assets\sounds'
if (Test-Path $sounds) { New-Item -ItemType Directory -Force (Join-Path $dist 'sounds') | Out-Null; Copy-Item (Join-Path $sounds '*') (Join-Path $dist 'sounds') -Force }

# Çevrimdışı ses tanıma (Vosk + Türkçe model) exe'nin yanına
$vendor = Join-Path $root 'vendor\vosk'
if ((Test-Path $vendor) -and -not (Test-Path (Join-Path $dist 'vosk\model-tr\final.mdl'))) {
    Copy-Item $vendor (Join-Path $dist 'vosk') -Recurse -Force
}
# Doğal Türkçe konuşma sesi (Piper + dfki modeli)
$pvendor = Join-Path $root 'vendor\piper'
if ((Test-Path $pvendor) -and -not (Test-Path (Join-Path $dist 'piper\tr_TR-dfki-medium.onnx'))) {
    Copy-Item $pvendor (Join-Path $dist 'piper') -Recurse -Force
}
# Komutu yazıya dökmek için Whisper (whisper.cpp + large-v3-turbo modeli, açık tutulan sunucu); eksik dosyalar tek tek kopyalanır
$wvendor = Join-Path $root 'vendor\whisper'
if (Test-Path $wvendor) {
    $wdist = Join-Path $dist 'whisper'
    New-Item -ItemType Directory -Force $wdist | Out-Null
    Get-ChildItem $wvendor -File | ForEach-Object {
        $target = Join-Path $wdist $_.Name
        if (-not (Test-Path $target) -or (Get-Item $target).Length -ne $_.Length) { Copy-Item $_.FullName $target -Force }
    }
}
Write-Host "Tamam: $out"

# Kurulum: uygulama OneDrive dışındaki yerel bir klasörden çalışır (%LOCALAPPDATA%\Programs\Pardux).
# OneDrive klasörleri açılışta henüz eşitlenmemiş ya da "yalnızca çevrimiçi" olabildiği için Windows ile başlarken açılmıyordu;
# ayrıca 1 GB'lık ses modellerinin buluta eşitlenmesi pil ve internet harcıyordu. Yalnızca değişen dosyalar kopyalanır.
if (-not $env:PARDUX_NO_INSTALL) {
    $inst = Join-Path $env:LOCALAPPDATA 'Programs\Pardux'
    New-Item -ItemType Directory -Force $inst | Out-Null
    Get-Process Pardux -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($inst, 'OrdinalIgnoreCase') } | Stop-Process -Force
    Get-Process piper, whisper-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($inst, 'OrdinalIgnoreCase') } | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    robocopy $dist $inst /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Kurulum kopyalaması başarısız ($LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
    Write-Host "Kuruldu: $(Join-Path $inst 'Pardux.exe')"

    # Eski adla (Emo Asistan) yapılmış kurulum kapatılıp kaldırılır; ayarlar ve kişiler %APPDATA%'da durur, uygulama onları kendisi taşır
    $old = Join-Path $env:LOCALAPPDATA 'Programs\EmoAsistan'
    if (Test-Path $old) {
        Get-Process EmoAsistan, piper, whisper-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($old, 'OrdinalIgnoreCase') } | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Eski kurulum kaldirildi: $old"
    }
}
