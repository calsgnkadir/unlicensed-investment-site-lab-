"""Doğrulama tablosundaki EVET satırlarını yükleyicinin beklediği CSV'ye çevirir.

Girdi:  data/private/phase0/dogrulama.xlsx
Çıktı:  data/private/phase0/engellenenler.csv

Yalnızca "karar" sütunu EVET olan ve zorunlu alanları (karar_tarihi, kategori,
kaynak_url) dolu satırlar aktarılır. Eksik satırların yalnızca satır numarası yazılır.
"""
from __future__ import annotations

import csv
import datetime as dt
import sys
from pathlib import Path

from openpyxl import load_workbook

ROOT = Path(__file__).resolve().parents[2]
PHASE0 = ROOT / "data" / "private" / "phase0"
SHEET = PHASE0 / "dogrulama.xlsx"
OUTPUT = PHASE0 / "engellenenler.csv"
COLUMNS = ["alan_adi", "bulten_no", "karar_tarihi", "kategori", "ilk_sertifika",
           "sertifika_sayisi", "arsiv_kaydi", "kaynak_url"]


def as_date(value) -> str:
    if isinstance(value, (dt.datetime, dt.date)):
        return value.strftime("%Y-%m-%d")
    text = str(value or "").strip()
    for fmt in ("%Y-%m-%d", "%d.%m.%Y", "%d/%m/%Y"):
        try:
            return dt.datetime.strptime(text, fmt).strftime("%Y-%m-%d")
        except ValueError:
            pass
    return ""


def main() -> int:
    ws = load_workbook(SHEET, read_only=True).active
    rows = ws.iter_rows(values_only=True)
    header = [str(h or "").strip() for h in next(rows)]
    col = {name: header.index(name) for name in header}

    out, missing, counts = [], [], {"EVET": 0, "HAYIR": 0, "EMIN_DEGIL": 0, "bos": 0}
    for number, row in enumerate(rows, start=2):
        decision = str(row[col["karar"]] or "").strip().upper()
        counts[decision if decision in counts else "bos"] += 1
        if decision != "EVET":
            continue
        record = {
            "alan_adi": str(row[col["alan_adi"]] or "").lstrip("'").strip(),
            "bulten_no": str(row[col["bulten_no"]] or "").strip(),
            "karar_tarihi": as_date(row[col["karar_tarihi"]]),
            "kategori": str(row[col["kategori"]] or "").strip(),
            "ilk_sertifika": "", "sertifika_sayisi": "", "arsiv_kaydi": "",
            "kaynak_url": str(row[col["kaynak_url"]] or "").lstrip("'").strip(),
        }
        if not all(record[k] for k in ("alan_adi", "bulten_no", "karar_tarihi", "kategori", "kaynak_url")):
            missing.append(number)
            continue
        out.append(record)

    with OUTPUT.open("w", encoding="utf-8", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=COLUMNS)
        writer.writeheader()
        writer.writerows(out)

    print(f"EVET: {counts['EVET']}, HAYIR: {counts['HAYIR']}, EMIN_DEGIL: {counts['EMIN_DEGIL']}, bos: {counts['bos']}")
    print(f"aktarilan: {len(out)}, eksik alanli EVET satiri: {len(missing)} {missing[:20]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
