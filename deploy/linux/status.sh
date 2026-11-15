#!/usr/bin/env bash
# Canlı akışın ve günlük ağ kaydının durumu. YALNIZCA SAYI basar: ct_candidates içindeki adlar, dns_snapshots içindeki
# cevaplar ve ip_asn_snapshots / port_snapshots içindeki IP'ler hiçbir sorguda okunmaz.
# Log adları (ör. sycamore2026h2) yapılandırmadaki log kısa adlarıdır, alan adı değildir.
#
# Kullanım: ./status.sh            ayrıntılı durum
#           ./status.sh --check    tek satır özet ve çıkış kodu (THREAT_MODEL M3; Windows bildirimi bunu çağırır):
#             0 = temiz
#             1 = ateşlenen uyarı veya beklenmeyen yayımlanan Docker portu var (uyarı adları / port numaraları)
#             2 = Prometheus'a ulaşılamıyor ya da Watchdog uyarısı yok (uyarı hattı çalışmıyor)
#             3 = bir konteyner çalışmıyor veya sağlıksız
#           Birden fazla sorun varsa öncelik 2 > 3 > 1 (uyarı hattı kör ise diğer sonuçlara güvenilmez); özet satırı
#           hepsini yazar. Alan adı ve IP basmaz.
# Ayar: AVG_TILE_KB  İndirilen veri tahmini için ortalama veri tile'ı boyutu (KB).
#                    Varsayılan 175: ölçülen ~7 GB / ~40 bin tile. Kendi ölçümünüzle değiştirin.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.ct.yml"
MONITORING_FILE="$SCRIPT_DIR/docker-compose.monitoring.yml"
ENV_FILE="$SCRIPT_DIR/.env"
AVG_TILE_KB="${AVG_TILE_KB:-175}"
# Sürekli çalışması gereken servisler (migrate tek seferliktir, burada yok).
LONG_RUNNING="db ct-follow network prometheus grafana port-audit port-audit-exporter"

fail() { printf '[hata] %s\n' "$*" >&2; exit 1; }

compose() { docker compose -f "$COMPOSE_FILE" -f "$MONITORING_FILE" --env-file "$ENV_FILE" "$@"; }

mode="full"
case "${1:-}" in
    --check) mode="check" ;;
    "") ;;
    *) fail "Kullanım: ./status.sh [--check]" ;;
esac

grafana_port() {
    local port
    port="$(grep -E '^GRAFANA_PORT=' "$ENV_FILE" 2>/dev/null | head -n 1 | cut -d= -f2 || true)"
    printf '%s' "${port:-53000}"
}

# Docker'ın yayımladığı bütün portlar (bu proje dahil bütün konteynerler), /proc/net'ten bağımsız (THREAT_MODEL M2):
# userland-proxy kapalıysa yayımlanan port yalnızca bir DNAT kuralıdır ve port denetimi onu göremez.
# Beklenen tek yayın 127.0.0.1:GRAFANA_PORT->3000/tcp. Çıktı: "port/protokol(kapsam)", adres basılmaz.
unexpected_published_ports() {
    local expected entry bind_part bind_port bind_addr proto scope
    expected="127.0.0.1:$(grafana_port)->3000/tcp"
    # Hata veya hiç yayın yoksa boş çıktı; "set -o pipefail" altında betiği durdurmasın diye "|| true".
    { docker ps --format '{{.Ports}}' 2>/dev/null || true; } | tr ',' '\n' | sed 's/^ *//' | { grep -F -- '->' || true; } | while read -r entry; do
        [ "$entry" = "$expected" ] && continue
        bind_part="${entry%%->*}"
        bind_port="${bind_part##*:}"
        bind_addr="${bind_part%:*}"
        proto="${entry##*/}"
        case "$bind_addr" in
            127.* | "[::1]" | "::1") scope="loopback" ;;
            0.0.0.0 | "[::]" | "::" | "") scope="wildcard" ;;
            *) scope="specific" ;;
        esac
        printf '%s/%s(%s)\n' "$bind_port" "$proto" "$scope"
    done | sort -u
}

# Ateşlenen uyarıların adları (satır başına bir ad). Prometheus'a ulaşılamazsa 1 ile döner.
firing_alerts() {
    local raw
    raw="$(compose exec -T prometheus promtool query instant http://127.0.0.1:9090 \
        'count by (alertname) (ALERTS{alertstate="firing"})' 2>/dev/null)" || return 1
    printf '%s\n' "$raw" | sed -nE 's/.*alertname="([^"]+)".*/\1/p' | sort -u
}

# Çalışmayan veya sağlıksız servisler (satır başına "servis:durum").
unhealthy_services() {
    local states service line state health
    states="$(compose ps --all --format '{{.Service}} {{.State}} {{.Health}}' 2>/dev/null)" || { echo "docker:erişilemiyor"; return 0; }
    for service in $LONG_RUNNING; do
        line="$(printf '%s\n' "$states" | awk -v s="$service" '$1 == s { print; exit }')"
        if [ -z "$line" ]; then
            echo "$service:yok"
            continue
        fi
        state="$(printf '%s' "$line" | awk '{ print $2 }')"
        health="$(printf '%s' "$line" | awk '{ print $3 }')"
        if [ "$state" != "running" ]; then
            echo "$service:$state"
        elif [ "$health" = "unhealthy" ]; then
            echo "$service:unhealthy"
        fi
    done
}

run_check() {
    local code=0 rank=0 parts=() alerts others published unhealthy
    # Öncelik sırası: 2 (kör) > 3 (konteyner) > 1 (uyarı).
    raise() {
        local new="$1" new_rank
        case "$new" in 2) new_rank=3 ;; 3) new_rank=2 ;; *) new_rank=1 ;; esac
        if [ "$new_rank" -gt "$rank" ]; then rank="$new_rank"; code="$new"; fi
    }

    if [ ! -f "$ENV_FILE" ]; then
        echo "fraud-lab: kurulum yok (.env bulunamadı)"
        exit 3
    fi

    if alerts="$(firing_alerts)"; then
        if ! printf '%s\n' "$alerts" | grep -qx 'Watchdog'; then
            raise 2
            parts+=("Watchdog yok, uyarı hattı çalışmıyor")
        fi
        others="$(printf '%s\n' "$alerts" | grep -vx 'Watchdog' | grep -v '^$' | paste -sd ',' - || true)"
        if [ -n "$others" ]; then
            raise 1
            parts+=("alarm: ${others//,/, }")
        fi
    else
        raise 2
        parts+=("Prometheus'a ulaşılamıyor")
    fi

    published="$(unexpected_published_ports | paste -sd ',' - || true)"
    if [ -n "$published" ]; then
        raise 1
        parts+=("beklenmeyen Docker portu: ${published//,/, }")
    fi

    unhealthy="$(unhealthy_services | paste -sd ',' - || true)"
    if [ -n "$unhealthy" ]; then
        raise 3
        parts+=("konteyner: ${unhealthy//,/, }")
    fi

    if [ "$code" -eq 0 ]; then
        echo "fraud-lab: temiz"
    else
        local joined
        joined="$(printf '%s; ' "${parts[@]}")"
        echo "fraud-lab: ${joined%; }"
    fi
    exit "$code"
}

if [ "$mode" = "check" ]; then
    run_check
fi

case "$AVG_TILE_KB" in
    '' | *[!0-9]*) fail "AVG_TILE_KB pozitif bir tam sayı olmalı." ;;
esac
[ -f "$ENV_FILE" ] || fail ".env yok. Önce install.sh çalıştırın."

echo "== Konteynerler =="
compose ps --all --format 'table {{.Service}}\t{{.State}}\t{{.Status}}'

echo
echo "== Yayımlanan Docker portları (bütün konteynerler) =="
published="$(unexpected_published_ports)"
if [ -z "$published" ]; then
    echo "Yalnızca Grafana (127.0.0.1:$(grafana_port)). Beklenmeyen yayın yok."
else
    echo "BEKLENMEYEN yayın (port/protokol(kapsam)):"
    printf '%s\n' "$published" | sed 's/^/  /'
fi

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
echo "== Tarafsız CT örneklemi (ct_neutral_sample, yalnızca sayı) =="
psql_run <<'SQL'
SELECT first_log_name                                                  AS ilk_log,
       count(*)                                                        AS alan_adi,
       count(*) FILTER (WHERE first_seen_at >= now() - interval '24 hours') AS son_24_saat_yeni,
       coalesce(sum(seen_count), 0)                                    AS gorulen_kayit,
       date_trunc('second', now() - max(last_seen_at))                 AS son_kayittan_beri
FROM ct_neutral_sample
GROUP BY first_log_name
ORDER BY first_log_name;
SQL
echo "(Oran ~%1, kelimeye bakılmadan. Tablo boşsa: ct-follow tuzsuz başlamış olamaz; migration veya akışı kontrol edin.)"

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
echo "== Ağ kaydı: DNS, son 24 saat (dns_snapshots) =="
psql_run <<'SQL'
SELECT resolver                                        AS cozucu,
       transport                                       AS yol,
       count(*)                                        AS sorgu,
       count(*) FILTER (WHERE outcome = 'Success')     AS cevapli,
       count(*) FILTER (WHERE outcome = 'Empty')       AS bos,
       count(*) FILTER (WHERE outcome = 'NxDomain')    AS nxdomain,
       count(*) FILTER (WHERE outcome = 'Error')       AS hata,
       count(*) FILTER (WHERE block_marker)            AS engel_isareti,
       count(*) FILTER (WHERE truncated)               AS tc_tcp_gecis,
       round(avg(latency_ms))                          AS ort_gecikme_ms
FROM dns_snapshots
WHERE queried_at >= now() - interval '24 hours'
GROUP BY resolver, transport
ORDER BY resolver, transport;
SQL

echo
echo "== Ağ kaydı: DNS hata türleri, son 24 saat =="
psql_run <<'SQL'
SELECT transport AS yol, error_kind AS hata_turu, count(*) AS adet
FROM dns_snapshots
WHERE outcome = 'Error' AND queried_at >= now() - interval '24 hours'
GROUP BY transport, error_kind
ORDER BY transport, adet DESC;
SQL

echo
echo "== Ağ kaydı: RIPEstat ve InternetDB, son 7 gün (yalnızca sayı) =="
psql_run <<'SQL'
SELECT 'RipeStat'                                       AS kaynak,
       count(*)                                         AS satir,
       count(*) FILTER (WHERE outcome = 'Success')      AS basarili,
       count(*) FILTER (WHERE outcome = 'Empty')        AS bos,
       count(*) FILTER (WHERE outcome = 'Error')        AS hata,
       count(*) FILTER (WHERE cdn_or_cloud IS NOT NULL) AS cdn_bulut,
       max(queried_at)                                  AS son_sorgu
FROM ip_asn_snapshots WHERE queried_at >= now() - interval '7 days'
UNION ALL
SELECT 'InternetDb', count(*),
       count(*) FILTER (WHERE outcome = 'Success'),
       count(*) FILTER (WHERE outcome = 'Empty'),
       count(*) FILTER (WHERE outcome = 'Error'),
       NULL,
       max(queried_at)
FROM port_snapshots WHERE queried_at >= now() - interval '7 days';
SQL
echo "(Ayrıntılı karşılaştırma: docker compose -f docker-compose.ct.yml run --rm network dns-compare)"

echo
echo "== Alarmlar (Prometheus) =="
# Yalnızca uyarı adı, durumu ve sayısı. Uyarı etiketleri sabit değerlerdir (log kısa adı, yol, protokol, port).
if [ -n "$(compose ps -q prometheus 2>/dev/null || true)" ]; then
    alarms="$(compose exec -T prometheus promtool query instant http://127.0.0.1:9090 \
        'count by (alertname, alertstate) (ALERTS)' 2>/dev/null || echo '__hata__')"
    if [ "$alarms" = "__hata__" ]; then
        echo "Prometheus sorgulanamadı (konteyner açılıyor olabilir)."
    else
        # Watchdog her zaman ateşlenir; yokluğu uyarı hattının çalışmadığını gösterir (THREAT_MODEL M3).
        if printf '%s\n' "$alarms" | grep -q 'alertname="Watchdog"'; then
            echo "Uyarı hattı (Watchdog): çalışıyor."
        else
            echo "Uyarı hattı (Watchdog): YOK. Kurallar değerlendirilmiyor olabilir."
        fi
        others="$(printf '%s\n' "$alarms" | grep -v 'alertname="Watchdog"' | grep -v '^$' || true)"
        if [ -z "$others" ]; then
            echo "Ateşlenen veya bekleyen başka uyarı yok."
        else
            # Satır biçimi: {alertname="CtFollowerStalled", alertstate="firing"} => 1 @[zaman]
            printf '%s\n' "$others" | sed -E 's/.*alertname="([^"]+)".*alertstate="([^"]+)".* => ([0-9]+).*/  \1: \2 (\3)/'
        fi
    fi
    echo "(Ayrıntı: Grafana, 'fraud-lab izleme' panelinin 'Uyarılar' satırı.)"
else
    echo "prometheus çalışmıyor; uyarılar değerlendirilmiyor."
fi

echo
echo "== Port denetimi (son değişiklikler) =="
# Log yalnızca sayı, protokol, port ve kapsam içerir; adres ve süreç adı yazılmaz.
if [ -n "$(compose ps -q port-audit 2>/dev/null || true)" ]; then
    compose logs --tail 200 port-audit 2>/dev/null | cut -d'|' -f2- \
        | grep -E 'Port denetimi|Beklenmeyen dinleyici|Beklenen dinleyici yok' | tail -n 5 \
        || echo "Henüz denetim logu yok."
else
    echo "port-audit çalışmıyor."
fi

echo
echo "== Veritabanı boyutu =="
psql_run <<'SQL'
SELECT pg_size_pretty(pg_database_size(current_database()))          AS veritabani,
       pg_size_pretty(pg_total_relation_size('ct_candidates'))       AS ct_candidates,
       pg_size_pretty(pg_total_relation_size('ct_neutral_sample'))   AS ct_neutral_sample,
       pg_size_pretty(pg_total_relation_size('collection_log'))      AS collection_log,
       pg_size_pretty(pg_total_relation_size('dns_snapshots'))       AS dns_snapshots;
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
