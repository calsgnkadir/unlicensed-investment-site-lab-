# Aşama 0 sonucu: Devam et / bırak

Tarih: 2026-11-13. Kaynak: Yerel veritabanı, `scripts/phase0/phase0_summary.py` çıktısı. Bu belgede yalnızca toplu sayılar var. Alan adı yazılmaz.

**Kısa cevap:** Çekirdek proje için devam. Dört şarttan üçü tam karşılandı. Static CT hacmi ölçüldü, ancak hedef makinede bir günlük gerçek ölçüm henüz yapılmadı. Ağ katmanı kararı Hafta 4'e kaldı.

Bu sonuçlar bir sinyalin işe yaradığını göstermez. Henüz kontrol grubu yok, bu yüzden yanlış alarm oranı ölçülmedi. Buradaki sayılar yalnızca şu soruya cevap verir: "Engellenen sitelerin karardan önce iz bırakıp bırakmadığına bakılabilir mi?"

## Veri

- 2026 SPK bültenleri: 78 bülten tarandı, 21 bültenden 175 kayıt çıktı. Bu kayıtlarda 171 farklı alan adı var. Yöntem için [DECISIONS.md](../DECISIONS.md) dosyasına bak.
- **Birim:** Alan adı. Birden fazla bültende geçen bir alan adı için en erken karar tarihi alındı.
- **crt.sh:** 171 alan adının hepsi için yanıt alındı. İlk turdaki 7 hata yeniden denendi ve hepsi başarılı oldu. Toplam 17.214 sertifika kaydı saklandı.
- **Wayback CDX:** 171 alan adının hepsi için yanıt alındı. 108 alan adında kayıt bulundu, 63 alan adı kesin boş döndü, hata olmadı. Toplam 135.067 arşiv kaydı saklandı. 8 alan adı sayfa sınırına takıldı, bu alan adlarının arşiv kayıtları eksik olabilir.

## Sertifika: Karardan önce iz var mı?

| Ölçü | Sonuç |
|---|---|
| Karardan önce en az bir sertifika | 171 / 171 (%100) |
| İlk sertifika ile karar arası, ortanca | 148 gün |
| Çeyrekler | 36 ve 380 gün |
| En az / en çok | 2 gün / 5961 gün |
| Bülten ortancalarının ortancası (21 bülten) | 110 gün |

| İlk sertifika kararla arasında | Alan adı |
|---|---|
| 28 gün veya daha az | 40 (%23) |
| 60 gün veya daha az | 52 (%30) |
| 180 gün veya daha az | 96 (%56) |
| 365 gün veya daha az | 127 (%74) |
| 365 günden fazla | 44 (%26) |

Bülten başına ortanca süre büyük ölçüde değişiyor:

| Bülten | Alan adı | Ortanca gün | 1 yıldan yeni |
|---|---|---|---|
| 2026/1 | 24 | 216 | 16 |
| 2026/3 | 9 | 219 | 6 |
| 2026/4 | 8 | 62,5 | 8 |
| 2026/6 | 9 | 14 | 8 |
| 2026/8 | 14 | 175 | 10 |
| 2026/9 | 14 | 34,5 | 9 |
| 2026/10 | 6 | 12,5 | 4 |
| 2026/12 | 31 | 299 | 21 |
| 2026/14 | 2 | 14 | 2 |
| 2026/16 | 1 | 110 | 1 |
| 2026/18 | 5 | 147 | 5 |
| 2026/20 | 2 | 1023,5 | 0 |
| 2026/28 | 6 | 133,5 | 6 |
| 2026/31 | 5 | 77 | 4 |
| 2026/36 | 6 | 77 | 4 |
| 2026/41 | 1 | 49 | 1 |
| 2026/45 | 7 | 125 | 6 |
| 2026/49 | 15 | 176 | 10 |
| 2026/59 | 2 | 72 | 2 |
| 2026/67 | 3 | 106 | 3 |
| 2026/77 | 1 | 182 | 1 |

Kategoriye göre ortanca süre: diğer 146,5 gün (112 alan adı), kaldıraçlı 141,5 gün (48), kripto 178 gün (11). Kripto grubu küçük olduğu için bu fark yorumlanmamalı.

**Okuma:**

- %100 sonucu zayıf bir kapıdır, başarı kanıtı değildir. Bugün hemen her site sertifika aldığı için kontrol grubunda da bu oran %100'e yakın çıkacaktır.
- Erken uyarı için asıl önemli olan sayı süre dağılımı. Alan adlarının yaklaşık dörtte birinde ilk sertifika ile karar arasında 28 günden az var. Bu grupta erken uyarının fark yaratacağı pencere çok dar.
- Alan adlarının dörtte birinde ilk sertifika bir yıldan eski, en eskisi yaklaşık 16 yıllık. Bu alan adları büyük olasılıkla daha önce başka sahipler tarafından kullanıldı veya uzun süre başka bir amaçla açık kaldı. Bu yüzden "ilk sertifika" her zaman "sitenin açıldığı gün" anlamına gelmez.

## Arşiv: Karardan önce içerik var mı?

| Grup | Herhangi bir kayıt | Karardan önce 200 yanıtlı kayıt |
|---|---|---|
| Tümü | 108 / 171 (%63) | 82 / 171 (%48) |
| İlk sertifikası 1 yıldan yeni | 65 / 127 (%51) | 39 / 127 (%31) |
| İlk sertifikası 1 yıldan eski | 43 / 44 (%98) | 43 / 44 (%98) |

**Okuma:**

- Yeni açılan sitelerin yalnızca üçte birinde karar öncesi okunabilir bir arşiv kaydı var. Arşiv içeriği bu yüzden ana sinyal olamaz. Ana yükü sertifika, alan adı ve ağ sinyalleri taşımalı.
- Eski alan adlarındaki karar öncesi kayıtlar önceki sahibe ait olabilir. Bu kayıtlar sitenin yatırım içeriği taşıdığını göstermez.
- Wayback sorgusu `matchType=domain` kullanır, yani alt alan adlarını da sayar.

**Hata düzeltmesi:** Özet betiğinin ilk sürümü, Wayback'in kesin olarak boş döndüğü alan adlarını "bilinmiyor" sayıyordu. Bu yüzden arşiv oranı yanlışlıkla %100 çıktı. Hata bu belge yazılırken düzeltildi. Kesin boş sonuç artık "kayıt yok" sayılıyor, yalnızca hata alınan sonuç "bilinmiyor" sayılıyor.

## Devam şartları

| Şart | Durum |
|---|---|
| En az 50 alan adı bültenden doğrulandı | Karşılandı: 171. İki bağımsız inceleme ve kurala dayalı bir kontrol yapıldı. Rastgele 10 satırlık elle örnek kontrolü henüz yapılmadı. |
| En az 35 alan adında karardan önce sertifika var | Karşılandı: 171. Bu zayıf bir kapı. |
| Süre dağılımı çıkarıldı ve bülten başına ayrıldı | Karşılandı. Tablolar yukarıda. |
| Bir günlük Static CT hacmi ölçüldü ve makineye sığıyor | Kısmen karşılandı. 15 dakikalık ölçüme göre günde 15–16 GB indirme gerekiyor ([STATIC_CT_HACIM.md](STATIC_CT_HACIM.md)). Disk sorunu yok, çünkü yalnızca sayaçlar ve adaylar saklanıyor. Ubuntu makinede 72 saatlik kesintisiz ölçüm bekleniyor. |

## Açık kalanlar

- **Arşiv içeriği kontrolü:** İlk sertifikası bir yıldan yeni olan 20 alan adının karar öncesi arşiv kaydı açılacak. Amaç, kaydın gerçekten yatırım içeriği mi yoksa park sayfası mı olduğunu görmek. Henüz yapılmadı.
- **Ağ katmanı:** DNS, RIPEstat ve InternetDB denemeleri Hafta 4'e kaldı. DNS ölçüm konumu kararı da o hafta verilecek.
- **Kontrol grubu:** Aynı dönemde sertifika almış ve listede olmayan alan adlarından oluşacak. Bu grup olmadan hiçbir oran yorumlanamaz.

## Veri sınırları

- Yalnızca 2026 bültenleri, tek bir ülke ve tek bir kurum kullanıldı. 171 alan adı küçük bir örnek.
- Etiket, "SPK erişim engeli listesinde" demektir, "dolandırıcı" demek değildir.
- crt.sh, bütün CT loglarını eksiksiz göstermeyebilir. `%.alanadi` sorgusu alt alan adlarının sertifikalarını da kapsar.
- Bültende yanlış yazılmış bir adres veride de yanlış kalır.
- Bu belge yatırım tavsiyesi değildir.
