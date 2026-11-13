using System.Buffers.Binary;
using Fraud.Collectors.Http;
using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>Bir DNS sorgusunun yorumlanmış sonucu.</summary>
/// <param name="Rcode">Geçerli cevaptaki RCODE; geçerli cevap yoksa boş.</param>
/// <param name="Answers">Saklanacak cevap kayıtları (yalnızca sorduğumuz ad ve onun CNAME zinciri), cevaptaki sırayla.</param>
/// <param name="NegativeTtl">NXDOMAIN veya boş cevapta SOA'dan hesaplanan olumsuz önbellek süresi.</param>
/// <param name="Truncated">UDP cevabı kesikti ve TCP'ye geçildi.</param>
/// <param name="Attempts">Gönderilen paket sayısı.</param>
public sealed record DnsQueryResult(
    DnsOutcome Outcome,
    DnsErrorKind ErrorKind,
    int? Rcode,
    IReadOnlyList<DnsAnswer> Answers,
    long? NegativeTtl,
    bool Truncated,
    int Attempts,
    TimeSpan Latency);

public interface IDnsQueryClient
{
    /// <summary>
    /// Bir adı bir çözücüye, seçilen yoldan sorar. Ağ ve biçim hataları sonuç olarak döner, istisna olarak değil.
    /// Yalnızca dışarıdan iptal edilirse <see cref="OperationCanceledException"/> fırlatır.
    /// </summary>
    Task<DnsQueryResult> QueryAsync(
        DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken);
}

/// <summary>
/// Bir sorgunun baştan sona akışı: paketi kur, hız sınırını bekle, gönder, ayrıştır, doğrula, gerekirse tekrar dene
/// veya TCP'ye geç, sonucu yorumla.
/// </summary>
/// <remarks>
/// <para>
/// Hız sınırı: Bütün taşıma yolları aynı <see cref="RequestPacer"/>'ı paylaşır. Her paket (tekrar deneme ve TCP'ye
/// geçiş dahil) göndermeden önce sırasını bekler. Varsayılan: saniyede en fazla 1 paket.
/// </para>
/// <para>
/// Tekrar deneme: Yalnızca zaman aşımı ve ağ hatasında, en fazla <c>maxRetries</c> kez (varsayılan 1).
/// Her denemede yeni işlem kimliği üretilir. SERVFAIL gibi cevap kodları tekrar denenmez: Onlar da bir cevaptır.
/// </para>
/// </remarks>
public sealed class DnsQueryClient : IDnsQueryClient
{
    /// <summary>
    /// İzlenecek en uzun CNAME zinciri (halka sayısı). Daha uzun zincir veya döngü <c>Error:Malformed</c> olur.
    /// RFC bir sayı koymaz; gerçek zincirler birkaç halkadır.
    /// </summary>
    public const int MaxCnameChain = 16;

    private readonly Dictionary<DnsTransport, IDnsTransport> _transports;
    private readonly RequestPacer _pacer;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxRetries;

    public DnsQueryClient(IEnumerable<IDnsTransport> transports, RequestPacer pacer, TimeProvider timeProvider, int maxRetries)
    {
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        _transports = transports.ToDictionary(t => t.Kind);
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _maxRetries = maxRetries;
    }

    public async Task<DnsQueryResult> QueryAsync(
        DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var started = _timeProvider.GetTimestamp();

        var outcome = await SendWithRetryAsync(resolver, transport, name, type, cancellationToken).ConfigureAwait(false);
        var attempts = outcome.Attempts;
        var truncated = false;

        // TC biti: Cevap 512 bayta sığmadı, elimizdeki kısmi. RFC 1035 Bölüm 4.2.1 ve RFC 7766 Bölüm 5:
        // Aynı soru TCP ile yeniden sorulur. Kesik cevabın içindeki kayıtlar hiç kullanılmaz; paket ortasından
        // kesilmiş ve ayrıştırılamıyor olsa bile (bkz. SendWithRetryAsync) doğrudan TCP'ye geçilir.
        if (transport == DnsTransport.Udp && outcome.Truncated)
        {
            truncated = true;
            outcome = await SendWithRetryAsync(resolver, DnsTransport.Tcp, name, type, cancellationToken).ConfigureAwait(false);
            attempts += outcome.Attempts;

            // TCP cevabında TC biti anlamsızdır (sığmama sorunu yok); böyle bir cevap tutarsızdır.
            if (outcome.Truncated)
            {
                return Failed(DnsErrorKind.Malformed, rcode: null, truncated, attempts, started);
            }
        }
        else if (outcome.Truncated)
        {
            // TCP veya DoH cevabında TC biti: tutarsız.
            return Failed(DnsErrorKind.Malformed, rcode: null, truncated, attempts, started);
        }

        if (outcome.Message is null)
        {
            return Failed(outcome.Error, rcode: null, truncated, attempts, started);
        }

        return Interpret(outcome.Query!, outcome.Message, truncated, attempts, started);
    }

    /// <summary>Cevap kodunu ve kayıtları sonuca çevirir.</summary>
    private DnsQueryResult Interpret(DnsQuery query, DnsMessage message, bool truncated, int attempts, long started)
    {
        var rcode = message.Header.Rcode;
        if (rcode is not ((int)DnsRcode.NoError or (int)DnsRcode.NxDomain))
        {
            var kind = (DnsRcode)rcode switch
            {
                DnsRcode.ServFail => DnsErrorKind.ServerFailure,
                DnsRcode.Refused => DnsErrorKind.Refused,
                _ => DnsErrorKind.OtherRcode,
            };
            return Failed(kind, rcode, truncated, attempts, started);
        }

        IReadOnlyList<DnsAnswer> answers;
        try
        {
            answers = SelectAnswers(query, message.Answers);
        }
        catch (DnsFormatException)
        {
            // CNAME döngüsü, çatallanan veya çok uzun zincir.
            return Failed(DnsErrorKind.Malformed, rcode, truncated, attempts, started);
        }

        if (rcode == (int)DnsRcode.NxDomain)
        {
            // NXDOMAIN cevabında da CNAME zinciri olabilir: Ad bir CNAME, zincirin sonundaki ad yok (RFC 6604).
            return new DnsQueryResult(DnsOutcome.NxDomain, DnsErrorKind.None, rcode, answers, NegativeTtl(message),
                truncated, attempts, Elapsed(started));
        }

        if (answers.Count > 0)
        {
            return new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, rcode, answers, null, truncated, attempts, Elapsed(started));
        }

        // Cevap bölümü dolu ama hiçbir kayıt sorumuza ait değil (başka ad, başka tür): Bu "kayıt yok" değil,
        // tutarsız bir cevaptır (THREAT_MODEL N3). Hata olarak yazılır.
        if (message.Answers.Count > 0)
        {
            return Failed(DnsErrorKind.UnrelatedAnswer, rcode, truncated, attempts, started);
        }

        // NOERROR ve cevap bölümü gerçekten boş (NODATA, RFC 2308 Bölüm 2.2): Ad var, bu türde kaydı yok.
        return new DnsQueryResult(DnsOutcome.Empty, DnsErrorKind.None, rcode, [], NegativeTtl(message), truncated, attempts, Elapsed(started));
    }

    /// <summary>
    /// Cevap bölümünden yalnızca bizim sorumuza ait kayıtları seçer: sorulan ad ve CNAME ile ondan varılan adlar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Çözücü cevap bölümüne sorulmayan bir ad için kayıt da koyabilir (eski önbellek zehirleme yöntemi: "bailiwick"
    /// dışı kayıt). Böyle kayıtlar saklanmaz. Sıra korunur. Yalnızca sınıf IN ve tür olarak sorulan tür veya CNAME alınır.
    /// </para>
    /// <para>
    /// Zincir halka halka izlenir: sorulan ad -> CNAME hedefi -> onun CNAME hedefi ... Kayıtlar sırasız gelebilir; her
    /// adımda bütün liste taranır. Döngü (bir ada ikinci kez varmak), aynı ad için iki farklı CNAME (RFC 2181 Bölüm 10.1:
    /// bir ad için tek CNAME olabilir) veya <see cref="MaxCnameChain"/>'den uzun zincir <see cref="DnsFormatException"/> verir.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DnsAnswer> SelectAnswers(DnsQuery query, IReadOnlyList<DnsResourceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(records);

        // 1) Zinciri kur.
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { query.Name };
        var current = query.Name;
        var links = 0;
        while (true)
        {
            var targets = records
                .Where(r => r.Type == (int)DnsRecordType.CNAME && r.Class == DnsWire.ClassIn && r.Value is not null
                            && string.Equals(r.Name, current, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
            {
                break; // zincirin sonu
            }

            if (targets.Count > 1)
            {
                throw new DnsFormatException("Aynı ad için birden fazla CNAME.");
            }

            links++;
            if (links > MaxCnameChain)
            {
                throw new DnsFormatException("CNAME zinciri çok uzun.");
            }

            if (!owners.Add(targets[0]))
            {
                throw new DnsFormatException("CNAME döngüsü.");
            }

            current = targets[0];
        }

        // 2) Zincirdeki adlara ait, sorduğumuz türde veya CNAME kayıtlarını sırayla al.
        var selected = new List<DnsAnswer>();
        foreach (var r in records)
        {
            if (r.Class == DnsWire.ClassIn && owners.Contains(r.Name)
                && (r.Type == (int)query.Type || r.Type == (int)DnsRecordType.CNAME))
            {
                selected.Add(new DnsAnswer(TypeName(r.Type), r.Value, r.Ttl));
            }
        }

        return selected;
    }

    /// <summary>
    /// RFC 2308 Bölüm 5: Olumsuz cevabın önbellek süresi, yetki bölümündeki SOA kaydının TTL'i ile
    /// SOA'nın MINIMUM alanından küçük olanıdır. SOA yoksa boş.
    /// </summary>
    public static long? NegativeTtl(DnsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var soa = message.Authority.FirstOrDefault(r => r.Type == (int)DnsRecordType.SOA && r.Class == DnsWire.ClassIn);
        return soa?.SoaMinimum is { } minimum ? Math.Min(soa.Ttl, minimum) : null;
    }

    /// <summary>Tür numarasını ada çevirir. Bilinmeyen tür RFC 3597 Bölüm 5'teki gibi <c>TYPE123</c> yazılır.</summary>
    public static string TypeName(int type) => Enum.IsDefined((DnsRecordType)type)
        ? ((DnsRecordType)type).ToString()
        : "TYPE" + type.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Ayrıştırılamayan bir UDP paketi yine de "kesik cevap" mı? Başlığın ilk 4 baytına bakar: QR = 1, OPCODE = 0 ve
    /// TC = 1 ise sunucu "sığmadı, TCP ile sor" demiştir. Kimlik UDP taşımasında zaten denetlendi. Paketin geri kalanına
    /// güvenmeyiz; içindeki hiçbir kayıt kullanılmaz.
    /// </summary>
    private static bool LooksTruncated(byte[] packet)
    {
        if (packet.Length < 4)
        {
            return false;
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2));
        return (flags & DnsWire.FlagQr) != 0 && (flags & DnsWire.OpcodeMask) == 0 && (flags & DnsWire.FlagTc) != 0;
    }

    /// <summary>Bir yoldan gönderir; zaman aşımı ve ağ hatasında yeni kimlikle tekrar dener.</summary>
    private async Task<SendOutcome> SendWithRetryAsync(
        DnsResolver resolver, DnsTransport kind, string name, DnsRecordType type, CancellationToken cancellationToken)
    {
        var transport = _transports.TryGetValue(kind, out var t)
            ? t
            : throw new InvalidOperationException(kind + " taşıması kayıtlı değil.");

        var attempts = 0;
        var lastError = DnsErrorKind.None;
        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            // DoH'ta kimlik 0 (RFC 8484 Bölüm 4.1); UDP ve TCP'de her denemede yeni rastgele kimlik.
            var id = kind == DnsTransport.Doh ? (ushort)0 : DnsQueryBuilder.NewId();
            var query = DnsQueryBuilder.Build(id, name, type);

            await _pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
            attempts++;
            var exchange = await transport.ExchangeAsync(resolver, query, cancellationToken).ConfigureAwait(false);

            if (exchange.Response is null)
            {
                lastError = exchange.Error;
                if (exchange.Error is DnsErrorKind.Timeout or DnsErrorKind.NetworkError)
                {
                    continue; // geçici hata: varsa bir hak daha
                }

                return SendOutcome.Failed(exchange.Error, attempts);
            }

            DnsMessage message;
            try
            {
                message = DnsMessageParser.Parse(exchange.Response);
            }
            catch (DnsFormatException)
            {
                // Ortasından kesilmiş ama TC bitli UDP paketi "bozuk" değil "kesik"tir: TCP'ye geçilecek.
                return kind == DnsTransport.Udp && LooksTruncated(exchange.Response)
                    ? new SendOutcome(null, null, DnsErrorKind.None, attempts, Truncated: true)
                    : SendOutcome.Failed(DnsErrorKind.Malformed, attempts);
            }

            var check = DnsResponseValidator.Validate(query, message);
            if (check != DnsErrorKind.None)
            {
                return SendOutcome.Failed(check, attempts);
            }

            return new SendOutcome(query, message, DnsErrorKind.None, attempts, message.Header.IsTruncated);
        }

        return SendOutcome.Failed(lastError, attempts);
    }

    private DnsQueryResult Failed(DnsErrorKind error, int? rcode, bool truncated, int attempts, long started) =>
        new(DnsOutcome.Error, error, rcode, [], null, truncated, attempts, Elapsed(started));

    private TimeSpan Elapsed(long started) => _timeProvider.GetElapsedTime(started);

    /// <param name="Truncated">Cevapta TC biti vardı (ayrıştırılabilsin ya da ayrıştırılamasın).</param>
    private sealed record SendOutcome(DnsQuery? Query, DnsMessage? Message, DnsErrorKind Error, int Attempts, bool Truncated)
    {
        public static SendOutcome Failed(DnsErrorKind error, int attempts) => new(null, null, error, attempts, false);
    }
}
