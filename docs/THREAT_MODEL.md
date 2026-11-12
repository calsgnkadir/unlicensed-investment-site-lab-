# Tehdit Modeli

Tarih: 12 Kasım 2026, `00c09c7` sonrasına göre güncellendi. Kapsam: Hafta 1 ve Hafta 2 kodu (`src/`, `docker-compose.yml`, Dockerfile'lar, CI). Panel, DNS istemcisi ve Static CT istemcisi henüz yok; onlarla ilgili maddeler "gelecek risk" olarak işaretlendi.

Bu belge her yeni dış kaynak, yeni uç veya yayına alma öncesinde güncellenir. Testler: `tests/Fraud.Security.Tests`. Açık bulgular orada `Skip = "açık: ..."` ile görünür durur.

**Durum:** Hafta 1-2 denetiminin 11 bulgusu `00c09c7` commit'inde kapatıldı (Bölüm 5). Her biri kodda tek tek doğrulandı. Güvenlik testlerinde `Skip` kalmadı; `dotnet test tests/Fraud.Security.Tests` sonucu 239 başarılı, 0 atlanan, 0 başarısız. Kalan açık riskler Bölüm 6'da bir arada.

## 1. Varlıklar

| Varlık | Neden önemli | Nerede |
|---|---|---|
| Gizli aday listesi, kontrol grubu, pasif port verisi | Sızarsa listede olmayan siteler suçlanmış olur (Kural 4, 8) | `data/private/`, veritabanı |
| SPK resmi işlem kayıtları | Etiketin tek kaynağı; bozulursa bütün ölçüm bozulur | `official_actions`, `domains` |
| Toplanan sertifika ve arşiv kayıtları | Sinyallerin girdisi; zehirlenirse sonuç yanlış çıkar | `certificates`, `certificate_domains`, `archive_snapshots` |
| `collection_log` | "Veri yok" ile "toplanamadı" ayrımını taşır; kapsama oranı buradan çıkar | `collection_log` |
| İleriye dönük test taahhüdü | Sonradan değiştirilirse test geçersiz olur | Repoda yalnızca kriptografik taahhüt (Hafta 6) |
| Veritabanı parolası | Veritabanına tam erişim verir | `.env` (git dışı), konteyner ortam değişkeni |
| Laboratuvar makinesi ve iç ağ | SSRF ile ulaşılabilecek hedef | Docker ağı, `127.0.0.1`, bulut metadata adresi |

## 2. Güven sınırları

1. **İnternet → toplayıcı:** crt.sh ve Wayback CDX yanıtları güvenilmez veridir. TLS kaynağın kimliğini doğrular, içeriğin doğruluğunu ve zararsızlığını doğrulamaz. CT loglarına herkes sertifika kaydettirebilir; arşive herkes sayfa kaydettirebilir.
2. **Arşiv sayfası → güvenli çekici:** `SafeFetcher` arşivden sayfa metni çeker (Hafta 2'de kayıtlı ama hiçbir iş çağırmıyor). Sayfa içeriği ve yönlendirmeleri saldırgan kontrolünde kabul edilir.
3. **SPK PDF → CSV → veritabanı:** Bülten metni betikle çıkarılır, elle doğrulanır, sonra yüklenir. PDF resmi kaynak olsa da çıkarılan metin hatalı veya beklenmedik olabilir.
4. **Veritabanı → API → (ileride) panel:** Veritabanındaki her metin güvenilmez kaynaktan gelmiştir; ekrana yalnızca kodlanmış metin olarak basılır.
5. **Uygulama → log, metrik, Grafana:** Bu kanallar daha geniş kitleye açıktır; alan adı ve çekilen metin buraya girmez.
6. **Laboratuvar makinesi → GitHub:** Repo herkese açıktır. Gizli liste, `.env`, `data/private/` girmez.

## 3. Tehditler, önlemler, açık riskler

### 3.1 Kötü niyetli site içeriği ve kaynak yanıtı

**Senaryo:** Saldırgan, arşive veya CT loguna özel hazırlanmış veri koyar: bozuk sıkıştırma, sıkıştırma bombası, geçersiz UTF-8, yinelenen JSON sütunu, kontrol karakteri, çok uzun alan.

**Mevcut önlemler:**
- Boyut sınırı sıkıştırma açıldıktan sonra sayılır (`BoundedStreamReader`); `Content-Length` yalanı işe yaramaz. gzip, deflate ve brotli bombaları test edildi.
- Deneme başına ve toplam süre sınırı var; yavaş akan gövde zaman aşımına düşer.
- JSON derinlik sınırı (crt.sh 8, CDX 4). Yanlış türdeki satır atlanır ve sayılır; HTML hata sayfası "geçersiz yanıt" olur, "kayıt yok" sayılmaz.
- Alan uzunlukları veritabanı sütunlarıyla uyumlu kesilir veya satır atlanır.

- Bozuk gzip, deflate ve brotli gövdesi `PoliteHttpClient` ve `SafeFetcher` içinde "geçersiz yanıt" olur, istisna fırlatmaz ve tekrar denenmez.
- crt.sh ve CDX ayrıştırıcıları geçersiz UTF-8'i baştan (`Utf8.IsValid`), kaçış dizisiyle yazılmış yalnız vekil karakteri de `catch` ile reddeder. Yinelenen CDX başlık sütunu yanıtı geçersiz yapar.
- `SourceCollector.CollectAsync`, kaynak tarafında gözden kaçan her istisnayı `InvalidResponse`, iç süre sınırını `Timeout` olarak `collection_log`'a yazar. `CollectionRunner` yazma hatasında turu durdurmaz; o hedef sonraki turda yeniden denenir. İstisna mesajı loglanmaz.

**Durum:** Bulgu 1-5 kapatıldı (`00c09c7`). Not: Genel `catch`, ileride bir programlama hatasını da "geçersiz yanıt" diye gizleyebilir. Sonuç yine "kayıt yok" sayılmadığı için ölçüm bozulmaz, ama `InvalidResponse` oranı izlenmeli; ani artış önce kod hatası olarak incelenmeli.

### 3.2 SSRF

**Senaryo:** Çekici, iç adrese (127.0.0.1, 169.254.169.254, Docker ağı, IPv6 eşlemeli adresler) istek atmaya kandırılır. Yollar: IP'nin başka yazımları (ondalık, sekizlik, onaltılık), kullanıcı bilgisi hilesi, izinli hosttan iç adrese yönlendirme, DNS rebinding.

**Mevcut önlemler:**
- Yalnızca `https`, yalnızca izin listesindeki tam ad (`web.archive.org`), varsayılan port, kullanıcı bilgisi yok, IP yazımı yok (`SafeUrlPolicy`).
- Yönlendirme otomatik izlenmez; her `Location` aynı denetimden geçer. En fazla 5 yönlendirme.
- Bağlantı anında denetim (`SafeConnector`): Ad bir kez çözülür, bütün adresler denetlenir, soket denetlenen IP'ye açılır. Bu, DNS rebinding'i kapatır. Karışık cevapta (biri özel) hepsi reddedilir.
- IPv6'da yalnızca 2000::/3 kabul edilir; 6to4, Teredo, NAT64, IPv4 eşlemeli ve uyumlu biçimler içerdikleri IPv4'e göre ya da toptan reddedilir.
- Vekil sunucu kullanılmaz (vekil, bağlantı denetimini atlatırdı).
- Toplayıcı istemcileri sabit adreslere gider ve yönlendirme izlemez.

- İstek sürümü HTTP/2 ve `RequestVersionOrLower` ile sabit (`SafeFetcher.RequestVersion`); `ConnectCallback` dışında kalan HTTP/3'e (QUIC) yükselmez. Hem istemci varsayılanı hem her istek bu değeri taşır; `tests/Fraud.Integration.Tests/Fetching/SafeFetcherTests.cs` denetler.

**Açık riskler:** Bilinen SSRF açığı yok. HTTP sürümü sertleştirmesi kapatıldı (`00c09c7`).

### 3.3 Stored XSS (gelecek risk, panel Hafta 10)

**Senaryo:** Sertifika issuer'ı, arşiv adresi, MIME türü, sayfa başlığı veya `listed_as` alanı `<script>`, `javascript:` bağlantısı, RTL karakteri ya da Punycode ile benzer görünen ad içerir ve panelde çalışır veya okuyanı yanıltır.

**Mevcut önlemler:**
- Sertifika adları yalnızca DNS adı biçiminde saklanır (izin listesi regex'i).
- Arşiv adresi yeniden kurulur: yalnızca http ve https, `<`, `>`, `"`, satır sonu ve RTL yüzde kodlanır, ana bilgisayar Punycode'a çevrilir.

- Issuer'da kontrol karakterleri boşluğa çevrilir, Unicode biçim karakterleri (yön işaretleri, sıfır genişlikli karakterler) silinir (`00c09c7`).
- `listed_as` artık ham hücre değil, normalleştirilmiş ASCII ad tutar (`00c09c7`).

**Açık riskler ve kurallar:** Issuer hâlâ serbest metindir (CA adı) ve HTML açısından zararsız sayılamaz. Panel yapılırken: `MarkupString` yok, `href` yalnızca `https:` ile başlayan ve izinli hosttaki adresler için, Punycode ad hem ASCII hem Unicode gösterilir, grafik kütüphanesine giden etiketler kodlanır, CSP `script-src 'self'` ile başlar.

### 3.4 Kötü niyetli DNS cevabı (gelecek risk, Hafta 4)

**Senaryo:** Kendi yazılacak DNS istemcisine sıkıştırma işaretçisi döngüsü, uzunluk taşması, yanlış işlem kimliği, sahte cevap veya beklenmedik kesik (TC) bayrağı gelir.

**Plan:** İşaretçi atlama sayısı ve toplam okunan bayt sınırlı; her uzunluk alanı kalan tampona göre denetlenir; işlem kimliği ve soru bölümü eşleşmezse cevap atılır; TCP'ye geçiş yalnızca TC bayrağıyla ve boyut sınırıyla. Fuzz testleri yalnızca yerelde, kayıtlı paketlerle çalışır. Hata "kayıt yok" olarak yazılmaz.

### 3.5 Sahte log verisi ve log enjeksiyonu

**Senaryo:** Çekilen metin loga yazılır ve `\r\n` ile sahte satır üretir ya da alan adı loga girer (Kural 4).

**Mevcut önlemler:** Bütün loglar derleme anında üretilen `LoggerMessage` metotlarıyla yazılır; parametreler sayı, enum ve kimliktir. Alan adı yerine `domain_id` yazılır. HttpClientFactory'nin istek adresi yazan log işleyicileri kaldırılmıştır. Beklenmeyen hatada yalnızca istisna türü yazılır. `LogSurfaceTests`, yeni bir log metodunun gözden geçirilmemiş metin parametresi almasını engeller.

**Açık risk:** Yok. Not: Komut satırı modunda (`collect-crtsh`) yakalanmayan istisna, .NET tarafından tam metniyle stderr'e basılır.

### 3.6 Veri zehirleme

**Senaryo:** Saldırgan, aday alan adına benzer sertifika kaydettirir veya arşive sahte sayfa koydurur; ya da CSV'ye hatalı satır girer.

**Mevcut önlemler:** Her kayıt kaynak, zaman ve `observed_at` taşır; `ON CONFLICT DO NOTHING` ile ilk görülen değer korunur. CSV'de kaynak bağlantısı yalnızca `spk.gov.tr`; aynı anahtarın farklı içeriği sessizce ezilmez, çakışma olarak raporlanır. Platform alan adları (t.me, instagram.com ...) reddedilir.

**Açık riskler:** Bülten numarası artık yalnızca ASCII rakam ve `\z` ile denetleniyor (kapatıldı, `00c09c7`). Sertifika ve arşiv verisinin içerik doğruluğu kanıtlanmaz; Static CT'de Merkle kanıtı (Hafta 3) bu boşluğu sertifika tarafında kapatacak.

### 3.7 Yanlış suçlama

**Senaryo:** Listede olmayan bir site, sistem tarafından "SPK listesinde" diye kaydedilir veya adıyla yayımlanır.

**Mevcut önlemler:** Elle doğrulama tablosu; platform alan adı reddi; API yalnızca toplu sayı döndürür; log ve raporda alan adı yok.

**Kapatılan:** `@` veya `mailto:` içeren girdi normalleştiricide `EmailOrUserInfo` hatasıyla reddedilir; e-posta adresi artık sağlayıcının alan adı için resmi işlem kaydına dönüşmez (`00c09c7`).

**Açık riskler:**
- Düzeltmeden önce yüklenmiş veritabanı satırları (olası): `00c09c7` veriyi temizleyen bir migration içermiyor. Eski kodla bir e-posta hücresi yüklendiyse sağlayıcının alan adı `domains` ve `official_actions` içinde, ham metin de `listed_as` içinde kalır. Veritabanı içeriği denetlenmedi. Öneri: CSV yeniden yüklenmeden önce tablolar boşaltılsın ya da `listed_as` için `@` içeren veya `=`, `+`, `-` ile başlayan değerleri arayan tek seferlik bir sorgu çalıştırılıp sonucu not edilsin.
- crt.sh `common_name` alanı, paylaşımlı sertifikalarda listede olmayan başka bir sitenin adı olabilir ve veritabanına giriyor (olası; crt.sh'in `name_value` içeriği ayrıca doğrulanmadı). Veritabanı gizli kalır, ama panel ve API bu alanları hedef alan adına göre süzmeden göstermemeli.

### 3.8 Gizli listenin sızması

**Senaryo:** Aday listesi git'e, Docker imajına, loga, metriğe, hata mesajına veya CI çıktısına girer.

**Mevcut önlemler:** `data/private/` ve `.env` `.gitignore` içinde; git geçmişinde hiç yer almadı (denetlendi). `.dockerignore` izin listesi mantığında, `data/` derleme bağlamına girmez. CI yalnızca okuma yetkili, eylemler commit'e sabitli, kimlik bilgisi saklanmıyor. API listelenmeyen alan adını adıyla döndürmüyor.

**Açık riskler:** Test ve belgelerdeki örnek adlar gerçek kayıtlı alan adı olabilir. Kısmen kapandı: `Fraud.Domain` ve normalleştirici belgeleri `.example`'a geçti; ama `tests/Fraud.Integration.Tests` altındaki crt.sh, Wayback, normalleştirici ve CSV testlerinde ve `docs/ROADMAP.md` içinde hâlâ `.com` ve `.com.tr` uzantılı örnekler var. Örneklerde `.example` ve `.test` uzantıları kullanılmalı. Herkese açık DNS sunucusu aday adları görür (bilinen sınır, NETWORK_PLAN 4.4).

### 3.9 Kişi verisi

**Mevcut önlemler:** Sertifika adlarında e-posta ve boşluklu metin düşer; issuer'da yalnızca C, O, OU, CN kalır ve `@` içeren değer düşer; veritabanında `@` için CHECK kısıtı var. Arşiv adresinden sorgu, parça ve kullanıcı bilgisi atılır, `@` içeren yol parçası `[redacted]` olur.

**Kapatılan (`00c09c7`):** Issuer'da çok değerli RDN (`CN=X + telephoneNumber=...`) `+` işaretinden bölünür ve izin listesi dışındaki alan düşer; değerin içindeki `+90...` bölmez. Arşiv adresinin yol parçası en fazla 4 kat yüzde kodlaması çözülerek denetlenir (`%40`, `%2540`); daha derin kodlama güvenli tarafta `[redacted]` olur. `listed_as` artık normalleştirilmiş ad tutar.

**Açık riskler:** Telefon numarası ve kişi adı yol parçasında kalabilir; bunu kesin ayıklamak mümkün değil, raporda sınır olarak yazılmalı.

### 3.10 Yapılandırma ve yetki

**Mevcut önlemler:** Compose portları yalnızca `127.0.0.1`; uygulama konteynerleri root değil, salt okunur dosya sistemi, bütün yetenekler düşürülmüş, `no-new-privileges`. Toplayıcı varsayılan olarak başlamaz. Parola `.env` dosyasından zorunlu alınır. API'de host filtresi yalnızca yerel adlar.

**Açık riskler:**
- API ve toplayıcı aynı PostgreSQL süper kullanıcısıyla bağlanıyor. API'nin yalnızca okuma yetkisi olmalı.
- API'de kimlik doğrulama, istek sınırı ve güvenlik başlıkları yok. Bugün yalnızca yerelden erişilebildiği için kabul edilebilir; yayından (Hafta 11) önce kapatılmalı.
- Taban imajlar sürüm etiketiyle sabitli, özet (digest) ile değil.
- CI'da bağımlılık zafiyet taraması ve statik analiz yok.

### 3.11 Listede olmayan aday adların sızması (gelecek risk, Hafta 3)

**Senaryo:** Hafta 3'te Static CT akışından, adında finans kelimesi geçen yeni sertifikalar `ct_candidates` tablosuna yazılacak. Bu adların neredeyse tamamı SPK veya USOM listesinde değildir; çoğu meşru şirkettir. Ad bir log satırına, Prometheus etiketine, Grafana panosuna, hata mesajına, rapora veya API yanıtına girerse sistem, listede olmayan bir siteyi "aday" bağlamında adıyla yayımlamış olur (Kural 4 ve 8). Olası yollar:
- Hata ayıklama için eklenen ve adı metin olarak yazan bir log satırı.
- Alan adını etiket yapan metrik (`ct_candidates_total{domain="..."}`); aynı zamanda etiket sayısı patlaması.
- Sorgu adresini veya değeri içeren istisna mesajı; ProblemDetails içine giren EF Core veya Npgsql hata metni (benzersiz anahtar çakışmasında değer yazılır).
- Aday listeleyen, sayfalı liste dönen ya da `?host=` ile var/yok cevabı veren bir API ucu. Adı bilen biri, sitenin aday olup olmadığını öğrenir.
- Rapora, CSV dışa aktarımına veya Grafana tablosuna giren örnek satırlar; `data/private/` dışına yazılan dışa aktarım dosyası.
- İleriye dönük test taahhüdünün tuzsuz özeti: aday adları tahmin edilip özetle karşılaştırılarak doğrulanabilir.

**Önlemler (Hafta 3 kodu yazılırken):**
- `ct_candidates` ayrı tablo kalır; `domains` veya `official_actions` ile birleşmez, aynı API sorgusuna girmez. Etiketin tek kaynağı yine SPK kaydıdır.
- Loglar mevcut `LoggerMessage` düzeniyle yazılır; aday için ad değil `candidate_id` veya sayı. `LogSurfaceTests` Static CT log metotlarını da kapsamalı.
- Metrik etiketleri yalnızca sabit küme (kaynak, sonuç türü). Aday sayısı toplu sayı olarak yayımlanır (ROADMAP Hafta 5, "günlük aday sayısı").
- API'de aday ucu yok. Gerekirse yalnızca toplu sayı döner; tek ad sorgusu, "bu ad aday mı" cevabı ve aday listesi yazılmaz. Geliştirme dışı ortamda ProblemDetails yalnızca genel başlık ve izleme kimliği taşır, istisna metni dönmez.
- Rapor ve panelde listede olmayan aday adı yazılmaz; yalnızca toplu sayı, oran ve yanlış alarm oranı. Bir aday sonradan SPK listesine girerse adıyla görünmesi SPK kaydına dayanır, aday tablosuna değil.
- Aday dışa aktarımı yalnızca `data/private/` altına yazılır. İleriye dönük test taahhüdü listenin kendisini değil, tuzlu ve tarihli kriptografik özetini repoya koyar; tuz `data/private/` altında kalır.
- Adaylar sertifika içeriğinden gelir; Bölüm 3.9'daki kişi verisi temizliği (yalnızca DNS adı biçimi, `@` reddi, issuer izin listesi) burada da uygulanır.

**Doğrulama (Hafta 3 ile birlikte, `tests/Fraud.Security.Tests`):** `.example` uzantılı sahte aday adlarıyla tam bir toplama turu çalıştırılır; yakalanan log çıktısı, metrik dökümü ve bütün API yanıtları bu adlar için aranır, hiçbiri geçmemeli. Aynı arama bir istisna yolunda (veritabanı çakışması) da yapılır.

## 4. Bilinçli olarak kapsam dışı

- Hedef sitelere doğrudan bağlantı, port taraması, TLS el sıkışması (Kural 2). Bu yüzden ilgili saldırı yüzeyi de yok.
- Laboratuvar makinesinin kendisinin ele geçirilmesi.
- crt.sh veya Internet Archive'ın bilerek yanlış veri vermesi. Tek kaynak olduklarında bu ölçülemez; raporda sınır olarak yazılır.

## 5. Denetim bulguları (Hafta 1-2)

Önem: Yüksek = gizli veri veya iç ağa erişim; Orta = toplama durur, yanlış kayıt veya kişi verisi saklanır; Düşük = dar koşulda veya ileride sorun.

Bütün bulgular `00c09c7` commit'inde kapatıldı. "Durum" sütunu kodda neyin değiştiğinin denetlendiğini gösterir. İlgili testlerin `Skip`'i kaldırıldı ve testler yeşil.

| # | Önem | Yer | Özet | Durum | Test |
|---|---|---|---|---|---|
| 1 | Orta | `Collection/SourceCollector.cs` (CollectAsync) | Kaynak istemcisinden çıkan istisna yakalanmıyordu; tur duruyor, `collection_log` satırı yazılmıyordu | Kapatıldı (`00c09c7`). Genel `catch` sonucu `InvalidResponse`, iç süre sınırını `Timeout` yazar; `CollectionRunner` yazma hatasında turu durdurmaz | `Malformed_cdx_page_does_not_escape_the_collector_as_an_exception` |
| 2 | Orta | `Http/PoliteHttpClient.cs` (SendOnceAsync) | Bozuk gzip, deflate, brotli gövdesi istisna fırlatıyordu | Kapatıldı (`00c09c7`). Gövde okuma `InvalidDataException` ve `InvalidOperationException` yakalar, tekrar denemeden `InvalidResponse` döner | `Corrupt_compressed_response_becomes_an_error_kind_not_an_exception` |
| 3 | Orta | `Wayback/CdxResponseParser.cs` (Parse, ReadRow) | Yinelenen başlık sütunu ve geçersiz UTF-8 istisna fırlatıyordu | Kapatıldı (`00c09c7`). `Utf8.IsValid` ön denetimi, `TryAdd` ile yinelenen sütun reddi, yalnız vekil için `catch` | `Cdx_duplicate_header_column_...`, `Cdx_invalid_utf8_...` |
| 4 | Orta | `CrtSh/CrtShResponseParser.cs` (GetString) | Geçersiz UTF-8 istisna fırlatıyordu | Kapatıldı (`00c09c7`). Aynı ön denetim ve `catch` | `CrtSh_invalid_utf8_inside_a_string_is_invalid_not_thrown` |
| 5 | Orta | `Fetching/SafeFetcher.cs` (ReadBodyAsync, GetEncoding) | Bozuk brotli ve `charset=utf-7` istisna fırlatıyordu | Kapatıldı (`00c09c7`). Brotli için `InvalidOperationException`, karakter kümesi için `NotSupportedException` yakalanır; UTF-7 açık olsa bile UTF-8'e düşülür | `Corrupt_compressed_body_...("br")`, `Hostile_charset_...("utf-7")` |
| 6 | Orta | `DomainNames/PublicSuffixDomainNormalizer.cs` ve CSV yükleyici | E-posta adresi sağlayıcının alan adı olarak kaydediliyordu; `listed_as` e-posta saklıyordu | Kod tarafında kapatıldı (`00c09c7`): `@` ve `mailto:` reddi, `listed_as` artık normalleştirilmiş ad. **Kısmen açık (olası):** düzeltmeden önce yüklenmiş satırlar temizlenmedi, Bölüm 3.7 | `Email_address_in_domain_column_...`, `Listed_as_text_does_not_store_...` |
| 7 | Düşük | `OfficialActions/OfficialActionCsvParser.cs` | `listed_as` formül önekiyle saklanıyordu | Kod tarafında kapatıldı (`00c09c7`): normalleştirilmiş ad formül öneki taşıyamaz. Eski satırlar için bulgu 6'daki not geçerli. CSV dışa aktarımı yazılınca serbest metin alanları (issuer) için formül kaçışı yine eklenmeli | `Listed_as_text_does_not_keep_spreadsheet_formula_prefix` |
| 8 | Düşük | `OfficialActions/OfficialActionCsvParser.cs` (BulletinNumberPattern) | `\d` Unicode rakam kabul ediyordu | Kapatıldı (`00c09c7`). `^[0-9]{4}/[0-9]{1,4}\z` | `Bulletin_number_accepts_only_ascii_digits` |
| 9 | Düşük | `CrtSh/CertificateFieldSanitizer.cs` (SanitizeIssuer) | Çok değerli RDN bölünmüyordu; kontrol ve yön karakterleri kalıyordu; kesme vekil çiftini bölebiliyordu | Kapatıldı (`00c09c7`). `+` ile bölme (yalnızca ardından alan adı ve `=` gelirse), Cc ve Cf temizliği, yüksek vekilde bir karakter önce kesme | `Issuer_multi_valued_...`, `Issuer_control_...`, `Issuer_truncation_...` |
| 10 | Düşük | `CrtSh/CertificateFieldSanitizer.cs` (DnsNamePattern) | `$` sondaki satır sonunu kabul ediyordu | Kapatıldı (`00c09c7`). `\z`; aynı düzeltme CDX zaman damgası ve özet kalıplarına da uygulandı | `Dns_name_check_...`, `CrtSh_query_builder_...` |
| 11 | Düşük | `Wayback/ArchiveUrlSanitizer.cs` | Çift kodlanmış `%2540` yakalanmıyordu | Kapatıldı (`00c09c7`). En fazla 4 kat çözme, daha derini `[redacted]` | `Archive_url_double_encoded_email_is_redacted` |
| - | Sertleştirme | `Fetching/SafeFetcher.cs` | HTTP/3'e yükselme bağlantı anındaki IP denetimini atlatabilirdi (Bölüm 3.2) | Kapatıldı (`00c09c7`). HTTP/2 ve `RequestVersionOrLower` | `SafeFetcherTests` (Integration) |

Yeni bulgu açılınca ilgili test `Skip = "açık: ..."` ile eklenir; kapatılınca `Skip` kaldırılır ve test yeşil olmalıdır.

## 6. Açık riskler

Kodda bilinen açık bulgu yok. Aşağıdakiler sertleştirme ve gelecek riskleridir; yayından (Hafta 11) önce kapatılmalı.

| Risk | Neden önemli | Öneri | Bölüm |
|---|---|---|---|
| API ve toplayıcı aynı PostgreSQL kullanıcısıyla bağlanıyor | API ele geçirilirse veriyi değiştirebilir veya silebilir | API için yalnızca `SELECT` yetkili ayrı rol; migration ayrı bir şema rolüyle | 3.10 |
| Taban imajlar özetle (digest) sabit değil | Aynı etiket başka içerikle yeniden yayımlanabilir | `FROM ...@sha256:...`; güncelleme bilinçli ve ayrı commit'te | 3.10 |
| CI'da bağımlılık zafiyet taraması ve CodeQL yok | Bilinen zafiyetli paket fark edilmeden kalır | `dotnet list package --vulnerable --include-transitive` adımı, CodeQL iş akışı, Dependabot | 3.10 |
| API'de kimlik doğrulama, istek sınırı ve güvenlik başlıkları yok | Bugün yalnızca `127.0.0.1`; yayında herkes çağırabilir, sayım sorgularıyla veritabanı yorulabilir | Yönetim ve sağlık uçları dışa kapalı, `AddRateLimiter`, CSP, HSTS, `X-Content-Type-Options`, `Referrer-Policy` | 3.10 |
| `ct_candidates` adlarının sızması (Hafta 3) | Listede olmayan siteler adıyla görünür (Kural 4) | Bölüm 3.11'deki önlemler ve sızıntı testi | 3.11 |
| Düzeltme öncesi yüklenmiş satırlar (olası) | Eski e-posta türevli kayıt veya ham `listed_as` kalabilir | Tablo temizliği veya tek seferlik denetim sorgusu | 3.7 |
| Örnek adlarda gerçek uzantılar | Test ve belgede gerçek bir siteye ad verilmiş olabilir | `.example`, `.test` | 3.8 |
| Issuer serbest metin; crt.sh `common_name` paylaşımlı sertifikada başka siteyi gösterebilir (olası) | Kodlanmadan basılırsa XSS; süzülmeden gösterilirse yanlış ilişkilendirme | Panel kuralları, hedef ada göre süzme | 3.3, 3.7 |
| Arşiv yolunda telefon ve kişi adı | Kesin ayıklanamaz | Raporda sınır olarak yazılır | 3.9 |
| Herkese açık DNS sunucusu aday adları görür | Bilinen sınır | NETWORK_PLAN 4.4 | 3.8 |
