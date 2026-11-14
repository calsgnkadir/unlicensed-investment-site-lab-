"""Etiketlerden rastgele 10 satırlık elle kontrol sayfası üretir.

Amaç: DECISIONS.md'deki "rastgele 10 satırlık elle örnek kontrolü" maddesi. Kontrolü yapan kişi her satırı
bültenin PDF'inde bulur ve dört soruyu cevaplar. Seçim sabit tohumla yapılır, böylece kimse satır seçemez ve
aynı seçim tekrar üretilebilir.

Girdi:  data/private/phase0/engellenenler.csv, data/private/phase0/bultenler/manifest.csv
Çıktı:  data/private/phase0/elle_kontrol.xlsx (git dışında; alan adı içerir)
"""
from __future__ import annotations

import csv
import hashlib
import sys
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.worksheet.datavalidation import DataValidation

ROOT = Path(__file__).resolve().parents[2]
PRIVATE = ROOT / "data" / "private" / "phase0"
SEED = "elle-kontrol-2026-11-15"
COUNT = 10


def safe(value: str) -> str:
    """Excel formül enjeksiyonunu önler: =, +, -, @ ile başlayan metnin başına tek tırnak konur."""
    return "'" + value if value[:1] in ("=", "+", "-", "@", "\t", "\r") else value


def main() -> int:
    rows = list(csv.DictReader((PRIVATE / "engellenenler.csv").open(encoding="utf-8")))
    pdfs = {r["kaynak_url"]: r["dosya"] for r in csv.DictReader((PRIVATE / "bultenler" / "manifest.csv").open(encoding="utf-8"))}

    # Sabit tohumlu sıralama: her satırın (alan adı + bülten) özetine göre dizilir, ilk 10 alınır.
    def key(r: dict) -> str:
        return hashlib.sha256(f"{SEED}:{r['alan_adi']}:{r['bulten_no']}".encode()).hexdigest()

    picked = sorted(rows, key=key)[:COUNT]

    wb = Workbook()
    ws = wb.active
    ws.title = "kontrol"
    headers = [
        "sira", "alan_adi", "bulten_no", "pdf_dosyasi", "karar_tarihi", "kategori",
        "1_adres_bultende_var_mi", "2_erisim_engeli_bolumunde_mi", "3_tarih_dogru_mu", "4_kategori_dogru_mu", "not",
    ]
    ws.append(headers)
    for i, r in enumerate(picked, 1):
        ws.append([i, safe(r["alan_adi"]), r["bulten_no"], pdfs.get(r["kaynak_url"], "?"),
                   r["karar_tarihi"], r["kategori"], "", "", "", "", ""])

    dv = DataValidation(type="list", formula1='"EVET,HAYIR,EMIN_DEGIL"', allow_blank=True)
    ws.add_data_validation(dv)
    dv.add(f"G2:J{COUNT + 1}")

    fill = PatternFill("solid", fgColor="FFF2CC")
    for col in "GHIJ":
        for row in range(2, COUNT + 2):
            ws[f"{col}{row}"].fill = fill
    for cell in ws[1]:
        cell.font = Font(bold=True)
        cell.alignment = Alignment(wrap_text=True, vertical="top")
    widths = [5, 30, 10, 14, 12, 12, 16, 18, 14, 16, 30]
    for idx, w in enumerate(widths):
        ws.column_dimensions[chr(ord("A") + idx)].width = w
    ws.freeze_panes = "B2"

    help_ws = wb.create_sheet("nasil")
    for line in [
        "Her satır için bültenin PDF'ini aç: data/private/phase0/bultenler/<pdf_dosyasi>",
        "PDF'te Ctrl+F ile alan adını ara.",
        "1: Adres bültende birebir geçiyor mu? (alt alan adı veya yol ile geçiyorsa da EVET, nota yaz)",
        "2: Adres 'erişim engeli' kararı verilen listede mi? (suç duyurusu tablosu değil)",
        "3: karar_tarihi, bültenin ilk sayfasındaki tarihle aynı mı?",
        "4: kategori doğru mu? Liste yalnızca kripto diyorsa kripto, yalnızca kaldıraçlı diyorsa kaldıraçlı, ikisi birden veya tür yoksa diğer.",
        "Sarı hücrelerde açılır listeden EVET / HAYIR / EMIN_DEGIL seç. Bitince dosyayı kaydet.",
    ]:
        help_ws.append([line])
    help_ws.column_dimensions["A"].width = 120

    out = PRIVATE / "elle_kontrol.xlsx"
    wb.save(out)
    print(f"{len(picked)} satır yazıldı: {out.relative_to(ROOT)} (tohum {SEED})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
