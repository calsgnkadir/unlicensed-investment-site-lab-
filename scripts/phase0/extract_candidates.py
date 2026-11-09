#!/usr/bin/env python3
"""Aşama 0 geçici yardımcısı: SPK bülten PDF'lerinden alan adı ADAYLARI çıkarır.

Bu betik karar vermez ve elle doğrulamanın yerine geçmez. Çıkan her satır
yalnızca bir adaydır. Proje koduna girmez.

Kullanım:
    python extract_candidates.py <bulten_pdf_klasoru>

Çıktı (git'te yok sayılır): data/private/phase0/adaylar.csv
Uyarılar:                  data/private/phase0/uyarilar.txt
Ağ erişimi yoktur. Konsola yalnızca sayılar yazılır.
"""
from __future__ import annotations

import csv
import datetime
import re
import sys
import unicodedata
from pathlib import Path

try:
    import fitz  # PyMuPDF
except ImportError:  # pragma: no cover
    fitz = None

SUTUNLAR = ["dogrulandi", "ham_metin", "normal_alan_adi", "tur", "bulten_no",
            "bulten_tarihi", "sayfa", "baglam", "dosya"]

# Güvensiz girdi sınırları
MAX_PDF_BYTES = 50 * 1024 * 1024
MAX_PAGES = 1500
MAX_PAGE_CHARS = 1_000_000
MAX_ROWS_PER_FILE = 50_000
CTX_HALF = 50

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = REPO_ROOT / "data" / "private" / "phase0"

# Platform listeleri (uzantı listesi değil). Kayıtlı alan adına göre bakılır.
SOSYAL = {
    "instagram.com", "facebook.com", "fb.com", "fb.me", "twitter.com", "x.com",
    "t.me", "telegram.me", "telegram.org", "telegram.dog", "wa.me",
    "whatsapp.com", "youtube.com", "youtu.be", "tiktok.com", "linkedin.com",
    "discord.gg", "discord.com", "threads.net", "reddit.com", "snapchat.com",
    "pinterest.com", "vk.com", "medium.com", "linktr.ee",
}
KISA = {
    "bit.ly", "bitly.com", "tinyurl.com", "t.co", "goo.gl", "is.gd", "v.gd",
    "cutt.ly", "rb.gy", "shorturl.at", "ow.ly", "buff.ly", "tiny.cc", "s.id",
    "t.ly", "lnkd.in", "rebrand.ly", "shorturl.com", "tr.ee", "shre.ink",
    "clck.ru",
}
RESMI_KAYITLI = {"kap.org.tr"}


# ---------------------------------------------------------------- Public Suffix
class PublicSuffix:
    """publicsuffixlist paketini saran ince katman. Elle uzantı listesi yok."""

    def __init__(self, psl):
        self._psl = psl

    def registered(self, host: str):
        """Kayıtlı alan adı (eTLD+1) ya da None (uzantı bilinmiyor / host bir uzantı)."""
        try:
            return self._psl.privatesuffix(host)
        except Exception:
            return None

    def is_suffix(self, name: str) -> bool:
        try:
            return self._psl.publicsuffix(name) == name
        except Exception:
            return False


def load_psl() -> PublicSuffix:
    try:
        from publicsuffixlist import PublicSuffixList
    except ImportError:
        raise SystemExit(
            "HATA: 'publicsuffixlist' paketi kurulu değil. Kayıtlı alan adı için "
            "Public Suffix List gerekiyor, elle liste kullanılmaz.\n"
            "Kurulum: pip install -r scripts/phase0/requirements.txt")
    return PublicSuffix(PublicSuffixList(accept_unknown=False))


# ---------------------------------------------------------------- Türkçe harf
_TR_LOWER = str.maketrans({"İ": "i", "I": "ı"})
_ASCII_FOLD = str.maketrans("ışğüöçâîû", "isguocaiu")
_DOMAIN_LOWER = str.maketrans({"İ": "i", "I": "i"})


def fold(s: str) -> str:
    """Türkçe büyük/küçük harfe doğru küçültür, sonra ASCII'ye indirir (1 harf = 1 harf)."""
    return s.translate(_TR_LOWER).lower().translate(_ASCII_FOLD)


def domain_lower(s: str) -> str:
    """Alan adlarında büyük harf I ASCII'dir (i), İ de i olur."""
    return s.translate(_DOMAIN_LOWER).lower()


# ---------------------------------------------------------------- Metin temizliği
_DROP = dict.fromkeys(map(ord, "​‌‍⁠﻿"))
# U+00AD: bazı PDF yazı tipleri düz tireyi bu kodla verir, o yüzden tireye çevrilir.
_DASHES = dict.fromkeys(map(ord, "­‐‑‒–—―−"), "-")
_OTHER_WS = re.compile(r"[^\S\n]")
_CTRL = re.compile(r"[\x00-\x08\x0b-\x1f\x7f]")


def clean_text(t: str) -> str:
    t = unicodedata.normalize("NFKC", t)
    t = t.translate(_DROP).translate(_DASHES)
    t = t.replace("\r\n", "\n").replace("\r", "\n")
    t = _OTHER_WS.sub(" ", t)
    return _CTRL.sub(" ", t)


_DOT = r"(?:\.|\[\.\]|\(\.\)|\{\.\}|\[dot\]|\(dot\))"
_LABEL = r"[^\W_]+(?:-+[^\W_]+)*"
_DOT_IN_TOKEN = re.compile(r"[^\W_](?:\.|\[\.\])[^\W_]")


def _tail_token(line: str) -> str:
    parts = line.rstrip().split(" ")
    return parts[-1] if parts else ""


def _should_join(cur: str, nxt: str, psl) -> bool:
    tail = _tail_token(cur)
    nxt_s = nxt.lstrip()
    if len(tail) < 2 or not nxt_s:
        return False
    head = nxt_s.split(" ")[0]
    if tail.endswith("-") and tail[-2].isalnum() and _DOT_IN_TOKEN.search(head):
        return True  # ornek-\nyatirim.com
    for dot in (".", "[.]"):
        if tail.endswith(dot) and len(tail) > len(dot) and tail[-len(dot) - 1].isalnum():
            core = head.rstrip(".,;:)]}")
            if (core and core.islower() and psl.is_suffix(core)
                    and any(c.isalpha() for c in tail)):
                return True  # ornek.\ncom
    return False


def join_lines(text: str, psl) -> str:
    """Satır sonunda bölünmüş adresleri birleştirir. Diğer satır sonlarına dokunmaz."""
    lines = text.split("\n")
    out = []
    i = 0
    while i < len(lines):
        cur = lines[i]
        while i + 1 < len(lines) and _should_join(cur, lines[i + 1], psl):
            cur = cur.rstrip() + lines[i + 1].lstrip()
            i += 1
        out.append(cur)
        i += 1
    return "\n".join(out)


# ---------------------------------------------------------------- Kişi verisi maskesi
_EMAIL_RE = re.compile(
    r"(?<![\w.+%\-])[\w.+%\-]+@\n?[ ]?(?:" + _LABEL + r"(?:\.|\[\.\]))+" + _LABEL, re.I)
_PHONE_RE = re.compile(
    r"(?<![\w./])(?:\+ ?90|0)?[ \-]?\(?\d{3}\)?[ \-]?\d{3}[ \-]?\d{2}[ \-]?\d{2}"
    r"(?![\w-]|\.[^\W_])")


def mask_personal(text: str) -> str:
    text = _EMAIL_RE.sub("[e-posta]", text)
    return _PHONE_RE.sub("[telefon]", text)


# ---------------------------------------------------------------- Aday bulma
_URL_RE = re.compile(
    r"(?<![\w@.\-])"
    r"(?P<scheme>h(?:tt|xx)ps?(?::|\[:\])//)?"
    r"(?P<host>" + _LABEL + r"(?:" + _DOT + _LABEL + r")+)"
    r"(?P<port>:\d{1,5})?"
    r"(?P<path>[/?#][^\s<>\"']*)?", re.I)
_DOT_VARIANT = re.compile(r"\[\.\]|\(\.\)|\{\.\}|\[dot\]|\(dot\)", re.I)
_TRAIL = ".,;:!?)]}…"


def _decode_idn(host: str) -> str:
    out = []
    for lab in host.split("."):
        if lab.startswith("xn--"):
            try:
                lab = lab.encode("ascii").decode("idna")
            except Exception:
                pass
        out.append(lab)
    return ".".join(out)


def _is_official(host: str, reg: str) -> bool:
    return (host == "gov.tr" or host.endswith(".gov.tr") or reg.endswith(".gov.tr")
            or reg in RESMI_KAYITLI)


def find_candidates(text: str, psl):
    """Dönüş: ([(start, end, ham, normal, tur)], elenen_sayisi)"""
    found, dropped = [], 0
    for m in _URL_RE.finditer(text):
        end = m.end()
        path = m.group("path") or ""
        if path:
            stripped = path.rstrip(_TRAIL)
            end -= len(path) - len(stripped)
            path = stripped
        host = domain_lower(_DOT_VARIANT.sub(".", m.group("host")))
        host = _decode_idn(host)
        if len(host) > 253 or any(len(p) > 63 for p in host.split(".")):
            dropped += 1
            continue
        reg = psl.registered(host)
        if not reg:
            dropped += 1  # tarih, sürüm no, uzantısı kayıtlı olmayan metin vb.
            continue
        reg = domain_lower(reg)
        has_scheme = bool(m.group("scheme"))
        has_path = path not in ("", "/")
        bare = host[4:] if host.startswith("www.") else host
        if _is_official(host, reg):
            tur = "diger"
        elif reg in SOSYAL:
            tur = "sosyal_medya"
        elif reg in KISA:
            tur = "kisa_baglanti"
        elif has_scheme or has_path:
            tur = "tam_url"
        elif bare != reg:
            tur = "alt_alan_adi"
        else:
            tur = "alan_adi"
        found.append((m.start(), end, text[m.start():end], reg, tur))
    return found, dropped


# ---------------------------------------------------------------- Bülten no / tarih
_AYLAR = {"ocak": 1, "subat": 2, "mart": 3, "nisan": 4, "mayis": 5, "haziran": 6,
          "temmuz": 7, "agustos": 8, "eylul": 9, "ekim": 10, "kasim": 11, "aralik": 12}
_NO_RE = re.compile(r"(?<!\d)(20\d{2})\s*/\s*(\d{1,3})(?!\d)")
_DATE_NUM = re.compile(r"(?<![\d.])(\d{1,2})[./](\d{1,2})[./](20\d{2})(?!\d)")
_DATE_TXT = re.compile(r"(?<!\d)(\d{1,2})\s+(" + "|".join(_AYLAR) + r")\s+(20\d{2})(?!\d)")


def _safe_date(y, m, d):
    try:
        return datetime.date(int(y), int(m), int(d)).isoformat()
    except ValueError:
        return None


def parse_dates(line: str):
    f = fold(line)
    vals = []
    for m in _DATE_NUM.finditer(f):
        v = _safe_date(m.group(3), m.group(2), m.group(1))
        if v:
            vals.append(v)
    for m in _DATE_TXT.finditer(f):
        v = _safe_date(m.group(3), _AYLAR[m.group(2)], m.group(1))
        if v:
            vals.append(v)
    return vals


def _near_bulten(lines):
    idx = set()
    for i, ln in enumerate(lines):
        if "bulten" in fold(ln):
            idx.update(range(max(0, i - 2), min(len(lines), i + 3)))
    return idx


def read_bulten_meta(first_page_text: str):
    """(no, tarih, [uyarılar]). Okunamazsa boş döner, tahmin edilmez.
    Yalnızca 'bülten' sözcüğünün 2 satır çevresindeki değerler kabul edilir;
    birden fazla farklı değer varsa boş bırakılır."""
    lines = first_page_text.split("\n")
    near = _near_bulten(lines)
    warns = []
    nos, dates = set(), set()
    for i in near:
        for m in _NO_RE.finditer(lines[i]):
            nos.add(f"{m.group(1)}/{int(m.group(2))}")
        dates.update(parse_dates(lines[i]))
    no = tarih = ""
    if len(nos) == 1:
        no = next(iter(nos))
    else:
        warns.append("bülten numarası okunamadı" if not nos
                     else "bülten numarası birden fazla bulundu, boş bırakıldı")
    if len(dates) == 1:
        tarih = next(iter(dates))
    else:
        warns.append("bülten tarihi okunamadı" if not dates
                     else "bülten tarihi birden fazla bulundu, boş bırakıldı")
    return no, tarih, warns


# ---------------------------------------------------------------- CSV
def safe_cell(v) -> str:
    """CSV formül enjeksiyonunu önler (=, +, -, @ ile başlayanın önüne ' koyar)."""
    s = _CTRL.sub(" ", str(v)).replace("\n", " ").replace("\t", " ")
    if s.lstrip()[:1] in ("=", "+", "-", "@"):
        s = "'" + s
    return s


def write_csv(path: Path, rows) -> None:
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, lineterminator="\r\n")
        w.writerow(SUTUNLAR)
        for r in rows:
            w.writerow([safe_cell(r.get(c, "")) for c in SUTUNLAR])


def _safe_name(s: str) -> str:
    return re.sub(r"[^\x20-\x7e -￿]", "?", s)


# ---------------------------------------------------------------- PDF işleme
def process_pdf(path: Path, psl, stats, warns, max_bytes, max_pages):
    name = path.name
    try:
        if path.stat().st_size > max_bytes:
            warns.append(f"{name}: dosya boyut sınırını aşıyor, atlandı")
            stats["pdf_atlandi"] += 1
            return []
        doc = fitz.open(str(path))
    except Exception as e:
        warns.append(f"{name}: açılamadı ({type(e).__name__}), atlandı")
        stats["pdf_atlandi"] += 1
        return []
    rows = []
    try:
        if not doc.is_pdf or doc.needs_pass:
            warns.append(f"{name}: PDF değil ya da şifreli, atlandı")
            stats["pdf_atlandi"] += 1
            return []
        n_pages = doc.page_count
        if n_pages > max_pages:
            warns.append(f"{name}: {n_pages} sayfa var, ilk {max_pages} işlendi")
            n_pages = max_pages
        no = tarih = ""
        for pno in range(n_pages):
            try:
                raw = doc[pno].get_text("text")[:MAX_PAGE_CHARS]
            except Exception as e:
                warns.append(f"{name}: sayfa {pno + 1} okunamadı ({type(e).__name__})")
                continue
            text = clean_text(raw)
            if pno == 0:
                no, tarih, w = read_bulten_meta(text)
                warns.extend(f"{name}: {x}" for x in w)
                if not text.strip():
                    warns.append(f"{name}: ilk sayfada metin yok (taranmış görüntü olabilir)")
            stats["sayfa"] += 1
            text = mask_personal(join_lines(text, psl))
            cands, dropped = find_candidates(text, psl)
            stats["elenen"] += dropped
            for s, e, ham, normal, tur in cands:
                if len(rows) >= MAX_ROWS_PER_FILE:
                    warns.append(f"{name}: aday sınırına ulaşıldı, kalanı atlandı")
                    break
                ctx = " ".join((text[max(0, s - CTX_HALF):s] + text[s:e] + text[e:e + CTX_HALF]).split())
                rows.append({"dogrulandi": "", "ham_metin": ham, "normal_alan_adi": normal,
                             "tur": tur, "bulten_no": no, "bulten_tarihi": tarih,
                             "sayfa": pno + 1, "baglam": ctx, "dosya": name})
        stats["pdf_islendi"] += 1
    except Exception as e:
        warns.append(f"{name}: işlenirken hata ({type(e).__name__}), kısmen işlendi")
        stats["pdf_atlandi"] += 1
    finally:
        doc.close()
    return rows


def run(input_dir, output_dir, psl, max_bytes=MAX_PDF_BYTES, max_pages=MAX_PAGES):
    if fitz is None:
        raise SystemExit("HATA: PyMuPDF (fitz) kurulu değil. pip install -r scripts/phase0/requirements.txt")
    fitz.TOOLS.mupdf_display_errors(False)
    input_dir, output_dir = Path(input_dir), Path(output_dir)
    if not input_dir.is_dir():
        raise SystemExit("HATA: girdi klasörü bulunamadı.")
    files = sorted(p for p in input_dir.iterdir()
                   if p.suffix.lower() == ".pdf" and p.is_file() and not p.is_symlink())
    stats = {"pdf_toplam": len(files), "pdf_islendi": 0, "pdf_atlandi": 0,
             "sayfa": 0, "elenen": 0}
    warns, rows = [], []
    for p in files:
        rows.extend(process_pdf(p, psl, stats, warns, max_bytes, max_pages))
    output_dir.mkdir(parents=True, exist_ok=True)
    write_csv(output_dir / "adaylar.csv", rows)
    (output_dir / "uyarilar.txt").write_text(
        "\n".join(_safe_name(w) for w in warns) + ("\n" if warns else ""), encoding="utf-8")
    stats["aday"] = len(rows)
    stats["uyari"] = len(warns)
    for t in ("alan_adi", "alt_alan_adi", "tam_url", "sosyal_medya", "kisa_baglanti", "diger"):
        stats["tur_" + t] = sum(1 for r in rows if r["tur"] == t)
    return stats


def main(argv=None, psl=None, output_dir=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 1:
        print("Kullanım: python extract_candidates.py <bulten_pdf_klasoru>")
        return 2
    psl = psl or load_psl()
    stats = run(argv[0], output_dir or DEFAULT_OUT, psl)
    for k, v in stats.items():
        print(f"{k}: {v}")
    if stats["uyari"]:
        print("Uyarılar uyarilar.txt dosyasında (data/private/phase0/).")
    print("Bunlar aday. Doğrulama elle yapılır.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
