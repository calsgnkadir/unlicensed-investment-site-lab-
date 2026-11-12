using System.Net;
using System.Runtime.CompilerServices;

namespace Fraud.Integration.Tests.Infrastructure;

/// <summary>
/// Testlerin internete çıkmasını engeller. Test derlemesi yüklenir yüklenmez (herhangi bir testten önce)
/// varsayılan vekil sunucuyu kapalı bir yerel porta yönlendirir.
/// </summary>
/// <remarks>
/// Varsayılan ayarlarla oluşturulan her <see cref="HttpClient"/> (DI'daki toplayıcı istemcileri dahil) bu vekili kullanır.
/// Bir test yanlışlıkla gerçek crt.sh veya Wayback adresine istek atarsa istek 127.0.0.1:9'a gider ve
/// bağlantı reddedilir; hedefe ne DNS sorgusu ne bağlantı gider. Yerel adresler (Testcontainers) vekilden muaftır.
/// Güvenli çekici bilerek vekil kullanmaz; onun testleri sahte işleyici veya sahte DNS çözümleyici kullanır.
/// </remarks>
public static class NetworkGuard
{
    /// <summary>Discard portu; dinleyen olmadığı için bağlantı hemen reddedilir.</summary>
    public static readonly Uri BlackholeProxy = new("http://127.0.0.1:9");

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Test derlemesi; koruma her testten önce kurulmalı.")]
    public static void Install()
    {
        HttpClient.DefaultProxy = new WebProxy(BlackholeProxy) { BypassProxyOnLocal = true };
    }
}
