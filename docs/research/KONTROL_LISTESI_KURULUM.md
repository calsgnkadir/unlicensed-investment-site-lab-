# Kontrol Listelerinin Kurulabilirliği ve Kurulum Planı

Tarih: 20 Kasım 2026. Tüm erişim tarihleri 2026-11-20. Bu belge liste **kurmaz**. Yalnızca [KONTROL_GRUBU_YONTEMI.md](KONTROL_GRUBU_YONTEMI.md) (Bölüm 10.1 ile onaylanan hâli) için kaynakların ve yolların çalışıp çalışmadığına bakar ve somut bir kurulum planı verir.

**Bu belgede alan adı yok** (Kural 4). Kaynak sitelerin adresleri yalnızca kaynak göstermek içindir, kontrol grubuna ait hiçbir ad yazılmadı. Hiçbir şüpheli siteye ya da kontrol grubu üyesine istek atılmadı. Bu bir yatırım tavsiyesi değildir.

## Nasıl okunur

Üst belgedeki işaretlerle aynı:

- **[doğrulandı]** Sayfa bu çalışmada açıldı, ilgili yer bir araç özetiyle okundu. Tam metin satır satır okunmadı.
- **[özet]** Yalnızca arama sonucu özetinden görüldü.
- **[doğrulanamadı]** Aranıp bulunamadı ya da içerik alınamadı. "Yok" demek değildir.
- **[yerel]** Bu reponun kendi belgesinden ya da kodundan okundu.
- **[hesap]** Yerel rakamlardan yapılmış kaba hesap. Ölçüm değil.
- **[varsayım]** Ölçülmemiş örnek rakam.

---

## 0. Kısa sonuç

**Kesin engel yok.** Ama beş şey planı değiştiriyor:

1. **G1 küçülüyor.** Web adresi resmi sayfada yazılı olan tek grup bankalar (66 kuruluş). SPK'nın aracı kurum ve portföy yönetim sayfalarında web adresi yok. Belgedeki kurala göre bu kurumlar dışarıda kalır. G1 beklenen 150–250 yerine yaklaşık 66 olur (Bölüm 1).
2. **G2 için Kadircan'ın bir Cloudflare token'ı alması gerekiyor.** Token'sız çalışmıyor (Bölüm 2).
3. **crt.sh sınırı bizim ayarımızdan düşük olabilir.** Operatörün 2023 tarihli açıklaması dakikada 5 istek diyor. Koddaki ayar 5 saniyede 1 (dakikada 12). Bu hız en az 12 saniyeye çıkmalı (Bölüm 5).
4. **G3a büyük olasılıkla geçmez.** crt.sh'te tarihe göre arama yok ve tek sorgu yaklaşık 10.000 satırla sınırlı (Bölüm 3.3). Geçmezse belgede zaten yazılı yol uygulanır: Geçmiş testte zaman eşleşmeli zor negatif yok.
5. **G3 ve G4b'nin 1.000 ve 700 hedefi, F tarihinde (yaklaşık 6 Aralık) yoktur.** Haftalık en çok 60 kuralıyla hedefe 17 ve 12 haftada ulaşılır. Listeler tek seferde dondurulamaz, haftalık ve eklemeli dondurulur (Bölüm 4.3, Bölüm 5.3). Bu, "bütün gruplar F'de dondurulur" fikriyle çelişir ve Değişiklik kaydı gerektirir.

**Ana veritabanı önerisi: Ubuntu'daki (WSL) PostgreSQL.** Pozitiflerin crt.sh ve Wayback verisi Ubuntu'da toplayıcıları yeniden çalıştırarak getirilir, pg_dump ile taşınmaz (Bölüm 3.2).

**Dizüstü yükü:** Tam boyutta haftalık DNS yaklaşık 13 saat. Günde 8–12 saat açık makinede tek günde bitmez, ama hafta toplamına sığar. Yük ilk haftalarda küçüktür ve 17 haftada büyür (Bölüm 5).

---

## Kadircan'ın yapması gerekenler

Sırayla. Hepsi bu belgedeki planın önkoşuludur.

1. **Cloudflare hesabı ve Radar token'ı (yalnızca G2 seçenek A için).** Hesabı ve token'ı sen açarsın, ben açmam.
   - Ücretsiz bir Cloudflare hesabı aç (iki adımlı doğrulamayı aç).
   - Hesap ayarlarında "Custom Token" oluştur. İzin: Account > Radar > Read. Resmi rehber: <https://developers.cloudflare.com/radar/get-started/first-request/> [doğrulandı, 2026-11-20].
   - Token'ı yalnızca Ubuntu'daki yerel bir gizli dosyaya yaz (iznini 600 yap), repoya, log'a, ekran görüntüsüne koyma. Token'ı bana yapıştırma. Kod ortam değişkeninden veya dosyadan okur.
   - Radar uç noktasının sayfası ayrıca "User Details Read" izni anıyor [özet]. İlk istek 403 verirse bu izni de ekle. Hangisinin gerektiği doğrulanmadı.
   - Hesap açmak istemezsen G2 için B seçeneğine (Wikidata) geçilir, bu hesapsızdır (Bölüm 2.4). Ama B'nin geçerli olması için üst belgede A'nın önkoşul denemesinden **geçememesi** gerekiyor (Bölüm 10.1, karar 6). Yani hesap açmamak, kararı değiştirmek demektir ve Değişiklik kaydı ister.
2. **TSPB Excel dosyasının başlık satırına bak (1 dakika).** Bağlantı: <https://tspb.org.tr/wp-content/uploads/2026/11/TSPB_Uye_Bilgileri-4.xlsx>. İki sayfası var. Bir "web adresi" sütunu olup olmadığını söyle. Yalnızca başlıkları oku, kişi bilgisine ve içeriğe bakma, kaydetme ya da paylaşma. Sütun varsa aracı kurumlar G1'e girebilir (Bölüm 1.2). TSPB'nin "resmi kaynak" sayılıp sayılmayacağına da sen karar verirsin (TSPB bir meslek kuruluşu, kamu kurumu değil).
3. **BDDK "Kullanım Şartları" sayfasını elle oku.** Sayfanın altlığında bağlantısı var, ben içeriğini alamadım (Bölüm 1.1). Kullanımımız: yalnızca kurumun web adresini okumak, yayımlamamak.
4. **Aşağıdaki Değişiklik kaydı maddelerini onayla** (Bölüm 7). Hiçbiri sonuç görülmeden yazılmalı.
5. **Wikidata (B) gerekirse:** Wikimedia, istemcinin kimliğini ve iletişim yolunu içeren bir `User-Agent` istiyor (Bölüm 2.4). Hangi iletişim yolunu yazacağını sen seç (kişisel e-posta yazmak zorunda değilsin, proje adresi yeterli olabilir).
6. **Kod için onay:** Kontrol alan adlarının toplayıcılara verilmesi kodda yok (Bölüm 3.4). Bu bir toplayıcı kodu işi. İstenip istenmeyeceğine sen karar ver.

---

## 1. G1: Lisanslı kurumlar

### 1.1 Bankalar (BDDK)

| Konu | Bulgu | Durum |
|---|---|---|
| Sayfa | <https://www.bddk.org.tr/Kurulus/Liste/77>. Başlık "Bankalar", dört alt grup: mevduat 35, kalkınma ve yatırım 20, katılım 10, TMSF 1 (66) | [doğrulandı] |
| HTML yapısı | Tablo değil, iç içe liste öğeleri. Her kayıtta sıra numarası, kurum adı, web adresi bağlantısı ve "Detay" yazısı var. Bazı kayıtlarda "(Dijital Banka)" etiketi | [doğrulandı] |
| Web adresi | Listede her kayıt için bağlantı var. Bağlantı yazısı bazen kısaltılmış (üç nokta), bu yüzden metin değil **`href` özniteliği** okunmalı. İlk üç kaydın `href` değeri `http://` veya `https://` ile başlıyor | [doğrulandı] |
| Veri nasıl geliyor | Liste metni doğrudan sayfa işaretlemesinde (sunucu tarafı). "Detay" penceresi tıklayınca doldurulan bir kutu gibi, orada web adresi alanı bu yakalamada boştu | [özet]: Kesin değil |
| Güncelleme tarihi | Liste düzeyinde yok (üst belge Bölüm 2.2 ile aynı) | [yerel] |
| Kullanım şartı | Altlıkta "Kullanım Şartları" başlığı var, bağlantı adresi alınamadı, içerik okunamadı. Araç özeti, alıntının kaynakla yapılabildiğini ve ticari kullanımın yazılı izne bağlı olduğunu söyledi | [özet] + [doğrulanamadı]: Elle oku (yukarıda madde 3) |
| robots.txt | `https://www.bddk.org.tr/robots.txt` 404 verdi, yani dosya yok | [doğrulandı] |
| Hız sınırı | Yazılı sınır bulunamadı | [doğrulanamadı] |

**Sonuç:** Web adresleri makineyle okunabiliyor. Tek bir sayfa isteği yeter (tek GET, `href` değerlerini ayıkla). Bu bir **tek seferlik** indirmedir, 5 saniye aralık gereksiz bile. Okuma kuralı: Yalnızca `Kurulus/Liste/77` sayfası, bağlantı öğeleri, `http(s)` olanlar, kayıtlı alan adına indirge. Bir kurum için `href` boşsa ya da alan adına çevrilemezse kurum dışarıda kalır (`E10`, sayılır). Alan adı elle aranmaz (karar 5).

### 1.2 SPK aracı kurumlar ve portföy yönetim şirketleri

**Soru:** Resmi kaynakta web adresiyle birlikte veriliyor mu? **SPK'nın kendi sayfalarında hayır.**

| Kaynak | Bulgu | Durum |
|---|---|---|
| <https://spk.gov.tr/kurumlar/yatirim-kuruluslari/araci-kurumlar> | Kurum listesi değil, kartlı bir giriş sayfası. Kurum başına web adresi yok. İletişim ve sahiplik bilgisi için TSPB sitesine yönlendiriyor | [doğrulandı] |
| <https://spk.gov.tr/faaliyet-izinleri/araci-kurumlar> | Tablo başlıkları: Ünvan, Kurum Statüsü, Kurum Durumu, Belge Tarihi, Belge No, Belge Durumu. **Web adresi sütunu yok.** Satırlar bu yakalamada gelmedi (sonradan yükleniyor olabilir) | [doğrulandı] |
| <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri> | Sekiz kutu. Firma listesi yok. "İletişim Bilgileri" kutusu KAP'taki portföy yönetim şirketleri listesine gidiyor | [doğrulandı] |
| <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri/mevcut-yetki-belgeleri> | Sütunlar: Şirket Adı, Belge, No, Durumu. **Web adresi sütunu yok.** Satırlar JavaScript ile yükleniyor ("Yükleniyor..."), sayfa kaynağında yok | [doğrulandı] |
| <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri/surekli-bilgilendirme> | Arama sonucu özeti, yaklaşık 50 şirketlik alfabetik liste gösteriyor, web adresi bilgisi yok. Sayfa açılmadı | [özet] |
| TSPB üye bilgileri <https://www.tspb.org.tr/uye-bilgileri/> | Sayfada HTML üye listesi yok. "Detaylı üye listesi" bir Excel dosyası (`.xlsx`, iki sayfa, yaklaşık 119 KB, bu çalışmada indirildi). Sütunlar **okunamadı** (dosya sıkıştırılmış, araç içini açamadı). İki ek bağlantı `uys2.tspb.org.tr` adresine gidiyor, biri giriş formu, genel bir liste yok | [doğrulandı] sayfa, [doğrulanamadı] sütunlar |
| TSPB kullanım koşulları <https://www.tspb.org.tr/kullanim-kosullari-ve-gizlilik/> | Yayımlanan çalışmalar kaynak gösterilerek izinsiz yayımlanabilir, ticari kullanım yazılı izne bağlı. Otomatik erişim, kazıma ya da API hakkında hiçbir şey yazmıyor | [doğrulandı] |
| KAP (portföy iletişim bilgileri) | KAP sayfası zaman aşımına uğradı, `robots.txt` isteği anlamsız bir durum kodu döndü. Şirket sayfasında web adresi olup olmadığı, otomatik erişim şartı: hiçbiri okunamadı | [doğrulanamadı] |

**Sonuç, belgenin kuralına göre (Bölüm 2.3, karar 5):**

- **Portföy yönetim şirketleri: dışarıda kalır.** SPK'nın kendi listesinde web adresi yok. KAP'ta olabilir, ama KAP'a erişim ve şartlar doğrulanmadı. Adres elle aranmaz.
- **Aracı kurumlar: şimdilik dışarıda.** Tek açık kapı TSPB Excel'i. Sütunu varsa ve Kadircan TSPB'yi resmi kaynak sayarsa girebilir. Bu belge metnin ("resmî bir kaynakta") içinde kalır, ama TSPB'nin bu sınıfa girip girmediği bir yorumdur. Karar Kadircan'ın.
- Kripto "faaliyette bulunanlar" listesi zaten G1 dışında (karar 5).

**G1 boyutu:** Yalnızca bankalar ise en çok 66 kurum (bir kurumun birden çok adresi varsa biraz fazla alan adı, aynı kuruma bağlı tek küme). Üst belgedeki 150–250 tahmini bankalarla sınırlı kalınca çöküyor [hesap]. Bu G1'in "sayım" olmasını bozmaz. Ama 66 üzerinden bir oranın aralığı geniş çıkar, bu yüzden G1 hiçbir zaman genel yanlış alarm oranına eklenmiyor (Bölüm 2.4 üst belge), kayıp küçük.

---

## 2. G2: Tranco + Cloudflare Radar kategorisi

### 2.1 Tranco

| Konu | Bulgu | Durum |
|---|---|---|
| Kalıcı kimlik nasıl alınır | `GET https://tranco-list.eu/top-1m-id` bugünün standart 1 milyonluk listesinin kimliğini döndürüyor. Bugün aldığım kimlik 5 karakterli bir kod (GQ9JK), yalnızca örnek, **dondurulmuş değil** | [doğrulandı] <https://tranco-list.eu/top-1m-id> |
| Tarihe göre kimlik | `GET https://tranco-list.eu/api/lists/date/{YYYYmmdd}` (ya da `latest`) cevabında `list_id`, `download`, `created_on`, `available` var | [doğrulandı] <https://tranco-list.eu/api_documentation>, <https://tranco-list.eu/api/lists/date/latest> |
| Kimliğe göre | `GET https://tranco-list.eu/api/lists/id/{list_id}` | [doğrulandı] aynı belge |
| İndirme adresi | `https://tranco-list.eu/download/{ID}/1000000` (ilk 1 milyon). Liste sayfasında `/download/{ID}/full` bağlantısı da var. Dosya biçimi (CSV, sütunlar) **indirilip kontrol edilmedi** | [doğrulandı] adres (JSON `download` alanı ve liste sayfası <https://tranco-list.eu/list/GQ9JK>), [doğrulanamadı] biçim |
| Bu listenin ayarları (örnek) | Günlük liste, 30 günlük pencere (21 Ekim – 19 Kasım 2026), sağlayıcılar CrUX, Farsight, Majestic, Radar, Umbrella, birleştirme Dowdall, **PLD süzgeci açık**, TLD süzgeci kapalı | [doğrulandı] |
| Lisans | Tranco sitesinde veri için lisans metni **bulunamadı**. Resmi python paketinin kodu MIT, veri lisansı yazılmamış. Yalnızca kaynak sağlayıcıların lisansları sayılıyor (Majestic CC BY 3.0, CrUX CC BY-SA 4.0, Cloudflare Radar CC BY-NC 4.0) | [doğrulandı] <https://tranco-list.eu/>, <https://github.com/DistriNet/tranco-python-package> |
| Atıf | Liste sayfası NDSS 2019 makalesini (Le Pochat ve diğerleri, DOI 10.14722/ndss.2019.23386) ve BibTeX'ini istiyor. Metinde listeye dipnot olarak liste sayfasının adresiyle başvurmayı öneriyor | [doğrulandı] <https://tranco-list.eu/list/GQ9JK> |
| Hız sınırı | Yalnızca `ranks/domain` ucunda saniyede 1 sorgu. Liste indirme için sayı **bulunamadı**. Biz tek indirme yaparız, `ranks` ucunu kullanmayız | [doğrulandı] <https://tranco-list.eu/api_documentation> |
| Kullanım şartı | Şart sayfası yok, yalnızca gizlilik politikası bağlantısı | [doğrulandı] |

**Kurulum adımı (Kadircan'ın kararı gerekmiyor):** Seçim günü `lists/date/{gün}` ile kimliği al, kimliği, `created_on` tarihini ve indirilen dosyanın SHA-256'sını manifestoya yaz. Dosya `data/private/control/src/` altında kalır.

**Pratik not:** Listede PLD süzgeci açık olduğu için alan adları zaten "kayıtlı alan adı" gibi. Biz yine kendi PSL'mizle yeniden indirgeriz, çünkü Tranco'nun PSL sürümü bilinmiyor (G4 adım 3).

**Lisans yorumu (hukuki görüş değil):** Tranco'nun lisansı yazılı değil, Radar bileşeni CC BY-NC. Biz ticari olmayan araştırma yapıyoruz. Raporda liste kimliği, makale atfı ve "Radar, CrUX, Majestic, Umbrella, Farsight kaynaklı birleşik liste" notu yazılır.

### 2.2 Cloudflare Radar

| Konu | Bulgu | Durum |
|---|---|---|
| Token gerekiyor mu | **Evet.** "API token'ını önce almalısın", token Custom Token olarak oluşturulur, izin Account > Radar > Read | [doğrulandı] <https://developers.cloudflare.com/radar/get-started/first-request/> |
| Kimlik doğrulama biçimi | `Authorization: Bearer {token}` başlığı. Eski yöntem (e-posta + Global API key) var, belge token'ı öneriyor | [doğrulandı] <https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get/> |
| Ücret | "Radar API'si ücretsizdir" (belge cümlesi) | [doğrulandı] <https://developers.cloudflare.com/radar/> |
| Hesap türü | Hangi plan gerektiği yazılmıyor. Token oluşturmak hesap gerektirir (çıkarım) | [doğrulanamadı] |
| Lisans | API verisi CC BY-NC 4.0. Atıf şartı Radar sayfasında ayrıca anlatılmıyor | [doğrulandı] <https://developers.cloudflare.com/radar/> |
| Hız sınırı | Radar'a özel sınır **bulunamadı**. Genel Cloudflare API sınırı: kullanıcı başına 5 dakikada 1.200 istek (dashboard, anahtar ve token toplamı), aşınca 5 dakika engel (429). Bizim hızımız saniyede 1 yani 5 dakikada 300 | [doğrulandı] <https://developers.cloudflare.com/fundamentals/api/reference/limits/> |
| Uç nokta | `GET https://api.cloudflare.com/client/v4/radar/ranking/domain/{domain}`. Cevapta `result.details_0` içinde `categories` listesi (`id`, `name`, `superCategoryId`) | [doğrulandı] aynı belge |
| Kullanım şartı (Radar'a özel) | Radar şartları sayfası (`/radar/platform/terms/`) 404. Genel Cloudflare şartları Radar'ı anmıyor | [doğrulanamadı] |
| Kategori sınıflaması | Bir alan adının en çok iki içerik kategorisi olabilir. Etiketleri bir dış satıcı veriyor, ölçütleri açık değil | [özet] |

Gizlilik: Radar'a yalnızca Tranco'nun herkese açık, popüler alan adları sorulur. Bu bir hedef siteye istek değil, Kural 2'yi bozmaz.

### 2.3 Kategori adları (üst belgenin "etiket adlarını doğrulayamadım" boşluğu)

Resmi alan adı kategorileri belgesi <https://developers.cloudflare.com/cloudflare-one/traffic-policies/domain-categories/> [doğrulandı, kimlik tablosu içinde, ben tabloyu okumadım] şunu veriyor:

- **Business & Economy** (üst kimlik 3): Brokerage & Investing, Business, Economy & Finance, Cryptocurrency, Personal Finance.
- **Education** (üst kimlik 6): Education, Educational Institutions, Reference, Science, Space & Astronomy.
- Üst düzey "News" yok. İki alt kategori var: **News & Media** (Entertainment altında) ve **News, Portal & Search** (Technology altında).
- Personal Finance (185), Brokerage & Investing (186) ve Reference (189) kimlikleri bir değişiklik duyurusundan [doğrulandı] <https://developers.cloudflare.com/changelog/2025-05-14-domain-category-improvements/>. Diğer kimlikleri ben okumadım.

**Önerim (karar Kadircan'ın ve seçimden ÖNCE belgeye yazılmalı):** G2 "haber ve eğitim" diyor. Aday küme: Economy & Finance, Personal Finance, News & Media, Educational Institutions. "Brokerage & Investing" ve "Cryptocurrency" **dışarıda** (aracılık ve kripto hizmeti sunan siteler "haber sitesi" değil, kolay etiketlenemez). Bu bir öneri, dondurma Kadircan'ın.

### 2.4 Önkoşul denemesi (3.3) ve B seçeneği

**Deneme tasarımı (Kadircan token'ı aldıktan sonra):**

1. Tranco listesinin ilk 1.000 sırası. Tohumlu özetle (`SHA-256(tohum|alan_adı)` artan) ilk 100.
2. Her biri için `ranking/domain/{alan_adı}`, saniyede 1 istek (100 istek, yaklaşık 2 dakika).
3. Sayım: Kaç tanesinde `categories` boş olmayan bir liste döndü. Hata, zaman aşımı, 401, 403, 429, 404 ayrı sayılır ve "kategori yok" sayılmaz (üst belge 3.2).
4. Eşik: En az 80/100. Altındaysa B.
5. Sonuç yayımlanmaz, yalnızca geçti/geçmedi ve sayı kaydedilir.

**Bilinen belirsizlik:** Üst belge "kategori döndü"nün ne demek olduğunu yazmıyor. "Boş olmayan liste" tanımı bu planda öneridir ve denemeden önce Değişiklik kaydına yazılmalı.

**B seçeneği (Wikidata), token yok, hesap yok:**

| Konu | Bulgu | Durum |
|---|---|---|
| Veri lisansı | Yapılandırılmış veri CC0 (kamu malı), atıf gerekmiyor | [doğrulandı] <https://www.wikidata.org/wiki/Wikidata:Licensing> |
| Sorgu hizmeti sınırı | Kesin 60 saniye süre sınırı. İstemci başına 60 saniyelik pencerede 60 saniye işlem zamanı, dakikada 30 hatalı sorgu, IP başına en çok 5 paralel sorgu. Aşınca 429 ve `Retry-After`. Sınırlar değişebilir | [doğrulandı] <https://www.mediawiki.org/wiki/Wikidata_Query_Service/User_Manual> |
| User-Agent | Betimleyici ve iletişim yolu içeren bir User-Agent zorunlu. `curl` gibi genel adlar kabul edilmiyor, tarayıcı kimliği kopyalanmamalı | [doğrulandı] <https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy> |
| Tarihe sabitleme | Sorgu hizmeti canlı veriyi sorar, tarihli bir anlık görüntü sunmuyor. Sabitleme: Sorgu metninin SHA-256'sı, çalıştırma anı ve çıktı dosyasının SHA-256'sı manifestoya yazılır | [hesap]: Yöntemin kendisi |

Kullanılacak öğe kimlikleri bu çalışmada Wikidata arayüz hizmetiyle doğrulandı [doğrulandı] (<https://www.wikidata.org/w/api.php>, `wbgetentities`): Q11032 gazete, Q41298 dergi, Q106651372 iş gazetesi (business newspaper, arama sonucu), Q8134 iktisat, Q43015 finans, P31 örneği, P279 alt sınıfı, P856 resmi web sitesi, P921 ana konu. Q43 Türkiye (gerekirse ülke süzgeci için).

Önerilen sorgu iskeleti (**çalıştırılmadı**, boyutu bilinmiyor):

```sparql
SELECT DISTINCT ?item ?site WHERE {
  { ?item wdt:P31 wd:Q106651372 }
  UNION
  { ?item wdt:P31 ?cls . VALUES ?cls { wd:Q11032 wd:Q41298 }
    { ?item wdt:P921 wd:Q8134 } UNION { ?item wdt:P921 wd:Q43015 } }
  ?item wdt:P856 ?site .
}
```

Sınırlar: Alt sınıf zinciri (`P279*`) kullanılmadı, çünkü 60 saniye sınırını aşma riski var. Kapsam büyük yayınlara ve İngilizce içeriğe kayar, bu yüzden 100'ün altı çıkabilir (üst belgedeki "yeterli örnek yok" kuralı). `P856` birden çok değer taşıyabilir, bitmiş kayıtlar (bitiş zamanı niteleyicisi) ayrıca ayıklanmalı. Hepsi boyut ve kalite için ölçülmemiş varsayım.

---

## 3. G3: Zor negatifler

### 3.1 Bugünkü durum [yerel]

- Ubuntu (WSL) PostgreSQL: `ct_candidates` (8 terimli, Çerçeve K), `ct_neutral_sample` (alan adı başına tek satır: kayıtlı alan adı, ilk görülme, ilk `not_before`, görülme sayısı, Çerçeve N), `dns_snapshots`, `ip_asn_snapshots`, `port_snapshots`. Ağ servisi `official_actions` için DNS, RIPEstat ve InternetDB topluyor ([deploy/linux/README.md](../../deploy/linux/README.md)).
- Windows geliştirme veritabanı: Pozitiflerin crt.sh (`certificates`, `certificate_domains`) ve Wayback (`archive_snapshots`) verisi.
- Şema ikisinde de aynı (aynı göç dosyaları).

### 3.2 Ana veritabanı: Ubuntu. Gerekçe

1. **Yeniden üretilemeyen veri Ubuntu'da.** `ct_candidates` ve `ct_neutral_sample` canlı akıştan geliyor, ham tile diske yazılmıyor. Kaybolursa geri kurulamaz. Pozitiflerin crt.sh ve Wayback verisi ise aynı kaynaktan **yeniden toplanabilir** (171 alan adı).
2. **Ağ verisi de Ubuntu'da.** DNS, RIPEstat, InternetDB orada ve haftalık kontrol DNS'i de orada çalışacak. Değerlendirme DNS dışında kalan veriyi kullanıyor ama bütün tablolar tek yerde olur.
3. **Aynı yer, aynı toplayıcı kuralı.** Üst belge 1.5 "bütün gruplar aynı konumdan ve aynı toplayıcıdan" ister. Pozitifleri Ubuntu'da yeniden toplamak bunu sağlar. Windows'tan taşınan veri ayrı makinede, ayrı tarihte, olası ayrı sürümle toplanmış olur (`collection_log` konumları farklı).
4. **pg_dump ile taşımak zahmetli ve hataya açık.** Kimlik sütunları `GENERATED ALWAYS` ([yerel], `UseIdentityAlwaysColumn`). `domains.id` iki veritabanında aynı olmak zorunda değil. `certificate_domains`, `archive_snapshots` ve `collection_log` bu kimliklere bağlı. Taşımak için ara şema, ana bilgisayar adı üzerinden eşleme ve `OVERRIDING SYSTEM VALUE` gerekir. Bir kimlik kayması sessizce yanlış alan adına veri bağlar.
5. **Maliyet düşük.** 171 alan adı için yeniden toplama yaklaşık 1–2 saat (aşağıda), elle bakılacak bir şey yok.

**Plan:**

1. Ubuntu veritabanında `official_actions` kaydını say (171 olmalı, 175'e yakın README ifadesi eski olabilir). Eksikse `import-official-actions` komutuyla gizli CSV'den yükle (CSV yalnızca `data/private/` altında kopyalanır).
2. `network` servisini durdur, elle tur (README'deki yöntem): `collect-crtsh`, sonra `collect-wayback` (komutlar var, [yerel] `Program.cs`). İkisi farklı sunuculara gittiği için aynı anda çalışabilir.
3. **crt.sh hızını önce ayarla** (Bölüm 5.1). Varsayılan 5 saniye, ayarla en az 12 saniyeye çıkar.
4. Karşılaştırma (yalnızca sayı): Pozitiflerden kaçının sertifikası var, kaçının arşiv kaydı var. Windows veritabanıyla farkı yaz. Beklenmeyen büyük fark varsa nedenini araştırmadan ilerleme. Windows veritabanı silinmez, yalnızca karşılaştırma ve yedek.
5. Değerlendirme komutları Ubuntu'daki veritabanıyla çalışır. `db` portu dışarı açık değil ([yerel]), bu yüzden değerlendirme Ubuntu içinde ya da `pg_dump` ile alınmış **salt okunur kopya** üzerinde yapılır (README "Veriyi Windows makineye taşıma"). Kilitli test yalnızca bir kez çalışır ve temiz commit ister, bunu Ubuntu'daki klonda çalıştırmak en az sürpriz verir.

**Bunun üst belgeyle çelişkisi yok.** Hiçbir önceden kayıtlı karar ana veritabanını söylemiyor.

**Dikkat:** Yeniden toplanan pozitif sertifikaların `observed_at` değeri bugünün tarihi olacak. Geçmiş testi sertifikayı `not_before` ile, arşivi kayıt zamanı + 7 günle bilinir sayıyor ([DECISIONS.md](../DECISIONS.md) 2026-11-16), `observed_at` kullanılmıyor. Bu nedenle sorun çıkarmaz. İleriye dönük testte "bizim gördüğümüz an" kullanılıyor ve bu pozitifler ileriye dönük teste girmiyor.

### 3.3 G3a denemesi: somut tasarım

**Önce kötü haber (kaynaklara göre):**

- crt.sh operatörü (Sectigo) Haziran 2023'te `crt.sh:443` için IP başına **dakikada 5 istek** ve `crt.sh:5432` için IP başına 5 eşzamanlı bağlantı dedi ve sınırların daha da düşebileceğini ekledi [doğrulandı] <https://groups.google.com/g/crtsh/c/QXQFoy331pE>. Üst belgedeki "dakikada 60" (2020) eski kalıyor.
- Kimlik aramasında tarih süzgeci yok. Operatör, kimlik aramasının PostgreSQL tam metin aramasıyla yapıldığını ve tarih dizinine kolayca bağlanamadığını söylüyor. Büyük sonuçlarda alt sorguda `LIMIT 10000` var ve kaldırılmayacak [doğrulandı] <https://groups.google.com/g/crtsh/c/yyqrsDdhmiQ> (yaklaşık 2021 tarihli konu).
- Eylül 2025'te bir kullanıcı `exclude=expired` ile zaman aşımı ve yaklaşık 900 satır sınırı bildirdi, sayfalama yok [özet].
- SLA yok, cevaplar "en iyi çaba" [özet].
- Tarih parametresi için resmi belge **bulunamadı** [doğrulanamadı].

**Çıkarım [hesap, ölçülmedi]:** "Tek ay, tek terim" için sorgu yazılamıyor. `%terim%` sorgusu o terimi içeren tüm yıllardaki sertifikaları kapsar ve en çok yaklaşık 10.000 satır döner. Belirli bir ayın en az 50 kayıtlı alan adını verme şansı düşük. G3a **büyük olasılıkla geçmeyecek**. Bu kötü değil: Üst belge bunun yolunu önceden yazdı ("Geçmiş testte zaman eşleşmeli zor negatif yok").

**Deneme (üst belge 4.4'ün somutlaştırılması):**

| Alan | Değer |
|---|---|
| Sorgu sayısı | 3 (tek ay, tek terim, 3 deneme) |
| Terim | Baseline'ın 8 teriminden tohumlu özetle biri. Alan adı yok, yalnızca terim |
| Ay | Pozitiflerin ilk sertifika aylarından biri (ayar bölümünden), tohumlu özetle |
| Hız | Deneme arası 15 saniye (operatör sınırı dakikada 5, yani en az 12 saniye). Her saatin ilk 10 dakikasında deneme yok (üst belge) |
| Zaman aşımı | 120 saniye. Cevap tam gelmeli (kesilmiş değil) |
| Başarı | Üst belge: Her iki denemede 120 sn içinde tam yanıt ve en az 50 kayıtlı alan adı. Bu hedef ayda geçerli: Cevaptaki sertifikalardan `not_before` o ayda olanların kayıtlı alan adı sayısı |
| Kayıt | Her denemenin sonucu sınıflanır: tam, zaman aşımı, 429, 5xx, boş, kesik (yaklaşık 10.000 satır). Hata "sonuç yok" sayılmaz |
| Süre | En çok yaklaşık 8 dakika |

**Üst belgedeki iki belirsizlik (Değişiklik kaydında netleştirilmeli, sonuçtan önce):**

1. Metin "3 deneme" diyor ama başarı ölçütü "her iki denemede" diyor. Önerim: 3 denemenin en az 2'si geçmeli.
2. "Tek ay" crt.sh'te sorguya dönüştürülemiyor. Gerçek ölçüt, dönen sertifikaların `not_before` alanı o aya düşenlerin sayısı olmalı.

**İkinci yol (yeni yöntem, kayıtla ilk):** crt.sh'in doğrudan PostgreSQL erişimi (5432). 2017 duyurusu [doğrulandı, yalnızca duyuru] <https://groups.google.com/g/crtsh/c/sUmV0mBz8bQ>, 2023'te hâlâ sınır tanımlanmış olması bugün de var olduğunu gösteriyor [doğrulandı] (aynı Haziran 2023 açıklaması). Bugün çalışıp çalışmadığı ve tarih süzgeçli bir `ILIKE` sorgusunun zaman aşımına uğrayıp uğramadığı **bilinmiyor**. Aynı ölçüt, aynı 3 deneme, IP başına en çok 1 bağlantı. Bu yöntem üst belgede yok, bu yüzden denemeden önce Değişiklik kaydına eklenmeli. Eklemek istemezsen bu yol hiç denenmez.

**Geçerse maliyet [hesap]:** Pozitiflerin 127 "yeni" alan adının ilk sertifika ayları yaklaşık 12–24 ay. Terim sayısı 55. Ay başına terim başına en az 1 istek: yaklaşık 660–1.320 istek. 15 saniyede 1 istekle 2,8–5,5 saat. Zaman aşımlarıyla en kötü durum 2 dakika × 1.320 = 44 saat. Geçmezse bu yük hiç doğmaz.

### 3.4 Kodda eksik olan [yerel]

Kontrol alan adlarını toplayıcılara verecek bir yer yok:

- `CollectionRunner` hedefleri yalnızca `official_actions` içinden alıyor. `NetworkTargetQuery.DomainsAsync` `OfficialActions` dışındaki her grupta `NotSupportedException` atıyor. Kodun kendi notu "yeni grup eklenince yalnızca bu sınıf değişir" diyor.
- `ct_neutral_sample` kayıtlı alan adını düz metin olarak saklıyor, `domains` tablosuyla bağı yok.
- Gerekenler: (a) kontrol grubu üyelerini tutan bir tablo (grup kodu G1..G4, çerçeve K/N, alan adına bağlı `domains` kaydı, atanma tarihi, tohum), (b) `DomainSet` için yeni değerler, (c) kimlik ve tuzlu Merkle taahhüdü. Üretim kodu ayrı bir iş, bu belge yalnızca ister.
- Seçim betiği Ubuntu'da çalışmalı (veritabanı ve `data/private/control/` orada).

---

## 4. G4: Seçim betiğinin adımları

Betik `scripts/control/` altında, sürüm kontrollü. Hiçbir alan adını ekrana ya da log'a yazmaz, yalnızca tuzlu kimlik ve sayı basar. Her eleme bir kod taşır ve sayılır.

### 4.1 Çalıştırma sırası

Öncelik sırası G1 > G2 > G3 > G4 olduğu için betikler bu sırayla çalışır: G1, G2, G3, sonra G4a ve G4b. Böylece E02 elemesi öncekilerin dondurulmuş listesine göre yapılır.

### 4.2 G4a (Tranco 300)

1. **Tohum.** `kontrol-g4a-YYYY-MM-DD` biçiminde bir metin. Seçimden **önce** yazılır ve commit edilir.
2. **Girdi.** Seçim günü sabitlenmiş Tranco liste kimliği, ilk 1.000.000 satır. Dosya ve SHA-256'sı manifestoya.
3. **Kayıtlı alan adına indirge.** Hafta 1 normalleştirmesi ile: Küçük harf, `www.` at, IDN için Unicode ve punycode ikisi de, tam Public Suffix List ile eTLD+1. PSL sürümünün commit ve tarihi manifestoya. Normalleştirilemeyen satırlar ayrı sayılır (`E00`). Çoğul kayıtlar tekilleştirilir.
4. **PSL özel bölümünü ele (`E06`).** Normalleştirici özel bölümü de kullanıyor ([yerel], `PublicSuffixDomainNormalizer` notu), yani ortak barındırma alt alanları "kayıtlı alan adı" sayılıyor. Yöntem: Her alan adı iki kez indirgenir, bir kez tam liste ile, bir kez yalnızca ICANN bölümü ile (liste `===BEGIN PRIVATE DOMAINS===` işaretinde kesilir). İki sonuç farklıysa alan adı özel bölümdeki bir soneğin altında demektir, elenir. ICANN bölümü işareti doğrulandı. Özel bölüm işareti araç çıktısı kesildiği için bu çalışmada okunamadı [doğrulandı: yalnızca ICANN işareti ve MPL 2.0 lisans başlığı, <https://publicsuffix.org/list/public_suffix_list.dat>]. Betik çalışırken iki işareti de kontrol edip yoksa durmalı.
5. **Pozitiflerle çakışmayı ele (`E01`).** 171 pozitif alan adının kayıtlı alan adıyla eşleşen satırlar. Karşılaştırma aynı normalleştirilmiş biçimde.
6. **Öncelik (`E02`).** G1, G2 ve G3 listelerinde olanlar elenir (öncelik G1 > G2 > G3 > G4). G3 haftalık eklemeli dondurulduğu için (Bölüm 5.3) G4a seçimi, o gün dondurulmuş G3 üyelerine göre yapılır. Sonradan G3'e giren bir alan adı G4a'dan çıkmaz, çünkü G4a o tarihte dondurulmuştur. Bu çakışmanın sayısı rapora yazılır, kuralın bu hâli de Değişiklik kaydına eklenmelidir.
7. **Kişi adına benzeyen desen (`E04`).** Betik yalnızca sayar (üst belge 1.6).
8. **Sırala.** `SHA-256(tohum + "|" + kayıtlı_alan_adı)` artan. İlk 300 alınır. Python ya da .NET rastgele sayı üreteci yok.
9. **Manifest.** Girdi sayısı, her eleme kodunun sayısı, seçilen sayı, PSL sürümü, tohum, betik commit'i, liste SHA-256, Merkle kökü. Repoya yalnızca alan adsız kısım girer.

### 4.3 G4b (CT tarafsız 700) ve G3 (zor negatifler)

G4b ve G3'ün N çerçevesi aynı havuzdan (`ct_neutral_sample`) geliyor, adımlar neredeyse aynı:

1. Tohum (`kontrol-g4b-...`, `kontrol-g3n-...`), seçimden önce yazılır.
2. Girdi: `ct_neutral_sample`, tarih penceresi içindeki satırlar. Bu tabloda kayıtlı alan adı zaten var, ama toplayıcının PSL sürümüyle. Sürüm manifestoya yazılır, betik yine tam ve ICANN-only karşılaştırmasıyla `E06` uygular (tablo özel bölüm adlarını bilerek tutuyor, üst belge Değişiklik kaydı).
3. `E01`, `E02` (G1 > G2 > G3 > G4 ve G4a). Not: G3'ün sözlük eşleşmesine giren ama G3'e seçilmeyen alan adı G4b'de kalabilir. Bu doğal rastgele örneklemin gereği. Üst belge bunu yazmıyor, Değişiklik kaydına tek satır eklenmesini öneririm.
4. G3 için ek süzgeç: Finans sözlüğü v1 ([FINANS_SOZLUGU.md](FINANS_SOZLUGU.md), SHA-256 `65b9dcc4…`) kayıtlı alan adının soldaki etiketine uygulanır. Hangi terim eşleşti kaydedilir (baseline alt kümesi mi, yalnızca geniş mi).
5. Hafta tabakası. **Hangi sütun?** Tabloda hem `first_seen_at` (süreç anı) hem `first_logged_at` (log zamanı) var. Dizüstü kapalıyken kayıtlar sonradan işlenir, `first_seen_at` kümelenir. Üst belge "ilk görülme" diyor ve sütun adı yok. Hangisinin kullanılacağı seçimden önce Değişiklik kaydına yazılmalı. Önerim: `first_logged_at` (gerçek olay zamanı, kapalı kalma etkilenmez).
6. Yeni / yaş sınıfı için aday başına bir kez crt.sh (üst belge 4.5 madde 1). Hata "yok" sayılmaz. Adaylar tohumlu özet sırasıyla sorulur ve kota dolunca durulur, böylece boşuna sorgu atılmaz.
7. Kota: G3 yalnızca "yeni" (≤ 28 gün) ve sözlükte olan, haftada en çok 60, tohumlu özetle ilk 60. G4b yaş sınıfı kotası %23 / %51 / %26. **Eksik:** 60'ın bu oranlara bölünmesinde yuvarlama kuralı yazılı değil (13,8 / 30,6 / 15,6). Seçimden önce yazılmalı (öneri: en büyük kalan yöntemi).
8. Haftalık çıktı eklemeli dondurulur (Bölüm 5.3). Hafta h için seçim, h haftası bittikten sonra bir kez yapılır, manifest zincire eklenir.

---

## 5. Veri toplama yükü

### 5.1 Varsayımlar

- Seçilen alan adı sayısı tam boyutta: G1 yaklaşık 66, G2 300, G3 1.000, G4a 300, G4b 700, pozitif 171. Kontrol 2.366, toplam yaklaşık 2.540. (Üst belgenin 2.700'ü G1'in 200 olması içindi.) [hesap]
- crt.sh: Seçilen alan adı başına en az 1 istek. G3 ve G4b adayları için "yeni mi / yaş sınıfı" sorusuyla seçilmeyenler de sorgulanıyor. Seçilen başına 1–3 fazladan sorgu **varsayımı** (ölçülmedi). Yeniden deneme payı %15.
- Wayback: Yeni alan adı başına 1–2 sayfa. Eski ve popüler alan adı (G1, G2, G4a) başına 3–10 sayfa (`MaxPages` 10 tavan, [yerel]). Varsayım.
- DNS: Alan adı başına 18 sorgu, saniyede 1 ([yerel] README, [hesap]).
- Hepsi **ölçülmedi**, hesap ve varsayımdır.

### 5.2 Süre tablosu

| İş | İstek (yaklaşık) | 5 sn aralık | 12 sn aralık | 15 sn aralık |
|---|---|---|---|---|
| crt.sh, yalnızca seçilenler (2.540) | 2.540 | 3,5 sa | 8,5 sa | 10,6 sa |
| crt.sh, adaylarla birlikte | 4.250–7.650 | 5,9–10,6 sa | 14,2–25,5 sa | 17,7–31,9 sa |
| Wayback | 4.900–9.500 | 6,8–13,2 sa | Gerekmez | Gerekmez |
| DNS, haftalık (18 sorgu, 1/sn) | 45.700 sorgu | | | 12,7 sa |

- crt.sh için 5 saniye sütunu yalnızca "kullanıcı varsayımı"nı göstermek için. Operatörün 2023 tarihli sınırı dakikada 5 (12 saniye). Koddaki varsayılan 5 saniye bunu aşıyor ([yerel] `CrtShOptions`, ROADMAP satır 60 da "5 saniyede 1 istekle başla" diyor). **15 saniye** öneririm (sınırın tam üstünde değil, pay bırakır).
- Wayback için yazılı resmi sınır yok. Topluluk dakikada ~15 diyor, 5 saniye (dakikada 12) altında ([yerel] DATA_SOURCES). Değişmiyor. Resmi belge hiçbir sınırdan bahsetmiyor [doğrulandı] <https://github.com/internetarchive/wayback/blob/master/wayback-cdx-server/README.md>. Arşiv sitesinin şartlar sayfası okunamadı [doğrulanamadı] <https://archive.org/about/terms.php>.
- DNS: 18 sorgu × 2.540 = 45.720 sorgu, saniyede 1 ise 12,7 saat. Zaman aşımları ve yeniden denemeler eklenince 13–15 saat. Çözücülerin sınırı bizimkinden çok yüksek: Google Public DNS varsayılanı IP başına saniyede 1.500 sorgu [doğrulandı] <https://developers.google.com/speed/public-dns/docs/isp>. Cloudflare 1.1.1.1 için yazılı sınır bu çalışmada aranmadı. Yani 1/sn sınırı **teknik zorunluluk değil, kendi koyduğumuz nezaket kuralı** ([yerel] README, "ortak sınır saniyede 1 paket").
- IP aşaması (RIPEstat ve InternetDB): Alan adı başına ortalama 1–3 benzersiz IP, CDN paylaşımı yüzünden daha az olabilir (**varsayım**), saniyede 1 istek, IP başına haftada bir: yaklaşık 1–3 saat/hafta.

### 5.3 Dizüstüne sığar mı

Makine günde 8–12 saat açık: haftada 56–84 saat.

| Dönem | Kontrol üyesi | Haftalık DNS | Durum |
|---|---|---|---|
| F'de (yaklaşık 6 Aralık) | G1 66 + G2 300 + G4a 300 + G3b ve G4b en çok yaklaşık 3 hafta × 60 × 2 = 360, toplam ≈ 1.030 (+171 pozitif) | ≈ 6 saat | Günde 8 saate sığar |
| Tam boyut (yaklaşık Mart 2027) | ≈ 2.540 | ≈ 13 saat | Tek güne **sığmaz**, haftaya sığar |

Haftalık sınırı (G3b ve G4b için 60) uygulayınca hedeflere ulaşma süresi: G3 1.000 / 60 ≈ 17 hafta, G4b 700 / 60 ≈ 12 hafta. Akış 14 Kasım'da başladığına göre G3'ün dolması yaklaşık Mart 2027 ortası [hesap]. Üstelik havuz her hafta 60'ı bulmayabilir: Günde 200 örneklenmiş yeni alan adı hedefi ([üst belge 4.5]) haftada 1.400, sözlükle eşleşme oranı birkaç yüzde, "yeni" olanlar bunun bir bölümü. Haftada 60 G3 adayı **çıkmayabilir** [varsayım, hesap]. Bunu bilmenin yolu, `ct_neutral_sample` üzerinde yalnızca sayı veren bir ölçüm (üst belge Bölüm 9 madde 2'nin parçası). Bu ölçüm seçim değil, sonuca göre kural değiştirmek de değil.

**Öneri (sırayla toplama, boyut küçültme yok):**

1. **Boyutları küçültme.** Önceden kayıtlı hedefler (karar 8) aynen kalır. Hedefe ulaşılan süre uzar, bu "ulaşılan boyut raporda yazılır" demek (üst belge 4.5: "1.000'e ulaşmayan haftalar boş kalır, doldurulmaz").
2. **Bir kerelik toplu iş (G1, G2, G4a, pozitifler):** crt.sh yaklaşık 830 istek, 15 saniyede ≈ 3,5 saat (+yeniden deneme ≈ 4 saat). Wayback yaklaşık 2.300–7.000 istek, ≈ 3–10 saat. İki kaynak paralel çalışır, **1–2 günlük açık süre** yeter.
3. **Haftalık akış (G3, G4b):** Hafta başına yüzlerce crt.sh sorgusu, 15 saniyede haftada yaklaşık 1–3 saat. Yani yük ilk haftalarda küçük, artık haftalık adım.
4. **DNS iki akşama bölünür, grup karışık.** Tam boyutta ≈ 13 saat tek güne sığmaz. Öneri: Haftada bir "parti" iki ardışık gün (örneğin cumartesi ve pazar, 6,5 saat + 6,5 saat). Alan adları tohumlu özetle iki güne **karışık** bölünür, böylece hiçbir grup sistematik olarak başka güne düşmez. Her satır zaten kendi zaman damgasını taşıyor.
5. **Uyku ve kesinti:** Ağ toplayıcısının önbelleği (`CacheTtl` 7 gün, [yerel]) yarım kalan turu kaldığı yerden sürdürüyor. Makine uyuyunca tur durur, açılınca devam eder.

**Bu öneri önceden kayıtlı bir kararla çelişiyor:** Üst belge 1.5 "pozitif ve kontrol **aynı gün** ve aynı partide" diyor. İki güne bölmek bu maddeyi gevşetir. Henüz hiçbir kontrol DNS verisi toplanmadığı için sonuç görülmeden değiştirilebilir, ama **Değişiklik kaydı** ister. Alternatif, DNS sorgu hızını 2/sn'ye çıkarmak (yaklaşık 6,5 saat, tek gün). O da ağ planındaki "saniyede 1 paket" kuralını değiştirir ([yerel] README). Çözücüler için teknik risk düşük, ama kuralın değişmesi bir karar. İkisinden birini Kadircan seçer. Benim tercihim ilki: 1/sn kuralı kalır, "aynı parti" tanımı iki güne genişler.

**CT izleyici için ayrı not [hesap]:** README, makinenin uykuya geçmemesini istiyor. 2027h1 saniyede yaklaşık 1 tile üretiyor, izleyici varsayılan 0,5 saniye aralıkla en fazla 2 tile/sn çekiyor. Günde 12 saat açık makine açıkken net +1 tile/sn kapatıyor, kapalıyken 1 tile/sn birikiyor: 12 saat açık/12 kapalı ancak dengede, 8 saat açık/16 kapalı **her gün ~29.000 tile geriden gider**. Çözüm yapılandırmada: `CT_MIN_REQUEST_INTERVAL=00:00:00.300` (en çok ~3,3 tile/sn) ile 8 saatlik açık süre dengeyi kurar. README bunu zaten anıyor. Ayrıca günde 15–16 GB'ı 8–12 saate sığdırmak ortalama 3–4 Mbit/s demek, bu da bant genişliği kotasıyla çakışırsa ayrıca bakılmalı. Log eski tile'ları sonsuza dek tutmayabilir, uzun kapalı kalma atlanan kayıt olur ve kapsama oranına yansır (README).

### 5.4 Çelişkiler, tek yerde

| Öneri | Çelişen kayıt | Gerekli iş |
|---|---|---|
| G1 yalnızca bankalar (≈ 66) | Üst belge 2.3 madde 4 "150–250 bekleniyor" | Tahmin yanlış çıktı, kural aynı kalıyor. Değişiklik kaydında tahmini düzelt |
| crt.sh en az 12 saniye (öneri 15) | ROADMAP "5 saniyede 1" ve `CrtShOptions` | Yapılandırma değişikliği, karar değil. DATA_SOURCES'a 2023 açıklaması eklenmeli |
| G3 ve G4b eklemeli haftalık dondurma | Üst belge Bölüm 9 madde 7 "tüm gruplar dondurulur", Bölüm 1.4 | Değişiklik kaydı |
| DNS "aynı parti" iki güne yayılır | Üst belge 1.5 "aynı gün" | Değişiklik kaydı |
| G3a ölçütü ve ikinci yol | Üst belge 4.4 ("3 deneme" ve "her iki") | Değişiklik kaydı, denemeden önce |
| Kota yuvarlaması, hafta sütunu | Üst belgede yazılı değil (çelişki değil, boşluk) | Değişiklik kaydı, seçimden önce |

---

## 6. Önerilen sıra

1. **Hemen:** Bölüm 7'deki Değişiklik kaydı maddeleri onaylanır. Kadircan token'ı alır. TSPB sütununa bakar. BDDK şartını okur.
2. **Hemen, sorgusuz ölçüm:** `ct_neutral_sample` için hafta başına havuz büyüklüğü sayısı (sayı, alan adı yok), pozitiflerin Let's Encrypt kapsama oranı (üst belge 4.6) ve `p_kw`.
3. **crt.sh hızı 15 saniyeye** ayarlanır, pozitifler Ubuntu'da yeniden toplanır, Windows ile karşılaştırılır (Bölüm 3.2).
4. G2 önkoşul denemesi (Bölüm 2.4). G3a denemesi (Bölüm 3.3). İkisi de sonuç yayımlanmadan kaydedilir.
5. G1: BDDK sayfası tek istekle okunur, `E10` sayılır.
6. Kontrol üyeleri için tablo ve `DomainSet` değişiklikleri.
7. Seçim betikleri sırayla: G1, G2, G4a bir kerelik. G3 ve G4b haftalık.
8. Kümeler (üst belge 7.5), sonra `E05` tabakası.

---

## 7. Değişiklik kaydına önerilen maddeler (Kadircan onaylayınca üst belgeye yazılır)

Hiç sonuç görülmeden yazılmalı. Bu belge üst belgeyi **değiştirmez**.

1. G1 beklenen boyutu bankalarla sınırlı (≈ 66). Aracı kurumlar yalnızca TSPB Excel'i web adresi içeriyor ve Kadircan TSPB'yi resmi sayıyorsa girer.
2. crt.sh isteği en az 12 saniye aralıkla (öneri 15).
3. G3 ve G4b'nin haftalık eklemeli dondurulması, ulaşılan boyutun raporlanması.
4. DNS partisi iki ardışık güne yayılabilir, gruplar karışık.
5. G3a ölçütünün somutlaştırılması ("3 deneme, en az 2'si", `not_before` ölçütü) ve ikinci yol (doğrudan veritabanı).
6. G2 kategori kümesi ("boş olmayan liste" tanımı ve seçilen kategori adları).
7. Hafta tabakasının sütunu (`first_logged_at`).
8. G4b ve G3 kota yuvarlama kuralı.
9. G3'te seçilmeyen sözlük eşleşmeleri G4b'de kalabilir.
10. Pozitiflerin crt.sh ve Wayback verisi Ubuntu'da yeniden toplandı (ana veritabanı Ubuntu).

---

## 8. Doğrulayamadıklarım

- BDDK "Kullanım Şartları" sayfasının içeriği. KAP'a erişim, KAP robots ve şartları, KAP şirket sayfasında web adresi alanı.
- TSPB Excel dosyasının sütunları.
- Radar'a özel hız sınırı ve şartları. Token için gereken hesap türü. `User Details Read` izninin gerekip gerekmediği.
- Tranco: indirilen dosyanın biçimi. Veri lisansı.
- Radar kategori kimlik tablosunun tamamı (isimler okundu, çoğu kimlik okunmadı).
- crt.sh: bugünkü gerçek hız sınırı (2023 açıklaması var, 2026 yok), tarih parametresi, doğrudan veritabanı erişiminin bugünkü durumu.
- Wayback: Resmi hız sınırı ve şartları (yok ya da okunamadı).
- Wikidata sorgusunun boyutu (çalıştırılmadı).
- Seçilmeyen aday sorgu sayısı, Wayback sayfa sayısı, IP başına alan adı sayısı, havuz büyüklüğü: hepsi varsayım.
- Ubuntu veritabanındaki `official_actions` sayısı (okunmadı).

## 9. Kaynaklar (erişim 2026-11-20)

- BDDK bankalar: <https://www.bddk.org.tr/Kurulus/Liste/77> [doğrulandı]. robots: <https://www.bddk.org.tr/robots.txt> 404 [doğrulandı]
- SPK: <https://spk.gov.tr/kurumlar/yatirim-kuruluslari/araci-kurumlar>, <https://spk.gov.tr/faaliyet-izinleri/araci-kurumlar>, <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri>, <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri/mevcut-yetki-belgeleri> [doğrulandı]. <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri/surekli-bilgilendirme> [özet]. <https://spk.gov.tr/robots.txt> 404 sayfası [doğrulandı]
- TSPB: <https://www.tspb.org.tr/uye-bilgileri/>, <https://www.tspb.org.tr/kullanim-kosullari-ve-gizlilik/> [doğrulandı]. Excel: <https://tspb.org.tr/wp-content/uploads/2026/11/TSPB_Uye_Bilgileri-4.xlsx> [indirildi, sütunlar okunamadı]
- Tranco: <https://tranco-list.eu/>, <https://tranco-list.eu/api_documentation>, <https://tranco-list.eu/top-1m-id>, <https://tranco-list.eu/api/lists/date/latest>, <https://tranco-list.eu/list/GQ9JK> [doğrulandı]. <https://github.com/DistriNet/tranco-python-package> [doğrulandı]
- Cloudflare: <https://developers.cloudflare.com/radar/get-started/first-request/>, <https://developers.cloudflare.com/radar/>, <https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get/>, <https://developers.cloudflare.com/fundamentals/api/reference/limits/>, <https://developers.cloudflare.com/cloudflare-one/traffic-policies/domain-categories/>, <https://developers.cloudflare.com/changelog/2025-05-14-domain-category-improvements/> [doğrulandı]. <https://developers.cloudflare.com/radar/platform/terms/> 404, <https://radar.cloudflare.com/about> 403 [doğrulanamadı]
- Wikidata: <https://www.wikidata.org/wiki/Wikidata:Licensing>, <https://www.mediawiki.org/wiki/Wikidata_Query_Service/User_Manual>, <https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy>, <https://www.wikidata.org/w/api.php> (`wbgetentities`, `wbsearchentities`) [doğrulandı]
- crt.sh: <https://groups.google.com/g/crtsh/c/QXQFoy331pE>, <https://groups.google.com/g/crtsh/c/yyqrsDdhmiQ>, <https://groups.google.com/g/crtsh/c/sUmV0mBz8bQ> [doğrulandı]. Eylül 2025 kullanıcı raporu ve "SLA yok" notu: arama özeti [özet] (<https://cert.imfht.com/crt-sh-rate-limit> ve ilgili konular)
- Wayback CDX: <https://github.com/internetarchive/wayback/blob/master/wayback-cdx-server/README.md> [doğrulandı]. <https://archive.org/about/terms.php> [doğrulanamadı]
- Google Public DNS sınırı: <https://developers.google.com/speed/public-dns/docs/isp> [özet]
- PSL: <https://publicsuffix.org/list/public_suffix_list.dat> [doğrulandı, kısmen]
- Yerel: [KONTROL_GRUBU_YONTEMI.md](KONTROL_GRUBU_YONTEMI.md), [FINANS_SOZLUGU.md](FINANS_SOZLUGU.md), [DECISIONS.md](../DECISIONS.md), [deploy/linux/README.md](../../deploy/linux/README.md), [DATA_SOURCES.md](../DATA_SOURCES.md), `src/Fraud.Collectors/` (`Program.cs`, `Collection/CollectionRunner.cs`, `Network/NetworkTargetQuery.cs`, `CrtSh/CrtShOptions.cs`, `Wayback/WaybackCdxOptions.cs`), `src/Fraud.Infrastructure/` (`PublicSuffixDomainNormalizer.cs`, `FraudDbContextModelSnapshot.cs`)
