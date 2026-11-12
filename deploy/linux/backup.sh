#!/usr/bin/env bash
# Veritabanının günlük yedeği: pg_dump (özel biçim, sıkıştırılmış). Son KEEP yedek tutulur, eskiler silinir.
#
# Kullanım: ./backup.sh
# Ayarlar (ortam değişkeni):
#   BACKUP_DIR  Yedek klasörü. Varsayılan: $HOME/fraud-lab-backups (depo klasörünün DIŞINDA).
#   KEEP        Tutulacak yedek sayısı. Varsayılan: 7.
#
# Yedek gizlidir: ct_candidates tablosu SPK/USOM listesinde olmayan alan adları içerir. Dosya izni 600, klasör 700.
# Dosya önce geçici adla yazılır, doğrulanınca asıl adına taşınır; yarım yedek asıl adla kalmaz.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.ct.yml"
ENV_FILE="$SCRIPT_DIR/.env"
BACKUP_DIR="${BACKUP_DIR:-$HOME/fraud-lab-backups}"
KEEP="${KEEP:-7}"

fail() { printf '[hata] %s\n' "$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

case "$KEEP" in
    '' | *[!0-9]*) fail "KEEP pozitif bir tam sayı olmalı." ;;
esac
[ "$KEEP" -ge 1 ] || fail "KEEP en az 1 olmalı."
[ -f "$ENV_FILE" ] || fail ".env yok. Önce install.sh çalıştırın."

umask 077
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

# Aynı anda iki yedek çalışmasın (cron üst üste binerse).
if command -v flock >/dev/null 2>&1; then
    exec 9>"$BACKUP_DIR/.backup.lock"
    flock -n 9 || fail "Başka bir yedek şu an çalışıyor."
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
final="$BACKUP_DIR/fraud_lab_${stamp}.dump"
partial="$final.partial"
trap 'rm -f "$partial"' EXIT

# Konteynerin içinden yerel soket ile bağlanılır; veritabanı portu dışarı açık olmadığı için başka yol yok.
compose exec -T db pg_dump -U fraud_lab -d fraud_lab --format=custom --compress=6 --no-owner >"$partial"

[ -s "$partial" ] || fail "Yedek dosyası boş."

# Arşiv okunabiliyor mu: içindekiler listesi çıkarılabiliyorsa dosya bütündür.
if ! compose exec -T db pg_restore --list >/dev/null <"$partial"; then
    fail "Yedek doğrulanamadı (pg_restore --list başarısız)."
fi

chmod 600 "$partial"
mv "$partial" "$final"
trap - EXIT

# Eski yedekleri sil: Adlar UTC zamanla başladığı için alfabetik sıra = zaman sırası.
shopt -s nullglob
backups=("$BACKUP_DIR"/fraud_lab_*.dump)
shopt -u nullglob
count=${#backups[@]}
if [ "$count" -gt "$KEEP" ]; then
    for old in "${backups[@]:0:count-KEEP}"; do
        rm -f -- "$old"
    done
fi

size="$(du -h "$final" | cut -f1)"
printf '[bilgi] Yedek tamam: %s (%s). Tutulan yedek: %s\n' "$final" "$size" "$(( count > KEEP ? KEEP : count ))"
