using System.Runtime.CompilerServices;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Depo kökündeki dosyaları bulur. Derleme çıktısı başka bir klasöre yazılsa bile (ör. --artifacts-path)
/// çalışsın diye kök, bu kaynak dosyanın derleme anındaki yolundan bulunur.
/// </summary>
public static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public static string[] ReadLines(string relativePath) => File.ReadAllLines(Path.Combine(Root, relativePath));

    private static string FindRoot([CallerFilePath] string sourcePath = "")
    {
        // Deterministik derlemede kaynak yolu "/_/..." olabilir; o zaman çıktı ve çalışma klasöründen yukarı aranır.
        foreach (var start in new[] { Path.GetDirectoryName(sourcePath), AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = start;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FraudLab.slnx")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException("Depo kökü (FraudLab.slnx) bulunamadı.");
    }
}
