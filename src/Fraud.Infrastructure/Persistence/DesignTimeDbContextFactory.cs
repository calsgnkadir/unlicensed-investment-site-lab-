using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fraud.Infrastructure.Persistence;

/// <summary>
/// Yalnızca "dotnet ef migrations add" için. Migration üretmek veritabanına bağlanmayı gerektirmez;
/// bu yüzden burada parolasız, sahte bir bağlantı metni yeterlidir.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FraudDbContext>
{
    public FraudDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FraudDbContext>()
            .UseNpgsql("Host=localhost;Database=fraud_lab_design")
            .Options;
        return new FraudDbContext(options);
    }
}
