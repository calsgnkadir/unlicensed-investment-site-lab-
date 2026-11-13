<#
.SYNOPSIS
    fraud-lab-check.ps1'i kullanıcıya özel bir klasöre kopyalar, izinlerini daraltır ve 15 dakikada bir çalışan
    zamanlanmış görevi kurar (THREAT_MODEL M5). -Uninstall ile görevi ve kopyayı kaldırır.

.DESCRIPTION
    Neden kopya: Depo klasörü (C:\dev\fraud-lab) C:\ kökünden "Authenticated Users: Değiştirme" iznini devralır;
    makinedeki başka bir yerel hesap depodaki betiği değiştirip, görev aracılığıyla sizin hesabınızla kod çalıştırabilirdi.
    Ayrıca depoya giren her değişiklik (git pull) gözden geçirilmeden zamanlanmış olarak çalışırdı.

    Bu betik:
      1. fraud-lab-check.ps1'i %LOCALAPPDATA%\fraud-lab\ altına kopyalar.
      2. Klasörün ve kopyanın devralınan izinlerini kaldırır (icacls /inheritance:r) ve yalnızca sizin hesabınıza,
         SYSTEM'e ve Administrators grubuna izin verir. Gruplar SID ile yazılır (Türkçe Windows'ta ad farklıdır).
      3. İzinleri geri okuyup doğrular; başka bir hesap kalmışsa durur.
      4. Görevi bu kopyadan çalışacak şekilde kurar (sizin hesabınız, oturum açıkken, yönetici yetkisi OLMADAN).
    Depo güncellenince kopya değişmez. Yeni sürümü kullanmak için değişikliği gözden geçirin ve bu betiği yeniden
    çalıştırın; kopyalama yalnızca o zaman olur.

    Windows PowerShell 5.1 ile çalışır. Harici modül kullanmaz; Windows'un kendi ScheduledTasks komutlarını kullanır.
    Yönetici yetkisi gerekmez.

.PARAMETER Uninstall
    Görevi siler, kopyalanan betiği ve durum dosyasını kaldırır.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\install-check-task.ps1
.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\install-check-task.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$TaskName = 'fraud-lab-check'
$Source = Join-Path $PSScriptRoot 'fraud-lab-check.ps1'
$TargetDir = Join-Path $env:LOCALAPPDATA 'fraud-lab'
$Target = Join-Path $TargetDir 'fraud-lab-check.ps1'

$UserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$UserName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$SystemSid = 'S-1-5-18'
$AdministratorsSid = 'S-1-5-32-544'
$AllowedSids = @($UserSid, $SystemSid, $AdministratorsSid)

function Set-PrivateAcl([string]$Path, [bool]$IsDirectory) {
    # Devralınan izinler kaldırılır (/inheritance:r), açık izinler yalnızca üç hesaba verilir (/grant:r eskisini değiştirir).
    $inherit = ''
    if ($IsDirectory) { $inherit = '(OI)(CI)' }
    $grants = @(
        ('*' + $UserSid + ':' + $inherit + '(F)'),
        ('*' + $SystemSid + ':' + $inherit + '(F)'),
        ('*' + $AdministratorsSid + ':' + $inherit + '(F)')
    )
    $output = & icacls.exe $Path /inheritance:r /grant:r $grants 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ('icacls başarısız (' + $LASTEXITCODE + '): ' + ($output -join ' '))
    }
}

function Assert-PrivateAcl([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw ($Path + ': izinler hâlâ devralınıyor.')
    }

    foreach ($rule in $acl.Access) {
        $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
        if ($AllowedSids -notcontains $sid) {
            throw ($Path + ': beklenmeyen izin: ' + $rule.IdentityReference.Value)
        }
    }
}

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Output ('Görev silindi: ' + $TaskName)
    }
    else {
        Write-Output ('Görev yok: ' + $TaskName)
    }

    if (Test-Path -LiteralPath $TargetDir) {
        Remove-Item -LiteralPath $TargetDir -Recurse -Force
        Write-Output ('Silindi: ' + $TargetDir)
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $Source)) {
    throw ('Kaynak betik yok: ' + $Source)
}

# 1) Klasör: yoksa oluştur, izinlerini daralt.
if (-not (Test-Path -LiteralPath $TargetDir)) {
    $null = New-Item -ItemType Directory -Path $TargetDir
}
Set-PrivateAcl $TargetDir $true
Assert-PrivateAcl $TargetDir

# 2) Kopya: önce kopyala, sonra izinleri daralt ve doğrula. Klasör zaten özel olduğu için ara durumda da açık değil.
Copy-Item -LiteralPath $Source -Destination $Target -Force
Set-PrivateAcl $Target $false
Assert-PrivateAcl $Target

$sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
$targetHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) {
    throw 'Kopya kaynakla aynı değil.'
}

# 3) Görev: bu kullanıcı, oturum açıkken (bildirim için gerekli), yönetici yetkisi olmadan; 15 dakikada bir.
$argument = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Target + '"'
$action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Argument $argument
$trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) `
    -RepetitionInterval (New-TimeSpan -Minutes 15) -RepetitionDuration (New-TimeSpan -Days 3650)
$principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
$null = Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'fraud-lab: status.sh --check, sorun varsa yerel bildirim (deploy/windows).' -Force

Write-Output ('Görev kuruldu: ' + $TaskName + ' (15 dakikada bir)')
Write-Output ('Çalışan betik: ' + $Target)
Write-Output ('SHA256: ' + $targetHash)
Write-Output 'Depo güncellenince kopya değişmez; yeni sürüm için değişikliği gözden geçirip bu betiği yeniden çalıştırın.'
