#!/usr/bin/env bash
# Canlı akışın durumu. YALNIZCA SAYI basar: ct_candidates içindeki adlar hiçbir sorguda okunmaz.
# Log adları (ör. sycamore2026h2) yapılandırmadaki log kısa adlarıdır, alan adı değildir.
#
# Kullanım: ./status.sh
# Ayar: AVG_TILE_KB  İndirilen veri tahmini için ortalama veri tile'ı boyutu (KB).
#                    Varsayılan 175: ölçülen ~7 GB / ~40 bin tile. Kendi ölçümünüzle değiştirin.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.ct.yml"
ENV_FILE="$SCRIPT_DIR/.env"
AVG_TILE_KB="${AVG_TILE_KB:-175}"

fail() { printf '[hata] %s\n' "$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

case "$AVG_TILE_KB" in
    '' | *[!0-9]*) fail "AVG_TILE_KB pozitif bir tam sayı olmalı." ;;
esac
[ -f "$ENV_FILE" ] || fail ".env yok. Önce install.sh çalıştırın."

echo "== Konteynerler =="
compose ps --all --format 'table {{.Service}}\t{{.State}}\t{{.Status}}'

follow_id="$(compose ps -q ct-follow 2>/dev/null || true)"
if [ -n "$follow_id" ]; then
    echo
    echo "== ct-follow kaynak kullanımı (konteyner açıldığından beri ağ) =="
    docker stats --no-stream --format 'CPU {{.CPUPerc}}  Bellek {{.MemUsage}}  Ağ (gelen / giden) {{.NetIO}}' "$follow_id"
    echo "Yeniden başlama sayısı: $(docker inspect --format '{{.RestartCount}}' "$follow_id")"
fi

if [ -z "$(compose ps -q db 2>/dev/null || true)" ]; then
    fail "db konteyneri çalışmıyor; veritabanı sayıları okunamadı."
fi

psql_run() {
    compose exec -T db psql -X -q -v ON_ERROR_STOP=1 -U fraud_lab -d fraud_lab -P footer=off -f -
}

echo
echo "== Static CT izleme durumu (ct_log_state) =="
psql_run <<'SQL'
SELECT log_name                                             AS log,
       next_index                                           AS siradaki_indeks,
       latest_checkpoint_size                               AS son_checkpoint_boyutu,
       latest_checkpoint_size - next_index                  AS geride_kalan,
       entries_processed                                    AS islenen,
       entries_skipped                                      AS atlanan,
       round(100.0 * entries_processed
             / nullif(entries_processed + entries_skipped, 0), 4) AS kapsama_yuzde,
       candidates_stored                                    AS aday,
       consecutive_failures                                 AS ust_uste_hata,
       date_trunc('second', now() - pending_checkpoint_timestamp) AS checkpoint_yasi,
       date_trunc('second', now() - updated_at)             AS son_degisim
FROM ct_log_state
ORDER BY log_name;
SQL

echo
echo "== Son 24 saat (collection_log, kaynak StaticCt) =="
psql_run <<SQL
SELECT ct_log_name                                                          AS log,
       count(*)                                                             AS tur,
       count(*) FILTER (WHERE outcome = 'Error')                            AS hatali_tur,
       coalesce(sum(item_count), 0)                                         AS islenen,
       coalesce(sum(skipped_item_count), 0)                                 AS atlanan,
       coalesce(sum(new_item_count), 0)                                     AS yeni_aday,
       coalesce(sum(attempts), 0)                                           AS http_istegi,
       coalesce(sum((range_end - range_start) / 256)
                FILTER (WHERE outcome = 'Success' AND range_start IS NOT NULL), 0) AS islenen_tile,
       round(coalesce(sum((range_end - range_start) / 256)
                FILTER (WHERE outcome = 'Success' AND range_start IS NOT NULL), 0)
             * $AVG_TILE_KB / 1024.0 / 1024.0, 2)                           AS tahmini_indirme_gb,
       coalesce(sum(item_count) FILTER (WHERE finished_at >= now() - interval '1 hour'), 0) AS son_1_saat_islenen
FROM collection_log
WHERE source = 'StaticCt' AND finished_at >= now() - interval '24 hours'
GROUP BY ct_log_name
ORDER BY ct_log_name;
SQL
echo "(tahmini_indirme_gb = işlenen tile x ${AVG_TILE_KB} KB; hash tile ve tekrar denemeler dahil değil.)"
echo "(son_1_saat_islenen 0 ise: log durmuş, dilim kapanmış veya izleyici hata alıyor olabilir.)"

echo
echo "== Son 24 saatte hata türleri =="
psql_run <<'SQL'
SELECT ct_log_name AS log, error_kind AS hata_turu, count(*) AS adet
FROM collection_log
WHERE source = 'StaticCt' AND outcome = 'Error' AND finished_at >= now() - interval '24 hours'
GROUP BY ct_log_name, error_kind
ORDER BY ct_log_name, adet DESC;
SQL

echo
echo "== Veritabanı boyutu =="
psql_run <<'SQL'
SELECT pg_size_pretty(pg_database_size(current_database()))          AS veritabani,
       pg_size_pretty(pg_total_relation_size('ct_candidates'))       AS ct_candidates,
       pg_size_pretty(pg_total_relation_size('collection_log'))      AS collection_log;
SQL

echo
echo "== Disk =="
docker_root="$(docker info --format '{{.DockerRootDir}}' 2>/dev/null || echo /var/lib/docker)"
if [ -d "$docker_root" ]; then
    df -h "$docker_root" | awk 'NR == 1 || NR == 2'
else
    echo "Docker veri klasörü okunamadı: $docker_root"
fi

backup_dir="${BACKUP_DIR:-$HOME/fraud-lab-backups}"
if [ -d "$backup_dir" ]; then
    shopt -s nullglob
    dumps=("$backup_dir"/fraud_lab_*.dump)
    shopt -u nullglob
    echo "Yedek sayısı: ${#dumps[@]} ($(du -sh "$backup_dir" | cut -f1))"
    if [ "${#dumps[@]}" -gt 0 ]; then
        echo "Son yedek: $(basename "${dumps[${#dumps[@]}-1]}")"
    fi
fi
