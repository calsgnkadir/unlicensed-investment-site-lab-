"""extract_candidates.py için sentetik testler. Gerçek bülten kullanılmaz.

Public Suffix List paketi bu testlerde gerekmez: küçük bir SAHTE liste
(FakePSL) kullanılır. Gerçek PSL ile davranış ayrıca elle denenmelidir.
Çalıştırma: python -m unittest scripts/phase0/test_extract_candidates.py -v
"""
import contextlib
import csv
import io
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import extract_candidates as ec  # noqa: E402
import fitz  # noqa: E402

ARIAL = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "arial.ttf"


class FakePSL:
    SUF = {"com", "net", "org", "example", "tr", "com.tr", "org.tr", "gov.tr",
           "me", "ly", "gg", "co", "io"}

    def registered(self, host):
        labels = host.split(".")
        for i in range(len(labels)):
            if ".".join(labels[i:]) in self.SUF:
                return ".".join(labels[i - 1:]) if i > 0 else None
        return None

    def is_suffix(self, name):
        return name in self.SUF


def make_pdf(path, pages, turkish=False):
    doc = fitz.open()
    for text in pages:
        page = doc.new_page()
        if turkish:
            page.insert_text((40, 60), text, fontsize=10, fontname="arial", fontfile=str(ARIAL))
        else:
            page.insert_text((40, 60), text, fontsize=10)
    doc.save(str(path))
    doc.close()


class Base(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.inp = Path(self._tmp.name) / "girdi"
        self.out = Path(self._tmp.name) / "cikti"
        self.inp.mkdir()
        self.psl = FakePSL()

    def run_all(self, **kw):
        stats = ec.run(self.inp, self.out, self.psl, **kw)
        with open(self.out / "adaylar.csv", encoding="utf-8-sig", newline="") as f:
            rows = list(csv.DictReader(f))
        return stats, rows

    def one_page(self, text, turkish=False, name="b.pdf"):
        make_pdf(self.inp / name, [text], turkish)
        return self.run_all()


class TestTurkishCase(unittest.TestCase):
    def test_fold(self):
        self.assertEqual(ec.fold("EYLÜL"), "eylul")
        self.assertEqual(ec.fold("İKİ"), "iki")
        self.assertEqual(ec.fold("IŞIK"), "isik")
        self.assertEqual(ec.fold("Şubat"), "subat")

    def test_domain_lower(self):
        self.assertEqual(ec.domain_lower("YATIRIM.COM"), "yatirim.com")
        self.assertEqual(ec.domain_lower("İŞ.com"), "iş.com")


class TestDates(unittest.TestCase):
    def test_formats(self):
        self.assertEqual(ec.parse_dates("14.08.2026"), ["2026-08-14"])
        self.assertEqual(ec.parse_dates("14/08/2026"), ["2026-08-14"])
        self.assertEqual(ec.parse_dates("30 Eylül 2026"), ["2026-09-30"])
        self.assertEqual(ec.parse_dates("30 EYLÜL 2026"), ["2026-09-30"])
        self.assertEqual(ec.parse_dates("1 EKİM 2026"), ["2026-10-01"])
        self.assertEqual(ec.parse_dates("5 Kasım 2026"), ["2026-11-05"])
        self.assertEqual(ec.parse_dates("30 Eylul 2026"), ["2026-09-30"])

    def test_invalid(self):
        self.assertEqual(ec.parse_dates("31.02.2026"), [])
        self.assertEqual(ec.parse_dates("30 Eylül"), [])

    def test_meta_ok(self):
        no, t, w = ec.read_bulten_meta("SPK BULTENI 2026/46\n14.08.2026\nbaska satir")
        self.assertEqual((no, t, w), ("2026/46", "2026-08-14", []))

    def test_meta_unreadable_is_blank(self):
        no, t, w = ec.read_bulten_meta("Merhaba\nrastgele metin")
        self.assertEqual((no, t), ("", ""))
        self.assertEqual(len(w), 2)

    def test_meta_ambiguous_is_blank(self):
        no, t, w = ec.read_bulten_meta("BULTEN 2026/46\n14.08.2026\n15.08.2026\nBULTEN 2026/47")
        self.assertEqual((no, t), ("", ""))
        self.assertEqual(len(w), 2)

    def test_meta_far_from_word_not_used(self):
        text = "BULTEN\n\n\n\n\n2026/46 14.08.2026"
        self.assertEqual(ec.read_bulten_meta(text)[:2], ("", ""))


class TestCsvSafety(unittest.TestCase):
    def test_safe_cell(self):
        for bad in ("=1+1", "+1", "-1", "@SUM(1)", "  =x"):
            self.assertTrue(ec.safe_cell(bad).startswith("'"), bad)
        self.assertEqual(ec.safe_cell("ornek.com"), "ornek.com")
        self.assertNotIn("\n", ec.safe_cell("a\nb"))


class TestExtraction(Base):
    def by_norm(self, rows, norm):
        return [r for r in rows if r["normal_alan_adi"] == norm]

    def test_split_hyphen(self):
        _, rows = self.one_page("Erisim engeli: ornek-\nyatirim.example kararidir")
        r = self.by_norm(rows, "ornek-yatirim.example")
        self.assertEqual(len(r), 1)
        self.assertEqual(r[0]["tur"], "alan_adi")

    def test_split_dot(self):
        _, rows = self.one_page("adres ornek.\ncom listesi")
        self.assertEqual(len(self.by_norm(rows, "ornek.com")), 1)

    def test_sentence_end_not_joined(self):
        _, rows = self.one_page("Karar verildi.\nCom hakkinda bilgi.")
        self.assertEqual(rows, [])

    def test_full_url(self):
        _, rows = self.one_page("bkz https://ornek.com/yatirim/giris?x=1.")
        r = self.by_norm(rows, "ornek.com")
        self.assertEqual(r[0]["tur"], "tam_url")
        self.assertEqual(r[0]["ham_metin"], "https://ornek.com/yatirim/giris?x=1")

    def test_subdomain(self):
        _, rows = self.one_page("panel.ornek.com ve www.ornek.net")
        a = self.by_norm(rows, "ornek.com")[0]
        b = self.by_norm(rows, "ornek.net")[0]
        self.assertEqual(a["tur"], "alt_alan_adi")
        self.assertEqual(b["tur"], "alan_adi")  # yalnızca www.

    def test_social_and_short(self):
        _, rows = self.one_page("instagram.com/ornekhesap t.me/ornek bit.ly/abc123")
        turler = {r["normal_alan_adi"]: r["tur"] for r in rows}
        self.assertEqual(turler["instagram.com"], "sosyal_medya")
        self.assertEqual(turler["t.me"], "sosyal_medya")
        self.assertEqual(turler["bit.ly"], "kisa_baglanti")

    def test_official_kept_as_diger(self):
        _, rows = self.one_page("www.spk.gov.tr ve kap.org.tr adresleri")
        self.assertEqual({r["tur"] for r in rows}, {"diger"})
        self.assertEqual(len(rows), 2)

    def test_email_domain_not_candidate_and_masked(self):
        _, rows = self.one_page("iletisim: ali.veli@ornek-posta.com ve ornek.net")
        self.assertEqual(self.by_norm(rows, "ornek-posta.com"), [])
        self.assertEqual(len(self.by_norm(rows, "ornek.net")), 1)
        for r in rows:
            self.assertNotIn("ali.veli", r["baglam"])
            self.assertNotIn("@", r["baglam"])

    def test_phone_masked_in_context(self):
        _, rows = self.one_page("tel 0532 123 45 67 ornek.com")
        self.assertEqual(len(rows), 1)
        self.assertNotIn("123 45 67", rows[0]["baglam"])

    def test_obfuscated(self):
        _, rows = self.one_page("hxxps://ornek[.]com/x ve ornek(.)net")
        self.assertEqual(self.by_norm(rows, "ornek.com")[0]["tur"], "tam_url")
        self.assertEqual(len(self.by_norm(rows, "ornek.net")), 1)

    def test_non_domains_dropped_and_counted(self):
        stats, rows = self.one_page("tarih 14.08.2026 ip 1.2.3.4 surum 3.5.1")
        self.assertEqual(rows, [])
        self.assertGreaterEqual(stats["elenen"], 3)

    def test_formula_injection(self):
        _, rows = self.one_page("=HYPERLINK ornek.com\n-cmd ornek.net")
        self.assertTrue(rows)
        with open(self.out / "adaylar.csv", encoding="utf-8-sig", newline="") as f:
            for row in csv.reader(f):
                for cell in row:
                    self.assertNotIn(cell.lstrip()[:1], ("=", "+", "-", "@"), cell)

    def test_context_length(self):
        _, rows = self.one_page("a " * 30 + "ornek.com " + "b " * 30)
        self.assertLessEqual(len(rows[0]["baglam"]), 50 + 50 + len("ornek.com"))

    @unittest.skipUnless(ARIAL.exists(), "arial.ttf yok")
    def test_turkish_and_idn(self):
        _, rows = self.one_page("YATIRIM.COM ve yatırım-örnek.com ve İŞ.net", turkish=True)
        norms = {r["normal_alan_adi"] for r in rows}
        self.assertIn("yatirim.com", norms)
        self.assertIn("yatırım-örnek.com", norms)
        self.assertIn("iş.net", norms)

    def test_punycode_decoded(self):
        puny = "yatırım.com".encode("idna").decode("ascii")
        _, rows = self.one_page("adres " + puny)
        self.assertEqual([r["normal_alan_adi"] for r in rows], ["yatırım.com"])

    @unittest.skipUnless(ARIAL.exists(), "arial.ttf yok")
    def test_meta_from_pdf_turkish_month(self):
        make_pdf(self.inp / "b.pdf", ["SPK BÜLTENİ 2026/46\n30 EYLÜL 2026\nornek.com"], True)
        stats, rows = self.run_all()
        self.assertEqual(rows[0]["bulten_no"], "2026/46")
        self.assertEqual(rows[0]["bulten_tarihi"], "2026-09-30")
        self.assertEqual(rows[0]["sayfa"], "1")
        self.assertEqual(rows[0]["dogrulandi"], "")

    def test_meta_missing_gives_warning_and_blank(self):
        stats, rows = self.one_page("sadece ornek.com")
        self.assertEqual(rows[0]["bulten_no"], "")
        self.assertEqual(rows[0]["bulten_tarihi"], "")
        self.assertGreaterEqual(stats["uyari"], 2)
        self.assertTrue((self.out / "uyarilar.txt").read_text(encoding="utf-8").strip())

    def test_page_number(self):
        make_pdf(self.inp / "b.pdf", ["bos sayfa", "ornek.com"])
        _, rows = self.run_all()
        self.assertEqual(rows[0]["sayfa"], "2")


class TestRobustness(Base):
    def test_broken_pdf_does_not_stop_run(self):
        (self.inp / "a_bozuk.pdf").write_bytes(b"%PDF-1.4\nbu bir pdf degil \x00\xff" * 5)
        (self.inp / "b_bos.pdf").write_bytes(b"")
        make_pdf(self.inp / "c_iyi.pdf", ["ornek.com"])
        stats, rows = self.run_all()
        self.assertEqual(stats["pdf_toplam"], 3)
        self.assertEqual(stats["pdf_islendi"], 1)
        self.assertEqual(stats["pdf_atlandi"], 2)
        self.assertEqual(len(rows), 1)

    def test_size_limit(self):
        make_pdf(self.inp / "b.pdf", ["ornek.com"])
        stats, rows = self.run_all(max_bytes=10)
        self.assertEqual(rows, [])
        self.assertEqual(stats["pdf_atlandi"], 1)

    def test_page_limit(self):
        make_pdf(self.inp / "b.pdf", ["ornek.com", "ornek.net"])
        stats, rows = self.run_all(max_pages=1)
        self.assertEqual(len(rows), 1)

    def test_only_pdf_files_read(self):
        (self.inp / "not.txt").write_text("ornek.com", encoding="utf-8")
        stats, rows = self.run_all()
        self.assertEqual(stats["pdf_toplam"], 0)

    def test_long_token_is_fast(self):
        import time
        t0 = time.time()
        self.one_page("a" * 20000 + " ornek.com")
        self.assertLess(time.time() - t0, 5)

    def test_console_prints_only_numbers(self):
        make_pdf(self.inp / "b.pdf", ["ornek.com ornek-yatirim.example"])
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            code = ec.main([str(self.inp)], psl=self.psl, output_dir=self.out)
        self.assertEqual(code, 0)
        text = buf.getvalue()
        self.assertNotIn("ornek", text)
        self.assertIn("aday: 2", text)

    def test_missing_psl_package_is_explicit_error(self):
        with mock.patch.dict(sys.modules, {"publicsuffixlist": None}):
            with self.assertRaises(SystemExit) as cm:
                ec.load_psl()
        self.assertIn("publicsuffixlist", str(cm.exception))

    def test_missing_input_dir(self):
        with self.assertRaises(SystemExit):
            ec.run(self.inp / "yok", self.out, self.psl)


if __name__ == "__main__":
    unittest.main()
