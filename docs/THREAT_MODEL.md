# Tehdit Modeli

Tarih: 12 Kasım 2026. Kapsam: Hafta 1 ve Hafta 2 kodu (`src/`, `docker-compose.yml`, Dockerfile'lar, CI). Panel, DNS istemcisi ve Static CT istemcisi henüz yok; onlarla ilgili maddeler "gelecek risk" olarak işaretlendi.

Bu belge her yeni dış kaynak, yeni uç veya yayına alma öncesinde güncellenir. Testler: `tests/Fraud.Security.Tests`. Açık bulgular orada `Skip = "açık: ..."` ile görünür durur.

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

**Açık riskler (Bölüm 5):** Bozuk sıkıştırma, geçersiz UTF-8 ve yinelenen CDX sütunu istisna fırlatıyor. İstisna toplayıcıdan çıkınca o kaynağın turu durur ve `collection_log` satırı yazılmaz.

### 3.2 SSRF

**Senaryo:** Çekici, iç adrese (127.0.0.1, 169.254.169.254, Docker ağı, IPv6 eşlemeli adresler) istek atmaya kandırılır. Yollar: IP'nin başka yazımları (ondalık, sekizlik, onaltılık), kullanıcı bilgisi hilesi, izinli hosttan iç adrese yönlendirme, DNS rebinding.

**Mevcut önlemler:**
- Yalnızca `https`, yalnızca izin listesindeki tam ad (`web.archive.org`), varsayılan port, kullanıcı bilgisi yok, IP yazımı yok (`SafeUrlPolicy`).
- Yönlendirme otomatik izlenmez; her `Location` aynı denetimden geçer. En fazla 5 yönlendirme.
- Bağlantı anında denetim (`SafeConnector`): Ad bir kez çözülür, bütün adresler denetlenir, soket denetlenen IP'ye açılır. Bu, DNS rebinding'i kapatır. Karışık cevapta (biri özel) hepsi reddedilir.
- IPv6'da yalnızca 2000::/3 kabul edilir; 6to4, Teredo, NAT64, IPv4 eşlemeli ve uyumlu biçimler içerdikleri IPv4'e göre ya da toptan reddedilir.
- Vekil sunucu kullanılmaz (vekil, bağlantı denetimini atlatırdı).
- Toplayıcı istemcileri sabit adreslere gider ve yönlendirme izlemez.

**Açık riskler:** Bilinen SSRF açığı bulunmadı. Sertleştirme önerisi: İstek sürümü HTTP/1.1 veya 2'ye sabitlensin. HTTP/3 (QUIC) bağlantıları `ConnectCallback` üzerinden geçmez; bugün kullanılmıyor, ama biri sürümü yükseltirse bağlantı anındaki denetim atlanır.

### 3.3 Stored XSS (gelecek risk, panel Hafta 10)

**Senaryo:** Sertifika issuer'ı, arşiv adresi, MIME türü, sayfa başlığı veya `listed_as` alanı `<script>`, `javascript:` bağlantısı, RTL karakteri ya da Punycode ile benzer görünen ad içerir ve panelde çalışır veya okuyanı yanıltır.

**Mevcut önlemler:**
- Sertifika adları yalnızca DNS adı biçiminde saklanır (izin listesi regex'i).
- Arşiv adresi yeniden kurulur: yalnızca http ve https, `<`, `>`, `"`, satır sonu ve RTL yüzde kodlanır, ana bilgisayar Punycode'a çevrilir.

**Açık riskler ve kurallar:** Issuer ve `listed_as` serbest metin olarak saklanıyor; kontrol ve yön karakteri temizlenmiyor. Panel yapılırken: `MarkupString` yok, `href` yalnızca `https:` ile başlayan ve izinli hosttaki adresler için, Punycode ad hem ASCII hem Unicode gösterilir, grafik kütüphanesine giden etiketler kodlanır, CSP `script-src 'self'` ile başlar.

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

**Açık riskler:** Bülten numarası Unicode rakam kabul ediyor (aynı bülten iki ayrı anahtar olabilir). Sertifika ve arşiv verisinin içerik doğruluğu kanıtlanmaz; Static CT'de Merkle kanıtı (Hafta 3) bu boşluğu sertifika tarafında kapatacak.

### 3.7 Yanlış suçlama

**Senaryo:** Listede olmayan bir site, sistem tarafından "SPK listesinde" diye kaydedilir veya adıyla yayımlanır.

**Mevcut önlemler:** Elle doğrulama tablosu; platform alan adı reddi; API yalnızca toplu sayı döndürür; log ve raporda alan adı yok.

**Açık riskler:**
- E-posta adresi içeren CSV hücresi (`destek@posta-saglayicisi`) kullanıcı bilgisi atılarak e-posta sağlayıcısının alan adı olarak kaydediliyor.
- crt.sh `common_name` alanı, paylaşımlı sertifikalarda listede olmayan başka bir sitenin adı olabilir ve veritabanına giriyor (olası; crt.sh'in `name_value` içeriği ayrıca doğrulanmadı). Veritabanı gizli kalır, ama panel ve API bu alanları hedef alan adına göre süzmeden göstermemeli.

### 3.8 Gizli listenin sızması

**Senaryo:** Aday listesi git'e, Docker imajına, loga, metriğe, hata mesajına veya CI çıktısına girer.

**Mevcut önlemler:** `data/private/` ve `.env` `.gitignore` içinde; git geçmişinde hiç yer almadı (denetlendi). `.dockerignore` izin listesi mantığında, `data/` derleme bağlamına girmez. CI yalnızca okuma yetkili, eylemler commit'e sabitli, kimlik bilgisi saklanmıyor. API listelenmeyen alan adını adıyla döndürmüyor.

**Açık riskler:** Test ve belgelerdeki örnek adlar (`ornek.com`, `ornek-yatirim.com.tr` gibi) gerçek kayıtlı alan adı olabilir. Örneklerde `.example` ve `.test` uzantıları kullanılmalı. Herkese açık DNS sunucusu aday adları görür (bilinen sınır, NETWORK_PLAN 4.4).

### 3.9 Kişi verisi

**Mevcut önlemler:** Sertifika adlarında e-posta ve boşluklu metin düşer; issuer'da yalnızca C, O, OU, CN kalır ve `@` içeren değer düşer; veritabanında `@` için CHECK kısıtı var. Arşiv adresinden sorgu, parça ve kullanıcı bilgisi atılır, `@` içeren yol parçası `[redacted]` olur.

**Açık riskler:** Issuer'da çok değerli RDN (`CN=X + telephoneNumber=...`) bölünmüyor. Çift kodlanmış `%2540` yakalanmıyor. `listed_as` alanı ham metin tutuyor. Telefon numarası ve kişi adı yol parçasında kalabilir; bunu kesin ayıklamak mümkün değil, raporda sınır olarak yazılmalı.

### 3.10 Yapılandırma ve yetki

**Mevcut önlemler:** Compose portları yalnızca `127.0.0.1`; uygulama konteynerleri root değil, salt okunur dosya sistemi, bütün yetenekler düşürülmüş, `no-new-privileges`. Toplayıcı varsayılan olarak başlamaz. Parola `.env` dosyasından zorunlu alınır. API'de host filtresi yalnızca yerel adlar.

**Açık riskler:**
- API ve toplayıcı aynı PostgreSQL süper kullanıcısıyla bağlanıyor. API'nin yalnızca okuma yetkisi olmalı.
- API'de kimlik doğrulama, istek sınırı ve güvenlik başlıkları yok. Bugün yalnızca yerelden erişilebildiği için kabul edilebilir; yayından (Hafta 11) önce kapatılmalı.
- Taban imajlar sürüm etiketiyle sabitli, özet (digest) ile değil.
- CI'da bağımlılık zafiyet taraması ve statik analiz yok.

## 4. Bilinçli olarak kapsam dışı

- Hedef sitelere doğrudan bağlantı, port taraması, TLS el sıkışması (Kural 2). Bu yüzden ilgili saldırı yüzeyi de yok.
- Laboratuvar makinesinin kendisinin ele geçirilmesi.
- crt.sh veya Internet Archive'ın bilerek yanlış veri vermesi. Tek kaynak olduklarında bu ölçülemez; raporda sınır olarak yazılır.

## 5. Açık bulgular

Önem: Yüksek = gizli veri veya iç ağa erişim; Orta = toplama durur, yanlış kayıt veya kişi verisi saklanır; Düşük = dar koşulda veya ileride sorun.

| # | Önem | Yer | Özet | Test |
|---|---|---|---|---|
| 1 | Orta | `Collection/SourceCollector.cs` (CollectAsync) | Kaynak istemcisinden çıkan istisna yakalanmıyor; tur durur, o alan adı ve sonrakiler için `collection_log` satırı yazılmaz | `Malformed_cdx_page_does_not_escape_the_collector_as_an_exception` |
| 2 | Orta | `Http/PoliteHttpClient.cs` (SendOnceAsync) | Bozuk gzip, deflate, brotli gövdesi `InvalidDataException` veya `InvalidOperationException` fırlatıyor, yakalanmıyor | `Corrupt_compressed_response_becomes_an_error_kind_not_an_exception` |
| 3 | Orta | `Wayback/CdxResponseParser.cs` (Parse, ReadRow) | Yinelenen başlık sütunu `ArgumentException`, geçersiz UTF-8 `InvalidOperationException` | `Cdx_duplicate_header_column_...`, `Cdx_invalid_utf8_...` |
| 4 | Orta | `CrtSh/CrtShResponseParser.cs` (GetString) | Geçersiz UTF-8 `InvalidOperationException` | `CrtSh_invalid_utf8_inside_a_string_is_invalid_not_thrown` |
| 5 | Orta | `Fetching/SafeFetcher.cs` (OpenDecoder, GetEncoding) | Bozuk brotli ve `charset=utf-7` istisna fırlatıyor; çekici "sonuç döner, fırlatmaz" sözleşmesini bozuyor | `Corrupt_compressed_body_...("br")`, `Hostile_charset_...("utf-7")` |
| 6 | Orta | `DomainNames/PublicSuffixDomainNormalizer.cs` (ExtractHost) ve CSV yükleyici | E-posta adresi, sağlayıcının alan adı olarak resmi işlem kaydına dönüşüyor; `listed_as` e-posta ve kullanıcı bilgisi saklıyor | `Email_address_in_domain_column_...`, `Listed_as_text_does_not_store_...` |
| 7 | Düşük | `OfficialActions/OfficialActionCsvParser.cs` | `listed_as` formül önekiyle (`=`, `+`, `-`, `@`) saklanıyor; ileride CSV dışa aktarımında formül enjeksiyonu | `Listed_as_text_does_not_keep_spreadsheet_formula_prefix` |
| 8 | Düşük | `OfficialActions/OfficialActionCsvParser.cs` (BulletinNumberPattern) | `\d` Unicode rakam kabul ediyor | `Bulletin_number_accepts_only_ascii_digits` |
| 9 | Düşük | `CrtSh/CertificateFieldSanitizer.cs` (SanitizeIssuer) | Çok değerli RDN bölünmüyor; kontrol ve yön karakterleri kalıyor; 1024'te kesme vekil çiftini bölebiliyor | `Issuer_multi_valued_...`, `Issuer_control_...`, `Issuer_truncation_...` |
| 10 | Düşük | `CrtSh/CertificateFieldSanitizer.cs` (DnsNamePattern) | `$` sondaki satır sonunu kabul ediyor | `Dns_name_check_...`, `CrtSh_query_builder_...` |
| 11 | Düşük | `Wayback/ArchiveUrlSanitizer.cs` | Çift kodlanmış `%2540` yakalanmıyor | `Archive_url_double_encoded_email_is_redacted` |

Bir bulgu kapatılınca ilgili testteki `Skip` kaldırılır ve test yeşil olmalıdır.
