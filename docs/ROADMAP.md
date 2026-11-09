# Sahte ve İzinsiz Yatırım Sitesi Tespit Laboratuvarı: Yol Haritası

Sürüm 2, 9 Kasım 2026. Değişikliklerin gerekçesi: [ROADMAP_REVIEW.md](ROADMAP_REVIEW.md) ve [NETWORK_PLAN.md](NETWORK_PLAN.md).

**Ana soru:** SPK'nın erişim engeli getirdiği yatırım siteleri, karardan önce halka açık teknik izlerinden fark edilebilir miydi?

**İkinci soru:** Bu sitelerin ağ izleri (DNS, ASN, port), aynı dönemde açılmış normal alan adlarından farklı mı?

**İsteğe bağlı üçüncü soru:** Sitelerdeki "SPK lisanslıyız" gibi iddialar resmi kayıtlarla uyuşuyor mu? Zaman kalırsa yapılır (Bölüm 6).

Ürün değil, ölçülmüş bir veri, ağ ve güvenlik araştırması ile .NET backend projesi. Ağırlık veri toplama, veri izleme ve veri biliminde. Ağ ve siber güvenlik bunu destekliyor. Para kazanmak, şirket kurmak veya hazır bir engelleme servisi sunmak hedef değil.

**Çıktılar:**

- GitHub'da açık kaynak .NET projesi (testli, CI'lı, Docker ile çalışan)
- İnternette açık, küçük bir sonuç sayfası. Grafana internete açılmaz, rapora ekran görüntüsü ve metrik özeti girer.
- Yöntemi, sonucu ve sınırları anlatan bir rapor (sonuç kötü çıksa bile)

**Süre:** Haftada yaklaşık 8–10 saatle 12 hafta. Başvurular ve günlük PortSwigger çalışması durmaz. İleriye dönük test 12. haftadan sonra da sürer, 3–6 ay sonra ek rapor yazılır.

## 1. Değişmez kurallar

- **Kendin yaz:** Sinyal, ölçüm ve iddia karşılaştırma kodu senin. Yapay zekâ burada öğretmendir. Altyapıda yardım alabilirsin, ama anlamadığın satır projede kalmaz.
- **Sadece pasif inceleme:** Üye olma, giriş yapma, form doldurma, para yatırma, açık arama yok. Hedef sitelere port veya zafiyet taraması, TLS el sıkışması yok. Sayfa çekmek gerekirse önce arşiv kullan.
- **DNS sınırı:** DNS yalnızca herkese açık bir sunucu üzerinden, SPK listesindeki, aday ve kontrol grubundaki alan adları için ve düşük hızla sorulur.
- **Port sınırı:** Port denetimi yalnızca kendi makinelerinde yapılır. SPK veya USOM listesindeki alan adları ve kontrol grubu için hazır pasif port verisi okunabilir, sonuç yalnızca toplu olarak yayımlanır.
- **Doğru etiket:** "SPK tarafından engellendi", "dolandırıcı" demek değildir. Listede izinsiz faaliyet gösteren gerçek yabancı platformlar da olabilir.
- **Suçlama yok:** SPK veya USOM listesinde olmayan siteler panelde, repoda, logda, metriklerde ve raporda adıyla görünmez. Sistem "yalan" veya "dolandırıcı" demez, "resmi kayıtla uyuşmuyor" der.
- **Kişi adı yok:** Veri setinde kişi bilgisi tutulmaz.
- **Yatırım tavsiyesi değil:** README'de ve panelde açıkça yazar.
- **Sonuç şişirilmez:** Her rakamın yanında veri sınırı ve yanlış alarm oranı yazar.
- **Gizli liste:** Yeni bulunan şüpheli sitelerin ve kontrol grubunun listesi internete açılmaz. Kurumlara bildirim politikası, ileriye dönük test başlamadan yazılır (Hafta 8).
- **Commit'lerde yapay zekâ co-author satırı olmaz.**

## 2. Aşama 0: Fizibilite (3–4 gün). Proje kodu yok.

Küçük deneme betikleri olabilir, ama projeye girmez. Bu aşamadaki ön test sonuçları için [ROADMAP_REVIEW.md](ROADMAP_REVIEW.md) Bölüm 2'ye bak. O test bu aşamanın yerine geçmez.

### 2.1 Engellenen siteleri topla

Kaynak: SPK bültenleri. Haberler sadece vaka bulmak içindir, her kayıt bültenden doğrulanır. Tüm 2026 bültenlerini işlemeye çalış, bülten başına sonuç tut.

`data/private/phase0/engellenenler.csv` (elle doğrulanana kadar repoya girmez):

| Sütun | Örnek | Not |
|---|---|---|
| alan_adi | ornek-yatirim.com | |
| bulten_no | 2026/46 | |
| karar_tarihi | 2026-08-14 | Her zaman bültenden |
| kategori | kripto / kaldıraçlı / diğer | Bültende yazan |
| ilk_sertifika | 2026-05-02 | crt.sh |
| sertifika_sayisi | 7 | crt.sh, tekrarlar ayıklanmış |
| arsiv_kaydi | var / yok | Wayback |
| kaynak_url | … | Bülten bağlantısı |

Listelerde tam URL, alt alan adı, sosyal medya hesabı ve kısaltılmış bağlantı da var. Bunları elle ayır, kuralını not al.

### 2.2 Teknik izleri elle dene

- **crt.sh:** `%.ALANADI` sorgusu ve `deduplicate=Y`. 5 saniyede 1 istekle başla, sonucu kaydet, aynı sorguyu tekrar atma. Hatalı yanıtı "sertifika yok" sayma.
- **Wayback Machine:** CDX sorgusunda `url` ve `matchType=domain`. Sayfanın karar tarihinden önce arşivlenip arşivlenmediğine bak.
- **Arşiv içeriği:** İlk sertifikası kararla arasında 1 yıldan az olan 20 alan adının karar öncesi kaydını aç. Gerçekten yatırım içeriği var mı, park sayfası mı?

### 2.3 Ağ ve hacim denemeleri

- **Static CT:** Bir log parçasından bir günlük akışı okuyup hacmi, disk ve bant ihtiyacını ölç. Ham veriyi diske yığma, yalnızca sayaçları ve filtreden geçen adayları tut. Bugün hangi logların açık olduğunu kontrol et.
- **DNS:** Ölçüm konumu kararından sonra, SPK listesindeki 10 alan adını herkese açık sunucuya sor. Kaçı hâlâ cevap veriyor? Cevapları ve ölçüm konumunu kaydet.
- **RIPEstat ve InternetDB:** Birkaç IP ile dene, kullanım şartlarını oku. Geçmiş veri var mı, not al.

### 2.4 Devam et / bırak kararı

Çekirdek proje için devam şartları:

- En az 50 alan adı bültenden elle doğrulandı. Hedef, ulaşılabilen tüm 2026 kayıtları.
- En az 35'inde karar tarihinden önce sertifika kaydı var. Bu zayıf bir kapıdır, başarı kanıtı değil.
- İlk sertifika ile karar arasındaki sürenin dağılımı çıkarıldı, bülten başına ayrıldı.
- Bir günlük Static CT hacmi ölçüldü, seçilen makineye sığıyor.

Ağ katmanları için ayrı karar:

- DNS, RIPEstat ve InternetDB denemeleri çalıştı, şartları uygun.
- Şartlar uygun değilse o kaynak çıkarılır, proje devam eder.

İddia modülü (isteğe bağlı) için ayrı karar:

- Bu aşamada ayrıca iddia toplanmaz. Arşiv içeriğine bakarken rastlanan lisans iddiaları not alınır.
- Sonradan yapılacaksa önce karar öncesi arşivden en az 30 cümle bulunmalı ve resmi kaynakla karşılaştırma mümkün görünmeli.

## 3. Mimari

```
SPK bültenleri, USOM ──┐
crt.sh, Static CT ─────┤
Wayback ───────────────┼→ Toplama Worker'ları → PostgreSQL → Sinyal motoru → Ölçüm raporu
DNS, RIPEstat ─────────┤         │                  │
InternetDB ────────────┘         ↓                  ↓
                   Metrikler → Grafana     ASP.NET Core API → Sonuç sayfası
```

**Teknoloji:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, Blazor, ML.NET (baseline geçilirse), Prometheus, Grafana, xUnit, Testcontainers, Docker Compose, GitHub Actions

```
src/
  Fraud.Domain/          # Site, Sertifika, Karar, AğKaydı, Sinyal
  Fraud.Collectors/      # Bülten, crt.sh, Static CT, Wayback, DNS, RIPEstat, InternetDB
  Fraud.Infrastructure/  # EF Core, PostgreSQL, metrikler
  Fraud.Signals/         # Alan adı, sertifika ve ağ sinyalleri (SEN yazarsın)
  Fraud.Claims/          # İsteğe bağlı: İddia çıkarma ve karşılaştırma (SEN yazarsın)
  Fraud.Evaluation/      # Ölçüm (SEN yazarsın)
  Fraud.Api/             # ASP.NET Core
  Fraud.Web/             # Küçük sonuç sayfası
tests/
  Fraud.Signals.Tests/
  Fraud.Evaluation.Tests/
  Fraud.Integration.Tests/
  Fraud.Security.Tests/
docs/
  METHODOLOGY.md, DATA_SOURCES.md, THREAT_MODEL.md, DECISIONS.md, AI_USAGE.md
```

**Tablolar:** domains, official_actions, certificates, archive_snapshots, dns_snapshots, ip_asn_snapshots, port_snapshots, signals, evaluation_results, collection_log

Her ağ kaydında zaman, kaynak, ölçüm konumu ve hata durumu zorunludur. Hata, "kayıt yok" olarak yazılmaz.

## 4. Yol haritası

### Hafta 1: Temel yapı ve resmi veri

- Solution yapısı, Docker Compose, PostgreSQL, CI
- Aşama 0'daki CSV'yi veritabanına yükleme
- Her kayıtta bülten numarası ve kaynak bağlantısı zorunlu
- Alan adı temizleme: `www.`, büyük harf, alt alan adı, Türkçe karakterli (IDN) adlar, Public Suffix List

**Bitti sayılır:** `docker compose up` ile sistem açılıyor, CI yeşil, Aşama 0 kayıtları düzgün yükleniyor.

### Hafta 2: Veri toplayıcılar

- crt.sh ve Wayback toplayıcıları, `BackgroundService` ile
- Hız sınırı, hata olunca tekrar deneme, önbellek, loglama
- Aynı iş iki kez çalışınca veri çoğalmasın (idempotent)
- Kaynaklar arayüz arkasında, testte sahte kaynak kullanılır
- Güvenli sayfa çekici:
  - İç ağ adreslerine istek engeli (SSRF koruması), izinli arşiv adresleri, her yönlendirmede yeniden kontrol
  - Boyut ve süre sınırı
  - JavaScript çalıştırma yok, dosya indirip açma yok
  - Çekilen içerik güvenilmez veri sayılır

**Bitti sayılır:** Kayıtlar otomatik geliyor, veri kalitesi raporu çıkıyor, SSRF testleri geçiyor.

### Hafta 3: Sertifika logu istemcisi

- Static CT istemcisi: İmzalı ağaç başı (checkpoint), tile okuma, X.509 alanları
- Merkle kanıtı doğrulama, atlanan kayıt tespiti
- Canlı akış, yalnızca finans kelimesi geçen adayların saklanması
- Kesintiler `collection_log` içine yazılır, kapsama oranı hesaplanır

**Bitti sayılır:** Akış 72 saat kendi başına çalışıyor, kapsama oranı biliniyor, Merkle testleri geçiyor.

### Hafta 4: DNS, ASN ve port kayıtları

- DNS istemcisi: UDP paketini C# ile kur ve çöz, kayıt türleri, TTL, NXDOMAIN, kesik cevapta TCP'ye geçiş. Bunu kendin yazmanı öneririm, en çok burada öğrenirsin.
- RIPEstat ile IP'den ASN ve prefix. Şartlara uy, `sourceapp` ekle, raporda kaynak göster.
- InternetDB ile haftalık pasif port kaydı: Yalnızca SPK veya USOM listesi ve kontrol grubu. CVE alanı saklanmaz, sonuç `data/private/` altında kalır.
- Ölçüm konumu kararı `docs/DECISIONS.md` içine yazılır. Tüm gruplar aynı konumdan ölçülür.
- CDN arkasındaki adresler işaretlenir.

**Bitti sayılır:** Günlük kayıtlar otomatik geliyor. Bozuk DNS paketleriyle fuzz testleri geçiyor.

### Hafta 5: Veri izleme

- Metrikler: Veri tazeliği, log gerisinde kalma süresi, DNS hata oranı, gecikme, tekrar deneme sayısı, günlük aday sayısı
- Grafana paneli ve uyarılar
- Kendi sunucunun port denetimi: Beklenen liste ile gerçek durum karşılaştırılır, fark varsa uyarı çıkar
- Metrik etiketlerinde ve düz loglarda alan adı yok

**Bitti sayılır:** Toplayıcı bilerek durdurulunca uyarı geliyor. Sunucuda fazladan port açılınca uyarı geliyor. Grafana'da alan adı görünmüyor.

### Hafta 6: Ölçüm altyapısı (sinyalden ÖNCE)

Önce sınavı yaz, sonra modeli. Böylece sonuca göre kural uydurmazsın.

- **Pozitifler:** SPK'nın engellediği siteler. Eski gerçek platformlar ile yeni ve kısa ömürlü siteler alt grup olarak ayrılır.
- **Kontrol grupları:**
  - Lisanslı kurumlar
  - Normal finans haber ve eğitim siteleri
  - Aynı dönemde ilk sertifikasını almış, finans kelimesi içeren normal alan adları (asıl zor olanlar)
  - Rastgele normal siteler
- Kontrol listesi `data/private/` altında kalır, seçim yöntemi önceden yazılır.
- **Ayırma:** Zamana göre, bülten ve küme düzeyinde. Aynı bülten veya ağ hem ayar hem test verisine girmez.
- **Üç ayrı sınav:**
  - **Geçmiş veri testi:** Eski bültenlerle ayarla, daha yeni 2026 bültenlerinde bir kez çalıştır. En çok örnek buradadır, ama ön test ve değerlendirme bu bültenlere baktığı için kirlenme riski raporda yazılır.
  - **Kilitli test:** Kurallar donduktan sonra yayımlanan bültenler. Temiz ama yaklaşık 4 haftalık olduğu için çok az kayıt içerebilir. Kayıt azsa yüzde değil, ham sayı verilir.
  - **İleriye dönük test:** Karardan önce tarihli taahhüt gerektirir. Kilitli testten farkı budur.
- **Metrikler:**
  - Yakalama oranı (recall)
  - Precision ve yanlış alarm oranı, kontrol grubu başına ayrı ayrı
  - Gerçekçi taban oranda precision ve günlük alarm sayısı
  - Sabit yanlış alarm düzeyinde erken uyarı süresi. SPK karar tarihi ilk tespit tarihi değildir.
  - Bülten veya küme bazlı bootstrap ile güven aralığı
- **Taahhüt aracı:** Her alan adı için ayrı tuz, Merkle kökü, kayıt sayısı, bir önceki taahhüde bağ, OpenTimestamps veya RFC 3161 zaman damgası.

**Geleceği sızdırma tuzağı:** Bir siteyi karar tarihinde değerlendiriyorsan sadece o tarihte bilinen veriyi kullan. Bugünkü IP, DNS, port veya WHOIS kaydı o güne ait olmayabilir. Geçmişe ait kaydı yoksa o sinyal geçmiş testinde kullanılmaz.

**Bitti sayılır:** Rastgele sinyal üreten sahte model sisteme giriyor ve beklenen düşük sonucu veriyor. Elle hesaplanmış küçük örneklerle testler geçiyor. Taahhüt aracı, listeyi açmadan tek kaydı doğrulayabiliyor.

### Hafta 7: Alan adı ve sertifika sinyalleri

- **Basit referans (baseline):** Alan adı "yatırım, borsa, kripto, forex, trade" gibi kelimeler içeriyor mu? Daha karmaşık her yöntem bunu geçmek zorunda.
- 2–3 sinyalle başla, az sinyali iyi ölç. Adaylar: Marka benzerliği (yerli ve yabancı kurumlar), yeni sertifika ve finans kelimesi, kısa sürede çok sertifika, numaralı adlar.
- Baseline geçilirse basit bir model dene (ML.NET). Etiketli veri az olduğu için derin öğrenme yok.
- Her sinyalin değerini ayrı ölç: Bunu çıkarınca sonuç ne kadar kötüleşiyor?

**Bitti sayılır:** Tüm sinyaller baseline ile kıyaslanmış, işe yaramayanlar da raporda.

### Hafta 8: Ağ analizi ve eşikleri dondurma

- Ağ özellikleri ve betimleyici analiz: Engellenen siteler belirli ağlarda toplanıyor mu? Kontrol grubundan farklı mı? DNS ve port örüntüleri nasıl?
- Basit küme analizi: Sertifika ve ad benzerliği, ağ bilgisi. CDN arkasındakiler "aynı ağ" kümesine girmez.
- Sinyal kurallarını ve eşikleri dondur, tarih ve commit ile kaydet.
- Kurumlara bildirim politikasını `docs/DECISIONS.md` içine yaz.

**Bitti sayılır:** Küme sonucu, kuralların dondurulduğu commit ve bildirim politikası hazır.

### Hafta 9: İleriye dönük test ve dayanıklılık

- Dondurulmuş kuralları canlı akıştan gelen yeni kayıtlarda çalıştır. Logları geriye dönük taramak çıkarıldı: Tek logda 6–12 ay kabaca 1–2 milyar kayıt eder ([DATA_SOURCES.md](DATA_SOURCES.md)). Bu yüzden eşleşme az çıkabilir, test 12. haftadan sonra da sürer.
- Taahhüdü tarihli olarak yayımla, liste gizli kalır.
- Dayanıklılık: Docker içinde kendi toplayıcına gecikme ve paket kaybı ver, kapsama ve toparlanma süresini ölç. Hedef sitelere değil, yalnızca kendi test ortamına.

**Bitti sayılır:** İlk taahhüt zaman damgalı. Dayanıklılık raporu rakamlarıyla yazılı.

### Hafta 10: API ve sonuç sayfası

- API uçları: `GET /actions`, `GET /domains/{name}`, `GET /clusters`, `GET /evaluation/latest`
- Sayfalama, ProblemDetails, OpenAPI
- Küçük sonuç sayfası (Blazor statik SSR):
  - Sonuç özeti
  - SPK'nın engellediği site detayı ve zaman çizelgesi
  - Sistemin kaçırdıkları ve sınırlar
- Alan adları bozulmuş ve tıklanamaz gösterilir (`ornek[.]com`), favicon yok, her kayıtta "karar tarihi itibarıyla" yazar. Düzeltme ve kaldırma süreci yazılır.

**Bitti sayılır:** Giriş yapmadan biri sonuçları anlayabiliyor.

### Hafta 11: Güvenlik ve yayına alma

XSS bilgin burada gerçekten işe yarıyor: Panel, güvenilmez sitelerden gelen başlıkları ve metinleri gösteriyor. Yani bu projede stored XSS riski gerçek.

- Güvenilmez içerik panele sadece kodlanmış metin olarak basılır. `MarkupString` yok. `javascript:` bağlantısı, CSV formül enjeksiyonu ve log enjeksiyonu test edilir.
- CSP ve güvenlik başlıkları
- Yönetim uçları için kimlik doğrulama ve yetki testleri, istek sınırı
- `THREAT_MODEL.md`: Kötü niyetli site, SSRF, stored XSS, kötü niyetli DNS cevabı, sahte log verisi, veri zehirleme, yanlış suçlama riski
- CI'da statik analiz: Araç çalışıyorsa ekle, çalışmıyorsa CV'ye ve README'ye yazma. `dxa` adlı araç bulunamadı, alternatif adaylar: CodeQL, Roslyn analizörleri, Security Code Scan.
- Yayın: Azure for Students veya ucuz bir VPS (fiyatı ve koşulları o gün kontrol et)

**Bitti sayılır:** Sayfa açık bir adreste çalışıyor, XSS ve SSRF testleri CI'da yeşil.

### Hafta 12: Son test ve ara rapor

- Geçmiş veri testini ve kilitli testi birer kez çalıştır, ayrı raporla.
- İleriye dönük testin ara sonucunu açıkla: Resmi eşleşmeleri Merkle kanıtıyla göster, diğer adları açıklama. Test sürer.
- Sonucu, kötü olsa bile değiştirmeden yaz. Sıfır eşleşme de sonuçtur.
- Rapor: Soru, veri, yöntem, sonuç, kaçırılanlar, sınırlar, etik kararlar, sonraki adım
- Türkçe ve İngilizce kısa özet, LinkedIn yazısı

**Bitti sayılır:** Repo, sayfa ve rapor yayında. Projeyi 5 dakikada kendi cümlelerinle anlatabiliyorsun.

### Sonra: Ek rapor

3–6 ay sonra ileriye dönük testi kapat, gizli listeyi taahhütle doğrula, resmi eşleşmeleri ve toplam sayıları yayımla. Yanlış pozitif bu testte kesin ölçülemez, çünkü SPK o siteye henüz bakmamış olabilir.

**Beklenti:** İlk sertifika ile karar arasındaki süre ön testte çoğunlukla aylarla ölçüldü. Eşleşme az çıkabilir. Bunu baştan yaz.

## 5. Raporda açıkça yazılacak sınırlar

- **Etiket kusurlu:** SPK listesi "izinsiz faaliyet" listesidir, "dolandırıcılık" listesi değil.
- **Seçim yanlılığı:** Sadece SPK'nın yakaladığı siteler var. Hiç yakalanmayanlar ölçülemez.
- **Yanlış alarm kesin değil:** Listede olmayan bir site yarın engellenebilir.
- **Sertifika tarihi site açılış tarihi değildir:** Alan adı başka biri tarafından daha önce kullanılmış olabilir.
- **HTTP siteler ve eski kayıtlar:** Sertifikası olmayan veya arşivlenmemiş siteler görünmez.
- **Ağ verisi yeni:** Geçmiş DNS, IP ve port kaydı büyük olasılıkla yok. Bu veriler ana sonucu değil, betimleyici analizi ve ileriye dönük testi besler.
- **Hayatta kalan yanlılığı:** Karardan sonra kapanan alan adlarının ağ verisi eksik olur. Bugünkü durum karar günündeki durumu göstermez.
- **Ölçüm konumu:** DNS cevabı konuma ve sunucuya göre değişebilir, Türkiye'deki engel cevabı barındırma bilgisi sanılmamalı.
- **CDN:** Aynı IP veya ASN aynı sahip demek değildir. CDN arkasındaki sitenin portları sitenin kendisine ait değildir.
- **Pasif port verisi başkasının taramasından gelir:** Eski veya eksik olabilir.
- **Bilinen sızıntı:** Herkese açık DNS sunucusu aday adları görür.
- **Küçük örnek:** Etiketli site az ve bültenlerde toplu geliyor, güven aralıkları geniş olabilir.

## 6. Zaman kalırsa

Bu modüller ana planda değildir. Yapılmazsa raporda "yapılmadı" yazar.

- **Lisans iddiaları:** Site "SPK lisanslı" diyor, ama resmi kurum listesinde var mı? Kural tabanlı Türkçe çıkarma, en az 100 elle etiketli cümle, her sonuç türü için precision ve recall. Sonuç türleri: Uyuşuyor, Çelişiyor, Bulunamadı (tek başına kanıt değil), Belirsiz.
- **Halka arz iddiaları:** İddia, yazıldığı tarihte resmi kayıtla uyuşuyor mu? Başvuru ile onayı, taslak ile onaylı izahnameyi, söylenti ile kesin ifadeyi ayır. Ad eşleşme hatasını doğrudan "çelişiyor" sayma. Tarihi belli olmayan sayfa "belirsiz"dir.
- **Engel sonrası dönüş:** Engellenen siteye çok benzeyen yeni alan adları ne kadar sürede çıkıyor?
- **Dış karşılaştırma:** Aynı alan adları USOM, IOSCO I-SCAN veya başka düzenleyici listelerinde SPK'dan önce görünmüş mü?
- **USOM:** Resmi liste erişimi ve şartları netleşirse ikinci etiket kaynağı olarak eklenir. Üçüncü taraf kopyaları kullanılmaz.
- **OONI:** Lisansı (CC BY-NC-SA görünüyor) ve kapsamı uygunsa, veri varsa, SPK kararından sonra engel ne zaman ve hangi yolla uygulanmaya başladı?

## 7. Yapılmayacaklar

- Canlı "dolandırıcı siteler" listesi yayımlamak
- Şüpheli sitelerde hesap açmak, para yatırmak veya açık aramak
- Hedef sitelere port taraması, zafiyet taraması, TLS el sıkışması
- OONI Probe veya benzer araçla hedef sitelere bağlanmak
- Sosyal medya taraması, Telegram gruplarına girmek, kişi takibi
- Tarayıcı eklentisi, mobil uygulama, kullanıcı hesabı
- Baseline geçilmeden derin öğrenme veya LLM ile karar verme
- Tüm finans haberlerini doğrulamak

## 8. Mülakatta cevaplayabilmen gerekenler

1. Neden "SPK engelledi" etiketi "dolandırıcı" demek değil?
2. Geçmiş testinde gelecekten veri sızmadığını nasıl sağladın?
3. Kontrol grubunu nasıl seçtin? Sinyalin yalnızca "alan adı yeni mi" diye ölçmediğini nasıl gösterdin?
4. Basit kelime kuralını geçebildin mi? Geçemediysen neden?
5. Sertifika tarihi neden kusurlu bir erken uyarı ölçüsü?
6. Static CT ile eski RFC 6962 logları arasındaki fark ne? Merkle kanıtı neyi garanti eder?
7. DNS paketi nasıl çözülür? Sıkıştırma işaretçisi neden tehlikeli? UDP'den TCP'ye ne zaman geçilir?
8. Neden hedef sitelere port taraması yapmadın? Pasif port verisinin sınırı ne?
9. CDN arkasındaki sitede ASN ve port neden yanıltır?
10. Veri hattın durduğunda nasıl haberin oluyor? Kapsama oranını nasıl ölçtün?
11. SSRF'yi nasıl engelledin? DNS rebinding olursa ne olur?
12. Kötü niyetli sayfa başlığı panelde neden XSS'e yol açabilir? Testini göster.
13. İleriye dönük testte neden tek özet yerine tuzlu Merkle taahhüdü kullandın?
14. Yapay zekâyı nerede kullandın, nerede kullanmadın?

## 9. CV cümlesi (sadece proje bitince, gerçek rakamlarla)

[Proje adı] (C#, ASP.NET Core, PostgreSQL, Docker, Grafana): Built a .NET data pipeline that ingests Certificate Transparency logs and DNS records with a hand-written DNS client, monitors its own coverage and data freshness, and tests whether [N] investment sites blocked by Turkey's Capital Markets Board could be identified before the decision. Flagged [X]% with a median lead time of [Y] days at [Z]% false alarms against time-matched controls. Hardened against SSRF, stored XSS and malicious DNS responses. [Canlı link] | [GitHub]

Rakam yoksa veya sayfa çalışmıyorsa o kısmı yazma. Yapmadığın şeyi (örneğin kendi DNS istemcisini) yazma. Sonuç zayıfsa bunu da gizleme.

## 10. Kaynaklar ve doğrulama durumu

**Ön testte görüldü (9 Kasım 2026, kısmi örnek, elle doğrulanmadı):**

- SPK bültenlerindeki alan adları PDF metninden çıkarılabiliyor.
- Yaklaşık 100 alan adında, crt.sh'in yanıt verdiği 93'ünün hepsinde karar öncesi sertifika kaydı var. Ortanca süre 249 gün.
- Karar öncesi 200 kodlu arşiv kaydı 56 / 99. Sertifikası 1 yıldan yeni olanlarda 23 / 61.

**Belgelerden doğrulandı (9 Kasım 2026):**

- Let's Encrypt'in eski RFC 6962 loglarını kapatma takvimi ve Static CT: [duyuru](https://letsencrypt.org/2025/08/14/rfc-6962-logs-eol), [log listesi](https://letsencrypt.org/docs/ct-logs/). Kapanışın gerçekleştiği ayrıca doğrulanmadı.
- RIPEstat kullanım kuralları: [Data API](https://stat.ripe.net/docs/data_api), [şartlar](https://www.ripe.net/about-us/legal/ripestat-service-terms-and-conditions/)
- Shodan InternetDB: [servis](https://internetdb.shodan.io/), [duyuru](https://blog.shodan.io/introducing-the-internetdb-api/). Ticari olmayan kullanım için ücretsiz, haftalık güncellenir.

**İlk sürümden gelen kaynaklar (arama sonuçları üzerinden):** SPK'nın erişim engeli kararları, halka arz onaylarının bültenle duyurulması, crt.sh ve Wayback erişimi, USOM.

**Henüz doğrulanmadı, Aşama 0'da kontrol edilecek:**

- Tüm 2026 bültenlerinde çıkarma sonucu ve elle doğrulama
- Bir günlük Static CT hacmi, bugün açık loglar, geçmiş sertifikaları toplamanın yolu
- Ücretsiz geçmiş DNS verisi
- InternetDB'nin geçmiş sunup sunmadığı ve tam kullanım şartları
- OONI lisansı ve küçük alan adlarındaki kapsamı
- SPK engellerinin bugün hangi teknikle uygulandığı
- USOM listesinin güncel durumu ve kullanım şartları
- Windows ve Docker Desktop'ta paket kaybı simülasyonu
- ML.NET'in bu küçük veri için yeterliliği
- Yetkili kurum listesi ve halka arz tarihlerine erişim yolu (yalnızca isteğe bağlı modül için)
