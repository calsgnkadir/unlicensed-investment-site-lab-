# Derleme bağlamı depo köküdür: docker build -f src/Fraud.Collectors/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /src

# Önce yalnızca proje ve paket dosyaları kopyalanır; kod değişince paketler yeniden indirilmez.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Fraud.Domain/Fraud.Domain.csproj src/Fraud.Domain/packages.lock.json src/Fraud.Domain/
COPY src/Fraud.Infrastructure/Fraud.Infrastructure.csproj src/Fraud.Infrastructure/packages.lock.json src/Fraud.Infrastructure/
COPY src/Fraud.Collectors/Fraud.Collectors.csproj src/Fraud.Collectors/packages.lock.json src/Fraud.Collectors/
RUN dotnet restore src/Fraud.Collectors/Fraud.Collectors.csproj --locked-mode

COPY src/Fraud.Domain/ src/Fraud.Domain/
COPY src/Fraud.Infrastructure/ src/Fraud.Infrastructure/
COPY src/Fraud.Collectors/ src/Fraud.Collectors/
RUN dotnet publish src/Fraud.Collectors/Fraud.Collectors.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0.14-noble AS runtime
WORKDIR /app
COPY --from=build /app ./

# port-audit durum dosyasının klasörü (deploy/linux, port_audit_state birimi). Yeni adlandırılmış birim bu klasörün
# sahipliğini alır; böylece yetkisiz kullanıcı yazabilir. Başka komutlar bu klasörü kullanmaz.
RUN mkdir -p /var/lib/port-audit && chown "$APP_UID" /var/lib/port-audit && chmod 700 /var/lib/port-audit

# İmajdaki hazır, yetkisiz kullanıcı (root değil).
USER $APP_UID
ENTRYPOINT ["dotnet", "Fraud.Collectors.dll"]
CMD ["worker"]
