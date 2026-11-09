# SPK 2026 bülten çıkarımı (Aşama 0)

Tarih: 10 Kasım 2026

**Bu sayılar elle doğrulanmamış adaylardır.** Hiçbiri "engellendi" demek değildir. Bu belgede alan adı yoktur.

## Yöntem

1. `scripts/phase0/download_bulletins.py`, `spk.gov.tr/spk-bultenleri/2026-yili-spk-bultenleri` listesinden (sayfa 1-4) 2026 bülten PDF'lerinin bağlantılarını okudu. Yalnızca `spk.gov.tr` alanındaki, `.pdf` ile biten, `2026-N.pdf` adlı bağlantılar alındı. İstekler arası 3,5 sn, genel User-Agent, dosya başına 20 MB sınırı, `%PDF` başlık kontrolü. Bültende geçen alan adlarına istek atılmadı.
2. `scripts/phase0/extract_candidates.py` PDF metninden alan adı benzeri metinleri çıkardı. Kayıtlı alan adı için gerçek `publicsuffixlist` paketi kullanıldı.
3. Dosyalar `data/private/phase0/` altında, git dışında.

## Sayılar

| Ölçü | Sayı |
|---|---|
| İndirilen PDF | 78 |
| İndirilemeyen PDF | 0 (listede 78 bülten var, 2026-5 listede yok) |
| İşlenen sayfa | 314 |
| Toplam aday satırı | 1024 |
| alan_adi | 323 |
| alt_alan_adi | 5 |
| tam_url | 287 |
| sosyal_medya | 0 |
| kisa_baglanti | 0 |
| diger (resmi adres) | 409 |
| Elenen (alan adına benzemeyen) | 2097 |
| Uyarı | 88 |

Toplam sayı yanıltıcıdır. Şu ikisi çıkarılınca gerçek aday azalır:

- 314 `alan_adi` satırı sayfa altlığındaki "8.Km" adres ifadesinden geliyor (tek, aynı metin, her sayfada). Bu, yanlış alarmdır.
- 409 `diger` satırı resmi `gov.tr` adresidir (tek farklı adres).

Geriye kalan: 301 satır (287 tam_url, 9 alan_adi, 5 alt_alan_adi), 192 farklı kayıtlı alan adı, 24 bültende. Aynı alan adı bir bültende birden çok kez geçebiliyor.

- Aday çıkan bülten: 78 (hepsinde altlık ve resmi adres var). Gerçek adayı olan bülten: 24.
- Birden fazla bültende geçen alan adı: 9 (192 içinden), en çok 2 bültende.
- Bülten numarası okunamayan: 12 (birden fazla değer bulundu, boş bırakıldı). Dosya adından numara zaten biliniyor.
- Bülten tarihi okunamayan: 76 (66 bulunamadı, 10 birden fazla). Tarih okuyucu zayıf, elle okunmalı.

## Bilinen sınırlar

- Elle doğrulama yapılmadı. Her satırın erişim engeli kararı bölümünde olup olmadığı bilinmiyor. Yanlış alarm oranı: ölçülmedi, [N]. Tek bilinen kalıp: adres altlığı.
- Çıkarım hataları türden tarif edilirse: adres ve "Km" gibi kısaltmalar alan adı sanılıyor; tarih okuma çoğu bültende başarısız.
- Satır ortasında bölünen adresler yakalanmaz, kaçırılan alan adı sayısı bilinmiyor.
- Taranmış (görüntü) sayfa yok sayıldı, OCR yok. Böyle sayfa sayısı ölçülmedi.
- Bu sayılar yalnızca 2026 bültenleri içindir (78 PDF). Önceki yıllar kapsam dışı.
- Yatırım tavsiyesi değildir.
