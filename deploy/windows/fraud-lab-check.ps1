<#
.SYNOPSIS
    fraud-lab dış denetimi (THREAT_MODEL M3): WSL'deki status.sh --check komutunu çağırır, sorun varsa yerel bildirim gösterir.

.DESCRIPTION
    Prometheus uyarıları dışarıya gönderilmez (e-posta, webhook, dış servis yok). Bu betik Windows'ta 15 dakikada bir
    çalışır ve şunları yerel bir bildirime çevirir:
      0  temiz (bildirim yok; önceki sorun düzeldiyse bir kez "düzeldi" bildirimi)
      1  ateşlenen uyarı veya beklenmeyen yayımlanan Docker portu
      2  Prometheus'a ulaşılamıyor ya da Watchdog uyarısı yok (uyarı hattı kör)
      3  bir konteyner çalışmıyor veya sağlıksız
      WSL veya Ubuntu hiç cevap vermezse ya da komut çalıştırılamazsa da bildirim gösterilir.
    Aynı durum için bildirim saatte en fazla bir kez tekrarlanır. Durum dosyası: %LOCALAPPDATA%\fraud-lab\check-state.json.
    Bildirimde yalnızca başlık ve status.sh'in tek satırlık özeti (uyarı adları, port numaraları) bulunur; alan adı ve IP yoktur.

    Windows PowerShell 5.1 ile çalışır, harici modül kullanmaz. Bildirim Windows'un yerleşik
    Windows.UI.Notifications API'siyle gösterilir.

.PARAMETER Distro
    WSL dağıtımının adı (varsayılan Ubuntu). Yalnızca harf, rakam, nokta, tire ve alt çizgi.

.PARAMETER TimeoutSeconds
    WSL komutunun en uzun süresi. Aşılırsa süreç sonlandırılır ve "cevap vermedi" bildirimi gösterilir.

.PARAMETER NoToast
    Bildirim yerine konsola yazar (elle deneme için).

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\fraud-lab-check.ps1 -NoToast
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]
    [string]$Distro = 'Ubuntu',

    [ValidateRange(110, 900)]
    [int]$TimeoutSeconds = 120,

    [ValidateRange(1, 1440)]
    [int]$RepeatMinutes = 60,

    [switch]$NoToast
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Komut sabittir; parametreden gelmez (komut enjeksiyonu yok).
$CheckCommand = '~/fraud-lab/deploy/linux/status.sh --check'
# Linux tarafındaki süre sınırı (coreutils timeout). Windows tarafının sınırından kısa olmalı: Windows süreci
# kapatınca WSL içindeki status.sh ve çağırdığı docker komutları çalışmaya devam edebilirdi; timeout onları da bitirir.
$LinuxTimeoutSeconds = 100
$StateDir = Join-Path $env:LOCALAPPDATA 'fraud-lab'
$StateFile = Join-Path $StateDir 'check-state.json'
# Windows PowerShell'in kayıtlı uygulama kimliği; bildirim bu ad altında gösterilir (ayrı kayıt gerekmez).
$AppId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'

function Invoke-FraudLabCheck {
    # wsl.exe -d <dağıtım> --exec bash -lc '<komut>' ; çıkış kodu ve son satır döner.
    $wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
    if (-not (Test-Path -LiteralPath $wsl)) {
        return @{ Code = -2; Summary = 'wsl.exe bulunamadı' }
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $wsl
    $psi.Arguments = '-d ' + $Distro + ' --exec timeout ' + $LinuxTimeoutSeconds + ' bash -lc "' + $CheckCommand + '"'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8

    try {
        $process = [System.Diagnostics.Process]::Start($psi)
    }
    catch {
        return @{ Code = -2; Summary = 'WSL başlatılamadı' }
    }

    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $null = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch { }
            return @{ Code = -1; Summary = ('WSL veya Ubuntu ' + $TimeoutSeconds + ' saniyede cevap vermedi') }
        }

        $process.WaitForExit()
        $code = $process.ExitCode
        $lines = @($stdout.Result -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 })
        $last = ''
        if ($lines.Count -gt 0) { $last = $lines[$lines.Count - 1].Trim() }

        if ($code -ge 0 -and $code -le 3) {
            if ($last.Length -eq 0) { $last = 'özet yok' }
            return @{ Code = $code; Summary = $last }
        }

        if ($code -eq 124) {
            return @{ Code = -1; Summary = ('Ubuntu''da denetim ' + $LinuxTimeoutSeconds + ' saniyede bitmedi (Docker cevap vermiyor olabilir)') }
        }

        # wsl.exe'nin kendi hata kodları (dağıtım yok, WSL kapalı ...) veya betik bulunamadı (127).
        return @{ Code = -3; Summary = ('Ubuntu''da denetim çalıştırılamadı (çıkış kodu ' + $code + ')') }
    }
    finally {
        $process.Dispose()
    }
}

function Get-Title([int]$Code) {
    switch ($Code) {
        0 { return 'fraud-lab: sorun giderildi' }
        1 { return 'fraud-lab: alarm' }
        2 { return 'fraud-lab: uyarı hattı çalışmıyor' }
        3 { return 'fraud-lab: konteyner sorunu' }
        default { return 'fraud-lab: WSL cevap vermiyor' }
    }
}

function Get-CleanText([string]$Text, [int]$Max) {
    # Kontrol karakterleri atılır, uzunluk sınırlanır; XML için ayrıca kaçırılır.
    $clean = ($Text -replace '[\x00-\x1F\x7F]', ' ').Trim()
    if ($clean.Length -gt $Max) { $clean = $clean.Substring(0, $Max) + '...' }
    return [System.Security.SecurityElement]::Escape($clean)
}

function Show-Notification([string]$Title, [string]$Body) {
    if ($NoToast) {
        Write-Output ($Title + ' | ' + $Body)
        return
    }

    $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]

    $xmlText = '<toast><visual><binding template="ToastGeneric">' +
        '<text>' + (Get-CleanText $Title 100) + '</text>' +
        '<text>' + (Get-CleanText $Body 250) + '</text>' +
        '</binding></visual></toast>'
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($xmlText)
    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($AppId).Show($toast)
}

function Read-State {
    if (-not (Test-Path -LiteralPath $StateFile)) { return $null }
    try {
        $state = Get-Content -LiteralPath $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $state -or -not ($state.PSObject.Properties.Name -contains 'Signature') -or
            -not ($state.PSObject.Properties.Name -contains 'NotifiedUtc')) {
            return $null
        }
        return $state
    }
    catch {
        return $null
    }
}

function Write-State([string]$Signature, [datetime]$NotifiedUtc) {
    if (-not (Test-Path -LiteralPath $StateDir)) {
        $null = New-Item -ItemType Directory -Path $StateDir
    }

    $json = @{ Signature = $Signature; NotifiedUtc = $NotifiedUtc.ToString('o') } | ConvertTo-Json
    Set-Content -LiteralPath $StateFile -Value $json -Encoding UTF8
}

$result = Invoke-FraudLabCheck
$now = [datetime]::UtcNow
$previous = Read-State

if ($result.Code -eq 0) {
    # Sorun düzeldiyse bir kez haber ver; sonra durum "ok" olarak kalır.
    if ($null -ne $previous -and $previous.Signature -ne 'ok') {
        Show-Notification (Get-Title 0) $result.Summary
    }
    if ($null -eq $previous -or $previous.Signature -ne 'ok') {
        Write-State 'ok' $now
    }
    exit 0
}

$signature = [string]$result.Code + '|' + $result.Summary
$repeatDue = $true
if ($null -ne $previous -and $previous.Signature -eq $signature) {
    $last = [datetime]::MinValue
    if ([datetime]::TryParse([string]$previous.NotifiedUtc, [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$last)) {
        $repeatDue = ($now - $last.ToUniversalTime()).TotalMinutes -ge $RepeatMinutes
    }
}

if ($repeatDue) {
    Show-Notification (Get-Title $result.Code) $result.Summary
    Write-State $signature $now
}

if ($result.Code -lt 0) { exit 4 }
exit $result.Code
