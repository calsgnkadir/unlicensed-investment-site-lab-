"""Aşama 0 devam/bırak kararı için betimleyici özet.

Yerel veritabanından (docker compose, `db` servisi) okur, yalnızca toplu sayı basar.
Alan adı yazmaz. Sinyal veya model değildir; Aşama 0 fizibilite sorusunu cevaplar:
"Engellenen sitelerin karardan önce sertifika ve arşiv kaydı var mıydı?"

Birim: alan adı. Bir alan adı birden fazla bültende geçtiyse en erken karar tarihi alınır.
Kaynağı yalnızca hata veren alan adları bilinmeyen sayılır, "kayıt yok" sayılmaz.
Kaynağın başarılı ama boş döndüğü (Empty) alan adları bilinen sayılır: "kayıt yok" budur.
"""
from __future__ import annotations

import csv
import io
import statistics as st
import subprocess
import sys
from collections import defaultdict
from datetime import date

QUERY = """
with decision as (
  select domain_id, min(decision_date) as decided, min(bulletin_number) as bulletin, min(category) as category
  from official_actions group by domain_id
),
crt as (
  select domain_id,
         bool_or(outcome in ('Success', 'Empty')) as ok
  from collection_log where source = 'CrtSh' group by domain_id
),
wb as (
  select domain_id, bool_or(outcome in ('Success', 'Empty')) as ok
  from collection_log where source = 'WaybackCdx' group by domain_id
),
cert as (
  select cd.domain_id, min(c.not_before)::date as first_cert,
         count(*) as cert_count
  from certificate_domains cd join certificates c on c.id = cd.certificate_id
  group by cd.domain_id
),
arc as (
  select a.domain_id,
         min(a.captured_at)::date as first_capture,
         count(*) filter (where a.captured_at::date < d.decided) as pre_any,
         count(*) filter (where a.captured_at::date < d.decided and a.status_code = 200) as pre_200
  from archive_snapshots a join decision d on d.domain_id = a.domain_id
  group by a.domain_id
)
select d.domain_id, d.decided, d.bulletin, d.category,
       coalesce(crt.ok, false) as crt_ok, cert.first_cert, coalesce(cert.cert_count, 0) as cert_count,
       coalesce(wb.ok, false) as wb_ok, arc.first_capture, coalesce(arc.pre_any, 0) as pre_any,
       coalesce(arc.pre_200, 0) as pre_200
from decision d
left join crt on crt.domain_id = d.domain_id
left join wb on wb.domain_id = d.domain_id
left join cert on cert.domain_id = d.domain_id
left join arc on arc.domain_id = d.domain_id
"""


def fetch() -> list[dict]:
    cmd = ["docker", "compose", "exec", "-T", "db", "psql", "-U", "fraud_lab", "-d", "fraud_lab",
           "-c", f"copy ({QUERY}) to stdout with csv header"]
    out = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", check=True).stdout
    return list(csv.DictReader(io.StringIO(out)))


def pct(a: int, b: int) -> str:
    return f"{a} / {b} (%{100 * a / b:.0f})" if b else f"{a} / 0"


def quartiles(values: list[int]) -> str:
    if not values:
        return "veri yok"
    v = sorted(values)
    q = st.quantiles(v, n=4) if len(v) >= 2 else [v[0]] * 3
    return f"ortanca {st.median(v):.0f}, çeyrekler {q[0]:.0f} ve {q[2]:.0f}, en az {v[0]}, en çok {v[-1]}"


def main() -> int:
    rows = fetch()
    n = len(rows)
    for r in rows:
        r["decided"] = date.fromisoformat(r["decided"])
        r["first_cert"] = date.fromisoformat(r["first_cert"]) if r["first_cert"] else None
        r["first_capture"] = date.fromisoformat(r["first_capture"]) if r["first_capture"] else None
        r["lag"] = (r["decided"] - r["first_cert"]).days if r["first_cert"] else None
        for k in ("crt_ok", "wb_ok"):
            r[k] = r[k] == "t"
        for k in ("cert_count", "pre_any", "pre_200"):
            r[k] = int(r[k])

    crt_known = [r for r in rows if r["crt_ok"]]
    with_cert = [r for r in crt_known if r["first_cert"]]
    before = [r for r in with_cert if r["lag"] > 0]
    lags = [r["lag"] for r in before]
    young = [r for r in before if r["lag"] <= 365]
    old = [r for r in before if r["lag"] > 365]

    by_bulletin = defaultdict(list)
    for r in before:
        by_bulletin[r["bulletin"]].append(r["lag"])
    bulletin_medians = [st.median(v) for v in by_bulletin.values()]

    print(f"Alan adı (en erken karar): {n}")
    print(f"crt.sh yanıtı bilinen: {pct(len(crt_known), n)}; yalnızca hata (bilinmiyor): {n - len(crt_known)}")
    print(f"Sertifikası olan: {pct(len(with_cert), len(crt_known))}")
    print(f"Karardan önce sertifika: {pct(len(before), len(crt_known))}")
    print(f"İlk sertifika ile karar arası (gün): {quartiles(lags)}")
    print(f"Bülten ortancalarının ortancası: {st.median(bulletin_medians):.0f} gün ({len(bulletin_medians)} bülten)" if bulletin_medians else "")
    for limit in (28, 60, 180, 365):
        print(f"  <= {limit} gün: {pct(sum(1 for x in lags if x <= limit), len(lags))}")
    print(f"  > 365 gün: {pct(len(old), len(lags))}")

    wb_known = [r for r in rows if r["wb_ok"]]
    if wb_known:
        def arc_line(name, group):
            g = [r for r in group if r["wb_ok"]]
            print(f"{name}: herhangi kayıt {pct(sum(1 for r in g if r['first_capture']), len(g))}, "
                  f"karar öncesi 200 {pct(sum(1 for r in g if r['pre_200'] > 0), len(g))}")
        print(f"Wayback yanıtı bilinen (kayıtlı veya kesin boş): {pct(len(wb_known), n)}")
        arc_line("Arşiv, tümü", rows)
        arc_line("Arşiv, sertifikası 1 yıldan yeni", young)
        arc_line("Arşiv, sertifikası 1 yıldan eski", old)
    else:
        print("Wayback verisi henüz yok.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
