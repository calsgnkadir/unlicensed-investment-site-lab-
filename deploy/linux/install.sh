#!/usr/bin/env bash
# Canlı Static CT akışını kurar ve başlatır. Tekrar çalıştırmak güvenlidir (idempotent):
# .env varsa dokunmaz, imaj değişmediyse yeniden derlemez, çalışan servisleri gereksiz yere yeniden başlatmaz.
#
# Kullanım: ./install.sh
# Docker veya compose eklentisini KURMAZ; eksikse ne yapılacağını yazar ve çıkar.
#
# İki compose dosyası birlikte kullanılır: docker-compose.ct.yml (veri: db, migrate, ct-follow, network) ve
# docker-compose.monitoring.yml (izleme: prometheus, grafana, port-audit). Parolalar .env içinde üretilir ve
# hiçbir zaman ekrana basılmaz.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.ct.yml"
MONITORING_FILE="$SCRIPT_DIR/docker-compose.monitoring.yml"
ENV_FILE="$SCRIPT_DIR/.env"
EXPECTED_PORTS="$SCRIPT_DIR/port-audit.expected"
# Grafana parolası ortam değişkeninde değil dosyada (docker inspect'te görünmez). Klasör 700: bu makinedeki başka
# kullanıcılar okuyamaz. Dosya 644: konteynere salt okunur bağlanınca grafana kullanıcısı (uid 472) okuyabilsin diye.
SECRETS_DIR="$SCRIPT_DIR/secrets"
GRAFANA_PASSWORD_FILE="$SECRETS_DIR/grafana_admin_password"
MIN_FREE_GB=20

info() { printf '[bilgi] %s\n' "$*"; }
warn() { printf '[uyarı] %s\n' "$*" >&2; }
fail() { printf '[hata] %s\n' "$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" -f "$MONITORING_FILE" --env-file "$ENV_FILE" "$@"; }

# 64 karakterlik onaltılık rastgele değer. od tam 32 bayt okur; boru erken kapanmaz.
random_hex() {
    local value
    value="$(od -An -tx1 -N32 /dev/urandom | tr -d ' \n')"
    if [ "${#value}" -ne 64 ]; then
        fail "Rastgele değer üretilemedi."
    fi
    printf '%s' "$value"
}

# .env içinde anahtar yoksa ekler; varsa dokunmaz. Değer ekrana basılmaz.
ensure_env() {
    local key="$1" value="$2" comment="$3"
    [ -n "$value" ] || fail ".env: $key için boş değer yazılmaz."
    if ! grep -Eq "^${key}=" "$ENV_FILE"; then
        printf '# %s\n%s=%s\n' "$comment" "$key" "$value" >>"$ENV_FILE"
        info ".env: $key eklendi."
    fi
}

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

# Port denetimi, konteyneri host ağ ad alanında çalıştırır. Docker Desktop'ta "host" Docker Desktop'ın kendi sanal
# makinesidir; denetim bu Ubuntu'yu değil o makineyi görür. Docker Engine bu dağıtımın içine kurulmuş olmalı.
if docker info --format '{{.OperatingSystem}}' 2>/dev/null | grep -qi 'docker desktop'; then
    warn "Docker Desktop algılandı. port-audit bu Ubuntu'nun değil Docker Desktop sanal makinesinin portlarını görür. README: 'Port denetimi'."
fi

# userland-proxy kapalıysa Docker yayımlanan portu yalnızca DNAT kuralıyla yönlendirir; /proc/net'te dinleyen soket
# olmaz ve port-audit onu göremez (THREAT_MODEL M2). status.sh --check yayımlanan portları ayrıca "docker ps" ile denetler.
if { [ -r /etc/docker/daemon.json ] && grep -Eq '"userland-proxy"[[:space:]]*:[[:space:]]*false' /etc/docker/daemon.json; } \
    || { command -v pgrep >/dev/null 2>&1 && pgrep -a dockerd 2>/dev/null | grep -q 'userland-proxy=false'; }; then
    warn "Docker userland-proxy KAPALI: port-audit yayımlanan portları göremez; status.sh (--check) bunları 'docker ps' ile denetler. Grafana için WSL localhost iletimi de çalışmayabilir."
else
    info "Docker userland-proxy: açık (varsayılan). Yayımlanan portlar port-audit'te ve status.sh'te görünür."
fi

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
    # Onaltılık parola bağlantı metnini bozacak karakter içermez.
    password="$(random_hex)"

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
# Günlük ağ kaydının (DNS, RIPEstat, InternetDB) ölçüm konumu. Karar: docs/DECISIONS.md, 2026-11-13.
DNS_LOCATION=tr-ev-ubuntu
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

# Eski sürümden kalan, artık kullanılmayan Grafana parola satırı (parola artık secrets/ altında dosyada).
# Kullanılmayan bir sırrın düz metin olarak durmasının anlamı yok; satır ve açıklaması silinir. Değer basılmaz.
if grep -Eq '^GRAFANA_ADMIN_PASSWORD=' "$ENV_FILE"; then
    sed -i -e '/^GRAFANA_ADMIN_PASSWORD=/d' -e '/^# Grafana yönetici parolası/d' "$ENV_FILE"
    info ".env: kullanılmayan GRAFANA_ADMIN_PASSWORD satırı silindi (parola secrets/grafana_admin_password içinde)."
fi

# İzleme anahtarları: Eski kurulumun .env dosyasına da eklenir (var olan satırlara dokunulmaz).
ensure_env GRAFANA_PORT 53000 \
    "Grafana yalnızca 127.0.0.1 üzerinde bu portta. Değişirse port-audit.expected içindeki tcp/53000 satırı da değişmeli."
# Bu klasörde -f yazmadan çalışan "docker compose ..." komutları da iki dosyayı birlikte okusun.
ensure_env COMPOSE_FILE "docker-compose.ct.yml:docker-compose.monitoring.yml" \
    "Bu klasörde 'docker compose' iki dosyayı birlikte kullanır (veri + izleme)."
chmod 600 "$ENV_FILE"

# Grafana parolası: yoksa üretilir, varsa dokunulmaz. Hiçbir zaman ekrana basılmaz.
mkdir -p "$SECRETS_DIR"
chmod 700 "$SECRETS_DIR"
if [ ! -s "$GRAFANA_PASSWORD_FILE" ]; then
    # Alt kabuk: random_hex başarısız olursa alt kabuk hata ile biter ve "set -e" betiği durdurur.
    ( umask 022; random_hex >"$GRAFANA_PASSWORD_FILE" )
    info "Grafana parolası üretildi: secrets/grafana_admin_password (kullanıcı: admin)."
fi
chmod 644 "$GRAFANA_PASSWORD_FILE"

if [ ! -f "$EXPECTED_PORTS" ]; then
    fail "$EXPECTED_PORTS yok. Depodan gelmeli (git status ile kontrol edin)."
fi

# 5) Compose dosyalarını doğrula, imajı derle, başlat.
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

grafana_port="$(grep -E '^GRAFANA_PORT=' "$ENV_FILE" | head -n 1 | cut -d= -f2)"

cat <<EOF

Kurulum tamam. Sonraki adımlar:
  Durum:      $SCRIPT_DIR/status.sh
  Canlı log:  docker compose -f $COMPOSE_FILE logs -f --tail 50 ct-follow
  Ağ kaydı:   docker compose -f $COMPOSE_FILE logs -f --tail 50 network
  Yedek:      $SCRIPT_DIR/backup.sh   (günlük çalıştırma örneği README.md içinde)
  Grafana:    http://127.0.0.1:${grafana_port:-53000}  (Windows tarayıcısından da: http://localhost:${grafana_port:-53000})
              Kullanıcı: admin. Parola secrets/grafana_admin_password içinde; ekrana basmadan kopyalamak için README "Grafana".
  Denetim:    $SCRIPT_DIR/status.sh --check   (tek satır ve çıkış kodu; Windows bildirimi için README "Bildirim")
İlk sağlık sonucu birkaç dakika sürer (start_period 5 dk).
EOF
