using System.Reflection;
using Fraud.Collectors;
using Microsoft.Extensions.Logging;

namespace Fraud.Security.Tests.Injection;

/// <summary>
/// Kural 4 ve log enjeksiyonu: Log mesajlarına serbest metin (alan adı, sertifika alanı, sayfa metni) girmemeli.
/// Toplayıcı derlemesindeki bütün [LoggerMessage] metotlarının parametre türleri denetlenir.
/// </summary>
/// <remarks>
/// Yeni bir log metodu string parametre alırsa bu test kırmızı olur; parametre gözden geçirilip
/// aşağıdaki izin listesine gerekçesiyle eklenmelidir.
/// </remarks>
public sealed class LogSurfaceTests
{
    // Metot adı + parametre adı. Her birinin kaynağı sabit metin veya yapılandırmadır, çekilen içerik değil.
    private static readonly HashSet<string> ReviewedStringParameters = new(StringComparer.Ordinal)
    {
        "FileNotFound.path",       // yapılandırmadaki dosya yolu
        "Rejected.detail",         // sabit ret açıklaması veya enum adı
        "Conflict.detail",         // "farklı alan: ..." sabit sütun adları
        "RunCrashed.exceptionType", // yalnızca istisna türü adı
    };

    [Fact]
    public void Collector_log_methods_take_no_unreviewed_free_text()
    {
        var methods = typeof(CollectorServiceCollectionExtensions).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(methods);

        var unreviewed = methods
            .SelectMany(m => m.GetParameters()
                .Where(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(object) || p.ParameterType == typeof(Uri))
                .Select(p => $"{m.Name}.{p.Name}"))
            .Where(name => !ReviewedStringParameters.Contains(name))
            .ToList();

        Assert.Empty(unreviewed);
    }

    [Fact]
    public void Collector_log_methods_never_take_an_exception_object()
    {
        // İstisna nesnesi loglanırsa mesajı (istek adresi, yani alan adı) da yazılır.
        var methods = typeof(CollectorServiceCollectionExtensions).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<LoggerMessageAttribute>() is not null);

        Assert.DoesNotContain(methods, m => m.GetParameters().Any(p => typeof(Exception).IsAssignableFrom(p.ParameterType)));
    }
}
