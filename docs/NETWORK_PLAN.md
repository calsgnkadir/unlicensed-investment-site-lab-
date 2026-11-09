# Ağ, Port ve İzleme Planı

Tarih: 9 Kasım 2026. Karar: Proje SPK verisiyle devam ediyor, ağ ve izleme ana parçalardan biri oluyor. Kadircan, Bölüm 3B'deki Shodan InternetDB kullanımını kabul etti. Plan [ROADMAP.md](ROADMAP.md) dosyasına işlendi, çelişki olursa o dosya geçerlidir. Bu belge gerekçeleri tutar.

## 1. Değişmeyen çizgi

Hedef sitelere doğrudan bağlanılmaz. Port taraması, TLS el sıkışması ve siteye istek yoktur. DNS ve port bilgisi yalnızca aşağıda yazılan üçüncü taraf kaynaklardan gelir.

## 2. Katmanlar

| # | Katman | İş | Öğrenilen |
|---|---|---|---|
| 1 | Sertifika logları | Static CT istemcisi, Merkle kanıtı doğrulama, atlanan kayıt tespiti | HTTP, X.509, Merkle ağacı |
| 2 | DNS | UDP paketini C# ile kurup çözmek, herkese açık bir sunucuya düşük hızla sormak | Paket yapısı, kayıt türleri, TTL, NXDOMAIN, UDP'den TCP'ye geçiş |
| 3 | IP, ASN, BGP | IP'nin ağını RIPEstat ile bulmak, gruplar arası karşılaştırma | Yönlendirme, ASN, prefix |
| 4 | Portlar | Kendi sunucunun port denetimi, istenirse hazır pasif port verisi (Bölüm 3) | Port, servis, saldırı yüzeyi |
| 5 | Dayanıklılık | Docker içinde gecikme ve paket kaybı verip toparlanmayı ölçmek | Zaman aşımı, tekrar deneme, kayıp tespiti |
| 6 | İzleme | DNS hata oranı, gecikme, tekrar denemeler, log gerisinde kalma süresi, açık port farkı | Ağ gözlemlenebilirliği |

**Veri bilimi soruları:**

- Engellenen siteler belirli ağlarda toplanıyor mu?
- Aynı ağı paylaşan site grupları var mı?
- Bu örüntü, aynı dönemde açılmış kontrol alan adlarından gerçekten farklı mı?

## 3. Portlar

### A. Kendi sistemimiz (zorunlu ve serbest)

- Kendi sunucumuzda beklenen port listesi yazılır (örneğin yalnızca 443).
- Düzenli denetim, gerçek durumu bu listeyle karşılaştırır. Fark varsa Grafana uyarısı çıkar.
- Toplayıcının kendi trafiği de izlenir: 53 UDP ve TCP, 443 çıkışı, hata ve gecikme.
- Bu iş yalnızca sahibi olduğumuz makinelerde yapılır.

### B. Hazır pasif port verisi (isteğe bağlı, Kadircan'ın kararı)

[Shodan InternetDB](https://internetdb.shodan.io/) bir IP için açık portları ve etiketleri verir. Anahtar istemez, ticari olmayan kullanım için ücretsizdir ve [haftalık güncellenir](https://blog.shodan.io/introducing-the-internetdb-api/). Biz siteye bağlanmayız, ama veri başkasının taramasından gelir.

Kullanılırsa kurallar şöyle olur:

- Yalnızca SPK veya USOM listesindeki alan adlarının IP'leri ve zaman eşleşmeli kontrol grubu sorulur.
- Sonuç `data/private/` altında kalır, IP ve alan adı yayımlanmaz, yalnızca toplu sayı raporlanır.
- CVE alanı saklanmaz. Proje zafiyet araştırması değildir.

**Sınırları:**

- CDN arkasındaki sitede görünen portlar sitenin değil CDN'indir.
- Paylaşımlı barındırmada port bilgisi tek siteye ait değildir.
- Geçmiş veri verdiğini doğrulayamadım, bu yüzden geçmiş testine girmez.

## 4. Gelen plana göre düzeltmeler ve eklemeler

**Doğrulananlar ve düzeltmeler:**

- **Let's Encrypt:** "Kapatma sürecinde" değil, takvim çoktan geçti. [Duyuruya göre](https://letsencrypt.org/2025/08/14/rfc-6962-logs-eol) eski loglar 30 Kasım 2025'te salt okunur oldu, 28 Şubat 2026'da kapatılacaktı. Kapanışın gerçekleştiğini ayrıca doğrulamadım. İstemci baştan [Static CT](https://letsencrypt.org/docs/ct-logs/) ile yazılmalı.
- **RIPEstat:** [Resmi belgeye göre](https://stat.ripe.net/docs/data_api) günde 1.000'den fazla istek için kayıt önerilir, aynı IP'den en fazla 8 eşzamanlı istek olur, `sourceapp` parametresi önerilir. [Şartlar](https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/) yayımlanan araştırmada kaynak gösterilmesini ister.
- **OONI:** Web Connectivity testi [Citizen Lab test listelerine](https://ooni.org/post/2025-turkiye-throttling-social-media/) odaklanıyor. Küçük SPK alan adları bu listelerde büyük olasılıkla az bulunur. [yorum] Aşama 0'da birkaç alan adıyla bakılır, veri yoksa çıkarılır. OONI Probe çalıştırılmaz, çünkü siteye bağlanır. Yalnızca hazır veri okunur.

**Gelen planda olmayan riskler:**

1. **Ölçüm konumu:** Türkiye'deki servis sağlayıcılar engeli [DNS cevabını değiştirerek uygulayabiliyor](https://bianet.org/yazi/twitter-yasagini-guvenle-asma-kilavuzu-154366). Bu kaynak eski, bugünkü yöntemi doğrulamadım. Karar sonrası engel cevabı barındırma bilgisi sanılabilir. Bu nedenle her kayıtta ölçüm konumu ve sunucu tutulur, tüm gruplar aynı konumdan ölçülür.
2. **CDN yanılgısı:** Aynı ASN veya IP, aynı sahip demek değildir. CDN arkasındaki alan adları ayrı grup olur ve "aynı ağ" kümesine sokulmaz.
3. **Gelecek sızıntısı:** Yalnızca karardan önce alınan ağ kayıtları özellik olur. Karar sonrası kayıtlar yalnızca betimleyici analize girer.
4. **Gizlilik (Kural 4 ve 8):** Alan adı Prometheus etiketine, Grafana'ya veya düz loga yazılmaz. Yalnızca toplu sayı veya tuzlu kimlik kullanılır. RIPEstat'a alan adı değil IP gönderilir. Herkese açık DNS sunucusu aday adları görür, bu bilinen sınır olarak yazılır.
5. **DNS kodunun güvenliği:** Çekilen veri güvenilmezdir. Sıkıştırma işaretçisi döngüsü, uzunluk taşması, yanlış işlem kimliği ve sahte cevap test edilir. Bu, güvenlik testleri için gerçek bir fuzzing alanıdır.
6. **Hayatta kalan yanlılığı:** Eski alan adları kapanmış olabilir. Bugünkü durum, karar günündeki durumu göstermez.
7. **Küçük örnek:** Ağ verisi büyük olasılıkla bugünden itibaren toplanacak, ileriye dönük test de az eşleşme verecek. Bu sonuçlar ana bulgu değil, betimleyici analiz olarak yazılır.

## 5. Sahiplik

| İş | Kim |
|---|---|
| Static CT, RIPEstat, InternetDB toplayıcıları, izleme | Altyapı kodu, her satırı anlaşılır |
| DNS paket kurma ve çözme | Korumalı alan değil, ama öğrenme değeri yüksek. Kadircan'ın kendisinin yazmasını öneririm |
| Ağdan özellik çıkarma (`Fraud.Signals`) ve karşılaştırma (`Fraud.Evaluation`) | Yalnızca Kadircan |
| Saldırgan DNS girdisi ve parser testleri | Güvenlik test projesi |

Yeni tablolar: `dns_snapshots`, `ip_asn_snapshots`, `port_snapshots`. Her kayıtta zaman, kaynak, ölçüm konumu ve hata durumu zorunludur. Hata, "kayıt yok" olarak yazılmaz.

## 6. Önerilen takvim (12 hafta)

İddia ve halka arz modülleri ana plandan çıkar, zaman kalırsa yapılır. Panel tek sayfalık sonuç ekranına iner, izleme Grafana'da kalır.

| Hafta | İş |
|---|---|
| Aşama 0 | CSV doğrulama, bir günlük Static CT hacim denemesi, SPK listesindeki 10 alan adıyla DNS denemesi, OONI kontrolü |
| 1 | Solution, Docker, CI, veri yükleme, normalleştirme |
| 2 | crt.sh ve Wayback toplayıcıları, güvenli çekici |
| 3 | Static CT istemcisi, canlı akış, Merkle doğrulama |
| 4 | DNS istemcisi, RIPEstat, günlük kayıt toplama, ölçüm konumu kararı |
| 5 | İzleme, Grafana, uyarılar, kendi port denetimi |
| 6 | Ölçüm altyapısı, kontrol grupları, taahhüt aracı |
| 7 | Baseline ve sertifika sinyalleri |
| 8 | Ağ özellikleri, basit küme analizi, **kuralları dondur**, bildirim politikasını yaz |
| 9 | İleriye dönük test başlar. Paket kaybı ve gecikme denemeleri |
| 10 | Sonuç sayfası ve API |
| 11 | Güvenlik testleri, tehdit modeli, yayın |
| 12 | Geçmiş test, ara rapor. İleriye dönük test sürer, 3–6 ay sonra ek rapor |

İzleme ileriye dönük testten önce kuruluyor, çünkü kesintisiz toplama testin geçerliliği için gereklidir.

## 7. Proje kuralı için öneri

Kural 2'nin yerine:

> **Pasif inceleme:** Üye olma, giriş, form, para yatırma, açık arama, hedefe port veya zafiyet taraması, TLS el sıkışması yok. Şüpheli sitelere istek atmadan önce arşiv kaynakları kullanılır. DNS yalnızca herkese açık bir sunucu üzerinden, SPK listesindeki, aday ve kontrol grubundaki alan adları için ve düşük hızla sorulur. Port denetimi yalnızca kendi makinelerimizde yapılır.

Bölüm 3B kabul edilirse şu cümle eklenir:

> SPK veya USOM listesindeki alan adları ve kontrol grubu için hazır pasif port verisi okunabilir, sonuç yalnızca toplu olarak yayımlanır.

## 8. Doğrulanmayanlar

- Bir günlük Static CT verisinin hacmi ve bilgisayarın buna yetip yetmediği
- Ücretsiz geçmiş DNS verisi. Önceki taramalarda uygun kaynak görülmedi, kapsamlı aranmadı
- InternetDB'nin geçmiş sunup sunmadığı ve tam kullanım şartları
- OONI lisansı ve alan adına göre API sorgusu
- Türkiye'de SPK engellerinin bugün hangi teknikle uygulandığı
- Docker Desktop üzerinde `tc netem` veya Toxiproxy'nin Windows'ta çalışması
