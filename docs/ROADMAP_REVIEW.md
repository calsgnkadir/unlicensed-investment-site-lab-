# Yol Haritası Değerlendirmesi

Tarih: 9 Kasım 2026. Bu belge, yol haritasının ilk sürümü üzerine yapılan değerlendirmedir. Öneriler [ROADMAP.md](ROADMAP.md) sürüm 2'ye işlendi. Proje odağı sonradan veri, izleme ve ağa kaydığı için iddia modülleri artık isteğe bağlıdır, Bölüm 6'daki takvimin yerini de yeni yol haritası aldı.

Her bilginin dayanağı ayrı yazıldı:

- **[ölçüldü]:** Bu değerlendirmedeki ön testte sayıldı.
- **[okundu]:** Birincil kaynağın kendisi okundu.
- **[özet]:** Yalnızca arama sonucu veya kısa inceleme düzeyinde görüldü.
- **[yorum]:** Benim çıkarımım, ölçüm değil.

## 1. Kısa karar

**Fikir iyi, devam edilmeli. Ama bu haliyle 80–100 saate sığmaz ve ileriye dönük test bugünkü planla büyük olasılıkla çok az eşleşme verir.** [yorum]

Projeyi farklı kılan şey tespit yöntemi değil. Sertifika kayıtlarından tespit zaten bilinen bir yöntem. Fark şu dördünün birlikte olması:

1. Etiketin bir düzenleyici kurumdan gelmesi
2. Türkiye odağı
3. Kuralları önceden dondurup ileriye dönük test yapmak
4. Kötü sonucu da yazmak

Staj başvurusunda en güçlü malzeme de bu ölçüm disiplini.

### En önemli beş değişiklik

| # | Değişiklik | Neden |
|---|---|---|
| 1 | Sertifika toplamayı 2. haftada başlat, geçmişe dönük aday da ekle, testi 10. haftadan sonra da sürdür | 5 haftalık pencerede beklenen eşleşme çok az (Bölüm 3.1) |
| 2 | Tek SHA-256 yerine tuzlu Merkle taahhüdü ve zaman damgası kullan | Bugünkü yöntem eşleşmeyi kanıtlayamıyor (3.2) |
| 3 | Kontrol grubuna "aynı dönemde açılmış normal finans alan adları"nı ekle | Yoksa sinyal yalnızca "alan adı yeni mi" sorusunu ölçer (3.3) |
| 4 | Halka arz modülünü ana plandan çıkar, lisans modülünü arşiv kapısına bağla | Süre yetmez, arşiv verisi yeni alan adlarında zayıf (3.6, 4) |
| 5 | Kilitli testi, kurallar donduktan sonra çıkan bültenlerden kur | Ön test ve okunan bültenler 2026 verisini zaten kirletti (3.5) |

## 2. Ön test sonuçları

**Yöntem:** 2026 bültenlerinin 79'undan yaklaşık 21'i indirildi. Bu seçim rastgele değil. PDF metninden düzenli ifadeyle alan adları çıkarıldı. crt.sh ve Wayback, 5 saniyelik aralıklarla sorgulandı. Şüpheli sitelerin kendisine hiç istek atılmadı.

**Veri sınırları:**

- Alan adları **elle doğrulanmadı**. Bültende geçen ama engellenmemiş bir adres listeye karışmış olabilir.
- Çıkarım için yazılan alan adı ayrıştırması kaba. Gerçek proje Public Suffix List kullanmalı.
- crt.sh sorgusu tam alan adı ile yapıldı, yalnızca alt alan adında görünen sertifikalar eksik kalabilir.
- İlk sertifika, önceki sahibe ait olabilir. Eski alan adlarındaki gecikme bu yüzden şişmiş olabilir.
- Karar tarihi olarak bülten tarihi alındı.
- 200 kodlu arşiv kaydı, sayfanın yatırım içeriği taşıdığını göstermez. Park sayfası veya önceki sahibin sitesi olabilir. İçerikler okunmadı.
- Wayback sorgusu 5.000 kayıtla sınırlıydı. 11 alan adı bu sınıra takıldı, ancak ilk kayıtlar en eskiden başladığı için bu sonuçları büyük olasılıkla değiştirmez.

### Bültenlerden çıkarma

Alan adları PDF metninden çıkarılabiliyor. ROADMAP 10. bölümdeki açık madde böylece yanıtlanmış oldu. [ölçüldü]

- İncelenen yaklaşık 21 bültenin 9'unda filtre sonrası alan adı kaldı.
- 100 alan adının 60'ı yalnızca iki bültenden geliyor: 2026/1 (29) ve 2026/12 (31).
- Listelerde tam URL, alt alan adı, Instagram hesabı ve kısaltılmış bağlantı da var. Normalleştirme bunları ayırmalı.

### Sertifika

| Ölçüm [ölçüldü] | Sonuç |
|---|---|
| crt.sh'in yanıt verdiği alan adı | 93 / 100 (7'si servis hatası, "sertifika yok" anlamına gelmez) |
| Karardan önce sertifikası olan | 93 / 93 |
| İlk sertifika ile karar arası, ortanca | 249 gün (çeyrekler 106 ve 651, en az 6, en çok 5.961) |
| Bülten ortancalarının ortancası | 178 gün (9 bülten) |
| 28 gün veya daha yakın | 16 / 93 (%17), bunların 13'ü üç bültenden |
| 60 gün veya daha yakın | 18 / 93 (%19) |
| 180 gün veya daha yakın | 38 / 93 (%41) |
| 1 yıldan eski | 31 / 93 (%33) |

### Arşiv

| Grup [ölçüldü] | Karardan önce 200 kodlu kayıt |
|---|---|
| Tümü | 56 / 99 (%57) |
| İlk sertifikası kararla arasında 1 yıldan az olanlar | 23 / 61 (%38) |
| İlk sertifikası 1 yıldan eski olanlar | 30 / 31 (%97) |
| En yeni iki bülten (2026/59 ve 2026/67) | 0 / 5 |

**Düzeltme:** İlk 5 alan adıyla yaptığım denemede kayıtların hepsi karardan sonraydı ve "arşiv karardan sonra geliyor" diye düşünmüştüm. 100 alan adı bu genellemeyi çürüttü. Gerçek tablo daha karışık. Arşiv verisi en çok, yeni kurulmuş olmayan alan adlarında var. Asıl hedef olan yeni alan adlarında ise zayıf. [yorum]

### Bu sonuç ne demek?

- **Aşama 0 kapıları büyük olasılıkla geçilir (50 alan adı, 35 sertifika), ama bu kapılar zayıf.** Engellenen bir sitenin önceden sertifikası olması beklenen bir şey. %100 çıkması yöntemin işe yaradığını göstermez.
- **Bu sayı erken uyarı süresi değil.** Yalnızca sertifika ile karar arasındaki boşluğu gösteriyor. Hiçbir sinyal ölçülmedi.
- **Pozitifler tek tip değil.** Bir kısmı yeni ve kısa ömürlü görünüyor, üçte biri ise yıllardır sertifika kaydı olan adresler. Raporda iki alt grup ayrı verilmeli. [yorum]
- **"Yeni sertifika" tek başına pozitiflerin en az üçte birini kaçırabilir.** Önceki sahip etkisi bu oranı değiştirebilir, bunu Kadircan ölçmeli. [yorum]

Bu ön test Kadircan'ın Aşama 0 işinin yerine geçmez. CSV bültenlerden kendisi tarafından doğrulanmalı.

## 3. Kritik sorunlar

### 3.1 İleriye dönük testte eşleşme çok az çıkabilir

Plan, 6–10. haftalarda yeni sertifikaları izlemek. Yani bir alan adının hem sertifikası hem kararı yaklaşık 5 haftaya sığmalı. Ön testte 28 gün içine düşen oran %17.

Kaba hesap: [yorum]

- Örnekte yaklaşık 9 ayda 100 alan adı var, ayda 11 eder. Örnek eksik olduğu için gerçek hacim birkaç katı olabilir.
- 5 haftada bu, yaklaşık 13 ile birkaç on karar arası alan adı yapar. Kararlar da toplu geldiği için bu pencerede hiç karar çıkmayabilir.
- Bunların yaklaşık %17'si pencereye girer. Kuralların kaçırdıkları da düşünce beklenti **0 ile birkaç eşleşme** olur.

**Çözüm:**

1. Canlı sertifika toplamayı 2. haftaya al. Mevcut plandaki hafta 2 yalnızca belirli alan adlarını sorguluyor, ileriye dönük testin dayandığı canlı akış hiçbir haftada yok.
2. Kurallar donunca son 6–12 ayın sertifikalarını da aday yap. Testin geçerli olması için sertifikanın yeni olması gerekmez, **taahhüdün karardan önce yapılması** yeterlidir. Pilotta 180 gün içine %41, 1 yıl içine %67 düşüyor. Bu geçmiş veriyi toplamanın uygulanabilirliği denenmedi.
3. 10. haftada ara sonuç yayımla, testi sürdür, 3–6 ay sonra ek rapor yaz.
4. Düşük eşleşme olasılığını rapora baştan yaz. Sıfır çıkması sonucu geçersiz kılmaz.

**Çalışma ortamı:** Canlı akış için 7/24 açık bir makine gerekir. Kesintiler `collection_log` içine yazılmalı, rapora kapsama oranı olarak eklenmeli. Kendi `certstream-server-go` sunucusu aday çözüm, kaynak kullanımı denenmedi. [özet]

### 3.2 SHA-256 taahhüdü eşleşmeyi kanıtlayamaz

Tüm listenin özetini doğrulamak için tüm listeyi açmak gerekir. Bu, açıklanmaması gereken adları da açar. Üstelik git commit tarihi elle değiştirilebilir.

**Mantık:**

- Her alan adı için ayrı rastgele tuz kullan. Tuz yoksa biri alan adı tahmin ederek özeti çözebilir.
- Özetleri Merkle ağacına koy, kökü yayımla. Sonra yalnızca eşleşen kaydı, tuzunu ve yolunu açıkla.
- Kaydı OpenTimestamps veya RFC 3161 ile zaman damgala.
- Her taahhüt bir öncekini içersin, kayıt sayısını da söylesin. Böylece listeden sonradan silme veya tek bir liste seçip diğerlerini gizleme zorlaşır.
- Kullanılan araç ve format kilit öncesi `docs/DECISIONS.md` içine yazılsın.

### 3.3 Kontrol grubu sinyali yanıltabilir

Lisanslı kurumlar ve köklü haber siteleri eski alan adlarıdır. Pozitifler ise çoğunlukla yenidir. Bu durumda "yeni sertifika" kuralı parlak görünür ama yalnızca yaşı ölçmüş olur. [yorum]

**Ekle:** Aynı dönemde ilk sertifikasını almış, finans kelimesi içeren, normal görünen alan adları. Grubu seçme yöntemi test öncesinde yazılmalı.

**Kural 4 ile çakışma:** Kontrol listesi SPK veya USOM listesinde olmayan siteler içeriyor. Repoda, logda ve raporda adlarıyla bulunmamalı. Yanlış alarm veren site de adıyla yazılmamalı. Yöntem ve özet sayılar yayımlanır, liste `data/private/` altında kalır.

### 3.4 Metrikler gerçek hayatı göstermiyor

- **Taban oran:** Dengeli test setinde iyi görünen kural, milyonlarca sertifikada çok fazla alarm üretir. Crimson yaklaşık 6 milyar alan adından 43.572 site çıkardı. [okundu] Bu projede de "günde kaç alarm" ve gerçekçi oranda precision verilmeli.
- **Erken uyarı süresi:** Her şeyi işaretleyen kural bu metrikte en iyi çıkar. Süre, sabit bir yanlış alarm düzeyinde verilmeli. SPK karar tarihi de ilk tespit tarihi değildir, süre "SPK'dan önce bulduk" diye sunulmamalı.
- **Bağımsızlık:** Aynı bültendeki ve aynı ağdaki kayıtlar bağımsız değil. Bir bülten pilotun neredeyse üçte birini oluşturuyor. Ayırma ve bootstrap bülten veya küme düzeyinde yapılmalı.
- **Kapı değerleri:** 50 alan adı çok düşük. Elde olan tüm 2026 bültenleri işlenmeli, sonuç bülten başına raporlanmalı.

### 3.5 Kilitli test verisi zaten kirlenmiş olabilir

Bu değerlendirme için 2026 bültenleri okundu, ad kalıpları görüldü: Numaralı adlar, yabancı aracı kurum adlarının taklidi, Türkçe finans kelimeleri. Bu bilgi belgeye yazıldığı için artık Kadircan'ın kural seçimini etkileyebilir. [yorum]

**Çözüm:** Son kilitli test, kurallar dondurulduktan sonra yayımlanan bültenlerden oluşsun. Daha eski bültenlerle yapılan test "geçmiş veri testi" diye ayrı ve daha zayıf kanıt olarak raporlansın.

### 3.6 Arşive dayanan her şey riskli

Yeni alan adlarında karar öncesi 200 kodlu kayıt %38, en yeni iki bültende 0/5. İçerik kalitesi de henüz ölçülmedi. Bu yüzden arşiv içeriği sinyalleri ve iddia modülü, sertifika sinyalleri kadar güvenilir değil.

**Yeni kapı:** Aşama 0'da "1 yıldan yeni alan adlarında, karar öncesi kayıtların kaçı gerçekten yatırım içeriği taşıyor?" diye elle bak. Karar sonrası kayıtlar iddia incelemesinde kullanılabilir ama erken uyarı ölçümüne girmez.

### 3.7 Kurumlara bildirim testi bozar

"Şüpheli bulgular kurumlara bildirilir" kuralı ile ileriye dönük test çakışıyor. Bildirilen site sonra engellenirse bu, başarı değil kendi etkin olabilir. Ayrıca rastgele alarmları göndermek kuruma yük olur. [yorum]

**Karar gerekiyor:** Bildirim test bitene kadar ertelenebilir veya bildirilenler kaydedilip sonuçtan ayrılabilir. Açık, ağır bir mağdur riski varsa etik olarak bekletmemek gerekebilir. Politika test başlamadan `docs/DECISIONS.md` içine yazılmalı.

## 4. Çıkarılacak veya küçültülecekler [yorum]

| Madde | Öneri | Neden |
|---|---|---|
| Hafta 7, halka arz | Ana plandan çıkar, "zaman kalırsa" bölümüne al | En zor metin problemi, tarih bazlı karşılaştırma ister, çekirdek soruya hizmet etmez |
| Hafta 6, lisans iddiaları | Koşullu yap | 3.6'daki kapıyı ve 30 örneği geçmezse yapılmaz |
| Hafta 5, grafik ve sayfa benzerliği | Sertifika ve ad tabanlı basit kümeye indir | Küme bilgisi ayırma için gerekli, ama görselleştirme değil |
| Engel sonrası dönüş analizi | İsteğe bağlı | İlginç ama ek soru |
| Panel | 3–4 sayfa, statik sunum | Az saldırı yüzeyi, basit CSP. Panelde vitrin değil, ölçüm önemli |
| 5 kategorili sinyal seti | Baseline artı 2–3 sinyal ile başla | Az sinyali iyi ölçmek daha değerli |

## 5. Eklenecekler

### Ölçüm

- Lisanslı Türk kurumlarının yanına yabancı aracı kurum markaları da eklenmeli. Listelerde bu kalıp görüldü. [ölçüldü, sayılmadı]
- Eski gerçek platformlar ile kısa ömürlü siteler alt grup olarak ayrılmalı.
- Dış karşılaştırma: Aynı alan adları USOM, IOSCO I-SCAN veya başka düzenleyici listelerinde SPK'dan önce görünmüş mü? Bedava ve güçlü bir soru. Erişim ve şartlar doğrulanmadı.
- Crimson'daki gibi engel listeleriyle örtüşme ölçümü de eklenebilir (orada VirusTotal %20, Metamask %2, Google Safe Browsing %1). [okundu]

### Veri toplama

- crt.sh'te `%.alanadi` ve `deduplicate=Y` kullanılmalı. Ön sertifikalar aynı sertifikayı çift gösterir. Tam ad sorgusu ile sonuç farklı çıktı (14 ve 12 satır, tek örnek). [ölçüldü]
- Wayback'te `matchType=domain` denenmeli. Tek örnekte tam ad 4, alan adı düzeyi 188 kayıt verdi. Karar bir kez verilip belgelenmeli. [ölçüldü]
- crt.sh sık hata veriyor (100 sorguda 7 başarısız, üçer denemeyle). Hatalı sonuç "sertifika yok" olarak kaydedilmemeli. [ölçüldü]
- Kullanım şartları kaydı tutulmalı. Arşiv içeriğinin tamamı repoda veya panelde yeniden dağıtılmamalı.

### Etik ve panel

- Alan adları tıklanamaz ve bozulmuş biçimde gösterilmeli (`ornek[.]com`). Favicon ve uzak görsel çekilmemeli.
- Her kayıtta "karar tarihi itibarıyla" yazmalı. Alan adı sonradan el değiştirebilir veya karar kalkabilir.
- Düzeltme ve kaldırma talebi süreci yazılmalı. Yayın öncesi bir hukuk bilgisi olan kişiye sormak iyi olur, bu belge hukuki görüş değildir.
- Sertifika ve arşiv metninde kişi verisi temizlenmeli.

### Güvenlik

- Blazor'da `MarkupString` yasak. `javascript:` bağlantısı ve CSV formül enjeksiyonu test edilmeli.
- Statik SSR, CSP'yi basitleştirir. Sunucu etkileşimi gerekirse yeniden düşünülür.
- SSRF: Yalnızca izinli arşiv alan adları, her yönlendirmede yeniden doğrulama, DNS rebinding için `SocketsHttpHandler.ConnectCallback` ile bağlantı anında IP denetimi.

### Proje düzeni

- `data/private/` ve kontrol listesi OneDrive içinde durmamalı, çünkü orası buluta eşitleniyor. Git deposunu da OneDrive içinde tutmak bozulma riski taşır. [yorum]
- `dxa` adında uygun bir araç bulamadım. Ne olduğu netleşmeli. Alternatif adaylar: Security Code Scan, Roslyn analizörleri, CodeQL. Bunlar bu projede denenmedi.

## 6. Önerilen sıra [yorum]

| Hafta | İş |
|---|---|
| Aşama 0 | CSV, tüm 2026 bültenleri, gecikme dağılımı, arşiv içerik örneği, iddia modülü kararı |
| 1 | Solution, Docker, CI, veri yükleme, normalleştirme |
| 2 | crt.sh ve Wayback toplayıcıları, **canlı sertifika akışı**, güvenli çekici |
| 3 | Ölçüm altyapısı, kontrol gruplarının yazılı seçimi, **taahhüt aracı** |
| 4–5 | Baseline ve 2–3 sinyal, basit küme, **kuralları dondur, bildirim politikasını yaz** |
| 6 | İleriye dönük test başlar, geçmiş adaylar eklenir. Kapı geçtiyse lisans modülü |
| 7 | Lisans modülü veya tampon. Takvim kaydıysa burada toparla |
| 8 | API ve küçük panel |
| 9 | Güvenlik testleri, tehdit modeli, yayın |
| 10 | Geçmiş test, ara rapor, ileriye dönük test sürer |
| Sonra | 3–6 ay sonunda ek rapor |

## 7. Benzer çalışmalar

- **Crimson, "The Poorest Man in Babylon" (WWW 2025)** [okundu]: En yakın çalışma. Sertifika kayıtları, kelime filtresi ve büyük dil modeli ile 2024'ün 8 ayında 43.572 kripto yatırım sitesi raporluyor, %88'i en az bir kümede. Siteleri ziyaret edip üye oluyor, bu proje bunu yapmaz. Etiketi düzenleyiciden gelmiyor, Türkçe değil. **Sonuç:** Yöntem kanıtlanmış, ama aynı başarı beklenmemeli.
- **Double and Nothing** [özet]: Sertifika ve kelime ile hediye dolandırıcılığı tespiti.
- **DomainProfiler, DomainDynamics, DarkDNS** [özet]: Alan adı geçmişinden erken tespit. Başarı rakamlarının çoğu kendi yazarlarının ölçümü, doğrudan karşılaştırılmamalı.
- **Unit 42** [özet]: Satıcı kaynağı, bağımsız ölçüm değil.
- **phishing_catcher, certstreamcatcher, nettfiske, hunter** [özet]: Açık kaynak, kelime puanı tabanlı. phishing_catcher tarzı yöntemlerin bilinen sorunu yanlış alarm. Çoğu ölçüm içermiyor, bu projenin fark yaratabileceği yer burası.
- **Türkçe veya SPK odaklı çalışma:** Yaptığım tek aramada bulamadım. Bu "yok" demek değil, rapordan önce daha kapsamlı aranmalı.

## 8. Açık maddeler

| ROADMAP maddesi | Durum |
|---|---|
| Alan adlarının toplu çıkarılabilirliği | Evet, kısmi örnekte çalışıyor [ölçüldü] |
| Karar öncesi sertifika ve arşiv oranı | Ön sonuç var, doğrulanmış CSV ile tekrar edilmeli [ölçüldü] |
| Yetkili kurum listesi, halka arz tarihleri | Açık. SPK ve KAP için API bulamadım, SPK aracı kurum sayfasının içeriği okunmadı |
| USOM şartları | Açık. Resmi kullanım şartını bulamadım |
| Canlı sertifika izleme | Kısmi. `certstream-server-go` aday, denenmedi |
| dxa | Açık. Araç bulunamadı |
| Yöntemi ölçen akademik kaynak | Crimson ve benzerleri var, ama düzenleyici etiketiyle yapılmış bağımsız ölçüm bulamadım |
| urlscan limitleri, geçmiş DNS verisi | Doğrulanmadı |

## 9. Kaynaklar

Erişim: 9 Kasım 2026. Yalnızca Crimson makalesi baştan sona incelendi, diğerleri kısa inceleme düzeyindedir.

- Crimson: https://securitee.org/files/crimson_www2025.pdf
- Double and Nothing: https://par.nsf.gov/servlets/purl/10428450
- Unit 42: https://unit42.paloaltonetworks.com/detecting-malicious-stockpiled-domains/
- DomainDynamics: https://arxiv.org/pdf/2405.12010
- DarkDNS: https://arxiv.org/abs/2410.02096
- phishing_catcher: https://github.com/x0rz/phishing_catcher
- hunter: https://github.com/ImLunaHey/hunter
- Phishing Catcher değerlendirmesi: https://phish.report/blog/phishing-catcher
- IOSCO I-SCAN: https://www.iosco.org/i-scan
- Tranco: https://tranco-list.eu
- Common Crawl dizini: https://index.commoncrawl.org
- SPK 2026 bültenleri: https://spk.gov.tr/spk-bultenleri/2026-yili-spk-bultenleri
- USOM topluluk arşivi: https://github.com/elliotwutingfeng/USOM-Blocklists
- certstream-server-go: https://github.com/d-Rickyy-b/certstream-server-go
