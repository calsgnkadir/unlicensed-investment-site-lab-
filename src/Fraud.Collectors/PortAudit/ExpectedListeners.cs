using System.Globalization;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.PortAudit;

/// <summary>Beklenen dinleyicinin hangi adrese bağlı olabileceği.</summary>
public enum ExpectedScope
{
    /// <summary>Kısıt yok (joker adres dahil).</summary>
    Any = 1,

    /// <summary>Yalnızca loopback (<c>@loopback</c>). Ör. Grafana: 127.0.0.1:53000.</summary>
    Loopback = 2,

    /// <summary>Loopback veya belirli bir adres, joker değil (<c>@local</c>). Ör. WSL DNS tüneli, izleme köprüsü.</summary>
    Local = 3,
}

/// <summary>Beklenen listedeki bir satır: <c>tcp/53000@loopback</c>, <c>udp/53</c>, <c>tcp/32768-60999@loopback</c>.</summary>
public sealed record ExpectedListener(ListenerProtocol Protocol, int FromPort, int ToPort, ExpectedScope Scope)
{
    public bool IsRange => FromPort != ToPort;

    public bool Matches(Listener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return listener.Protocol == Protocol
               && listener.Port >= FromPort && listener.Port <= ToPort
               && Scope switch
               {
                   ExpectedScope.Loopback => listener.Scope == BindScope.Loopback,
                   ExpectedScope.Local => listener.Scope != BindScope.Wildcard,
                   _ => true,
               };
    }

    public override string ToString()
    {
        var c = CultureInfo.InvariantCulture;
        var ports = IsRange ? $"{FromPort.ToString(c)}-{ToPort.ToString(c)}" : FromPort.ToString(c);
        var scope = Scope switch
        {
            ExpectedScope.Loopback => "@loopback",
            ExpectedScope.Local => "@local",
            _ => string.Empty,
        };
        return $"{(Protocol == ListenerProtocol.Tcp ? "tcp" : "udp")}/{ports}{scope}";
    }
}

/// <summary>Beklenmeyen dinleyici (aynı protokol, port ve kapsam bir kez sayılır).</summary>
public sealed record UnexpectedListener(ListenerProtocol Protocol, int Port, BindScope Scope);

/// <summary>Bir denetimin sonucu.</summary>
public sealed record PortAuditResult(
    IReadOnlyList<Listener> Listeners,
    IReadOnlyList<UnexpectedListener> Unexpected,
    IReadOnlyList<ExpectedListener> Missing,
    int MalformedLines);

/// <summary>Beklenen liste dosyasını okur ve gerçek durumla karşılaştırır.</summary>
/// <remarks>
/// <para>Dosya biçimi: Satır başına bir veya virgül/boşlukla ayrılmış birkaç kayıt; <c>#</c> sonrası yorumdur.</para>
/// <list type="bullet">
/// <item><c>tcp/22</c>: TCP 22 her adreste beklenir.</item>
/// <item><c>tcp/53000@loopback</c>: Yalnızca 127.0.0.1 veya ::1 üzerinde. 0.0.0.0'da açılırsa "beklenmeyen" sayılır.</item>
/// <item><c>udp/53@local</c>: Joker adres (0.0.0.0) dışında.</item>
/// <item><c>tcp/32768-60999@loopback</c>: Aralık. Aralıklar "eksik" sayılmaz (hepsinin açık olması beklenmez).</item>
/// </list>
/// <para>
/// Karşılaştırma: Her gerçek dinleyici en az bir kayıtla eşleşmeli; eşleşmeyen "beklenmeyen"dir. Tek portlu her kayıt
/// için en az bir eşleşen dinleyici olmalı; olmayan "eksik"tir (ör. Grafana durmuş). Eksik, uyarı değil bilgi olarak gösterilir.
/// </para>
/// </remarks>
public static partial class ExpectedListeners
{
    /// <summary>Dosya satırlarını çözer. Hatalı kayıtta satır numarasıyla istisna fırlatır (açılışta durmak için).</summary>
    public static IReadOnlyList<ExpectedListener> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<ExpectedListener>();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var hash = raw.IndexOf('#', StringComparison.Ordinal);
            var line = (hash >= 0 ? raw[..hash] : raw).Trim();
            foreach (var token in line.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(ParseEntry(token)
                    ?? throw new FormatException($"Beklenen port listesi, satır {number}: kayıt 'tcp/PORT', 'udp/PORT', " +
                                                 "'tcp/A-B' biçiminde olmalı; isteğe bağlı '@loopback' veya '@local'."));
            }
        }

        return result;
    }

    /// <summary>Tek kaydı çözer; geçersizse <c>null</c>.</summary>
    public static ExpectedListener? ParseEntry(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var m = Entry().Match(token.Trim().ToLowerInvariant());
        if (!m.Success
            || !int.TryParse(m.Groups["from"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || from is < 1 or > 65535)
        {
            return null;
        }

        var to = from;
        if (m.Groups["to"].Success
            && (!int.TryParse(m.Groups["to"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out to) || to < from || to > 65535))
        {
            return null;
        }

        var protocol = m.Groups["proto"].Value == "tcp" ? ListenerProtocol.Tcp : ListenerProtocol.Udp;
        var scope = m.Groups["scope"].Value switch
        {
            "loopback" => ExpectedScope.Loopback,
            "local" => ExpectedScope.Local,
            _ => ExpectedScope.Any,
        };
        return new ExpectedListener(protocol, from, to, scope);
    }

    /// <summary>Gerçek dinleyicileri beklenen listeyle karşılaştırır.</summary>
    public static PortAuditResult Compare(IReadOnlyList<Listener> actual, IReadOnlyList<ExpectedListener> expected, int malformedLines = 0)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        var unexpected = actual
            .Where(l => !expected.Any(e => e.Matches(l)))
            .Select(l => new UnexpectedListener(l.Protocol, l.Port, l.Scope))
            .Distinct()
            .OrderBy(u => u.Protocol).ThenBy(u => u.Port).ThenBy(u => u.Scope)
            .ToList();

        var missing = expected
            .Where(e => !e.IsRange && !actual.Any(e.Matches))
            .Distinct()
            .ToList();

        return new PortAuditResult(actual, unexpected, missing, malformedLines);
    }

    [GeneratedRegex(@"^(?<proto>tcp|udp)/(?<from>[0-9]{1,5})(-(?<to>[0-9]{1,5}))?(@(?<scope>loopback|local))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Entry();
}
