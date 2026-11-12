"""Aday listesinden elle doğrulama tablosu (Excel) üretir.

Girdi:  data/private/phase0/adaylar.csv ve data/private/phase0/bultenler/manifest.csv
Çıktı:  data/private/phase0/dogrulama.xlsx

Her (alan adı, bülten) çifti tek satır olur. Sayfa altlığındaki adres ifadesi ve
resmi adresler ("diger" türü) çıkarılır. Karar vermez: "karar" sütununu sen doldurursun.
Ağ erişimi yok. Konsola yalnızca sayı yazar.
"""
from __future__ import annotations

import csv
import sys
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.worksheet.datavalidation import DataValidation

ROOT = Path(__file__).resolve().parents[2]
PHASE0 = ROOT / "data" / "private" / "phase0"
CANDIDATES = PHASE0 / "adaylar.csv"
MANIFEST = PHASE0 / "bultenler" / "manifest.csv"
OUTPUT = PHASE0 / "dogrulama.xlsx"

KEEP_TYPES = {"alan_adi", "alt_alan_adi", "tam_url"}
HEADERS = ["karar", "alan_adi", "bulten_no", "karar_tarihi", "kategori", "sayfalar",
           "baglam", "ham_metinler", "not", "pdf", "kaynak_url"]
WIDTHS = [12, 32, 11, 13, 12, 9, 70, 40, 30, 14, 20]


def safe(value: str) -> str:
    """Hücreye yazılan güvenilmez metnin formül olarak çalışmasını önler."""
    value = (value or "").replace("\r", " ").replace("\n", " ").strip()
    return "'" + value if value[:1] in ("=", "+", "-", "@") else value


def bulletin_no(file_name: str) -> str:
    stem = Path(file_name).stem  # 2026-14
    year, _, number = stem.partition("-")
    return f"{year}/{number}" if year.isdigit() and number.isdigit() else ""


def main() -> int:
    if not CANDIDATES.exists() or not MANIFEST.exists():
        print("adaylar.csv veya manifest.csv bulunamadı.")
        return 1

    with MANIFEST.open(encoding="utf-8-sig", newline="") as fh:
        urls = {row["dosya"]: row["kaynak_url"] for row in csv.DictReader(fh)}

    groups: dict[tuple[str, str], dict] = {}
    with CANDIDATES.open(encoding="utf-8-sig", newline="") as fh:
        for row in csv.DictReader(fh):
            domain = (row.get("normal_alan_adi") or "").strip().lower()
            if row.get("tur") not in KEEP_TYPES or not domain or domain.endswith(".km"):
                continue
            key = (domain, row["dosya"])
            g = groups.setdefault(key, {"pages": set(), "raw": [], "context": row.get("baglam", "")})
            if row.get("sayfa"):
                g["pages"].add(int(row["sayfa"]))
            raw = (row.get("ham_metin") or "").strip()
            if raw and raw not in g["raw"]:
                g["raw"].append(raw)

    def sort_key(item):
        (domain, file_name), _ = item
        no = bulletin_no(file_name)
        number = int(no.split("/")[1]) if no else 0
        return (number, domain)

    wb = Workbook()
    ws = wb.active
    ws.title = "dogrulama"
    ws.append(HEADERS)
    for cell in ws[1]:
        cell.font = Font(bold=True)
        cell.fill = PatternFill("solid", fgColor="DDDDDD")

    for (domain, file_name), g in sorted(groups.items(), key=sort_key):
        pages = sorted(g["pages"])
        pdf_path = PHASE0 / "bultenler" / file_name
        ws.append([
            "", safe(domain), bulletin_no(file_name), "", "",
            ", ".join(str(p) for p in pages), safe(g["context"]),
            safe(" | ".join(g["raw"][:5])), "", "PDF aç", safe(urls.get(file_name, "")),
        ])
        link = ws.cell(row=ws.max_row, column=HEADERS.index("pdf") + 1)
        link.hyperlink = pdf_path.as_uri() + (f"#page={pages[0]}" if pages else "")
        link.font = Font(color="0563C1", underline="single")

    last = ws.max_row
    decision = DataValidation(type="list", formula1='"EVET,HAYIR,EMIN_DEGIL"', allow_blank=True)
    category = DataValidation(type="list", formula1='"kripto,kaldıraçlı,diğer"', allow_blank=True)
    ws.add_data_validation(decision)
    ws.add_data_validation(category)
    decision.add(f"A2:A{last}")
    category.add(f"E2:E{last}")

    for index, width in enumerate(WIDTHS, start=1):
        ws.column_dimensions[ws.cell(row=1, column=index).column_letter].width = width
    for row in ws.iter_rows(min_row=2, min_col=7, max_col=8):
        for cell in row:
            cell.alignment = Alignment(wrap_text=True, vertical="top")
    ws.freeze_panes = "C2"
    ws.auto_filter.ref = f"A1:K{last}"

    if OUTPUT.exists():
        print("dogrulama.xlsx zaten var; üzerine yazılmadı. Silip tekrar çalıştır.")
        return 1
    wb.save(OUTPUT)
    print(f"satir: {last - 1}, bulten: {len({k[1] for k in groups})}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
