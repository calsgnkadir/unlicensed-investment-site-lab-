#!/usr/bin/env python3
"""Static CT hacim olcumu (Asama 0). Python 3.10, yalnizca standart kutuphane.

Tek bir Let's Encrypt Static CT logunun monitoring adresine en fazla 25 istek atar.
Istekler arasi en az 1 sn. Tile'lar diske yazilmaz, bellekte islenir.
Anahtar kelime eslesmesi yalnizca SAYILIR; alan adlari yazdirilmaz ve saklanmaz.
Bu betik sinyal veya iddia karsilastirma mantigi icermez, yalnizca hacim olcer.

Kullanim: python -I ct_volume_probe.py [--wait 660] [--out sonuc.json]
"""
import argparse
import gzip
import json
import struct
import time
import urllib.request
from datetime import datetime, timezone

BASE = "https://mon.sycamore.ct.letsencrypt.org/2026h2/"
UA = "Mozilla/5.0 (compatible; ct-volume-probe/0.1; research)"
MAX_REQUESTS = 25
MIN_GAP = 1.1
KEYWORDS = ["yatirim", "borsa", "kripto", "forex", "trade", "invest", "capital", "menkul"]

_count = 0
_last = 0.0


def get(path):
    """Hiz sinirli GET. (govde, gzip_baytlari, content_encoding) doner."""
    global _count, _last
    if _count >= MAX_REQUESTS:
        raise RuntimeError("istek butcesi doldu")
    wait = MIN_GAP - (time.monotonic() - _last)
    if wait > 0:
        time.sleep(wait)
    req = urllib.request.Request(BASE + path, headers={
        "User-Agent": UA, "Accept-Encoding": "gzip, identity"})
    _count += 1
    with urllib.request.urlopen(req, timeout=60) as r:
        raw = r.read()
        enc = r.headers.get("Content-Encoding", "identity")
    _last = time.monotonic()
    body = gzip.decompress(raw) if enc == "gzip" else raw
    return body, len(raw), enc


def checkpoint():
    body, _, _ = get("checkpoint")
    lines = body.decode("utf-8").split("\n")
    # satir 1: origin, satir 2: agac boyutu, satir 3: kok ozet
    return int(lines[1]), datetime.now(timezone.utc).isoformat(timespec="seconds"), time.time()


def tile_path(n):
    s = "%03d" % (n % 1000)
    n //= 1000
    parts = [s]
    while n > 0:
        parts.insert(0, "x%03d" % (n % 1000))
        n //= 1000
    return "tile/data/" + "/".join(parts)


def san_names(cert):
    """Sertifika DER icinden SAN dNSName listesi (bellekte kalir)."""
    i = cert.find(b"\x55\x1d\x11")
    if i < 0:
        return []
    p = i + 3

    def rd_len(p):
        b = cert[p]
        if b < 0x80:
            return b, p + 1
        k = b & 0x7F
        return int.from_bytes(cert[p + 1:p + 1 + k], "big"), p + 1 + k

    if cert[p] == 0x01:  # critical BOOLEAN
        p += 3
    if cert[p] != 0x04:
        return []
    _, p = rd_len(p + 1)
    if cert[p] != 0x30:
        return []
    ln, p = rd_len(p + 1)
    end = p + ln
    names = []
    while p < end:
        tag = cert[p]
        l, q = rd_len(p + 1)
        if tag == 0x82:
            names.append(cert[q:q + l].decode("ascii", "ignore").lower())
        p = q + l
    return names


def parse_tile(data):
    """Veri tile'ini ayristirir. Yalnizca sayilar ve bellek ici ad listeleri doner."""
    p = 0
    entries = []
    while p < len(data):
        ts, et = struct.unpack_from(">QH", data, p)
        p += 10
        if et == 0:  # x509_entry
            ln = int.from_bytes(data[p:p + 3], "big")
            cert = data[p + 3:p + 3 + ln]
            p += 3 + ln
        elif et == 1:  # precert_entry
            p += 32  # issuer_key_hash
            ln = int.from_bytes(data[p:p + 3], "big")
            p += 3 + ln  # tbs
        else:
            raise ValueError("bilinmeyen entry_type")
        el = struct.unpack_from(">H", data, p)[0]
        p += 2 + el  # extensions
        if et == 1:
            ln = int.from_bytes(data[p:p + 3], "big")
            cert = data[p + 3:p + 3 + ln]
            p += 3 + ln
        fl = struct.unpack_from(">H", data, p)[0]
        p += 2 + fl  # certificate_chain
        entries.append((et, cert))
    return entries


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--wait", type=int, default=660, help="checkpoint okumalari arasi sn (>=600)")
    ap.add_argument("--out", default="ct_volume_sonuc.json")
    a = ap.parse_args()
    if a.wait < 600:
        raise SystemExit("en az 600 sn bekle")

    size1, t1_iso, t1 = checkpoint()
    full = size1 // 256
    # en yeni 4 tam tile + log boyunca dagitilmis 6 tile
    idx = [full - 1, full - 2, full - 3, full - 4]
    idx += [int(full * f) for f in (0.1, 0.25, 0.4, 0.55, 0.7, 0.85)]
    tiles = []
    kw_certs = 0
    total_certs = 0
    names_total = 0
    for n in idx:
        body, gz_len, enc = get(tile_path(n))
        ents = parse_tile(body)
        k = 0
        for et, cert in ents:
            names = san_names(cert)
            names_total += len(names)
            if any(kw in nm for nm in names for kw in KEYWORDS):
                k += 1
        kw_certs += k
        total_certs += len(ents)
        tiles.append({"index": n, "records": len(ents), "wire_bytes": gz_len,
                      "content_encoding": enc, "raw_bytes": len(body),
                      "precert": sum(1 for e in ents if e[0] == 1),
                      "keyword_certs": k})
        del body, ents

    left = a.wait - (time.time() - t1)
    if left > 0:
        time.sleep(left)
    size2, t2_iso, t2 = checkpoint()

    dt = t2 - t1
    rate_s = (size2 - size1) / dt
    res = {
        "log": BASE, "requests_used": _count,
        "checkpoint1": {"size": size1, "utc": t1_iso},
        "checkpoint2": {"size": size2, "utc": t2_iso},
        "elapsed_s": round(dt, 1), "delta_entries": size2 - size1,
        "rate_per_hour": round(rate_s * 3600), "rate_per_day": round(rate_s * 86400),
        "tiles": tiles, "sample_certs": total_certs, "keyword_certs": kw_certs,
        "dns_names_in_sample": names_total,
        "keywords": KEYWORDS,
    }
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, indent=1)
    print(json.dumps(res))


if __name__ == "__main__":
    main()
