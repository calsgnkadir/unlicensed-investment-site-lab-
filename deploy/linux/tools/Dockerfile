# Araç servisi (compose profili "tools"): kontrol listesi ve ölçüm komutlarını depo kökünde çalıştırır.
# Taban imaj sürüm ve özetle sabit (manifest listesi özeti, 2026-11-20'de registry'den okundu).
FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5

# Konak kullanıcısıyla aynı kimlik: /repo altındaki dosyalar (ör. data/private, token dosyası 600) okunup yazılabilsin.
ARG TOOLS_UID=1000

# NuGet önbelleği ve derleme çıktısı birimleri bu klasörlere bağlanır. Docker yeni birime klasörün sahipliğini kopyalar;
# böylece birim kök değil TOOLS_UID'e ait olur. git imajda var (sdk imajı); safe.directory sistem ayarında: depo konakta
# başka bir kullanıcıya ait görünse bile git reddetmez. Yalnızca /repo için.
RUN mkdir -p /nuget /build /home/tools \
    && chown "${TOOLS_UID}:${TOOLS_UID}" /nuget /build /home/tools \
    && git config --system --add safe.directory /repo \
    && printf '%s\n' '#!/bin/sh' 'set -eu' \
       'dotnet build /repo/src/Fraud.Collectors/Fraud.Collectors.csproj -c Release --artifacts-path /build -p:RestoreLockedMode=true -nologo -v q >&2' \
       'exec dotnet /build/bin/Fraud.Collectors/release/Fraud.Collectors.dll "$@"' > /usr/local/bin/fraud-tools \
    && chmod 755 /usr/local/bin/fraud-tools

ENV NUGET_PACKAGES=/nuget \
    HOME=/home/tools \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    GIT_OPTIONAL_LOCKS=0

USER ${TOOLS_UID}
WORKDIR /repo
ENTRYPOINT ["/usr/local/bin/fraud-tools"]
