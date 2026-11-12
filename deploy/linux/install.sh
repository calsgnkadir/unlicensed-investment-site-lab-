#!/usr/bin/env bash
# Canlı Static CT akışını kurar ve başlatır. Tekrar çalıştırmak güvenlidir (idempotent):
# .env varsa dokunmaz, imaj değişmediyse yeniden derlemez, çalışan servisleri gereksiz yere yeniden başlatmaz.
#
# Kullanım: ./install.sh
# Docker veya compose eklentisini KURMAZ; eksikse ne yapılacağını yazar ve çıkar.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.ct.yml"
ENV_FILE="$SCRIPT_DIR/.env"
MIN_FREE_GB=20

info() { printf '[bilgi] %s\n' "$*"; }
warn() { printf '[uyarı] %s\n' "$*" >&2; }
fail() { printf '[hata] %s\n' "$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

# 1) Docker ve compose eklentisi
if ! command -v docker >/dev/null 2>&1; then
    fail "docker bulunamadı. Kurulum: https://docs.docker.com/engine/install/ (dağıtımınızın sayfası). Sonra bu betiği yeniden çalıştırın."
fi

if ! docker compose version >/dev/null 2>&1; then
    fail "docker compose eklentisi yok. Debian/Ubuntu için: sudo apt-get install docker-compose-plugin (Docker'ın kendi deposu eklenmiş olmalı)."
fi

if ! docker info >/dev/null 2>&1; then
    fail "Docker servisine bağlanılamadı. Servis çalışıyor mu: sudo systemctl enable --now docker. Yetki yoksa betiği sudo ile çalıştırın veya kullanıcıyı docker grubuna ekleyin (docker grubu root yetkisine denktir)."
fi

info "Docker: $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo bilinmiyor), compose: $(docker compose version --short 2>/dev/null || echo bilinmiyor)"

# 2) Saat senkronu (checkpoint yaşı ve observed_at doğru olsun diye). Yalnızca uyarır.
if command -v timedatectl >/dev/null 2>&1; then
    if [ "$(timedatectl show -p NTPSynchronized --value 2>/dev/null || echo no)" != "yes" ]; then
        warn "Saat senkronize görünmüyor. Açmak için: sudo timedatectl set-ntp true"
    fi
else
    warn "timedatectl yok; saat senkronunu (NTP) elle kontrol edin."
fi

# 3) Boş disk (Docker'ın veri klasörü). Yalnızca uyarır.
docker_root="$(docker info --format '{{.DockerRootDir}}' 2>/dev/null || echo /var/lib/docker)"
if [ -d "$docker_root" ]; then
    free_kb="$(df -Pk "$docker_root" | awk 'NR == 2 { print $4 }')"
    free_gb=$(( free_kb / 1024 / 1024 ))
    if [ "$free_gb" -lt "$MIN_FREE_GB" ]; then
        warn "$docker_root üzerinde yalnızca ${free_gb} GB boş. En az ${MIN_FREE_GB} GB önerilir."
    else
        info "Boş disk ($docker_root): ${free_gb} GB"
    fi
fi

# 4) .env: yoksa rastgele parolayla oluştur. Varsa dokunma, yalnızca izni düzelt.
if [ ! -f "$ENV_FILE" ]; then
    info ".env oluşturuluyor (rastgele parola)."
    # od tam 32 bayt okur; boru erken kapanmaz. Onaltılık parola bağlantı metnini bozacak karakter içermez.
    password="$(od -An -tx1 -N32 /dev/urandom | tr -d ' \n')"
    if [ "${#password}" -ne 64 ]; then
        fail "Rastgele parola üretilemedi."
    fi

    (
        umask 077
        cat >"$ENV_FILE" <<EOF
# Bu dosya gizlidir. Depoya girmez, kimseyle paylaşılmaz. İzni 600 olmalı.
POSTGRES_PASSWORD=$password
# Ölçüm konumu etiketi (collection_log.measurement_location). Kişisel bilgi yazmayın; ör. lab-linux.
MEASUREMENT_LOCATION=lab-linux
# İzlenecek loglar. 2027h1 tek başına günde ~15-16 GB indirir (kotalı bağlantıya uygun değil).
# 2026h2 17 Aralık 2026'da kapanıyor; açarsanız günde ~1-2 GB daha eklenir.
CT_ENABLE_2027H1=true
CT_ENABLE_2026H2=false
EOF
    )
    unset password
else
    info ".env zaten var; değiştirilmedi."
fi

chmod 600 "$ENV_FILE"
if ! grep -Eq '^POSTGRES_PASSWORD=.+' "$ENV_FILE"; then
    fail ".env içinde POSTGRES_PASSWORD yok veya boş."
fi

# 5) Compose dosyasını doğrula, imajı derle, başlat.
compose config -q
info "İmaj derleniyor (ilk seferde birkaç dakika sürebilir)."
compose build
info "Servisler başlatılıyor."
compose up -d --remove-orphans

info "Migration sonucu:"
compose ps -a migrate
migrate_exit="$(docker inspect --format '{{.State.ExitCode}}' "$(compose ps -a -q migrate)" 2>/dev/null || echo bilinmiyor)"
if [ "$migrate_exit" != "0" ]; then
    warn "migrate servisi 0 ile bitmedi (çıkış: $migrate_exit). Log: docker compose -f $COMPOSE_FILE logs migrate"
fi

cat <<EOF

Kurulum tamam. Sonraki adımlar:
  Durum:      $SCRIPT_DIR/status.sh
  Canlı log:  docker compose -f $COMPOSE_FILE logs -f --tail 50 ct-follow
  Yedek:      $SCRIPT_DIR/backup.sh   (günlük çalıştırma örneği README.md içinde)
İlk sağlık sonucu birkaç dakika sürer (start_period 5 dk).
EOF
