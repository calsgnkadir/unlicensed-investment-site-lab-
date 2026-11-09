# Aşama 0: Aday çıkarma yardımcısı

Geçici bir yardımcıdır, proje koduna girmez. SPK bülten PDF'lerindeki alan adı benzeri metinleri bir CSV'ye döker, böylece elle doğrulama hızlanır.

**Bu betik doğrulamanın yerine geçmez.** Hiçbir satır "engellendi" demek değildir. Her satır yalnızca bir adaydır. Bir alan adının bültende erişim engeli kararında yer alıp almadığına sen bakarsın.

## Kurulum

```
pip install -r scripts/phase0/requirements.txt
```

`PyMuPDF` ve `publicsuffixlist` gerekir. `publicsuffixlist` kurulu değilse betik açık bir hata verip durur. Çalışırken ağa çıkmaz.

## Çalıştırma

```
python scripts/phase0/extract_candidates.py <bulten_pdf_klasoru>
```

PDF'leri kendin indirip bir klasöre koy. Betik indirme yapmaz, hiçbir siteye istek atmaz. Klasörün içindeki `.pdf` dosyaları okunur, alt klasörlere bakılmaz.

Çıktılar (`data/private/`, git'te yok sayılır):

- `data/private/phase0/adaylar.csv`
- `data/private/phase0/uyarilar.txt`

Konsola yalnızca sayılar yazılır. `baglam` sütununda kişi adı olabilir, bu yüzden dosyalar başka yere kopyalanmaz.

Testler (ağ yok, sentetik PDF):

```
cd scripts/phase0
python -m unittest test_extract_candidates -v
```

## Çıktıyı okuma

| Sütun | Anlamı |
|---|---|
| `dogrulandi` | Boş. Sen doldurursun. |
| `ham_metin` | PDF'te bulunan metin (bölünmüş satırlar birleştirilmiş, `hxxp` ve `[.]` haliyle) |
| `normal_alan_adi` | Kayıtlı alan adı: küçük harf, `[.]` düzeltilmiş, punycode Unicode'a çevrilmiş, alt alan adı ve yol atılmış |
| `tur` | Aşağıya bak |
| `bulten_no`, `bulten_tarihi` | Sayfa 1'den okunduysa dolu (`2026/46`, `2026-08-14`). Okunamazsa boş. |
| `sayfa` | PDF sayfa numarası (1'den başlar) |
| `baglam` | Eşleşmenin çevresinde yaklaşık 100 karakter. E-posta ve telefon `[e-posta]`, `[telefon]` ile maskelenir ama kişi adı maskelenmez. |
| `dosya` | Kaynak PDF adı |

`tur` değerleri. Hiçbir satır atılmaz, yalnızca işaretlenir:

- `alan_adi`: yalın kayıtlı alan adı (`www.` ile olsa da)
- `alt_alan_adi`: kayıtlı alan adının önünde başka etiket var
- `tam_url`: `http(s)://` ya da yol/sorgu içeriyor
- `sosyal_medya`: bilinen sosyal medya ve mesajlaşma adresleri
- `kisa_baglanti`: bilinen kısaltma servisleri
- `diger`: `gov.tr` ve `kap.org.tr` gibi bilinen resmi adresler. Bunlar çoğunlukla engellenen site değildir, ama silinmedi.

Satırlarda `'` ile başlayan hücre görürsen: CSV formül enjeksiyonunu önlemek için `=`, `+`, `-`, `@` ile başlayan hücrelerin önüne `'` konur.

`uyarilar.txt`: okunamayan bülten no/tarihi, atlanan ya da bozuk PDF, metni olmayan (taranmış) sayfa 1 gibi durumlar.

## Doğrulama adımları (senin işin)

1. `uyarilar.txt` dosyasını oku. Atlanan PDF'leri ve boş `bulten_no` / `bulten_tarihi` olan dosyaları elle kontrol et.
2. `adaylar.csv` dosyasını aç, her satır için `dosya` ve `sayfa` bilgisiyle bülteni aç.
3. Metnin gerçekten erişim engeli kararı bölümünde olup olmadığına bak. Karar türünü, tarihini ve bülten numarasını bültenden oku. Betiğin bulduğu bülten no/tarihine güvenme, bültenle karşılaştır.
4. Karardaysa `dogrulandi` sütununa işaret koy. Karar dışındaysa (bilgi notu, başka bir liste, resmi adres) boş bırak ya da `hayir` yaz.
5. Doğruladıklarını `data/private/phase0/engellenenler.csv` dosyasına ROADMAP Bölüm 2.1'deki sütunlarla geçir (`alan_adi`, `bulten_no`, `karar_tarihi`, `kategori`, ...). Tam URL, alt alan adı, sosyal medya ve kısa bağlantı için kuralını notla.
6. Betiğin kaçırdığı olabilir. Birkaç bülteni baştan sona gözle de tara.

## Bilinen sınırlar

- Alan adı biçimine uymayan ya da uzantısı Public Suffix List'te olmayan metinler (tarih, IP, sürüm numarası) aday sayılmaz. Sayıları konsolda `elenen` olarak görünür. PSL'de olmayan yeni bir uzantıyı sessizce atlayabilir.
- Satır sonunda tireyle bölünen adreste tire korunur. Gerçekte tire yoksa (yalnızca satır kırma tiresi ise) yanlış alan adı çıkar. Satır ortasından, tiresiz bölünen adresler yakalanmaz. Doğruluğu bültende kontrol et.
- Tablo hücreleri yapışık çıkarsa (`ornek.com14.08.2026`) alan adı kaçabilir ya da bozuk çıkabilir.
- Büyük `I` harfli adlarda Türkçe ayrım bilinemez: `YATIRIM.COM` `yatirim.com` olur, `yatırım.com` değil.
- Kayıtlı alan adı PSL'in özel bölümünü de sayar (`x.blogspot.com` gibi). Bu durumda `x.blogspot.com` kayıtlı alan adı olur.
- Bülten no/tarihi yalnızca 1. sayfada "bülten" sözcüğünün 2 satır çevresinden okunur. Birden fazla farklı değer varsa boş bırakılır.
- Boyut sınırları: dosya 50 MB, 1500 sayfa, sayfa başına 1 milyon karakter, dosya başına 50.000 aday.
- Taranmış (görüntü) PDF'ten metin çıkmaz. OCR yoktur.
- Testler küçük sahte bir Public Suffix listesiyle çalışır. Gerçek paketle davranış ilk gerçek çalıştırmada kontrol edilmelidir. Gerçek PSL'de `.example` gibi ayrılmış uzantılar bulunmaz.
