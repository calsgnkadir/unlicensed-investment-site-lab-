#!/usr/bin/env python3
"""Aşama 0: SPK 2026 bülten PDF'lerini indirir. Sinyal veya ölçüm mantığı yoktur.

Yalnızca spk.gov.tr alanına istek atar (yönlendirmede de alan dışına çıkmaz).
Bültende geçen alan adlarına hiç istek atılmaz.
Kullanım: python download_bulletins.py
Çıktı (git'te yok sayılır): data/private/phase0/bultenler/2026-N.pdf ve manifest.csv
"""
from __future__ import annotations

import csv
import datetime
import hashlib
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

LIST_URL = "https://spk.gov.tr/spk-bultenleri/2026-yili-spk-bultenleri"
PAGES = (None, 1, 2, 3)  # ilk sayfa ve ?s=1..3
HOST = "spk.gov.tr"
UA = "Mozilla/5.0 (compatible; research-script)"
DELAY = 3.5
MAX_BYTES = 20 * 1024 * 1024
MAX_HTML = 2 * 1024 * 1024
OUT = Path(__file__).resolve().parents[2] / "data" / "private" / "phase0" / "bultenler"
NAME_RE = re.compile(r"/(2026-(\d{1,3}))\.pdf$", re.I)
HREF_RE = re.compile(r'href="([^"]+?\.pdf)"', re.I)


def host_ok(url: str) -> bool:
    p = urllib.parse.urlsplit(url)
    return p.scheme == "https" and p.hostname == HOST and p.path.lower().endswith(".pdf")


class SameHost(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if urllib.parse.urlsplit(newurl).hostname != HOST or not newurl.startswith("https://"):
            raise urllib.error.URLError("alan dışına yönlendirme reddedildi")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


OPENER = urllib.request.build_opener(SameHost)
_last = [0.0]


def get(url: str, limit: int) -> bytes:
    wait = DELAY - (time.monotonic() - _last[0])
    if wait > 0:
        time.sleep(wait)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    try:
        with OPENER.open(req, timeout=30) as r:
            data = r.read(limit + 1)
    finally:
        _last[0] = time.monotonic()
    if len(data) > limit:
        raise ValueError("boyut sınırı aşıldı")
    return data


def list_links() -> dict:
    found = {}
    for s in PAGES:
        url = LIST_URL if s is None else f"{LIST_URL}?s={s}"
        html = get(url, MAX_HTML).decode("utf-8", "replace")
        for href in HREF_RE.findall(html):
            full = urllib.parse.urljoin(url, href.replace("&amp;", "&"))
            full = urllib.parse.quote(full, safe=":/%?=&")
            m = NAME_RE.search(urllib.parse.unquote(full))
            if m and host_ok(full):
                found.setdefault(m.group(1), full)
    return found


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    manifest = OUT / "manifest.csv"
    rows = {}
    if manifest.exists():
        with open(manifest, encoding="utf-8", newline="") as f:
            rows = {r["dosya"]: r for r in csv.DictReader(f)}
    links = list_links()
    print(f"listede bulunan bülten bağlantısı: {len(links)}")
    ok = fail = skip = 0
    for key in sorted(links, key=lambda k: int(k.split("-")[1])):
        name = key + ".pdf"
        dest = OUT / name
        if dest.exists() and name in rows:
            skip += 1
            continue
        try:
            data = get(links[key], MAX_BYTES)
            if not data.startswith(b"%PDF"):
                raise ValueError("%PDF başlığı yok")
        except Exception as e:
            fail += 1
            print(f"başarısız: {name} ({type(e).__name__})")
            continue
        dest.write_bytes(data)
        rows[name] = {"dosya": name, "kaynak_url": links[key], "boyut": len(data),
                      "sha256": hashlib.sha256(data).hexdigest(),
                      "indirme_zamani": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")}
        ok += 1
    cols = ["dosya", "kaynak_url", "boyut", "sha256", "indirme_zamani"]
    with open(manifest, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        w.writerows(sorted(rows.values(), key=lambda r: int(r["dosya"][5:-4])))
    print(f"indirilen: {ok}, zaten vardı: {skip}, başarısız: {fail}")
    return 0 if fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
