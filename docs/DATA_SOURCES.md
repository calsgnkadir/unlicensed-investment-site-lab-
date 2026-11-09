# Veri Kaynakları (Aşama 0 araştırması)

Tarih: 9 Kasım 2026. Tüm erişim tarihleri 2026-11-09. Bu belge karar önerisidir.

## Nasıl okunur

- **[okundu]** Sayfa ya da belge doğrudan açıldı, ama tam metin satır satır kontrol edilmedi. Önemli şartları yayımlamadan önce sayfadan elle bir kez daha oku.
- **[özet]** Yalnızca arama sonucu özetinden görüldü. Doğrulanmış sayılmaz.
- **[bulunamadı]** Aranıp bulunamadı. "Yok" demek değildir, "bulamadım" demektir.
- **[hesap]** Okunan rakamlardan benim yaptığım kaba hesap. Ölçüm değildir.
- Bu belgede SPK veya USOM listesindeki hiçbir alan adı yok. Şüpheli sitelere hiç gidilmedi. Yalnızca SPK'nın kendi sayfaları, belge siteleri ve kaynakların genel arayüzleri açıldı.
- Sayfalardan gelen metin veri olarak ele alındı. Kişi bilgisi yazılmadı.

## Özet tablo

| # | Kaynak | Karar | Tek cümle |
|---|---|---|---|
| 1 | SPK bültenleri | Kullan | 78 bülten (2026/5 listede yok), 3 sayfa, PDF bağlantısı tahmin edilemez ama listeden çekilir. Kullanım şartı bulunamadı, yalnızca "tüm hakları saklıdır". |
| 2 | crt.sh | Koşullu | Resmi sınır yazılı değil. Eski bir resmi açıklama dakikada 60 istek diyor. 5 sn'de 1 istek ve hata ile "kayıt yok"u ayırmak şart. Ana kaynak olması riskli. |
| 3 | Wayback CDX | Kullan | Resmi hız sınırı yok, topluluk yaklaşık 1 istek/sn ve 15 istek/dk eşiğini söylüyor. Şart sayfası açılamadı. |
| 4 | Static CT | Koşullu | Canlı akış mümkün. Son 6-12 ayı log taramasıyla geriye dönük okumak bu proje için gerçekçi değil. |
| 5 | RIPEstat | Koşullu | Araştırma için serbest, ama veriyi yeniden dağıtmak yasak ve sonuçları RIPE'a bildirmek gerekiyor. |
| 6 | InternetDB | Koşullu | Ticari olmayan kullanım serbest, haftalık anlık görüntü, geçmiş yok (belgelenmemiş). Biz kendimiz haftalık kaydederek geçmiş yaparız. |
| 7 | OONI | Koşullu (zaman kalırsa) | Veri açık ama lisansı BY-NC-SA gibi görünüyor, küçük alan adı kapsamı doğrulanamadı. Çekirdek proje dışında. |
| 8 | USOM | Koşullu, belirsiz | .txt paylaşımının 1 Haziran 2026'da bittiği duyuruluyor. API'nin şartı ve erişimi bulunamadı. |
| 9 | Ücretsiz geçmiş DNS | Çıkar | Güvenilir ve kullanılabilir ücretsiz seçenek doğrulanamadı. Kendi kaydımızla ileriye dönük geçmiş oluştur. |
| 10 | SPK engel tekniği | Bilgi notu | Birincil teknik kaynak bulunamadı. Hukuki akış ve genel Türkiye uygulaması (çoğunlukla DNS) var. Kararı belirleyen bilgi değil. |

---

## 1. SPK bültenleri

**Resmi erişim yolu.** Liste sayfası: <https://spk.gov.tr/spk-bultenleri/2026-yili-spk-bultenleri>. Erişim 2026-11-09. [okundu]

- Sayfa başına 30 bülten, en yeni üstte. Sayfa 1: 2026/79 - 2026/50. Sayfa 2 (`?s=2`): 2026/49 - 2026/20. Sayfa 3 (`?s=3`): 2026/19 - 2026/1. Son sayfa 3. [okundu]
- Toplam: **78 bülten**; 10 Kasım 2026'daki indirmede listede 2026/5 yoktu [ölçüldü] (en yeni 2026/79, 6 Kasım 2026; ilki 2026/1, 8 Ocak 2026). Sayfa başına 30 bülten üzerinden toplam 79 çıkıyor. [okundu, hesap]
- Sayfalama biçimi: `?s=N`. Bağlantı gezgini "<<" ve "<" için `#` kullanıyor. [okundu]
- PDF bağlantısı: `https://spk.gov.tr/data/{24 karakterlik onaltılık kimlik}/2026-{no}.pdf`. Kimlik her bülten için farklı ve numaradan türetilemez. Bu yüzden PDF adresini tahmin etme, listeden oku. [okundu]
- Numaralama notu: Sayfa 3'te bir satır "2026-13" diye çıkıyor, diğerleri "2026/13" biçiminde. Ayrıştırıcı iki biçimi de kabul etmeli. [okundu]
- Bülten sıklığı düzensiz: aynı güne iki bülten düşebilir (2026/34 ve 2026/35, 5 Haziran), hafta sonu bülteni var. "Haftalık" varsayma. [okundu]

**Hız sınırı.** [bulunamadı] Yazılı sınır yok. `robots.txt` adresi 404 verdi, yani robots kuralı yok ya da başka yerde. [okundu] Öneri (benim): bülten başına en fazla 1 istek, 3-5 sn aralık, gece saatleri, tek seferlik indirme, `User-Agent` içinde iletişim yazısı. Toplam 79 PDF + 3 liste sayfası zaten küçük.

**Kullanım şartı.** Sayfa altlığında yalnızca "Sermaye Piyasası Kurulu Tüm hakları saklıdır" ve kişisel veri aydınlatma metni bağlantısı var. Ayrı bir "kullanım koşulları" bağlantısı görülmedi. [okundu] Ayrıca arama ile SPK'ya ait bir koşul sayfası aranıp bulunamadı. [bulunamadı]

- Saklama, yayımlama ve atıf kuralı: [bulunamadı]. Kamu kurumunun resmi kararlarının kısa alıntılanması normaldir ama bu hukuki görüş değildir. Güvenli yol: bültenin tamamını repoya koyma, yalnızca gerekli alanları (alan adı, bülten no, tarih, kaynak bağlantısı) `data/private/` altında tut, yayında bülten bağlantısını ver, kaynak olarak "SPK Bülteni 2026/NN" yaz. Emin olmak için SPK'ya e-posta ile sorulabilir (Kadircan'ın kararı).

**Geçmiş veri.** Bu sayfa yalnızca 2026'yı listeliyor. Önceki yıllar için benzer sayfa adı var mı: [bulunamadı] (bu incelemede aranmadı). İlk soru için 2026 yeterli, ama "kilitli test" için yeni bültenler zaten gelecek.

**Toplu indirmenin boyutu.** Bir örnek PDF (2026/79) açıldı: 10 sayfa, yaklaşık 375 KB. [okundu] Her bülten böyle olsaydı 79 x 0,4 MB = kabaca **30 MB** eder [hesap]. Bültenler arasında büyük fark olabilir, tüm PDF'ler ölçülmedi. Sonuç: boyut sorun değil, yük çok düşük.

**Not.** Bu incelemede PDF içindeki "erişim engeli" bölümünün biçimi okunamadı (PDF metni o sırada çıkarılamadı). Ön testte alan adlarının PDF metninden çıkarıldığı ROADMAP Bölüm 11'de yazılı, ama bu belgede doğrulanmadı. [bulunamadı]

**Karar: Kullan.** Ana etiket kaynağı. Şart belirsizliği yayım aşamasında tekrar kontrol edilmeli.

---

## 2. crt.sh

**Resmi erişim yolu.** Ana sayfa <https://crt.sh/> (işleten: Sectigo, © 2015-2026). Arama kutusu alan adı, kuruluş, parmak izi veya crt.sh kimliği alıyor. "Advanced" bağlantısı var. Sayfada kullanım şartı, hız sınırı ya da API notu yok. Erişim 2026-11-09. [okundu]

**Sorgu biçimi.** Bu incelemede sorgu atılmadı (SPK alan adlarını sorgulamak için erken, ve hedef yok). Yaygın biçim: `https://crt.sh/?q=%25.ALANADI&output=json`, süzme için `deduplicate=Y`. Bu biçim resmi bir belgeden okunmadı. [özet]

- `deduplicate=Y`: Let's Encrypt topluluk yazılarına göre ön sertifika ile son sertifika çiftini tek kayda indiriyor. Resmi belgesi bulunamadı. [özet] [bulunamadı]
- JSON çıktısında sayfalama (offset): onaylanmış resmi bir yol bulunamadı. Bir kullanıcı sordu, doğrulanmış cevap görülmedi. [özet]
- Büyük sonuçlarda zaman aşımı raporları var (Eylül 2025'te bir kullanıcı ~900 satır sınırından ve `excluding=expired` ile zaman aşımından söz ediyor). Ayrıca "saat başının ilk 10 dakikası daha sorunlu" gibi anekdotlar. Hepsi tek kullanıcı gözlemi. [özet]

**Hız sınırı.**
- Resmi açıklama: 27 Ocak 2020'de crt.sh işletmecisi, yoğun trafik yüzünden Nginx ile IP başına dakikada 60 istek sınırı getirdiğini yazmış. [okundu] (<https://groups.google.com/g/crtsh/c/NZJntKrBdmg>). Bu 6 yıllık bir açıklama, bugün de geçerli mi bilinmiyor.
- Üçüncü taraf blog "12-15 sn aralık" öneriyor, doğrulanamadı. [özet]
- Yol haritasındaki "5 sn'de 1 istek" bu sınırın çok altında ve makul. Yine de: HTTP 429/5xx ve zaman aşımını asla "sertifika yok" yazma. [okundu: ROADMAP 2.2]

**Kullanım şartı.** [bulunamadı] crt.sh sayfasında yok. Genel Sectigo şartları bu incelemede açılmadı.

**Geçmiş veri.** Evet, sertifika logları kalıcı kayıt olduğu için eski sertifikalar sorgulanabilir. Bu yüzden karar öncesi sertifika ölçümü crt.sh ile mümkün. Ama crt.sh tüm logları aynı kapsamla taramıyor olabilir, kapsama oranı ayrıca bilinmiyor. [bulunamadı]

**Not (teknik risk).** Static CT loglarına geçişle crt.sh'in yeni logları ne kadar hızlı eklediği bu incelemede doğrulanmadı. [bulunamadı] Ön testte crt.sh yaklaşık 100 alan adının 93'ünde yanıt verdi (ROADMAP Bölüm 11, burada doğrulanmadı). Bu yüzden yaklaşık 7 alan adında yanıt gelmedi, nedeni bilinmiyor.

**Karar: Koşullu.** Aşama 0 ve alan adı başına geriye dönük sorgu için kullan. Tek kaynak yapma. Her yanıt için durum kodu kaydet, sorguyu bir kez at, sonucu sakla.

---

## 3. Wayback Machine CDX

**Resmi erişim yolu.** CDX Server belgesi: <https://github.com/internetarchive/wayback/blob/master/wayback-cdx-server/README.md>. Erişim 2026-11-09. [okundu]

- `matchType`: varsayılan `exact`. `prefix` yol önekini, `host` tek ana makineyi, `domain` alt alan adlarını da kapsar (yalnızca SURT sıralı CDX'te). Sorguda `*.` başı `domain`, sonu `/*` `prefix` anlamına gelir. [okundu]
- `limit=N` ilk N sonuç. `limit=-N` son N sonuç (yavaş olabilir). `fastLatest=true` hızlı alternatif. `offset` büyük sorgularda kötü ölçeklenir. [okundu]
- Sayfalama: `showResumeKey=true` ile `resumeKey`, ya da `page`/`pageSize`/`showNumPages` (ZipNum gerekir, archive.org'da `pageSize` varsayılanı 50). [okundu]
- `collapse=alan` ya da `collapse=timestamp:10` ile ardışık tekrarları azaltır (örnek: saatte en fazla 1 kayıt). Bizim için `collapse=digest` ya da `timestamp:8` yararlı olur. [okundu]
- Çıktı: varsayılan düz CDX, `output=json` JSON dizisi (ilk satır başlık), `fl=` ile alan seçimi. Yanıt varsayılan olarak gzip. [okundu]
- Belge hız sınırı konusunda sessiz, sorular için `wwm@archive.org` adresine yönlendiriyor. [okundu]

**Hız sınırı.** Resmi sayı [bulunamadı]. Eski `wayback_api` sayfası da hız veya şart yazmıyor, son güncelleme 2013. [okundu] Topluluk kaynakları: yaklaşık 1 istek/sn, ve IP başına dakikada ~15 isteği aşınca 429/503 gelebilir. Bir kullanıcı 1,25 sn aralıkla 429 almış, bir başkası 10 bin istekte sorun yaşamamış. Resmi değil, tutarsız. [özet] Öneri: 429 gelince `Retry-After` ve üstel geri çekilme, dakikada 10-12 istek, tek eşzamanlı bağlantı.

**Kullanım şartı.** archive.org koşul sayfası iki adreste de açılamadı (biri sadece başlık, biri 404). [bulunamadı] Yayımlama ve atıf kuralını doğrulayamadım. Pratik: raporda "Internet Archive Wayback Machine" kaynak olarak yazılır, arşiv içeriği repoya ve yayına kopyalanmaz (zaten kişi verisi ve güvenilmez içerik kuralı var).

**Geçmiş veri.** Evet, asıl amacı bu: kayıt zaman damgalı. ROADMAP ön testinde 99 alan adında karar öncesi 200 kodlu kayıt 56 idi (Bölüm 11, bu belgede doğrulanmadı).

**Not.** Pasif inceleme kuralı gereği sitenin kendisine değil yalnızca `web.archive.org` ve CDX adresine istek atılır. Arşiv sayfasındaki içerik güvenilmez veridir. Arşivdeki sayfa yeniden yazılmış bağlantılar ve betikler içerir, çalıştırma.

**Karar: Kullan.**

---

## 4. Static CT (kritik)

### 4.1 Belge

- Özellik: C2SP Static CT API <https://github.com/C2SP/C2SP/blob/main/static-ct-api.md>. Erişim 2026-11-09. [okundu]
  - `checkpoint`: `<izleme öneki>/checkpoint`, düz metin, ağaç boyutu ve kök özet içerir, imzalıdır, önbellekte en fazla birkaç saniye tutulur.
  - Tile boyutu: 256 özet (8192 bayt). Düzey 0 yaprak özetlerini tutar, yukarı düzeyler alttaki tam tile'ı özetler (düzey 0-5).
  - Özet tile yolu: `<önek>/tile/<L>/<N>`; veri tile yolu: `<önek>/tile/data/<N>`. `<N>` üç haneli bölümlerle yazılır, sonuncu dışındakiler `x` ile başlar (örnek `x001/x234/067`). Kısmi tile için `.p/<W>` eki.
  - Veri tile'ında zaman damgalı kayıt, ön sertifika ve yayıncı parmak izleri var. Yayıncı sertifikaları `<önek>/issuer/<parmak izi>` adresinde.
  - İstemci: gzip kabul etmeli, `Accept-Encoding: gzip, identity` göndermeli. Tile ve issuer değişmez (uzun süre önbelleğe alınabilir), checkpoint değişken.
  - İstemci zorunlulukları: checkpoint imzasını doğrula, ardışık checkpoint'ler arasında tutarlılık kontrolü yap, bilinmeyen anahtar adlı imzaları yok say, destekleyen checkpoint olmadan rastgele kısmi tile isteme.
  - **Hız sınırı veya nezaket önerisi:** belgede yok. [okundu] [bulunamadı]

### 4.2 Let's Encrypt eski RFC 6962 logları

- Duyuru: <https://letsencrypt.org/2025/08/14/rfc-6962-logs-eol>. [okundu] Plan: 30 Kasım 2025'te salt okunur, 28 Şubat 2026'da tam kapanış. 18 Ağustos 2025 güncellemesi tarihleri, yeni Static CT loglarının tarayıcılarca tam güvenilir olmasını beklemek için kaydırdığını söylüyor. İzleyici yazılımlarının Static CT uyumlu olması gerektiği yazıyor.
- Bugünkü durum: Let's Encrypt log sayfası (<https://letsencrypt.org/docs/ct-logs/>, sayfa 4 Eylül 2026'da güncellenmiş) eski Trillian tabanlı RFC 6962 loglarını "kullanım süresi sona ermiş" olarak anıyor. Üretim logları Sycamore ve Willow, test logu Twig. [okundu] Kapanışın tam tarihte gerçekleştiğine dair ayrı bir doğrulama [bulunamadı], ama sayfa bugün "sona erdi" diyor.
- Chrome tarafı: Şubat 2025 duyurusu, RFC 6962 gerekliliğinin en erken Ekim 2025'te kalkabileceğini söylüyordu (plan). [okundu] (<https://groups.google.com/a/chromium.org/g/ct-policy/c/HBFZHG0TCsY/m/HAaVRK6MAAAJ>). Bir arama özeti, Chrome'un 15 Nisan 2026'dan beri SCT'leri yalnızca Static CT loglarından kabul ettiğini yazıyor. Bu iddia doğrulanmadı. [özet]

### 4.3 Bugün açık tiled loglar

Chrome log listesi (v3), sürüm 96.4, zaman damgası 2026-11-08T13:44:13Z: <https://www.gstatic.com/ct/log_list/v3/log_list.json>. Erişim 2026-11-09. [okundu, tam metin kontrol edilmedi]

`tiled_logs` içinde şu işleticiler var: Google (ParcelYard, PlumbersArms), Sectigo (Monument, Statue), Let's Encrypt (Sycamore, Willow), TrustAsia (Luoshu, Taiji), Geomys (Tuscolo, Trastevere), IPng Networks (Halloumi, Gouda), Microsec (Eszigno), GoDaddy (Aquamarine), HARICA (Selene). Cloudflare ve DigiCert'in `tiled_logs` dizisi boş görünüyor. Her log 6 aylık (veya TrustAsia'da ~1 yıllık) "bitiş tarihi dilimi"ne bölünmüş: dilim, o logda **sertifikanın bitiş tarihinin (notAfter)** düştüğü aralıktır, sertifikanın alındığı tarih değil.

Bugün "usable" görünenler (özet): Let's Encrypt Sycamore ve Willow 2026h2, 2027h1, 2027h2; Google ParcelYard ve PlumbersArms 2026h2-2027h2; Geomys Tuscolo 2026h2-2027h2; IPng Halloumi ve Gouda 2026h2-2027h2; TrustAsia Luoshu2027. Diğerleri "qualified". Tablo elle tam kontrol edilmediği için eksik ya da hatalı olabilir; kullanmadan önce JSON'u kendin ayrıştır. Bir arama özeti Chrome listesinde 15 Ekim 2026'da 43 usable log, 8 işletici, 22'si tiled diyor (özet, listeyle tam örtüşmüyor, bu fark çözülmedi). [özet]

Not: 2026h1 ve daha eski dilimler bu listenin "tiled_logs" çıktısında görünmedi. Let's Encrypt kendi sayfasında 2025h2d ve 2026h1 dilimlerini nihai boyutlarıyla gösteriyor. [okundu] Bir Let's Encrypt topluluk yazısının başlığı "2025 dilimlerinin kapatılması" diyor (özet, içerik okunmadı). Yani **eski dilimler bir gün kaybolabilir**, geriye dönük okuma zamanla zorlaşıyor. [özet]

### 4.4 Günlük hacim (Let's Encrypt, tek log)

Ölçülenler: [okundu]
- Sycamore2026h2 checkpoint'i bugün ağaç boyutu **1.090.644.463** diyor (<https://mon.sycamore.ct.letsencrypt.org/2026h2/checkpoint>). Yalnızca checkpoint okundu, tile indirilmedi.
- Dilim sayfası: 2025h2d nihai 154.630.816 kayıt, 2026h1 nihai 926.062.388 kayıt (<https://log.sycamore.ct.letsencrypt.org/>). Gönderim sınırı dilim başına 512 gönderim/sn (bu log işleticisinin sınırı, bizim değil).
- Let's Encrypt 2024'te günde 4 milyondan fazla sertifika yayımladığını, her birinin iki loga yazıldığını söylüyor (<https://letsencrypt.org/2024/03/14/introducing-sunlight>) [özet]. Aynı yazı bir dilimin depolamasını 5-10 TB olarak veriyor [özet], güncel değil.

Hesap [hesap]: 926 milyon kayıt, bitiş-tarihi dilimi ~180 gün + 90 günlük sertifika ömrü gibi yayılımla, ortalama **günde kabaca 4-6 milyon kayıt**, yalnızca bu tek logda. Bir veri tile'ı 256 kayıt tuttuğuna göre bu **günde yaklaşık 16-24 bin veri tile'ı** eder. Bayt boyutu ölçülmedi: tile başına birkaç yüz KB olabilir ama bu tahmin, ölçü değil. Bir günlük akışı tek log için okumak 16-24 bin istek ve GB düzeyinde bant demek, sınır belirsiz.

Diğer logların günlük hacmi: [bulunamadı].

Aşama 0'daki ölçüm bunu doğrulamalı: yalnızca bir log, çok küçük bir parça (örneğin 20-50 tile), süre, bayt ve süzgeçten geçen aday sayısı. "Hiçbir logu toplu indirme" kuralı gereği bu incelemede tile indirilmedi.

### 4.5 Son 6-12 ayı geriye dönük okumak

- Sertifikalar loga alınma zamanı sırasına göre eklenir (log sırası). Ama dilimler bitiş tarihine göre ayrıldığı için "son 12 ay" istemek birden fazla dilimi (2026h1, 2026h2, 2027h1, vb.) ve birden fazla logu (Google, Sectigo, Geomys, IPng, TrustAsia ...) kapsar.
- Tek Let's Encrypt logunda 6-12 ay = kabaca **1-2 milyar kayıt** [hesap], 4-8 milyon tile isteği. Bu, öğrenci makinesi ve nezaket açısından bu projeye uymuyor. Tek bir logda bile TB düzeyi olabilir (tahmin).
- Bir sertifika genelde iki logda olur (Chrome politikası), bu yüzden tek log taraması yeterli olabilir ama tamlık garanti değil.
- Sonuç: **Tam geriye dönük log taraması önerilmez.** Daha gerçekçi yollar: (a) canlı akışı şimdiden başlat, finans kelimesi geçen adayları sakla, (b) belirli aday alan adları için crt.sh sorgusu, (c) geçmiş testini zaten ROADMAP Hafta 9'daki gibi "mümkün görünmedi" diye raporla. Bu zaten ROADMAP'te öngörülmüş.

**Karar: Koşullu.** Canlı akış için kullan, ama önce bir tile'lık ölçüm yap. Geriye dönük toplu okuma: çıkar ya da yalnızca örnekleme.

---

## 5. RIPEstat Data API

**Erişim.** `https://stat.ripe.net/data/<uç>/data.json?param=değer`. `sourceapp`, `callback`, `preferred_version`, `cache=ignore` ortak parametreler. Her uçta `/meta` ile yöntem ve zaman seçimi destekleyen uçlar için kullanılabilirlik bilgisi alınır. Belge: <https://stat.ripe.net/docs/data_api>. [okundu]

**Hız sınırı.** İstek sayısına üst sınır yok. Düzenli olarak günde 1000'den fazla isteyeceksen kayıt yaptır. IP başına eşzamanlı en fazla 8 bağlantı. `sourceapp`: benzersiz bir ad (harf, rakam, tire, alt çizgi), adı ve iletişim adresini `stat@ripe.net`'e e-postala. [okundu]

**Kullanım şartları** (<https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/>) [okundu]:
- Amaç: ağ analizi, izleme, hata ayıklama, araştırma.
- Araştırma sonucu yayımlanırsa RIPE NCC'ye (ve farklıysa asıl veri kaynağına) atıf yapılır ve **sonuçlar `stat@ripe.net`'e bildirilir** (madde 3.2).
- **Hizmeti veya RIPEstat verisini yeniden paketleyemez, derleyemez, dağıtamazsın** (madde 3.3). Yani ham RIPEstat çıktısını açık veri seti olarak yayımlama. Özet istatistik ve bulgu yayımlamak farklı, ama bu sınır bulanık: emin değilsen RIPE'a sor.
- Ticari kullanım yazılı izin ister.
- Hız/hacim sayısı şartlarda yok, RIPE NCC kullanımı kısıtlayabilir (madde 4.2). Saklama kuralı: [bulunamadı].

**Geçmiş veri.** Kısmen var. `routing-history` ucu RIS toplayıcılarından bir önekin kökenini (origin ASN) zaman çizelgesiyle verir, `starttime`/`endtime` alır (<https://stat.ripe.net/docs/data-api/api-endpoints/routing-history/>) [okundu]. Bu, "karar tarihinde bu IP hangi ASN'deydi" sorusuna geçmişe dönük cevap verebilir. Ama IP'nin o gün hangi alan adına bağlı olduğunu söylemez (bunun için geçmiş DNS gerekir, bkz. Bölüm 9). `prefix-overview` için zaman parametresi bu incelemede doğrulanmadı. [bulunamadı]

**Karar: Koşullu.** Kullan: `sourceapp` kaydet, raporda RIPE NCC'yi kaynak göster, ham veriyi `data/private/` altında tut ve yayımlama, RIPE'a e-postayla bildir.

---

## 6. Shodan InternetDB

**Erişim.** `https://internetdb.shodan.io/<IP>`. Anahtar gerekmez. Alanlar: `cpes`, `hostnames`, `ip`, `ports`, `tags`, `vulns` (örnek bir genel IP'nin alan adları okundu, değerler yazılmadı). [okundu] (<https://internetdb.shodan.io/>, <https://blog.shodan.io/introducing-the-internetdb-api/>, <https://book.shodan.io/developer-apis/internetdb/>). Hedef alan adları için hiçbir IP sorgulanmadı.

**Kullanım şartı.** "Ticari olmayan kullanım ücretsiz." Kâr amaçlı kullanım için kurumsal lisans gerekiyor. Şirketler iç kullanım yapabilir ama ücret aldıkları ürün yapamaz. [okundu] Ayrı bir hizmet şartları metni bulunamadı. [bulunamadı] Bu proje ticari değil, ama CV/staj bağlamında "ticari" sayılıp sayılmayacağı belirsiz olabilir. Öneri: ticari ürün olmadığını README'de yaz.

**Hız sınırı.** Resmi Shodan kitabı: "saniyede 10.000 isteğe kadar patlama" izni, toplu zenginleştirme için `nrich` aracı. [okundu] Üçüncü taraf kaynaklar yaklaşık 1 istek/sn ya da "yayımlanmamış" diyor, çelişki. [özet] Biz haftalık, yalnızca liste ve kontrol grubu IP'leri için sorguladığımızdan sorun beklenmez. Nezaket için 1 istek/sn'nin altında kal.

**Geçmiş veri.** Belgede yok. Veritabanı her hafta UTC gece yarısı güncelleniyor, yani anlık görüntü. [okundu] "Geçmiş sunmuyor" diye kesin yazılı bir şey yok, yalnızca belgelenmemiş. [bulunamadı] Kurumsal SQLite indirmesi kurumsal müşterilere. [okundu] Sonuç: geçmiş port verisi yok kabul et, kendi haftalık kaydınla ileriye dönük geçmiş oluştur. Veri başkasının taramasından gelir, eski olabilir (ROADMAP Bölüm 5).

**Proje kuralı hatırlatması.** CVE alanını saklama, sonucu yalnızca toplu yayımla, `data/private/` altında tut.

**Karar: Koşullu (yalnızca anlık, ileriye dönük).** Geçmiş testinde kullanma.

---

## 7. OONI

**Erişim.** API ve Explorer: `api.ooni.org`, `explorer.ooni.org`. Ölçüm arama ucu `/api/v1/measurements`. [özet] API dokümantasyonu sayfası bu incelemede **429 (çok fazla istek)** verdi, açılamadı: <https://docs.ooni.org/api/>, <https://docs.ooni.org/backend/ooniapi/services/>. Alan adı süzgeci parametresinin tam adı doğrulanamadı. [bulunamadı]

**Hız sınırı.** OONI belgelerine göre IP ve erişim jetonuna göre sınırlı ve "toplu çekim için tasarlanmamış", sayı yok. [özet] Toplu veri için AWS Açık Veri kaydı var. [okundu: <https://registry.opendata.aws/ooni/>, saatlik güncelleme.]

**Lisans.**
- OONI Veri Politikası (sürüm 1.8.0, 22 Ekim 2026), lisansı `github.com/ooni/license/data` adresine bırakıyor, kendisi adını yazmıyor. [okundu] (<https://ooni.org/about/data-policy>)
- Lisans deposunda "yayımlanan tüm OONI verisi" için geçerli bir OONI Veri Lisansı var ve klasörde `CC4.0-BY-NC-SA.md` dosyası görünüyor. Atıf veya ticari şart metni okunamadı. [okundu, eksik] (<https://github.com/ooni/license/tree/master/data>)
- AWS Açık Veri kaydı: **CC BY-NC-SA 4.0** (atıf, ticari olmayan, aynı lisansla paylaşım). [okundu]
- Sonuç: Ticari olmayan bir araştırmada atıfla kullanılabilir görünüyor. "Aynı lisansla paylaş" kuralı bizim yayımladığımız türev veriyi bağlayabilir. Yayından önce `LICENSE.md` elle okunmalı.

**Türkiye ve küçük alan adı kapsamı.** Explorer ülke sayfası Türkiye için 31.862.860 ölçüm ve 323 ağ diyor (<https://explorer.ooni.org/country/TR>) [özet]. OONI'nin Türkiye raporları büyük platformlar (Twitter/X, Instagram, YouTube vb.) hakkında. [özet] Ölçümler test listesindeki ya da gönüllünün girdiği adreslere dayanır. SPK listesindeki küçük, ömrü kısa alan adlarının test listesinde olma ihtimali düşük, ama kapsamı gerçek bir alan adı sorgusuyla ölçmedik. [bulunamadı] Ayrıca OONI Probe çalıştırmak siteye bağlanmak demek, proje kuralına aykırı.

**Geçmiş veri.** Var (ölçümler saklanıyor), ama kapsama alan adı bazında sınırlı olabilir.

**Karar: Koşullu, düşük öncelik.** Önerim: çekirdek projeden çıkar. Aşama 0'da 5-10 alan adını (listeden) Explorer arayüzünden ya da API'den **yalnızca hazır ölçüm var mı** diye bak. Yoksa çıkar (ROADMAP 2.3 zaten böyle diyor). Veri varsa, engelin hangi yolla uygulandığı sorusuna (Bölüm 10) katkısı olabilir.

---

## 8. USOM

**Resmi erişim yolu.** USOM (Siber Güvenlik Başkanlığı / ulusal siber olaylara müdahale merkezi). Ana sayfa ve `/adres` sayfası açıldı: duyuruya göre zararlı bağlantı listesinin **.txt dosyası olarak paylaşımı 1 Haziran 2026 itibarıyla sona eriyor**, mevcut API servislerinin sürekliliği öncelikli, kullanıcılar API'ye geçmeli. Zararlı bağlantı listesi, CVE bildirimi ve güvenlik duyuruları yeni bir devlet siber güvenlik portalında yayımlanmaya devam edecek. [okundu] (<https://www.usom.gov.tr/>, <https://www.usom.gov.tr/adres>)

- API'nin adresi, başvuru yolu ve erişim şartı sayfada yok. [bulunamadı] Bir tarayıcı eklentisi "USOM açık API'si" ile entegre olduğunu yazıyor. Ayrıntı yok. [özet]
- Üçüncü taraf GitHub projeleri `url-list.txt` dosyasını günlük çekip yeniden yayımlıyor. USOM ile bağları yok, "olduğu gibi" veriyorlar. [özet] Bunları kaynak olarak kullanma: USOM'dan resmi bir yeniden dağıtım izni belgelenmemiş, listede dolandırıcılık dışı kayıtlar da var.
- Listedeki sitemap girdisi güncel (Nisan 2026) görünüyor. [özet]
- Girdi sayısı, güncelleme sıklığı: [bulunamadı]. Hız sınırı: [bulunamadı]. Kullanım şartı (saklama, yayımlama, atıf): [bulunamadı]. Geçmiş sürüm: [bulunamadı].

**Önemli not.** USOM listesi "zararlı bağlantı" listesidir (kimlik avı, zararlı yazılım, vb.), "izinsiz yatırım" listesi değildir. SPK kararlarıyla kesişimi sınırlı olabilir. Etiket kuralı aynı: "USOM listesinde" demek "dolandırıcı" demek değildir.

**Karar: Koşullu, belirsiz.** Çekirdek projeye gerek yok. Yalnızca "Zaman kalırsa" modülünde (dış karşılaştırma) ve ancak resmi API erişimi ve şartı netleşirse. Kadircan USOM'a resmi yoldan sorabilir.

---

## 9. Ücretsiz geçmiş DNS verisi

Doğrulanabilen resmi kaynak bulunamadı. Sonuçlar çoğunlukla satıcı sayfaları. Değerlendirme:

| Seçenek | Durum |
|---|---|
| Farsight DNSDB Community Edition | 2019 duyurusu var. Güncel şart ve kota 2026 için doğrulanamadı. Eski örnek anahtar 500 sorgu, sonuç sınırı 256. Toplu sorgu saatte 25. Alan adı listesi için yetersiz. Resmi sayfa adresi 404 verdi. [özet] [bulunamadı] |
| CIRCL Passive DNS | Yalnızca "güvenilir ortaklara", iletişim ve amaç açıklaması isteniyor. Ücret yazmıyor. Resmi sayfa okundu. [okundu] Bu yüzden Kadircan için kolay bir yol değil. |
| Robtex | Web aramasında ücretsiz, giriş yok, 2013'ten beri gözlem. Kapsam alan adına göre değişiyor. API ve otomatik çekim şartları bulunamadı. [özet] Otomatik çekmeden önce şartlar okunmalı. |
| Spamhaus Passive DNS | 30 günlük deneme, sürekli ücretsiz değil. [özet] |
| WhoisFreaks, DNSArchive, SecurityTrails | Küçük ücretsiz katman, kota veya ücretli. SecurityTrails ücretsiz katmanı bir blogdan, resmi doğrulama yok. [özet] |

Sonuç:
- Küçük, ömrü kısa alan adları için, karar tarihindeki DNS cevabını güvenle veren ücretsiz bir kaynak bulunamadı. Pasif DNS kapsamı büyük sitelere kayıyor (hayatta kalma ve kapsam yanlılığı).
- Kullanılmaları halinde bile bu veri yalnızca "gözlenmiş" kayıtlardır, DNS'in tamamı değil.
- Bu proje için güvenli yol: geçmiş DNS yok varsayılır (ROADMAP Bölüm 5 zaten böyle diyor). Karar tarihinde bilinmeyen DNS verisi geçmiş testinde kullanılmaz (geleceği sızdırma kuralı). Kendi DNS kaydımız ileriye dönük testte ve betimleyici analizde kullanılır.
- Bir istisna: RIPEstat `routing-history` (Bölüm 5) IP ve önek tarihçesi verir, ama alan adı-IP eşlemesi vermez.

**Karar: Çıkar** (çekirdek proje). Kadircan isterse DNSDB için araştırmacı hibesi başvurusu yapabilir, ama bu zaman ister ve garantisi yok.

---

## 10. Türkiye'de SPK erişim engelinin uygulanma tekniği

**Hukuki akış** (haber ve hukuk sitesi özetleri, resmi mevzuat bu incelemede açılmadı) [özet]:
- SPK, 6362 sayılı Kanun'un 99/3 maddesine (ve kripto için 99/A-3) dayanarak izinsiz sermaye piyasası faaliyeti yürüten sitelere erişim engeli kararı veriyor. Bülten metinleri 128/1-(a) maddesine atıf yapıyor.
- 7518 sayılı Kanun öncesinde BTK'nın SPK başvurusu üzerine erişimi engellediği, değişiklikten sonra kararların Erişim Sağlayıcıları Birliği'ne gönderilip birlik aracılığıyla erişim sağlayıcılarca uygulandığı yazılıyor. Bu geçişin tarihi ve metni resmi kaynaktan doğrulanmadı. [özet]
- Kararlara karşı iptal davası ve yürütmeyi durdurma yolu var. [özet]

**Teknik yöntem.**
- SPK'ya özgü, birincil bir teknik belge (BTK/ESB uygulama notu) bulunamadı. [bulunamadı]
- Türk hukuku kitap ve tezlerinde Türkiye'de uygulanan erişim engellemenin "çoğunlukla DNS engelleme" olduğu, IP engellemenin aynı IP'deki diğer alan adlarını da etkilediği (yan zarar) yazılıyor. Bunlar genel (5651 sayılı Kanun bağlamı) hukuk kaynakları, SPK'ya özel değil. [özet]
- OONI ölçümleri Türkiye'de belirli olaylarda DNS müdahalesi gösteriyor (2023 Twitter olayında bir işletmecide 127.0.0.1 cevabı, 2025'te YouTube için bir işletmecide DNS engeli). Bunlar büyük platformlara dair ölçümler, SPK sitelerini kapsamıyor. [özet] (<https://ooni.org/post/2023-turkey-throttling-blocking-twitter>, <https://ooni.org/post/2025-turkiye-throttling-social-media/>)
- SNI ya da DPI ile engelleme için Türkiye'ye özgü bir kaynak bulunamadı. [bulunamadı]
- Tek işletmeciden ölçülen yöntem diğer işletmeciler için geçerli olmayabilir.

**Projeye etkisi.** DNS cevabı ölçüm konumuna, sunucuya ve işletmeciye göre değişir. Türkiye'deki bir engel cevabı barındırma bilgisi sanılmamalı (ROADMAP Bölüm 5'te zaten var). Bu yüzden tüm gruplar aynı konumdan ve aynı açık sunucudan ölçülür. Engel cevabının biçimi ve işletmeciye göre değişimi hakkında iddia kurma. Bu konuda yalnızca "SPK kararından sonra OONI verisi varsa" sorusu (ROADMAP Bölüm 6) kalır.

**Karar: Bilgi notu.** Projede iddia kurma. Yöntem belirsiz olduğu için ölçümleri "engelin uygulanma tekniği" gibi okuma.

---

## Kadircan'ın vermesi gereken kararlar

1. **SPK bülten içeriği:** Şart bulunamadı. SPK'ya yazıp sorulacak mı, yoksa yalnızca alan adı, bülten no, tarih ve kaynak bağlantısı gibi minimum alanlarla mı ilerlenecek? (Öneri: minimum alan, yayımdan önce sor.)
2. **Static CT geriye dönük okuma:** Çıkarılsın mı? (Öneri: tam geriye dönük tarama çıkar. Canlı akış ve yalnızca aday alan adları için crt.sh.) Bir tile'lık ölçüm Aşama 0'da yapılsın mı ve hangi log (öneri: tek log, birkaç tile)?
3. **crt.sh güvenilirliği:** Hata yanıtı ile "kayıt yok"u ayıran bir kayıt kuralı şart. Ek olarak crt.sh'in 2026'daki yeni Static CT loglarını ne kadar kapsadığı bilinmiyor. Ölçülecek mi?
4. **RIPEstat:** `sourceapp` adı seçilecek ve `stat@ripe.net`'e kayıt maili atılacak. Ham RIPEstat verisi yayımlanmayacak, bulgu raporunda RIPE'a bildirim yapılacak.
5. **InternetDB:** Proje "ticari olmayan" olarak kabul edilsin mi? README'ye yazılacak mı?
6. **USOM:** Çekirdekten çıkar, "zaman kalırsa" modülüne bırak. API erişimi için USOM'a resmi başvuru yapılsın mı?
7. **OONI:** Çekirdekten çıkar mı? Lisans (BY-NC-SA) türev veri yayımına bağlanabilir. Yayımdan önce `LICENSE.md` okunacak.
8. **Geçmiş DNS:** Çıkar ve "geçmiş DNS yok" diye raporla mı, yoksa DNSDB araştırmacı hibesine başvurulsun mu?
9. **Wayback:** archive.org şartları okunamadı. Elle okuyup onaylayacak mısın?

## Doğrulayamadıklarım

- SPK sitesinin kullanım koşulu, atıf ve yeniden yayım kuralı. Önceki yılların bülten sayfaları.
- SPK bülten PDF'lerindeki "erişim engeli" bölümünün biçimi. PDF metni bu incelemede çıkarılamadı. Yalnızca bir PDF'nin boyutu (~375 KB, 10 sayfa) görüldü, diğer 78'i görülmedi.
- crt.sh: dakikada 60 istek sınırı bugün geçerli mi, `deduplicate=Y` davranışı, JSON sayfalaması, Sectigo kullanım şartı, yeni Static CT loglarının kapsamı.
- Wayback: resmi hız sınırı, archive.org kullanım şartı sayfası (iki adres de açılamadı).
- Static CT: tile başına gerçek bayt boyutu, günlük kayıt hacmi (yalnızca tek logdaki kaba hesabım var, ölçüm değil), diğer logların hacmi, Chrome'un 15 Nisan 2026 politika iddiası, Let's Encrypt eski loglarının tam kapanış tarihi, Chrome log listesi tablosunun eksiksizliği (elle tam kontrol edilmedi), eski dilimlerin ne zaman silineceği.
- RIPEstat: `prefix-overview` için zaman parametresi, saklama kuralı, "yeniden dağıtım" yasağının özet istatistiklere uygulanışı.
- InternetDB: geçmiş veri olmadığının yazılı doğrulaması, resmi hizmet şartları metni, gerçek hız sınırı (resmi kitap 10.000/sn burst, üçüncü taraf 1/sn).
- OONI: API parametreleri (API belge sayfası 429 verdi), hız sınırı sayıları, `LICENSE.md` metni, küçük alan adı kapsamı, Türkiye test listesi içeriği.
- USOM: API adresi, başvuru yolu, şartlar, girdi sayısı, .txt dosyasının gerçekten kapatılıp kapatılmadığı.
- Ücretsiz geçmiş DNS: DNSDB Community Edition'ın bugünkü durumu, Robtex şartları, SecurityTrails ücretsiz katmanı.
- SPK engelinin tekniği: BTK/ESB'nin uygulama biçimi, 7518 sayılı Kanun'daki değişikliğin resmi metni, SNI/DPI kullanımı.

## Ana kaynaklar (erişim 2026-11-09)

- SPK: <https://spk.gov.tr/spk-bultenleri/2026-yili-spk-bultenleri> [okundu]
- Wayback CDX: <https://github.com/internetarchive/wayback/blob/master/wayback-cdx-server/README.md> [okundu]
- C2SP Static CT: <https://github.com/C2SP/C2SP/blob/main/static-ct-api.md> [okundu]
- Let's Encrypt: <https://letsencrypt.org/2025/08/14/rfc-6962-logs-eol>, <https://letsencrypt.org/docs/ct-logs/>, <https://log.sycamore.ct.letsencrypt.org/> [okundu]
- Chrome log listesi: <https://www.gstatic.com/ct/log_list/v3/log_list.json> [okundu]
- RIPEstat: <https://stat.ripe.net/docs/data_api>, <https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/> [okundu]
- InternetDB: <https://internetdb.shodan.io/>, <https://blog.shodan.io/introducing-the-internetdb-api/>, <https://book.shodan.io/developer-apis/internetdb/> [okundu]
- OONI: <https://ooni.org/about/data-policy>, <https://github.com/ooni/license/tree/master/data>, <https://registry.opendata.aws/ooni/> [okundu]
- USOM: <https://www.usom.gov.tr/>, <https://www.usom.gov.tr/adres> [okundu]
- CIRCL: <https://www.circl.lu/services/passive-dns/> [okundu]
