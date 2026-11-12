using System.Net;
using System.Runtime.CompilerServices;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Testlerin internete çıkmasını engeller. Test derlemesi yüklenince, herhangi bir testten önce,
/// varsayılan vekil sunucu kapalı bir yerel porta (127.0.0.1:9) yönlendirilir.
/// </summary>
/// <remarks>
/// Varsayılan ayarlı her <see cref="HttpClient"/> (DI'daki toplayıcı istemcileri dahil) bu vekili kullanır.
/// Yerel adresler (127.0.0.1 üzerindeki test sunucusu) vekilden muaftır.
/// Güvenli çekici vekil kullanmaz; onun testleri yalnızca sahte işleyici veya sahte DNS çözümleyici ile çalışır
/// ve sahte çözümleyici gerçek bağlantı kurulan testlerde hiçbir zaman herkese açık bir adres döndürmez.
/// </remarks>
public static class NetworkGuard
{
    public static readonly Uri BlackholeProxy = new("http://127.0.0.1:9");

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Test derlemesi; koruma her testten önce kurulmalı.")]
    public static void Install()
    {
        HttpClient.DefaultProxy = new WebProxy(BlackholeProxy) { BypassProxyOnLocal = true };
    }
}
