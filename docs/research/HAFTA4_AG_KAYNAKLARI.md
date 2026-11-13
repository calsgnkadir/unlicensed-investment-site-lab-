# Hafta 4 Ağ Kaynakları: RIPEstat, InternetDB, Türkiye DNS engeli, açık çözücüler

Tarih: 13 Kasım 2026. Tüm erişim tarihleri 2026-11-13. Bu belge karar önerisidir, hukuki görüş değildir.

## Nasıl okunur

- **[doğrulandı]** Sayfa bu çalışmada doğrudan açıldı ve ilgili bölüm okundu. (Sayfa bir araç tarafından özetlenerek okundu, tam metin satır satır kontrol edilmedi. Yayımdan önce önemli şartları elle bir kez daha oku.)
- **[özet]** Yalnızca arama sonucu özetinden görüldü. Doğrulanmış sayılmaz.
- **[doğrulanamadı]** Aranıp bulunamadı ya da sayfa açılamadı. "Yok" demek değildir.
- Bu belgede SPK veya USOM listesindeki hiçbir alan adı yok. Hiçbir şüpheli siteye veya hedef IP'ye istek atılmadı.

## Karar özeti

| Konu | Sonuç |
|---|---|
| RIPEstat | Kullanılabilir. IP için `network-info` yalnızca bugünü verir. Geçmiş için `prefix-overview` (`query_time`) ve `routing-history` (`starttime`/`endtime`) var. |
| InternetDB | Ticari olmayan kullanım serbest, haftalık anlık görüntü, geçmiş belgelenmemiş, saklama şartı yazılı değil. |
| Türkiye'de DNS engeli | Engelin DNS cevabını değiştirerek uygulandığı bugün de güncel kaynaklarla görülüyor. Açık çözücülere giden 53 numaralı UDP trafiğinin bugün yakalandığına dair **güncel ölçüm bulunamadı**. Eski kanıt (2014, 2018) var. Ev bağlantısından ölçümün geçerliliği **kanıtlanmış değil**, kendi doğrulamamız şart. |
| Açık çözücüler | Üçünün de gizlilik metni var, düşük hızlı araştırma sorgusunu yasaklayan bir şart bulunamadı. Google 1500 sorgu/sn sınırı yazıyor, biz çok altındayız. |
| Ücretsiz geçmiş DNS | Yeni uygun kaynak bulunamadı. Önceki sonuç (çıkar) değişmedi. |

---

## 1. RIPEstat Data API

### 1.1 Hız sınırı ve sourceapp [doğrulandı]

Kaynak: <https://stat.ripe.net/docs/data_api> (erişim 2026-11-13)

- İstek sayısına üst sınır yok. Düzenli olarak günde 1000'den fazla isteyeceksen kayıt yaptırmak isteniyor. [doğrulandı]
- Aynı IP'den en fazla 8 eşzamanlı istek. [doğrulandı]
- `sourceapp` isteğe bağlı ama düzenli kullanımda öneriliyor. Projeye özgü bir ad olmalı (harf, rakam, tire, alt çizgi). Adı `stat@ripe.net`'e bildirmek isteniyor. [doğrulandı]
- Tarayıcı dışı istemcilerde veri hacmi uyarısını kapatmak için `data_overload_limit=ignore` var. `cache=ignore` yalnızca bayat sonuçta kullanılmalı. [doğrulandı]
- Resmi bir "saniyede en çok X istek" sayısı yok. Biz günlük birkaç yüz isteğin altında kalırız, ama nazik olmak için tek bağlantı ve saniyede 1 istek öneririm (benim önerim).

### 1.2 Kullanım şartları [doğrulandı]

Kaynak: <https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/> (erişim 2026-11-13)

- Araştırma sonucu yayımlanırsa RIPE NCC'ye, farklıysa asıl veri kaynağına atıf yapılır.
- Araştırma kullanımında sonuçları `stat@ripe.net`'e bildirmek gerekiyor.
- Hizmeti veya verisini yeniden paketleme, derleme, dağıtma yasak. Ham RIPEstat çıktısını açık veri seti yapma. Özet sayı yayımlamanın bu yasağa girip girmediği belirsiz. Emin olmak için RIPE'a sorulabilir.
- Ticari kullanım için yazılı izin gerekir.
- Saklama süresi ve biçimi konusunda şartlar sessiz. [doğrulanamadı: yazılı kural yok]

### 1.3 IP -> ASN + önek uçları

| Uç | Girdi | Çıktı | Geçmiş |
|---|---|---|---|
| `network-info` | `resource` (IP, zorunlu) | `asns`, `prefix` | Zaman parametresi belgelenmemiş. RISwhois'in 8 saatlik dökümlerine dayanıyor, yani **yalnızca güncel**. [doğrulandı] Kaynak: <https://stat.ripe.net/docs/data-api/api-endpoints/network-info/> |
| `prefix-overview` | `resource` (belge örneği önek), `query_time` (ISO 8601 veya Unix) | `asns` (asn + holder), `block`, `announced`, `query_time` | `query_time` ile geçmiş sorgulanabilir. Ne kadar geriye gittiği ve her alanın geçmişte dolu olup olmadığı belgede yazmıyor. [doğrulandı] Kaynak: <https://stat.ripe.net/docs/data-api/api-endpoints/prefix-overview/> |
| `routing-history` | `resource` (önek, **IP** veya ASN), `starttime`, `endtime`, `min_peers` (varsayılan 10) | Köken ASN'ye göre önek ve zaman çizelgesi (başlangıç/bitiş) | Evet, RIS verisiyle. Örnekleme aralığı belgede yok. [doğrulandı] Kaynak: <https://stat.ripe.net/docs/data-api/api-endpoints/routing-history/> |

Notlar:

- "Karar tarihinde bu IP hangi ASN'deydi" sorusu için en güvenli yol `routing-history` (IP'yi `resource` olarak verip karar gününü içeren aralık). `prefix-overview` + `query_time` de olur ama `resource` olarak tek IP kabul edip etmediği belgede açık değil. [doğrulanamadı] İlk denemede küçük bir deneme yapıp karşılaştır.
- Önceki belgede (DATA_SOURCES Bölüm 5) "`prefix-overview` zaman parametresi doğrulanmadı" yazıyordu. Şimdi belgede `query_time` olarak göründü.
- `routing-history` alan adı-IP eşlemesini vermez. IP'nin o gün hangi alan adına bağlı olduğu bilinmez. Bu eksik geçmiş DNS ile kapanmıyor (Bölüm 5).
- Hiçbir uçta bir API sorgusu atılmadı, yalnızca belge sayfaları açıldı. Yani gerçek yanıt biçimi ve gecikme bu çalışmada ölçülmedi.

---

## 2. Shodan InternetDB

Kaynaklar (erişim 2026-11-13): <https://internetdb.shodan.io/>, <https://book.shodan.io/developer-apis/internetdb/>

| Soru | Cevap |
|---|---|
| Ticari olmayan mı? | Ana sayfa "ticari olmayan kullanım için ücretsiz" diyor. Para kazanıyorsan kurumsal lisans gerekiyor. [doğrulandı] Resmi kitap sayfası ticari/ticari olmayan ayrımından söz etmiyor. [doğrulandı] |
| Hız sınırı | Resmi kitap: saniyede 10.000 isteğe kadar patlama izni. Ana sayfa sayı vermiyor. Daha önce üçüncü taraf kaynaklar 1 istek/sn demişti, çelişki sürüyor. [doğrulandı: resmi 10.000/sn burst] |
| Güncelleme | Haftalık, UTC gece yarısı (kitap). Ana sayfa "haftada bir" diyor. [doğrulandı] |
| Alanlar | `ip`, `ports`, `cpes`, `hostnames`, `tags`, `vulns`. Banner dönmüyor. `hostnames` ters DNS ve sertifika bilgisinden geliyor. `vulns` doğrulanmış ve doğrulanmamış açıkları birlikte içeriyor. [doğrulandı] |
| Geçmiş | Belgede yok. "Geçmiş sunmaz" diye de yazılı değil. [doğrulanamadı] |
| Saklama izni | Sayfalar bu konuda sessiz. Yalnızca SQLite indirmesi kurumsal müşterilere. Ayrı bir hizmet şartları metni bulunamadı. [doğrulanamadı] |

Proje için sonuç: `ports` ve `tags` alınır, `vulns` hiç saklanmaz (proje kuralı), her kayda sorgu zamanı eklenir. Geçmiş yok varsayılır, haftalık kendi kaydımız geçmiş olur. Saklama şartı yazılı olmadığı için "ticari olmayan araştırma, sonuçlar yalnızca toplu yayımlanıyor" notu README'ye konabilir.

---

## 3. Türkiye'de DNS engeli (2024-2026) ve açık çözücülere müdahale

Bu bölüm ev bağlantısından DNS ölçümünün geçerliliğini belirler. **Kısa sonuç: iki ayrı soru var ve cevapları farklı.**

### 3.1 Soru A: Engel DNS cevabını değiştirerek mi uygulanıyor?

Evet, bugün de güncel kaynaklarda görülüyor, ama kapsam sınırlı.

- OONI, Mart 2025 ve Eylül 2025 olaylarını analiz etti. 8 Eylül 2025'te YouTube'a erişimin TurkNet (AS12735) üzerinde DNS ile engellendiğini, çözümlemenin engelle ilişkilendirilen bir IP döndürdüğünü yazıyor. Aynı rapor Turkcell Superonline'da TLS el sıkışması zaman aşımı gözlemini ve diğer platformlarda bant daraltma bulgusunu da içeriyor. Kaynak: <https://ooni.org/post/2025-turkiye-throttling-social-media/> [özet] (sayfa doğrudan açılamadı: bağlantı sıfırlandı. İçerik arama özetlerinden alındı, iki farklı özette tutarlı.)
- OONI'nin DNS parmak izi listesi aynı IP'yi (195.175.254.2) Türkiye'de 2014'ten beri kullanılan zehirli DNS cevabı olarak tanımlıyor diye özetleniyor. Kaynak: <https://github.com/ooni/blocking-fingerprints/blob/main/fingerprints_dns.csv> [özet] (sayfa satırları göstermedi, doğrulanamadı.)
- OONI'nin 2026 tarihli bir raporu (Kasım 2025 - Nisan 2026 verisi), tek bir sitede Türk Telekom çözücülerinin iki IP döndürdüğünü ve engelin 2019'dan beri sürdüğünü yazıyor diye özetleniyor. Kaynak: <https://ooni.org/post/2026-women-on-web-blocked/> [özet] (sayfa doğrudan açılamadı.)
- Bir kişisel blog (yazım 2025-03-19, son değişiklik 2026-04-26) Türkiye'deki 6 işletmecide DNS engeli bulunduğunu söylüyor, işletmeciye göre ve belirsiz işaretlerle. Ölçüm çalışması değil, tek yazar. Kaynak: <https://blog.slonk.ing/posts/tr-censorship> [doğrulandı: sayfa okundu, güvenilirliği düşük]
- İFÖD EngelliWeb 2025 raporu: 2025 sonuna kadar 1.505.484 alan adı ve site engelli, 1.285.923 alan adı 5651 sayılı Kanun'un 8. maddesine dayanan idari tedbirle. Yöntem (DNS mi, IP mi) hakkında bu çalışmada bilgi bulunamadı. Kaynak: <https://ifade.org.tr/en/reports/engelliweb-2025/about/> [özet] ve özet haber: <https://www.diken.com.tr/?p=1737322> [özet] [doğrulanamadı: yöntem]

Sonuç A: Türkiye'de en az bazı işletmecilerin DNS cevabını değiştirdiği güncel (2025-2026) OONI bulgularıyla destekleniyor. Bu yüzden DNS ile engelli bir alan adı için alınan cevap, gerçek barındırma bilgisini yansıtmayabilir. Hangi işletmeci ve hangi karar türü (SPK) için bu geçerli, **doğrulanamadı**. OONI örnekleri büyük platformlar, SPK sitelerini kapsamıyor.

### 3.2 Soru B: 8.8.8.8 / 1.1.1.1 gibi açık çözücülere giden 53 numaralı UDP trafiği yakalanıyor mu?

**Güncel (2024-2026) bir ölçüm bulunamadı.** Eski kanıt şöyle:

- 2014: Google, Türk ISP'lerin çoğunun Google Public DNS'i yakaladığını bildirdi. Kaynak: <https://security.googleblog.com/2014/03/googles-public-dns-intercepted-in-turkey.html> [özet] (arama özetinde görüldü, sayfa doğrudan açılmadı.)
- 2014: BGPmon ve RIPE Atlas analizleri, 8.8.8.8 için yönlendirme ile sahte sunucu kurulduğunu gösteriyor. Kaynaklar: <https://www.bgpmon.net/turkey-hijacking-ip-addresses-for-popular-global-dns-providers/>, <https://labs.ripe.net/author/emileaben/a-ripe-atlas-view-of-internet-meddling-in-turkey/> [özet]
- 2018: Turkey Blocks, 1.1.1.1 başlatıldığı gün Wikipedia ve bir yayıncıya giden UDP 53 sorgularının "sessizce düşürüldüğünü" ve filtrenin 8.8.8.8 ile Quad9'a uygulananla benzer olduğunu yazdı. **DNS-over-TLS bu filtreden etkilenmedi** (doğru IP döndü), ama site yine de ayrı bir TCP filtresiyle erişilemezdi. DoH sonucu raporda yok. Kaynak: <https://turkeyblocks.org/2018/04/02/new-cloudflare-dns-service-filtered-turkey/> [doğrulandı]
- Kişisel blog (2025/2026): bir işletmecide yalnızca 1.1.1.1'in engellendiğini, 8.8.8.8 ve 9.9.9.9'a geçilmesinin önerildiğini, bazı işletmecilerde şifreli DNS veya VPN gerektiğini yazıyor. Ölçüm kanıtı yok. Kaynak: <https://blog.slonk.ing/posts/tr-censorship> [doğrulandı, düşük güven]
- Cloudflare topluluk forumunda yaklaşık bir ay önce yazıldığı belirtilen bir iş parçacığı: Türkiye'de bir kullanıcı 8.8.8.8'den Cloudflare bloğunda erişilemeyen bir IP aldığını, yerel ISP çözücüsünün başka sonuç verdiğini yazıyor diye özetleniyor. Tek kullanıcı hikayesi. Sayfa doğrudan açılamadı (403). Kaynak: <https://community.cloudflare.com/t/google-dns-routes-to-blocked-ips-turkish-visitors-cant-access-sites/968678> [özet] [doğrulanamadı] (Bu, 53 numaralı port yakalaması olduğunu göstermez.)
- Arama sonuçlarında yer alan bir TorrentFreak haberi (Ağustos 2024) Malezya ile ilgili, Türkiye ile değil. Karıştırma. Kaynak: <https://torrentfreak.com/isps-hijack-cloudflare-google-dns-requests-ending-site-blocking-workarounds-240807/> [doğrulandı: sayfa okundu, Malezya]

DoH/DoT durumu: 2018'de DoT'un etkilenmediği bulundu. 2024-2026 için DoH veya DoT'un Türkiye'de açık çözücülere karşı engellendiğine veya yakalandığına dair ölçüm **bulunamadı**. Cloudflare topluluğundaki "engellendi" başlığı da güncel kanıt değil: <https://community.cloudflare.com/t/turkey-blocked-1-1-1-1-dns-months-ago-hijacked/32360> [özet].

Akademik: ICLab (2017-2018 verisi) Türkiye'de DNS manipülasyonunu ölçtü, ama bu eski. Kaynak: <https://arxiv.org/pdf/1907.04245> [özet]. 2022-2024 aralığında Türkiye'ye özgü, yakalama (transparent proxy) konusunda hakemli çalışma bulamadım. [doğrulanamadı]

### 3.3 Karar için ne anlama geliyor

1. Güncel kanıt "yok" değil, "bulunamadı". Eski kanıt (2014, 2018) açık çözücü yakalamasının veya filtresinin **Türkiye'de uygulanmış olabildiğini** gösteriyor. Bu yüzden ev bağlantısından yapılan ölçümü varsayılan olarak geçerli sayma.
2. Ölçüm geçerliliği işletmeciye göre değişebilir. Tek işletmeciden alınan sonuç diğerleri için geçerli olmaz.
3. SPK listesindeki siteler için, ev bağlantısında alınan cevap şu üç şeyden biri olabilir: gerçek cevap, engel IP'si, boş cevap veya zaman aşımı. Bu üçünü "barındırma bilgisi" saymak yanlış olur.
4. Önerilen doğrulama adımları (hepsi kendi izin verdiğimiz sınırlar içinde, hedef siteye istek yok):
   - Aynı sorguyu üç açık çözücüye (8.8.8.8, 1.1.1.1, 9.9.9.9) UDP ve TCP ile sor ve cevap, gecikme ve zaman aşımı farkını kaydet. Fark yakalama belirtisidir.
   - Kendi kontrol alan adımız (bizim olan, kimseye ait olmayan bir test adı) varsa, sorgunun gerçekten seçilen çözücüden mi, yoksa ISP çözücüsünden mi geldiğini yetkili sunucu kaydından kontrol et. Bu yöntem genel bilgidir, bu çalışmada bir kaynakla doğrulanmadı. [doğrulanamadı]
   - Açık çözücülerin "ben kimim" TXT sorguları (çözücüyü kimliklendirme) kullanılabilir. Adları ve davranışları bu çalışmada doğrulanmadı. [doğrulanamadı]
   - Aynı alan adlarını Türkiye dışında bir yerden (örneğin küçük bir bulut sunucusundan) ölçüp karşılaştır. Bu, "engel cevabı" ile "gerçek cevap"ı ayırmanın en güçlü yolu.
   - Bilinen engel cevabı IP'si (yukarıda OONI'de geçen) cevapta görülürse kayıt "engel işareti" diye etiketlenir, barındırma bilgisi olarak girmez. Bu IP listesinin güncelliği ayrıca doğrulanmalı.
5. Her kayıtta ölçüm konumu, sunucu, taşıma (UDP/TCP/DoT), zaman ve cevap kodu zorunlu kalmalı (NETWORK_PLAN Bölüm 5 zaten böyle diyor).
6. DNS ölçümü ev bağlantısında yapılırsa sonuçlar "bu işletmeciden bu zamanda görünen cevap" olarak yazılmalı, genel doğru olarak değil.

**Dürüst sonuç:** Ev bağlantısının geçerli olup olmadığına şu an kanıtla cevap veremiyorum. Doğru yaklaşım, yukarıdaki doğrulama deneyini Hafta 4'te bir kez çalıştırıp sonucu rapora yazmak. Deney olumsuz çıkarsa DNS özellikleri yalnızca ikincil olur ve ağ özellikleri RIPEstat IP/ASN verisine yaslanır, ama o da hangi IP'nin alındığına bağlı olduğu için aynı soruyu taşır.

---

## 4. Açık çözücüler: şartlar, gizlilik, hız

| Çözücü | Gizlilik / kayıt | Hız sınırı | Araştırma şartı |
|---|---|---|---|
| Cloudflare 1.1.1.1 | Kaynak IP'ler kalıcı depolamaya yazılmıyor, kısaltılıp 25 saat içinde siliniyor. Sorgu adı ve türü gibi alanlar işletme ve araştırma için tutuluyor, 25 saat içinde silinir (örneklenmiş toplam veriler hariç). APNIC Labs anonim verilere sorgu adı dahil araştırma anlaşmasıyla erişebiliyor. [doğrulandı] <https://developers.cloudflare.com/1.1.1.1/privacy/public-dns-resolver/> | Ağ işletmeciler sayfası: tipik kullanımda sınır görülmemesi bekleniyor, nadiren "güvenlik tarama kullanımı veya vekil trafiği" sınırlanabilir. Aynı alan adı için çok sayıda önbelleğe alınamayan cevap (örn. SERVFAIL) sınırlanabilir. Sayı yok. [doğrulandı] <https://developers.cloudflare.com/1.1.1.1/infrastructure/network-operators/> | Ayrı kullanım şartı bu çalışmada bulunamadı. Gizlilik sayfası Cloudflare Gizlilik Politikası'na bağlı. [doğrulanamadı] |
| Quad9 9.9.9.9 | IP bellekte tutulur, cevap sonrası silinir. Sayaçlar kalıcı arşivlenebilir, süre yazılı değil. Sorgu etiketi için sayı ve ilk/son zaman tutulabiliyor. [doğrulandı] <https://quad9.net/privacy/policy/> | Sayfa sessiz. [doğrulanamadı] | Kabul edilebilir kullanım veya hız şartı bu sayfada yok. Quad9 kötü amaçlı alan adlarını engelleyen bir çözücü: bir alan adı için "yok" (NXDOMAIN) cevabı, gerçek yokluk değil, Quad9'un engeli olabilir. Bu davranış için kaynak bu çalışmada açılmadı, genel bilgidir. [doğrulanamadı] |
| Google 8.8.8.8 | Geçici kayıtlar (IP ve sorgu) yaklaşık 24-48 saatte silinir. Kalıcı kayıtlar IP yerine şehir veya bölge ile örneklenmiş kayıt. [doğrulandı] <https://developers.google.com/speed/public-dns/privacy> | Varsayılan **IP başına 1500 sorgu/sn**, kısa patlamalar serbest. [doğrulandı] <https://developers.google.com/speed/public-dns/docs/isp> | Ayrı kullanım şartı bulunamadı. Kullanım sayfası hız veya şart içermiyor. [doğrulandı: yok] <https://developers.google.com/speed/public-dns/docs/using> |

Notlar:

- Üç çözücü de bizim sorgu adımızı görür. Bu, NETWORK_PLAN Bölüm 4'te "bilinen sınır" olarak yazılı, burada da geçerli: Cloudflare sorgu adlarını araştırma için APNIC'e sunabiliyor, Quad9 sparse örnekleme sunabiliyor. SPK listesindeki alan adları bu hizmetlere kısa süreli görünür.
- Bizim hızımız (günde bir tur, saniyede 1-2 sorgu) bu sınırların çok altında. Yine de aynı alan adı için tekrarlayan SERVFAIL oluşursa Cloudflare tarafında sınırlanma ihtimali var.
- Birden fazla çözücü kullanmak aynı zamanda yakalama doğrulaması için gerekli (Bölüm 3.3).

---

## 5. Ücretsiz geçmiş / pasif DNS (kısa)

Önceki sonuç (DATA_SOURCES Bölüm 9) aynı kaldı: **uygun ücretsiz, geçmiş, küçük alan adı kapsamı veren kaynak bulunamadı.**

- OpenINTEL (ortak akademik ölçüm, günlük aktif DNS ölçümü): 308 milyon alan adı ve günde 5,9 milyar veri noktası diyor, CT günlüğü adları da kaynak, ama ücretsiz araştırma erişimi, lisans ve .tr kapsamı sayfada yazmıyor. Erişim için iletişim sayfası öneriliyor. Kaynak: <https://www.openintel.nl/> [doğrulandı] [doğrulanamadı: lisans, .tr kapsamı, küçük alan adları]. Kapsam yanılgısı olabilir: ölçülen liste zone dosyaları ve büyük listeler.
- Robtex: web aramasında ücretsiz, 2013'ten beri gözlem. API ve otomatik çekim farklı. Kaynak: <https://robtex.com/en/dns-history> [özet]
- DNSArchive: arama ücretsiz, API ücretli. Kaynak: <https://dnsarchive.net/vulnerability-discovery> [özet]
- Spamhaus: 30 günlük deneme. Kaynak: <https://www.spamhaus.com/product/passive-dns/> [özet]
- CESNET Passive DNS: Çek akademik ağ hizmeti, dışarıdan araştırmacıya açık mı bilinmiyor. Kaynak: <https://www.cesnet.cz/en/services/network-1/passive-dns-55> [özet] [doğrulanamadı]
- Rapid7 FDNS ve Common Crawl gibi kaynaklar bu çalışmada doğrulanmadı. [doğrulanamadı]

Sonuç: Geçmiş testinde DNS özelliği kullanılmaz. Kendi kaydımız ileriye dönük testte ve betimleyici analizde kullanılır. OpenINTEL'e araştırma amacıyla yazmak ucuz bir deneme olabilir, ama garantisi yok ve .tr/küçük alan adı kapsamı belirsiz.

---

## Doğrulayamadıklarım

- Güncel (2024-2026) ölçümle: Türkiye'de açık çözücülere giden 53 numaralı UDP/TCP trafiğinin yakalandığı veya düşürüldüğü. Eski kanıt (2014, 2018) var.
- 2024-2026'da Türkiye'de DoH veya DoT'un açık çözücülere karşı engellendiği veya yakalandığı. 2018'de DoT etkilenmedi.
- OONI'nin iki raporu (2025 bant daraltma ve 2026 Women on Web) doğrudan açılamadı. İçerik arama özetinden.
- OONI DNS parmak izi dosyasının satır içeriği.
- SPK kararlarının hangi yöntemle (DNS, IP, SNI) uygulandığı ve işletmeci bazında farkı.
- İFÖD'nin engelleme yöntemine dair bir bölüm.
- `prefix-overview` için tek IP kabul edip etmediği ve geçmiş verinin ne kadar geriye gittiği. `routing-history` örnekleme aralığı.
- RIPEstat verisini özet istatistik olarak yayımlamanın "yeniden dağıtım" yasağına girip girmediği, saklama süresi.
- InternetDB'nin geçmiş sunup sunmadığı, saklama izni, ayrı hizmet şartları.
- Cloudflare ve Quad9 için yazılı hız sınırı sayısı ve ayrı kullanım şartları.
- Açık çözücü kimlik sorguları (örneğin "ben kimim" TXT) ve Quad9 engel cevabı davranışı için kaynak.
- OpenINTEL lisansı ve .tr kapsamı. Rapid7 FDNS ve Common Crawl durumu.

## Ana kaynaklar (erişim 2026-11-13)

- RIPEstat: <https://stat.ripe.net/docs/data_api>, <https://stat.ripe.net/docs/data-api/api-endpoints/prefix-overview/>, <https://stat.ripe.net/docs/data-api/api-endpoints/network-info/>, <https://stat.ripe.net/docs/data-api/api-endpoints/routing-history/>, <https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/> [doğrulandı]
- InternetDB: <https://internetdb.shodan.io/>, <https://book.shodan.io/developer-apis/internetdb/> [doğrulandı]
- Turkey Blocks 2018: <https://turkeyblocks.org/2018/04/02/new-cloudflare-dns-service-filtered-turkey/> [doğrulandı]
- Cloudflare: <https://developers.cloudflare.com/1.1.1.1/privacy/public-dns-resolver/>, <https://developers.cloudflare.com/1.1.1.1/infrastructure/network-operators/> [doğrulandı]
- Quad9: <https://quad9.net/privacy/policy/> [doğrulandı]
- Google: <https://developers.google.com/speed/public-dns/privacy>, <https://developers.google.com/speed/public-dns/docs/isp> [doğrulandı]
- OpenINTEL: <https://www.openintel.nl/> [doğrulandı]
- OONI: <https://ooni.org/post/2025-turkiye-throttling-social-media/>, <https://ooni.org/post/2026-women-on-web-blocked/> [özet]
- Eski olaylar: <https://security.googleblog.com/2014/03/googles-public-dns-intercepted-in-turkey.html>, <https://www.bgpmon.net/turkey-hijacking-ip-addresses-for-popular-global-dns-providers/>, <https://labs.ripe.net/author/emileaben/a-ripe-atlas-view-of-internet-meddling-in-turkey/> [özet]
