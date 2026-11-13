# Hafta 4: İlk ağ ölçümü

- **Tarih:** 2026-11-13 (UTC)
- **Konum:** `tr-ev-ubuntu`. Bu, ev bağlantısında çalışan WSL üzerindeki Ubuntu.
- **Kapsam:** SPK listesindeki 171 alan adı.
- **Gizlilik:** Belgede yalnızca toplu sayılar var. Alan adı ve IP yazılmaz.

Her alan adı için A, AAAA ve NS kaydı soruldu. Her soru iki çözücüye (Cloudflare ve Google) üç yoldan gönderildi: UDP, TCP ve DoH. Bu, alan adı başına 18 sorgu eder. Toplam 3.078 sorgu yapıldı ve saniyede en fazla 1 sorgu gönderildi.

## DNS cevapları

Her yolda 1.026 sorgu var. Sonuçlar üç yolda birebir aynı çıktı:

| Sonuç | UDP | TCP | DoH |
|---|---|---|---|
| Başarılı | 530 | 530 | 530 |
| Kayıt yok (NOERROR, boş) | 166 | 166 | 166 |
| NXDOMAIN | 192 | 192 | 192 |
| Hata (SERVFAIL) | 138 | 138 | 138 |
| Engel işareti | 0 | 0 | 0 |
| Kesik cevap (TC) | 0 | 0 | 0 |

Alan adı düzeyinde bakınca:

| Durum | Alan adı | Pay |
|---|---|---|
| Ad artık yok (NXDOMAIN) | Yaklaşık 32 | %19 |
| Çözücü cevap veremiyor (SERVFAIL), yetkili sunucu büyük olasılıkla bozuk | 23 | %13 |
| En az bir kayıt dönüyor | Kalanlar | |

## Araya girme var mı?

İki çözücü için de 148 alan adında UDP ve TCP cevapları DoH cevabıyla karşılaştırıldı:

| Çözücü | UDP ile DoH farklı | TCP ile DoH farklı |
|---|---|---|
| Cloudflare | 6 / 148 | 6 / 148 |
| Google | 8 / 148 | 11 / 148 |

Sonuçlar şöyle:

- **Engel işareti yok:** Hiçbir cevapta bilinen engel sayfası IP'si çıkmadı.
- **Sonuç türleri birebir aynı:** NXDOMAIN, SERVFAIL ve boş cevap sayıları üç yolda da tamamen eşit.
- **Farklar araya girmeye benzemiyor:**
  - TCP de şifresizdir, ama TCP ile DoH arasındaki fark UDP ile DoH arasındaki farktan küçük değil. Google'da daha da büyük.
  - Bu farklar, aynı adın birden fazla IP'si olduğunda sunucunun her seferinde farklı bir IP döndürmesiyle (yük dağıtımı) uyumlu.
  - Bu sonuç, servis sağlayıcının cevapları değiştirdiğini göstermiyor.

**Sınır:** Bu, tek bir gündeki ve tek bir servis sağlayıcıdaki ölçüm. Yük dağıtımı varsayımı henüz farklı IP'lerin ağ (ASN) bilgisi karşılaştırılarak doğrulanmadı.

## Barındırma (RIPEstat)

- **IP sayısı:** DoH cevaplarından, engel işareti taşımayan ve herkese açık olan 316 farklı IP çıktı. RIPEstat'tan 316'sı için de ağ bilgisi alındı.
- **Bulut ve CDN:** 316 IP'nin 293'ü (%93) bulut veya CDN ağında:

| Sağlayıcı | IP |
|---|---|
| AWS | 165 |
| Cloudflare | 120 |
| Akamai | 4 |
| Google | 3 |
| Microsoft | 1 |

Bu IP'lerin çoğu sitenin kendi sunucusu değil, önündeki CDN ya da bir park hizmeti. Port ve barındırma sinyalleri bu yüzden CDN dışındaki küçük bir grupta anlamlı olacak.

## Açık portlar (InternetDB)

| Sonuç | IP |
|---|---|
| Kayıtlı port bilgisi var | 208 |
| Bilgi yok (404) | 108 |

## Sonraki adım

- **Tekrarlanabilirlik:** Ölçüm her gün tekrarlanacak. Farkların her gün aynı alan adlarında mı çıktığı, yoksa rastgele mi dağıldığı yük dağıtımı varsayımını sınayacak.
- **Kontrol grubu:** Kontrol grubu Hafta 6'da aynı konumdan ve aynı çözücülerle ölçülecek. O zamana kadar bu sayılar karşılaştırma için değil, yalnızca betimleme için.
