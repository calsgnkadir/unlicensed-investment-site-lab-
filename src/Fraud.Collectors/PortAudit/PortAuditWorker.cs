using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.PortAudit;

/// <summary>
/// <c>port-audit</c> komutu: Belirli aralıklarla denetler, sonucu metriğe ve (ayarlıysa) durum dosyasına yazar.
/// Sonuç değişince log satırı basar (yalnızca sayı, protokol, port ve kapsam; adres yok).
/// </summary>
/// <remarks>
/// Linux kurulumunda bu süreç host ağındadır ama hiçbir port dinlemez (THREAT_MODEL M1): Metrikleri HTTP ile sunmak
/// yerine özeti <see cref="PortAuditOptions.StateFile"/>'a yazar; iç ağdaki <see cref="PortAuditExporterWorker"/> sunar.
/// </remarks>
public sealed partial class PortAuditWorker(
    PortAuditor auditor,
    PortAuditOptions options,
    PortAuditMetrics metrics,
    TimeProvider timeProvider,
    ILogger<PortAuditWorker> logger) : BackgroundService
{
    private string? _lastSignature;

    /// <summary>Bir denetim. Başarılıysa sonucu, hata varsa <c>null</c> döner (son denetim zamanı güncellenmez).</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Okuma hatası denetimi durdurmamalı; hata sayılır, zaman güncellenmez ve PortAuditStale uyarısı çıkar.")]
    public PortAuditResult? RunOnce()
    {
        MeasureDisk();
        PortAuditResult? result = null;
        try
        {
            var expected = auditor.LoadExpected();
            result = auditor.Run(expected);
            metrics.Completed(result, expected.Count, timeProvider.GetUtcNow());
            ReportIfChanged(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            metrics.Failed();
            Log.AuditFailed(logger, ex.HResult);
            _lastSignature = null;
            result = null;
        }

        WriteState();
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RunOnce();
            try
            {
                await Task.Delay(options.Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Özeti durum dosyasına yazar. Yazılamazsa dosyadaki zaman eskir ve PortAuditStale çıkar.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Durum dosyası yazılamaması denetimi durdurmamalı; yalnızca HResult loglanır (yol yazılmaz).")]
    private void WriteState()
    {
        if (string.IsNullOrWhiteSpace(options.StateFile) || metrics.Snapshot is not { } snapshot)
        {
            return;
        }

        try
        {
            PortAuditStateFile.Write(options.StateFile, snapshot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.StateWriteFailed(logger, ex.HResult);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Disk ölçümü denetimi durdurmamalı; ölçülemezse gösterge kaldırılır (DiskLow yanlış 'iyi' demez, panelde boş görünür).")]
    private void MeasureDisk()
    {
        if (string.IsNullOrWhiteSpace(options.DiskPath))
        {
            return;
        }

        try
        {
            var drive = new DriveInfo(options.DiskPath);
            metrics.Disk(drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            metrics.Disk(null, null);
        }
    }

    private void ReportIfChanged(PortAuditResult result)
    {
        var signature = string.Join(',', result.Unexpected.Select(u => $"{u.Protocol}/{u.Port}/{u.Scope}"))
                        + "|" + string.Join(',', result.Missing.Select(m => m.ToString()));
        if (signature == _lastSignature)
        {
            return;
        }

        _lastSignature = signature;
        Log.Summary(logger, result.Listeners.Count, result.Unexpected.Count, result.Missing.Count);
        foreach (var u in result.Unexpected)
        {
            Log.Unexpected(logger, u.Protocol, u.Port, u.Scope);
        }

        foreach (var m in result.Missing)
        {
            Log.Missing(logger, m.Protocol, m.FromPort);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 950, Level = LogLevel.Information,
            Message = "Port denetimi: {Listeners} dinleyici, {Unexpected} beklenmeyen, {Missing} eksik.")]
        public static partial void Summary(ILogger logger, int listeners, int unexpected, int missing);

        [LoggerMessage(EventId = 951, Level = LogLevel.Warning, Message = "Beklenmeyen dinleyici: {Protocol} {Port} ({Scope}).")]
        public static partial void Unexpected(ILogger logger, ListenerProtocol protocol, int port, BindScope scope);

        [LoggerMessage(EventId = 952, Level = LogLevel.Information, Message = "Beklenen dinleyici yok: {Protocol} {Port}.")]
        public static partial void Missing(ILogger logger, ListenerProtocol protocol, int port);

        [LoggerMessage(EventId = 953, Level = LogLevel.Error, Message = "Port denetimi tamamlanamadı (HResult {HResult}).")]
        public static partial void AuditFailed(ILogger logger, int hresult);

        [LoggerMessage(EventId = 954, Level = LogLevel.Error, Message = "Port denetimi durum dosyası yazılamadı (HResult {HResult}).")]
        public static partial void StateWriteFailed(ILogger logger, int hresult);
    }
}

/// <summary>
/// <c>port-audit-exporter</c> komutu: port-audit'in durum dosyasını okur ve metrikleri iç ağda (9464) sunar.
/// /proc okumaz, host ağında değildir, beklenen listeyi görmez.
/// </summary>
public sealed partial class PortAuditExporterWorker(
    PortAuditOptions options,
    PortAuditMetrics metrics,
    TimeProvider timeProvider,
    ILogger<PortAuditExporterWorker> logger) : BackgroundService
{
    /// <summary>Dosyayı bir kez okur. Dosya yoksa (port-audit henüz yazmadı) hiçbir şey değişmez.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bozuk dosya sunucuyu durdurmamalı; hata sayılır, eski değer kalır ve zamanı eskiyince PortAuditStale çıkar.")]
    public bool RefreshOnce()
    {
        try
        {
            if (PortAuditStateFile.Read(options.StateFile!, timeProvider.GetUtcNow()) is not { } snapshot)
            {
                return false;
            }

            metrics.Apply(snapshot);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            metrics.StateReadFailed();
            Log.ReadFailed(logger, ex.HResult);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RefreshOnce();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 960, Level = LogLevel.Warning, Message = "Port denetimi durum dosyası okunamadı (HResult {HResult}).")]
        public static partial void ReadFailed(ILogger logger, int hresult);
    }
}
