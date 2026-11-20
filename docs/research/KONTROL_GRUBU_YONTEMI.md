# Kontrol Grubu Seçim Yöntemi (önceden kayıtlı)

Tarih: 15 Kasım 2026 (Hafta 6). Tüm erişim tarihleri 2026-11-15. Bu belge bir karar önerisidir. Kadircan onaylayıp commit'leyince "önceden kayıtlı" sayılır. Sinyal kodu yazılmadan önce dondurulur. Sonradan değişiklik yalnızca en alttaki "Değişiklik kaydı" bölümüne tarihle eklenir, eski metin silinmez.

**Amaç:** Kontrol gruplarını sonucu görmeden, kuralla seçmek. Böylece sinyal kontrol grubuna göre ayarlanamaz.

**Bu belgede alan adı yok.** SPK listesinde olmayan hiçbir sitenin adı yazılmadı (Kural 4). Gerçek listeler betikle `data/private/control/` altında üretilir ve repoya girmez (Kural 8). Kişi bilgisi toplanmaz (Kural 5). Bu bir yatırım tavsiyesi değildir.

## Nasıl okunur

- **[doğrulandı]** Sayfa bu çalışmada açıldı ve ilgili bölüm bir araç özetiyle okundu. Tam metin satır satır kontrol edilmedi. Yayımdan önce önemli şartları elle bir kez daha oku.
- **[özet]** Yalnızca arama sonucu özetinden görüldü.
- **[doğrulanamadı]** Aranıp bulunamadı ya da sayfa içeriği alınamadı. "Yok" demek değildir.
- **[yerel]** Bu reponun kendi belgesinden veya koddan okundu.
- **[hesap]** Yerel rakamlardan yapılmış kaba hesap. Ölçüm değildir.
- **[varsayım]** Ölçülmemiş örnek rakam. Yalnızca yöntemi göstermek için.

---

## 0. Kısa özet

1. Dört grup var, ama aynı "yanlış alarm" sayısına karıştırılmaz. Her grup ayrı raporlanır (ROADMAP Hafta 6).
2. En önemli sorun: Elimizdeki canlı sertifika akışı (`ct_candidates`) zaten baseline'ın terimleriyle süzülüyor. Bu yüzden o akıştan seçilen "zor negatifler" baseline için tanım gereği hep yanlış alarmdır. Bu **döngüsellik** (Bölüm 4.3). Çözüm: Tarafsız bir örneklem akışı eklemek ve sonuçları "örnekleme çerçevesi" başına ayrı vermek.
3. Pozitiflerin ilk sertifikaları çoğunlukla akışın başladığı günden (12 Kasım 2026) önce. Yani canlı akıştan seçilen kontroller geçmiş pozitiflerle ay ay eşleştirilemez. Geçmiş eşleşme için crt.sh denemesi gerekir ve çalışacağı doğrulanmadı (Bölüm 4.4).
4. Kilitli testte çok az pozitif beklenir (büyük olasılıkla 2–16, son aylarda ortalama 2). Ham sayı verilir, yüzde vermeyiz (Bölüm 7.4).
5. Gerçekçi taban oran şu an hesaplanamaz. Üç farklı günlük aday rakamı birbirini tutmuyor. Yöntem Bölüm 8'de, ölçüm veritabanından yapılacak.

---

## 1. Ortak kurallar (dört grup için)

### 1.1 Birim ve kayıtlı alan adı

- Birim: **kayıtlı alan adı** (eTLD+1). Hesaplama, Hafta 1'deki normalleştirmeyle aynı: `www.` atılır, küçük harf, IDN için Unicode ve punycode ikisi de saklanır, Public Suffix List (PSL). PSL sürümünün commit'i ve tarihi manifestoya yazılır.
- **Ortak barındırma ve ücretsiz alt alan adı platformları dışarıda.** PSL'nin özel bölümünde (private section) yer alan sonekler altındaki adlar kontrol grubuna girmez. Pozitiflerde de aynı kural var ([DECISIONS.md](../DECISIONS.md), "Ortak barındırma alanındaki tek bir alt alan adı: veriye alınmadı"). Böylece iki taraf aynı kurala bağlı.
- Park sayfası ayrımı **yapılmaz**: Siteye gitmeden park durumu anlaşılamaz. Bu bir sınırdır, raporda yazılır. Arşiv kaydı varsa yalnızca sayı olarak not edilir.

### 1.2 Pozitiflerle çakışma ve önceliklendirme

- Kontrol adayı, 171 pozitif alan adından birinin kayıtlı alan adıysa elenir (kod: `E01`).
- Bir alan adı birden fazla kontrol grubuna düşerse öncelik sırası: G1 > G2 > G3 > G4. Diğerlerinden elenir (kod: `E02`).
- **Etiket gecikmesi (label noise):** Kontrol listesi her yeni SPK bülteni indirildiğinde pozitiflerle yeniden karşılaştırılır. Sonradan SPK listesine giren kontrol, kontrolden çıkar ve **pozitife taşınır** (karar tarihi yeni bültenin tarihi). Bu kural dondurulmadan önce yazıldığı için sonuç görüldükten sonra seçmek sayılmaz. Her taşıma sayılır ve raporda yazılır (kod: `E03`).
- USOM listesi: Resmi erişim yolu ve şartları belirsiz ([DATA_SOURCES.md](../DATA_SOURCES.md) Bölüm 8, [yerel]). Erişim netleşmedikçe elemeler yalnızca SPK bültenine dayanır. Bu, "kontrol grubunda hâlâ gerçek ama listelenmemiş siteler olabilir" demektir. Yanlış alarm oranı bu yüzden **üst sınır** değil, **kesin değil** olarak yazılır.

### 1.3 Seçim nasıl yapılır (el değmeden)

- Tüm rastgele seçimler tohumlu özetle yapılır: Adayları `SHA-256(tohum + "|" + kayıtlı_alan_adı)` değerine göre sırala, her tabakadan ilk k'yı al. Python veya .NET rastgele sayı üreteci yok, böylece sonuç sürümden bağımsız tekrar edilir.
- Tohum biçimi (örnek): `kontrol-g3-2026-MM-DD`. Tohum, seçimden **önce** yazılır.
- Elle eleme yok. Her eleme bir kod taşır (`E01`...). Manifest, koda göre kaç alan adının neden elendiğini sayar. Elle ekleme ve çıkarma yasak. Bir hata bulunursa düzeltme "Değişiklik kaydı"na yazılır ve listeyi yeniden üretmek gerekir.
- Betik: `scripts/control/` altında, sürüm kontrollü. Betik hiçbir alan adını log'a veya ekrana yazmaz, yalnızca tuzlu kimlik ve sayı yazar.

### 1.4 Dondurma

Her liste için `data/private/control/manifest_<grup>.json` içinde şunlar bulunur: dondurma tarihi (UTC), betiğin git commit'i, tohum, kaynak dosyaların SHA-256'sı (kaynak anlık görüntüleri de private altında saklanır), PSL sürümü, eleme sayıları, listenin SHA-256'sı ve alan adı başına tuzlu Merkle taahhüdünün kökü (ROADMAP Hafta 6 taahhüt aracı). Repoya yalnızca manifestonun alan adı içermeyen kısmı (sayılar, kök, tarih) girer.

### 1.5 Ölçüm: aynı yer, aynı çözücü, aynı toplayıcı

- Tüm gruplar aynı konumdan (`tr-ev-ubuntu`), aynı iki çözücüden (Cloudflare, Google), aynı üç yoldan (UDP, TCP, DoH) ve aynı kayıt türleriyle (A, AAAA, NS) sorulur ([DECISIONS.md](../DECISIONS.md) 2026-11-13).
- Aynı ölçüm sıklığı: **haftalık**, pozitif ve kontrol aynı gün ve aynı partide. Gruba özel yol, çözücü veya sıklık yok.
- **Bütçe [hesap]:** Alan adı başına 18 DNS sorgusu, saniyede en çok 1 sorgu ([HAFTA4_ILK_OLCUM.md](../HAFTA4_ILK_OLCUM.md): 171 alan adı = 3.078 sorgu). Günde en çok 6 saat ayrılırsa haftada yaklaşık 150 bin sorgu, yani yaklaşık 8.400 alan adı sığar. Önerilen toplam (pozitif + kontrol) yaklaşık 2.700 alan adı, haftalık yaklaşık 49 bin sorgu, yaklaşık 14 saat. Günlük ölçüm tüm gruplar için sığmaz, bu yüzden haftalık.
- InternetDB (haftalık) ve RIPEstat yalnızca IP sorar. Kontrol grubunda da yalnızca toplu sonuç yayımlanır, sonuç `data/private/` altında kalır (Kural 2).
- Kontrol alan adlarına doğrudan istek yok: DNS dışında yalnızca arşiv ve sertifika kaynakları.

### 1.5.1 Hangi veri geriye dönük toplanabilir

| Veri | Pozitif | Kontrol | Not |
|---|---|---|---|
| Sertifika geçmişi (crt.sh) | Evet | Evet | Hata ile "kayıt yok" ayrılır, 5 sn'de 1 istek |
| Wayback CDX kayıtları | Evet | Evet | Yalnızca arşiv adresine istek |
| DNS | Yalnızca bugünden | Yalnızca bugünden | Geçmiş DNS yok. Geçmiş testinde kullanılmaz |
| ASN geçmişi (RIPEstat routing-history) | Önek için evet | Evet | Alan adı-IP eşlemesi vermez |
| Port (InternetDB) | Yalnızca bugünden | Yalnızca bugünden | Geçmiş belgelenmemiş |

**Geleceği sızdırma kuralı:** Bir alan adı için "referans tarihi" tanımlıdır (pozitifte karar tarihi, kontrolde Bölüm 5'teki sahte referans). Geçmiş testinde yalnızca referans tarihinden önce zaman damgası taşıyan veri girer. Bugünkü DNS, IP, port ve bugünkü ASN geçmiş testine girmez. Pozitiflerin karar sonrası DNS cevapları (engel, NXDOMAIN) yalnızca betimleyici analize girer ve "karardan sonra N gün" ayrımıyla yazılır. Kontroller aynı takvim günlerinde ölçüldüğü için bu fark kontrol grubuna kopyalanmaz. Yorumda "pozitif karar sonrası, kontrol karar yok" farkı yazılır.

### 1.6 Gizlilik ve yayım

- Listeler `data/private/control/` altında, git dışında.
- Panel, log, metrik ve rapor: Yalnızca grup başına sayı ve oran. Alan adı yok. Prometheus etiketlerinde grup adı olabilir (G1..G4), alan adı olamaz.
- G1 kurumları resmi ve herkese açık olsa da adları yine yayımlanmaz (tutarlılık için). Resmi liste bağlantısı yeterlidir.
- Kişi adı taşıyan alan adları (kişisel site, avukat, danışman) bulunursa kontrolden elenir (kod: `E04`, otomatik tespit yok, yalnızca üretim betiğinin bildirdiği "kişi adına benzeyen" desen sayılır). Emin değilsek **çıkarırız**, çünkü kontrol grubu büyük, bir alan adı eksilmesi zarar vermez.

---

## 2. Grup 1: Lisanslı kurumlar

**Soru:** Gerçek, düzenlenen kurumlar yanlış alarm veriyor mu? Marka benzerliği sinyalinin referans listesi buradan çıkabilir.

### 2.1 Kaynaklar

| Alt grup | Resmi kaynak | Biçim | Web adresi var mı | Durum |
|---|---|---|---|---|
| Bankalar | BDDK Kuruluş Listesi: <https://www.bddk.org.tr/Kurulus/Liste/77> | HTML liste, 66 kuruluş (mevduat 35, kalkınma ve yatırım 20, katılım 10, TMSF 1). 6 kayıtta "dijital banka" etiketi | **Evet**, her kuruluşta bağlantı | [doğrulandı] (2026-11-15) |
| Aracı kurumlar | SPK Faaliyet İzinleri: <https://spk.gov.tr/faaliyet-izinleri/araci-kurumlar> | Filtreli arama tablosu: Ünvan, Kurum Statüsü, Kurum Durumu, Belge Tarihi, Belge No, Belge Durumu | **Hayır** | [doğrulandı]: Sayfa açıldı ama satırlar alınamadı (başlıklar var, veri sonradan yükleniyor olabilir). Satır sayısı bilinmiyor |
| Aracı kurum sayısı | SPK 2025 Faaliyet Raporu: <https://spk.gov.tr/data/61e48f651b41c60d1404d687/Faaliyet%20Raporu_2025.pdf> | PDF | Hayır | [özet]: 2025 sonunda 66 aracı kurum ve 56 banka diyor. Rapor doğrudan okunmadı |
| Aracı kurum iletişim ve üye listesi | TSPB Üye Bilgileri: <https://www.tspb.org.tr/uye-bilgileri/> | "Detaylı üye listesi" Excel (.xlsx) | Sütunlar bilinmiyor | [doğrulandı]: Excel bağlantısı var, dosya açılmadı. Web adresi sütunu **doğrulanamadı** |
| Portföy yönetim şirketleri | SPK: <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri> ("Mevcut Yetki Belgeleri" ve "İletişim Bilgileri" kutuları) | İç sayfa, İletişim Bilgileri KAP şirket sayfasına gidiyor | Doğrulanamadı | [doğrulanamadı]: Liste sayfasının içeriği alınamadı |
| Kripto varlık hizmet sağlayıcıları | SPK: <https://spk.gov.tr/kurumlar/kripto-varlik-hizmet-saglayicilar/faaliyette-bulunanlar-listesi> | HTML tablo: No, Güncel Ünvan, Eski Ünvan. 48 kayıt. "Geçici Liste" başlığı | **Hayır** | [doğrulandı] (2026-11-15) |

**Kripto listesi lisans listesi değildir.** Sayfa, listede yer almanın kurumun yetkili olduğu anlamına gelmediğini, listenin 6362 sayılı Kanun geçici 11. madde kapsamında faaliyette bulunacağını beyan edenleri kamuya duyurmak için yayımlandığını söylüyor. [doğrulandı] Bu yüzden bu alt grubu "lisanslı" diye adlandırmak **yanlış** olur. Öneri: G1'e **koyma**. İstenirse ayrı alt grup "SPK kripto: faaliyette bulunanlar listesi (yetki değil)" olarak ve ana yanlış alarm oranına karışmadan raporla. Kripto için yetki verilenler ayrı bir liste olarak sayfada görünmedi (alt listeler: Başvuru Süreçleri, Faaliyette Bulunanlar, Tasfiye Sürecinde Olanlar). [doğrulandı]

Önceki aramada çıkan haberlerde de "faaliyette bulunanlar" ile "izin verilenler" ayrımı vurgulanıyor [özet], birincil metin yukarıdaki SPK sayfası.

### 2.2 Güncellik ve şartlar

- Güncelleme tarihi: BDDK listesinde ve SPK kripto listesinde liste düzeyinde tarih **yok**. BDDK sayfasında yalnızca site genelinde "Temmuz 2018" notu var (eski duyuruların bağlantıları güncel olmayabilir). [doğrulandı] Bu yüzden her anlık görüntü **indirme tarihiyle** damgalanır, kaynağın "son güncelleme" tarihi bilinmiyor diye yazılır.
- Kullanım şartı: SPK sayfalarının altlığında yalnızca "Tüm hakları saklıdır" görüldü. [doğrulandı] BDDK için siteye özel yeniden kullanım şartı **bulunamadı**. TSPB'nin bir "Kullanım Koşulları ve Gizlilik" sayfası var, içeriği alınamadı. [doğrulanamadı] Kullanımımız: Resmi listeden yalnızca kurumun alan adı çıkarılır, liste ve alan adları yayımlanmaz. Bu hukuki görüş değildir.
- Hız sınırı: Üç kaynakta da yazılı sınır **bulunamadı**. Tek seferlik indirme, sayfa başına tek istek, 5 sn aralık.
- Robots kuralları bu çalışmada kontrol edilmedi. [doğrulanamadı] Betik çalıştırılmadan önce `robots.txt` bakılır.

### 2.3 Kesin seçim kuralı

1. Girdi: BDDK banka listesinin tamamı (66, bağlantısı olanlar). SPK aracı kurum ve portföy yönetim şirketi listelerinden, **resmî bir kaynakta web adresi açıkça yazılı** olan kuruluşlar.
2. Web adresi yazılı değilse arayıp bulma, internet araması yok, tahmin yok. Alan adı resmi kaynakta yoksa kuruluş G1'e girmez (kod: `E10`, sayılır).
3. Çıkarılan adres tek bir kayıtlı alan adına indirilir. Bir kurumun birden fazla adresi varsa resmi kaynakta yazılanların hepsi alınır, aynı kuruma bağlı olarak tek küme sayılır.
4. Örnekleme yok: Tüm uygun kurumlar alınır (sayım). Beklenen boyut yaklaşık 150–250 alan adı. Kesin sayı bu çalışmada bilinmiyor, çünkü aracı kurum ve portföy listelerinin web adresi alanı doğrulanamadı.
5. Zaman eşleşmesi **yok**: Bu kurumlar eski, sertifika yaşları yüksek. Karşılaştırma zamanla değil, "köklü ve düzenlenen" özellikle yapılır. Raporda "yaşı eşleştirilmemiş kontrol" yazar.

### 2.4 Uyarılar

- **Kendi kendine eşleşme:** Marka benzerliği sinyalinin sözlüğü G1'den kuruluyorsa, G1'in kendi alan adları sözlükle birebir eşleşir. Bu yanlış alarm değildir. Kural: G1 üyelerinin **kendi** alan adları marka sinyalinin yanlış alarm sayımından çıkarılır. Sinyalin sınavı, G1'e **benzeyen ama G1'de olmayan** alan adları üzerindedir (pozitifler, G3, G4).
- G1 kolay negatiftir (eski, bilinen, çoğu CDN arkasında). G1'de düşük yanlış alarm, sinyalin iyi olduğu anlamına gelmez. Bu yüzden G1 sonucu hiçbir genel yanlış alarm oranına eklenmez.

### 2.5 İş yükü

G1 küçük, tek seferlik ve düşük riskli. Zaman kalırsa iddia modülü (ROADMAP Bölüm 6) için de resmi referans olur.

---

## 3. Grup 2: Normal finans haber ve eğitim siteleri

**Soru:** Finans konuşan ama yatırım sunmayan meşru sitelerde yanlış alarm var mı?

**Sorun:** "Finans haber sitesi" listesini elle yazarsak seçim bize bağlı olur. Kural: **Dışarıdan sabitlenmiş bir listeyi, önceden yazılmış bir kategori kuralıyla süz.**

### 3.1 Seçenekler

| Seçenek | Kural | Artı | Eksi | Durum |
|---|---|---|---|---|
| **A (öneri, koşullu)** | Tranco listesinin sabitlenmiş bir sürümünde ilk 100.000 sıradan, Cloudflare Radar alan adı kategorisi önceden yazılmış kümeye (haber, ekonomi ve finans, eğitim) uyanlar | Dışarıdan, tekrar edilebilir, sıra sabit | Kategori sınıflaması bir satıcıya ait ve etiket adlarını doğrulayamadım. API anahtarı gerekiyor. Cloudflare Radar verisi CC BY-NC 4.0 | Doğrulanamadı (aşağıda) |
| B | Wikidata sorgusu (haber sitesi, gazete ve finans basını örnekleri, resmi web sitesi alanı) tarih sabitli | Açık veri, kategori insan tanımlı | Kapsam Türkiye'ye ve büyük yayınlara kayar, boyutu bilinmiyor | Doğrulanmadı, bu çalışmada denenmedi |
| C | Elle seçilmiş liste | Hızlı | Bize bağlı, tekrar edilemez | **Önerilmez** |

**Seçenek A'nın doğrulanan kısımları:**
- Cloudflare Radar'ın `ranking/domain/{domain}` ucu bir alan adı için kategori listesi (`id`, `name`, `superCategoryId`) döndürüyor. [özet] (<https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get>, arama sonucu).
- "Ekonomi", "Haber" gibi **kategori adlarının tam yazılışı doğrulanamadı** (iki belge örneği farklı kimlikler gösteriyor). Seçenek A'yı uygulamadan önce kategori taksonomisi bir kez elle okunmalı ve yazılacak küme bu belgeye **seçimden önce** eklenmeli. [doğrulanamadı]
- Radar API'sinin hız sınırı ve şartı okunmadı. [doğrulanamadı]

**Öneri:** Önce A'yı küçük bir deneme ile dene (Bölüm 3.3). Çalışmazsa B. C kullanılmaz.

### 3.2 Seçim kuralı (A için)

1. Tranco listesi tek bir tarihe sabitlenir (Bölüm 5.4). Liste kimliği ve indirme tarihi manifestoya yazılır.
2. İlk 100.000 sıra alınır. Bu sınır bir seçim, doğru sınır değildir. Gerekçesi: Daha aşağısı kategori kapsamı ve küçük siteler için güvenilmez, daha yukarısı çok az örnek verir.
3. Her alan adı için Radar kategorisi sorulur, hız saniyede en çok 1. Kategori yazılan kümedeyse aday olur. Kategori yok ya da hata: Aday değil, sayılır (kod: `E20`). Hata "kategori yok" sayılmaz, ayrı sayılır.
4. Bölüm 1.1 ve 1.2 elemeleri uygulanır.
5. Hedef boyut: **300**. 300'den fazla aday varsa tohumlu özetle ilk 300. Daha az varsa hepsi. 100'ün altındaysa grup raporda "yeterli örnek yok" diye işaretlenir ve oran verilmez.
6. Zaman eşleşmesi yok. Bu kontrol "köklü ve popüler" siteleri temsil eder, yeni ve kısa ömürlü olanları değil. G3 bunun yerini tutmaz.

### 3.3 Önkoşul denemesi (seçimden önce, sonuç yayımlanmaz)

Aynı Tranco listesinin ilk 1.000 sırasından 100 alan adı için kategori sorgula, kaçında kategori döndüğünü say. Dönmesi %80'in altındaysa A bırakılır, B'ye geçilir. Eşik %80 elle seçildi, gerekçesi: Kategorisi olmayan çoğunluk filtreyi anlamsız yapar.

### 3.4 Uyarı

Çok yüksek sıralı siteler hem kolay negatiftir hem de başka CDN ve DNS özellikleri taşır. G2'nin yanlış alarm oranı, "finans konuşan normal site yanlış alarm vermiyor" iddiasının **alt sınırı**dır, tamamı değil.

---

## 4. Grup 3: Aynı dönemde ilk sertifikasını almış, finans kelimesi içeren normal alan adları (zor negatifler)

**Soru:** Sinyal, yalnızca "yeni ve finans kelimeli" olmanın ötesinde bir şey söylüyor mu? (ROADMAP Bölüm 8, soru 3).

Bu grup en önemli ve en tehlikelisi. Aşağıda dört ayrı sorun ve çözümü var.

### 4.1 Mevcut akış ve sınırları [yerel]

- `ct_candidates` tablosu yalnızca şu 8 terimden biri DNS adında alt dize olarak geçen sertifikaları saklıyor: `yatirim`, `borsa`, `kripto`, `forex`, `trade`, `invest`, `capital`, `menkul` (`KeywordCandidateFilter`, `appsettings.json`).
- Akış başlangıcı: 12 Kasım 2026 (ilk toplayıcı log dosyaları ve `AddStaticCtCandidates` göçü). Yapılandırmada yalnızca Let's Encrypt Sycamore logunun 2026h2 ve 2027h1 dilimleri var. Başka CA'ların sertifikaları ve Sycamore'a hiç gitmeyenler burada **görünmez**. Kapsama oranı bilinmiyor (bkz. 4.6).
- Aday sayısı alt dize eşleşmesidir: Aynı alan adının yenilemeleri, ön sertifika ve nihai sertifika çiftleri ve alt alan adları ayrı satır olabilir. Kayıtlı alan adına indirgenmeli.

### 4.2 Etiket gürültüsü: Gelecekte engellenecek siteler

Zor negatifler, SPK'nın henüz bakmadığı alan adlarını içerebilir. Pozitifler için ilk sertifika ile karar arası ortanca 148 gün, üst çeyrek 380 gün ([ASAMA0_SONUC.md](../phase0/ASAMA0_SONUC.md), [yerel]). Yani son 6 ayda açılmış bir alan adının etiketi **henüz olgunlaşmadı**.

Kurallar:
1. Bölüm 1.2'deki gibi, sonradan SPK listesine girenler pozitife taşınır ve sayılır.
2. **Olgunlaşma notu:** Her kontrolün "gözlem yaşı" (SPK listesinde olmadığının kontrol edildiği son bülten tarihi ile ilk görülme arasındaki gün) kaydedilir. Raporda yanlış alarm oranı gözlem yaşı < 180 gün ve ≥ 180 gün olarak ayrı verilir. 180 günlük sınır pozitiflerin ortancasına yakın seçildi (148 gün).
3. Yanlış alarm oranı hiçbir zaman "yanlış" diye yazılmaz. "SPK listesinde olmayan" yazılır. Alarm veren ama listede olmayan bir alan adı gerçekten meşru olmayabilir, ölçülemez.

### 4.3 Döngüsellik: Baseline ile aynı kelime listesi (kritik)

**Sorun.** Baseline sinyali "alan adı 'yatırım, borsa, kripto, forex, trade' gibi kelime içeriyor mu?" (ROADMAP Hafta 7). Akış bu sekiz terimle süzüyor. Dolayısıyla akıştan seçilen **her** zor negatif baseline'ı tetikler: Baseline'ın bu gruptaki yanlış alarm oranı tanım gereği %100. Aynı şekilde akıştaki her pozitif baseline'ı tetikler: Recall tanım gereği %100. İkisi de ölçüm değil, filtrenin yansımasıdır. Bu çerçevede baseline ile başka sinyali kıyaslayamayız.

**Çözümler (üçü birlikte):**

1. **Çerçeve etiketi.** Her sonuç "örnekleme çerçevesi" ile yazılır:
   - **Çerçeve K (terim koşullu):** Akıştaki 8 terimli adaylar. Yalnızca "terim içeren alan adları arasında sinyal ayırt edebiliyor mu?" sorusunu cevaplar. Baseline bu çerçevede değerlendirilmez, rapora "tanım gereği" notuyla yazılır.
   - **Çerçeve N (tarafsız):** Terime bakmadan, kayıtlı alan adının özetiyle seçilmiş yeni alan adları (4.5). Baseline'ın gerçek yanlış alarm ve günlük alarm hacmi buradan çıkar.
   - Pozitiflerin baseline recall'u akıştan değil **doğrudan pozitif listesinden** hesaplanır: Kaç pozitifin adında terim var.
2. **Daha geniş kelime kümesi sonradan, tarafsız örneklemde.** G3 için kullanılacak finans sözlüğü, baseline sözlüğünden **ayrı ve daha geniş** olarak bu belge dondurulmadan önce yazılır (yazılacak liste Kadircan'ın kararıdır, bkz. Bölüm 10). G3, Çerçeve N içinden bu sözlükle süzülerek oluşur. Böylece G3'ün iki alt kümesi vardır: "baseline terimi içerenler" ve "yalnızca geniş sözlükte olanlar". Sinyal ikisinde ayrı raporlanır. İkinci alt küme baseline'a karşı adil sınavdır.
3. **Sözlük dondurulduktan sonra eklenmez.** Sonuç görüldükten sonra sözlük genişletilirse (veya baseline sözlüğü değişirse) kilitli test geçersiz sayılır.

### 4.4 Zaman eşleşmesi: İlk sertifika ayı

**Hedef:** Her pozitif için aynı ayda ilk sertifikasını almış kontroller.

**Gerçek:** Pozitiflerin ilk sertifikaları çoğunlukla 12 Kasım 2026'dan **önce** ([yerel], ortanca 148 gün). Canlı akış geçmiş pozitiflerin ilk sertifika aylarını kapsayamaz. Bu yüzden iki aşamalı:

- **Aşama G3a (geçmiş, crt.sh):** Pozitiflerin ilk sertifika aylarında (yalnızca "yeni" olanlar, ilk sertifikası karardan ≤ 365 gün önce, 127 alan adı) ve geniş sözlükle crt.sh'ten tarihe göre aday toplamak. Bu **doğrulanmadı**: crt.sh'in joker karakterli kimlik aramasının (`%terim%`) büyük sonuçlarda zaman aşımına uğradığına dair raporlar var [özet], doğrudan veritabanı erişimi (`guest` kullanıcısı, port 5432) 2017'de duyurulmuş ve 2019'da kesintiler bildirilmiş, bugünkü durumu **doğrulanamadı** [doğrulandı: yalnızca duyuru sayfası, <https://groups.google.com/g/crtsh/c/sUmV0mBz8bQ>]. Web arayüzü için bilinen sınır 2020'den kalma "IP başına dakikada 60 istek" ([DATA_SOURCES.md](../DATA_SOURCES.md) Bölüm 2). Önkoşul denemesi (aşağıda) geçmezse G3a **yoktur**.
- **Aşama G3b (canlı):** Akıştan 12 Kasım 2026'dan itibaren. Bu gelecek pozitiflerle zaman olarak eşleşir, geçmişlerle değil.

**G3a önkoşul denemesi (seçimden önce, sonuç yayımlanmaz):** Tek ay, tek terim, tek istek için 3 deneme. Başarı ölçütü: Her iki denemede 120 sn içinde tam yanıt ve en az 50 kayıtlı alan adı. Aksi halde G3a bırakılır. Bu denemede hata "sonuç yok" sayılmaz. Saat başının ilk 10 dakikasında deneme yapılmaz (topluluk gözlemi, [özet]).

**G3a yoksa:** Geçmiş veri testinde (Bölüm 7) yanlış alarm yalnızca G1, G2, G4 ile ölçülür ve zor negatif yanlış alarm oranı yalnızca canlı pencereden (Bölüm 7.3 ve 7.4) gelir. Bunu raporda açık yaz: "Geçmiş testte zaman eşleşmeli zor negatif yok."

**Ay içi eşleşme:** Eşleşme "aynı takvim ayı" düzeyindedir (gün düzeyi değil). Gerekçe: Gün düzeyi, havuzu fazla küçültür. Ek tabakalama değişkeni: **yaş sınıfı** (ilk sertifika yaşı ≤ 28 gün, 29–365 gün, > 365 gün). Pozitiflerdeki dağılım 40 / 87 / 44 alan adı, yani %23 / %51 / %26 ([ASAMA0_SONUC.md](../phase0/ASAMA0_SONUC.md), [yerel]). Hedef kota aynı oranda.
- **Ana değerlendirme sadece "yeni" alan adlarında** (≤ 365 gün: 127 pozitif). > 365 gün olan 44 pozitifte ilk sertifika sitenin açılış tarihi sayılamaz (önceki sahip olabilir), bu yüzden betimleyici analiz.

### 4.5 Tarafsız örneklem akışı (öneri, altyapı isteği)

Çerçeve N'i oluşturmak için toplayıcıya **yeni bir saklama kuralı** eklenmesi gerekir. Şu an yalnızca 8 terimli kayıtlar saklanıyor, tarafsız örnek saklanmıyor ve geriye dönük kurulamaz (ham tile diske yazılmıyor).

- Kural: Her sertifikadaki her kayıtlı alan adı için `SHA-256(tuz + kayıtlı_alan_adı) mod 100 = 0` ise (yaklaşık %1) alan adının **tüm** sertifikaları saklanır. Örnekleme **alan adı düzeyinde**, kayıt düzeyinde değil: Böylece "kaç sertifika" ve "yenileme düzeni" gibi sinyaller kontrolde de hesaplanabilir.
- Terim süzgeci yok. Tuz private'ta, taahhütle bağlanır.
- Oran bir öneridir. Gerçek günlük yeni kayıtlı alan adı sayısı ölçülmeden doğru oran belli değil (bkz. Bölüm 8.1). Ölçüt: Günde en az 200 örneklenmiş yeni alan adı, disk 100 MB/gün altında.
- Alan adı saklama kuralı aday tablosuyla aynı (kişi verisi yok, issuer temizlenmiş, Kural 5).
- Bu iş altyapı kodudur ve `Fraud.Collectors`/`Fraud.Infrastructure` alanındadır. Bu belge yalnızca ister.
- Dondurmadan önce **en az 3 hafta** veri birikmesi gerekir. Bu yüzden isteğin hemen yapılması önemlidir.

Ek seçim kuralı (G3 hedef 1.000):

1. Çerçeve N'den, **ilk görülme haftasına** göre tabakalama. İlk görülme = tarafsız örneklem akışında ilk kayıt. "Yeni" kararı için her aday bir kez crt.sh'e sorulur (ilk sertifika tarihi, hata ≠ yok).
2. Yeni (ilk sertifika ≤ 28 gün önce) ve geniş finans sözlüğünden en az biri adında geçenler aday.
3. Haftalık kota: Eşit, haftada en çok 60 alan adı. Tohumlu özetle ilk 60.
4. Hedef 1.000 alan adı (aşağıda gerekçe). 1.000'e ulaşmayan haftalar boş kalır, doldurulmaz.
5. Çerçeve K için ayrıca: Mevcut 8 terimli `ct_candidates` aynı kuralla (haftalık en çok 60) örneklenir.

### 4.6 Kapsama oranı

Canlı akış yalnızca yapılandırılmış Let's Encrypt logundan beslenir. Pozitiflerin ilk sertifika yayımcıları veritabanında var ([yerel], `certificates` tablosu). **Önce şunu hesapla:** Pozitiflerin kaçında en az bir Let's Encrypt sertifikası var? Bu oran, ileriye dönük testin hangi pozitif türünü görebileceğinin üst sınırıdır. Düşükse akış testi tek başına yetmez. Bu hesap henüz yapılmadı, bu belgede rakamı yok.

### 4.7 Boyut ve gerekçe

- **Hedef 1.000 alan adı** (pozitif başına yaklaşık 6; kilitli ve ayar için ayrı).
- Gerekçe (Clopper-Pearson, %95, iki yanlı): 1.000 kontrol ve **0 yanlış alarm** bile gerçek oranın %0,37'ye kadar olabileceğini söyler [hesap]. 3'ün kuralı yaklaşık %0,3. Yanlış alarm %1 civarında beklenirse, 1.000 örnekte %95 aralığı yaklaşık 0,5–1,8. Yani **tek bir yüzde** vermek yerine aralık verilir. Daha az örnek bu oranın tahminini işe yaramaz hâle getirir.
- Üst sınır: Alan adı başına DNS bütçesi (Bölüm 1.5). Toplam 2.700 alan adı haftalık ölçüme sığar.
- Çerçeve K ve N ayrı tabakalarda olduğu için her birinde en az 300 alan adı hedeflenir, çerçeve başına oran ayrı yayımlanır.

---

## 5. Grup 4: Rastgele normal siteler

**Soru:** Tamamen rastgele bir alan adı, sinyali ne kadar sık tetikliyor? Bu, sinyalin genel yanlış alarm ve günlük alarm hacmi için alt sınırdır.

İki alt grup, ikisi de ayrı raporlanır. Birleştirilmez.

### 5.1 G4a: Tranco rastgele (köklü siteler)

- **Kaynak:** Tranco, araştırma için yapılmış, manipülasyona dayanıklı üst site sıralaması. Atıf: Le Pochat, Van Goethem, Tajalizadehkhoob, Korczyński, Joosen, "Tranco: A Research-Oriented Top Sites Ranking Hardened Against Manipulation", NDSS 2019, DOI 10.14722/ndss.2019.23386. [özet] (arama özetinden, <https://lepoch.at/publication/tranco>)
- **Sabitleme:** Tranco ana sayfası, kalıcı liste kimliği ve günlük liste için kalıcı adres sunuyor. Kimlik `/top-1m-id` adresinden alınır, tarihe göre sorgu `GET https://tranco-list.eu/api/lists/date/{YYYYmmdd}` ile yapılır, kimliğe göre `GET .../api/lists/id/{id}`. [doğrulandı] (<https://tranco-list.eu/>, <https://tranco-list.eu/api_documentation>, 2026-11-15). Liste günlüktür, her gün 00:00 UTC'ye kadar güncellenir. [doğrulandı]
- **Şartlar:** Tranco sayfalarında **lisans metni bulunamadı**. Yalnızca kaynak sağlayıcılar için atıf notları var (Majestic CC BY 3.0, CrUX CC BY-SA 4.0, Cloudflare Radar CC BY-NC 4.0). Atıf zorunluluğu sayfada açıkça yazılı değil, ama makale kalıcı alıntı olarak sunuluyor. [doğrulandı] Önerim: Ticari olmayan araştırma, makale alıntısı ve liste kimliği raporda yazılır. Hız sınırı: Yalnızca `ranks/domain` ucunda saniyede 1 sorgu yazıyor. [doğrulandı] Liste indirme için ayrı sayı **bulunamadı**. Biz tek indirme yaparız.
- **Seçim:** Sabitlenmiş listenin tamamından (1.000.000), Bölüm 1.1 ve 1.2 elemelerinden sonra tohumlu özetle ilk **300**.
- **Sınır:** Tranco bir popülerlik listesidir. Seçilenler köklü, çoğu CDN arkasında, yaşları yüksek. Yeni ve kısa ömürlü siteleri temsil **etmez**. Çoğu .com ve çoğu yabancı. Türkiye'ye özgü olmadığı raporda yazılır. Tranco, kaynak olarak CrUX ve Radar gibi sağlayıcılar kullanıyor. Aynı Radar'dan G2 için de kategori alıyorsak G2 ve G4a ilişkili olabilir. Bu yüzden G2 ile G4a çakışan alan adları G2'de kalır (öncelik sırası).

### 5.2 G4b: CT akışından tarafsız örnek (ana grup)

- **Kaynak:** Bölüm 4.5'teki tarafsız örneklem akışı (Çerçeve N). Terime bakılmadan seçilen yeni kayıtlı alan adları.
- **Seçim:** İlk görülme haftasına göre tabakalı, haftalık en çok 60, hedef **700**.
- **Zaman eşleşmesi:** İlk görülme haftası. Yaş sınıfı (≤ 28, 29–365, > 365 gün) pozitiflerle aynı kota (%23 / %51 / %26). Yaş sınıfı için crt.sh'e bir kez sorulur.
- **Faydası:** Gerçek yeni alan adı karışımı, baseline için gerçek yanlış alarm ve günlük alarm hacmi (Bölüm 8).
- **Sınır:** Yalnızca akışın görebildiği CA ve loglar (4.1). Tarafsız örnek başlamadan önceki geçmiş zaman yok.

### 5.3 G4a ile G4b arasındaki fark

| | G4a (Tranco) | G4b (CT tarafsız) |
|---|---|---|
| Alan adı yaşı | Köklü | Yeni ve eski karışık |
| Zaman eşleşmesi | Yok | İlk görülme haftası |
| Geçmişe dönük | Evet (tarihli liste) | Hayır (12 Kasım'dan sonra, tarafsız akış kurulduktan sonra) |
| Taban oran için | Uygun değil | Uygun |

### 5.4 Sabitleme özeti

Tranco: Tarih ve liste kimliği. CT örneklemi: Tuz özeti, ilk ve son görülme tarihi, saklama kuralının commit'i. Aynı manifestoya.

---

## 6. Ölçüm tablosu: hangi grup, ne için

| Grup | Boyut hedefi | Zaman eşleşmesi | Amaç | Ana risk |
|---|---|---|---|---|
| G1 Lisanslı kurumlar | 150–250 (sayım, tahmin) | Yok | Marka benzerliği, kolay negatif | Alan adı kaynakta yok. Kripto listesi lisans değil |
| G2 Finans haber ve eğitim | 300 (koşullu) | Yok | Konu benzer ama meşru | Kategori kaynağı doğrulanamadı. Popüler yanlılığı |
| G3 Zor negatifler | 1.000 | Ay (G3a, koşullu) ve hafta (G3b) | Ana yanlış alarm ölçüsü | Döngüsellik, etiket gürültüsü, geçmiş eşleşme yok |
| G4 Rastgele | 300 (G4a) + 700 (G4b) | G4b için hafta | Taban yanlış alarm, hacim | G4a köklü sitelere kayık |
| **Toplam** | **yaklaşık 2.450–2.550** | | | Haftalık DNS yaklaşık 12–13 saat |

171 pozitifle birlikte yaklaşık 2.650–2.750 alan adı.

---

## 7. Bölme (train / test) kuralları

Birim: Alan adı. Her alan adı **en erken** karar tarihli bültenine göre atanır (Aşama 0'daki kuralla aynı). Bülten düzeyinde: Aynı bültenin bütün alan adları aynı tarafta.

### 7.1 Bülten ve tarih tablosu [yerel]

Kaynak: `data/private/phase0/engellenenler.csv` (yalnızca bülten numarası, tarih ve sayı), [ASAMA0_SONUC.md](../phase0/ASAMA0_SONUC.md). Alan adı sayısı en erken bülten esasına göre.

| Bülten | Tarih | Alan adı | İlk sertifikası ≤ 365 gün | Kümülatif | Bölme |
|---|---|---|---|---|---|
| 2026/1 | 2026-01-08 | 24 | 16 | 24 | Ayar |
| 2026/3 | 2026-01-15 | 9 | 6 | 33 | Ayar |
| 2026/4 | 2026-01-23 | 8 | 8 | 41 | Ayar |
| 2026/6 | 2026-02-05 | 9 | 8 | 50 | Ayar |
| 2026/8 | 2026-02-12 | 14 | 10 | 64 | Ayar |
| 2026/9 | 2026-02-18 | 14 | 9 | 78 | Ayar |
| 2026/10 | 2026-02-25 | 6 | 4 | 84 | Ayar |
| 2026/12 | 2026-03-04 | 31 | 21 | 115 | Ayar |
| 2026/14 | 2026-03-10 | 2 | 2 | 117 | Ayar |
| 2026/16 | 2026-03-17 | 1 | 1 | 118 | Ayar |
| 2026/18 | 2026-03-26 | 5 | 5 | 123 | Ayar |
| 2026/20 | 2026-04-02 | 2 | 0 | 125 | Geçmiş değerlendirme |
| 2026/28 | 2026-04-29 | 6 | 6 | 131 | Geçmiş değerlendirme |
| 2026/31 | 2026-05-15 | 5 | 4 | 136 | Geçmiş değerlendirme |
| 2026/36 | 2026-06-10 | 6 | 4 | 142 | Geçmiş değerlendirme |
| 2026/41 | 2026-06-24 | 1 | 1 | 143 | Geçmiş değerlendirme |
| 2026/45 | 2026-07-16 | 7 | 6 | 150 | Geçmiş değerlendirme |
| 2026/49 | 2026-08-05 | 15 | 10 | 165 | Geçmiş değerlendirme |
| 2026/59 | 2026-09-16 | 2 | 2 | 167 | Geçmiş değerlendirme |
| 2026/67 | 2026-09-30 | 3 | 3 | 170 | Geçmiş değerlendirme |
| 2026/77 | 2026-10-27 | 1 | 1 | 171 | Geçmiş değerlendirme |

Toplam 21 bülten, 171 alan adı (ASAMA0_SONUC ile aynı). 2026'daki 78 bültenden yalnızca 21'inde pozitif var.

### 7.2 Önerilen bölme

| Bölme | Bültenler | Alan adı | Yeni (≤ 365 gün) | Kural |
|---|---|---|---|---|
| **Ayar** | 2026/1 – 2026/18 (8 Ocak – 26 Mart) | 123 (%72) | 90 | Serbest kullanım: Eşik, sinyal seçimi, hata analizi |
| **Geçmiş veri testi** | 2026/20 – 2026/77 (2 Nisan – 27 Ekim) | 48 (%28) | 37 | **Bir kez** çalışır, ayrı raporlanır. Sonuca göre kural değişmez |
| **Kilitli test** | Dondurma tarihi F'den **sonra** yayımlanan bültenler | Henüz yok | Henüz yok | Bir kez çalışır |
| İleriye dönük | Taahhüdün tarihinden sonra eklenen yeni alan adları | | | Taahhüt gerekir (Hafta 9) |

Kesim noktası seçimi: Kronolojik. 26 Mart ile 2 Nisan arası bir hafta tampon (bitişik bültenlerin aynı operatör işi olma ihtimaline karşı). Alternatif: Kesim 2026/12'de (4 Mart), ayar 115, değerlendirme 56. Kadircan seçer (Bölüm 10). Hangisi seçilirse seçilsin **sonuç görülmeden** yazılır.

**Dürüst uyarılar:**
- Aşama 0 analizi ([ASAMA0_SONUC.md](../phase0/ASAMA0_SONUC.md)) 171 alan adının hepsine baktı: Süre dağılımı, arşiv oranı, ilk sertifika. Bu bir betimleyici analizdi ve sinyal ayarı değildi, ama "geçmiş veri testi" **temiz değildir**. Raporda yazılır (ROADMAP Hafta 6 da böyle diyor).
- Değerlendirmede 48 alan adı var, yeni olanlar 37. 48 üzerinden bir recall oranının %95 aralığı kabaca ±14 puan (örneğin 24/48 için yaklaşık %35–65, Clopper-Pearson [hesap]). Küçük fark bu aralık içinde kalır.
- Ayar tarafı öne yüklü: 2026/12 tek başına 31 alan adı (ayarın %25'i). Tek bir operatörün toplu işi olabilir. Küme kuralı (7.5) bunu hesaba katar.

### 7.3 Kontrol grubu üyelerinin bölünmesi (ilk sertifika zamanına göre)

Pozitifler karar tarihine göre bölünür, kontrollerin karar tarihi yok. Tutarlı kural:

1. Referans zamanı kontrol için **ilk sertifika ayı**dır.
2. Her ay M için, o ayda ilk sertifikası olan pozitiflerin ayar ve değerlendirme oranı (n_ayar(M) : n_değ(M)) hesaplanır. M ayındaki kontroller **küme düzeyinde** (7.5) bu oranda iki bölmeye ayrılır. Atama tohumlu özetle yapılır.
3. Pozitifi olmayan aylardaki kontroller **eşleşmesiz** sayılır: Yalnızca yanlış alarm ayarı için ("ayar, yalnız yanlış alarm"), değerlendirme sayısına girmez.
4. Canlı akıştan gelen G3b ve G4b: İlk görülme 12 Kasım'dan dondurma tarihi F'ye kadar olanlar **ayar (yalnız yanlış alarm)**. F'den sonra ilk görülenler **kilitli test**. Bunlar için eşleşen pozitifler henüz bilinmiyor, SPK sonradan listeleyebilir. Bu nedenle kilitli test kontrolleri ve kilitli test pozitifleri aynı zaman penceresine bağlıdır.
5. G1, G2, G4a: Zaman eşleşmesi yok. Tohumlu özetle küme düzeyinde %70 ayar ve %30 değerlendirme, kilitli teste girmez. (Oranlar pozitiflerdeki 72/28'e yakın seçildi.)

### 7.4 Kilitli test penceresi

- **Dondurma tarihi F:** Hafta 8 sonunda, kurallar ve eşikler taahhütle yayımlandığında. Öneri: Bugünden en erken 3 hafta sonra (yaklaşık 6 Aralık 2026), Hafta 8 gerçek tarihinde netleşir. F'den sonra yayımlanan bültenler kilitli testtedir.
- **Pencere:** F'den F+28 gün. Gerekçe: ROADMAP "yaklaşık 4 hafta" diyor.
- **Beklenen pozitif sayısı [hesap]:** 2026'da 171 alan adı / 292 gün (8 Ocak – 27 Ekim) = günde 0,59. Ama dağılım öne yüklü: 5 Ağustos sonrası (2026/59, 67, 77) 6 alan adı / yaklaşık 93 gün (5 Ağustos – 6 Kasım) = günde 0,065. 28 günde **ortalama hızla 16, son dönem hızıyla yaklaşık 2** alan adı. Gerçek sayı bu aralıkta ya da dışında olabilir.
- **Bildirim kuralı:** 10'dan az pozitif varsa **yüzde verilmez, ham sayı verilir** (ROADMAP Hafta 6). Örnek: 5 pozitifte 5'i yakalandı için %95 Clopper-Pearson alt sınırı yaklaşık %48. 10 kontrol alarmsız için üst sınır yaklaşık %31 [hesap]. Yani bu testle "yöntem çalışıyor" denemez, yalnızca "bir çelişki çıkmadı" denir. Sonuç ne olursa olsun raporda yazılır.
- Kilitli testin pozitif etiketleri F+28 gün sonunda dondurulur. Sonradan gelen bülten, kilitli teste girmez (ayrı not).

### 7.5 Küme düzeyinde bölme

Aynı operatör veya altyapıdaki alan adları ayar ve test arasında bölünmez, yoksa bilgi sızar.

**Küme tanımı (alan adları arasındaki kenarlar):**
1. Aynı sertifikanın SAN listesinde birlikte bulunma.
2. Aynı IPv4 /24 (IPv6 /48) içinde **CDN veya bulut dışı** IP paylaşma. CDN ve bulut önekleri (RIPEstat toplayıcısının işaretlediği ağlar) kenar üretmez. [yerel] İlk ölçümde IP'lerin %93'ü bulut veya CDN'de ([HAFTA4_ILK_OLCUM.md](../HAFTA4_ILK_OLCUM.md)), yani bu kural sadece IP'lerin küçük bir bölümünde çalışacak. Bu yüzden küme sayısı büyük çıkacak, bu bir sınır.
3. Aynı DNS yetkili sunucu (NS) ailesi: **yalnızca** CDN dışı ve ortak barındırma olmayan NS'ler. Bu kural zayıf, kullanılacaksa Hafta 8'de kararlaştırılır. Şimdilik **kullanılmaz**.
4. Ad benzerliği: Etiketten rakamlar ve TLD atıldıktan sonra aynı gövde (örnek: aynı sözcük ve farklı sayı). Bu kural deterministik ve basit, benzerlik puanı değil.

Kümeler **bölmeden önce bir kez** hesaplanır ve kimlikleri dondurulur. Küme hesabı bugünkü DNS ve IP verisini kullanabilir (yalnızca bölme için, sinyale girmez).

**Çakışma kuralı:** Bir küme birden fazla bölmeye düşerse bütünü **en erken** bölmeye gider (ayar). Taşınan alan adı sayısı rapora yazılır. Değerlendirme havuzu bu yüzden küçülebilir. Küçülürse yeniden bölünmez.

**Pozitifle bağlı kontroller:** Bir kontrol, bir pozitifle aynı kümedeyse ana yanlış alarmdan çıkarılır, ayrı "pozitifle ilişkili" tabakada ham sayı olarak yazılır (kod: `E05`). Aynı altyapıyı kullanan başka bir alan adı gerçekten meşru olmayabilir, ama SPK listesinde değil. Bu kural kontrolleri "kolaylaştırır", yani yanlış alarmı biraz düşük gösterebilir. Bu yüzden sayısı yazılır.

**Güven aralığı:** Bootstrap birim olarak önce **küme**, ikinci olarak **bülten** kullanır. İkisi de yazılır. 21 bültenin çoğunda 1–9 alan adı var, iki bülten (2026/1 ve 2026/12) toplamın %32'si. Bülten bazlı aralık geniş çıkacak.

---

## 8. Gerçekçi taban oran ve günlük alarm

**Amaç:** Precision'ı kontrol grubundaki yapay oran yerine gerçekçi yaygınlıkla (prevalence) hesaplamak ve günlük alarm sayısını söylemek.

### 8.1 Mevcut rakamlar birbirini tutmuyor

| Kaynak | Rakam | Durum |
|---|---|---|
| [STATIC_CT_HACIM.md](../phase0/STATIC_CT_HACIM.md) | Günde yaklaşık 10,3 milyon kayıt, süzgeçten geçen %0,08 (2/2.560, aralık %0,02–0,28). Yaklaşık 8 bin sertifika/gün (2 bin – 29 bin) | [yerel], hesap |
| Çağrı notu | Daha önce 4 saatte 4 bin – 8 bin aday. Bu, günde yaklaşık 24 bin – 48 bin eder | **Doğrulanamadı**, bu çalışmada bir belgesi bulunamadı |
| [deploy/linux/README.md](../../deploy/linux/README.md) | "2027h1'de saatte onlarca aday". Günde yaklaşık yüzlerce | [yerel] |

İlk iki rakam yaklaşık 3–6 kat, üçüncü rakam ise iki kat farklı. Üstelik hepsi **kayıt** sayısı, kayıtlı alan adı sayısı değil. Birini seçmem doğru olmaz. Bu belge hiçbirini kullanmaz, yalnızca yöntemi verir. Ölçüm `ct_candidates` ve tarafsız örneklem tablosundan yapılır.

### 8.2 Yöntem

Tanımlar (hepsi **kayıtlı alan adı** ve **ilk görülme gününe** göre):

- `N_kw(gün)`: O gün ilk kez görülen ve 8 terimden birini içeren, yeni (ilk sertifika ≤ 28 gün) kayıtlı alan adı sayısı. `ct_candidates`'tan, kayıtlı alan adına indirgenip tekrarsız sayılarak.
- `N_tüm(gün)`: O gün ilk kez görülen tüm yeni kayıtlı alan adları. Tarafsız örneklemden: `sayı / örnekleme oranı`.
- `f_kw = N_kw / N_tüm`: Baseline'ın tüm yeni alan adları içindeki alarm payı. (Tarafsız örneklemde sözlükle doğrudan sayılarak da kontrol edilir.)
- `A_poz(gün)`: O gün ilk sertifikası alınan ve sonra SPK'ya giren "yeni" alan adı. Bu **sonradan** bilinir (gecikme ortanca 148 gün, üst çeyrek 380 gün). Bu yüzden son 6–12 ay için `A_poz` **eksik sayılır** (sağdan kırpılma). Güvenilir tahmin yalnızca en az 380+ gün önceki aylar için yapılabilir. Elimizdeki pozitiflerin ilk sertifika aylarına göre dağılımı bu amaç için kullanılır.
- Yaygınlık: `π = A_poz × p_kw / N_kw`, burada `p_kw` pozitiflerin adında terim bulunma oranı (pozitif listesinden doğrudan sayılır).

Alarm hacmi: `Günlük_alarm = YA × N_tüm`, `YA` seçilen sinyalin yanlış alarm oranı (G3b ve G4b'den, Çerçeve N'de).

Precision (PPV): `PPV = r·π / (r·π + YA·(1−π))`, r = recall.

### 8.3 Kaba büyüklükler [hesap]

- SPK tarafı: 171 / 292 gün = günde 0,59; yeni olanlar 127 / 292 = günde 0,43. Son dönemde (Ağustos sonrası) çok daha düşük (günde yaklaşık 0,07). Bu **SPK'nın listelediği** sayı. Listelenmeyen gerçek yatırım dolandırıcılığı bundan fazladır, ölçülemez.
- Aday tarafı: `N_kw` bilinmiyor (8.1). Gösterim için şu **varsayım** örnekleri verilir ([varsayım], ölçüm değil): Yeni alan adı hızı 0,43/gün ve bunların yarısı terim içeriyorsa A_poz × p_kw ≈ 0,2/gün. `N_kw` = 200/gün olursa π ≈ %0,1, `N_kw` = 2.000/gün olursa π ≈ %0,01.
- Aynı varsayımla YA = %1, r = %50 için PPV: π = %1 → %33,6, π = %0,1 → %4,8, π = %0,01 → %0,5. `N_kw` = 200/gün ve YA = %1 ise günde yaklaşık 2 yanlış alarm.

Bu rakamlar yöntemi göstermek içindir, sonuç değildir. Gerçek `N_kw` ölçülünce hepsi yeniden hesaplanır.

### 8.4 Dürüstlük notları

- Yaygınlık `π` **SPK listesine göre**. Listelenmemiş sahte siteler yanlış alarm gibi görünür. Bu yüzden PPV "SPK etiketine göre precision" diye yazılır, "dolandırıcıları bulma oranı" diye değil.
- Yaygınlık zamanla değişir (2026'da öne yüklü). Tek sayı yerine ay ay aralık verilir.
- Hata yapan sinyal bu hesapta büyük yanlış alarm sayısı üretebilir. Günlük alarm hacmi, panelin işe yarayıp yaramayacağını belirler, rapora ayrı yazılır.

---

## 9. Yapılacaklar sırası

1. (Altyapı isteği) Tarafsız örneklem akışı: Bölüm 4.5. Dondurmadan en az 3 hafta önce başlamalı.
2. Veritabanından ölçüm: Pozitiflerin Let's Encrypt kapsama oranı (4.6), pozitiflerin terim oranı `p_kw`, `ct_candidates`'ta günlük kayıtlı alan adı sayısı.
3. G1 için resmi kaynaklardan web adresi çıkarma (aracı kurum ve portföy kaynaklarının web adresi alanı önce doğrulanmalı).
4. G2 önkoşul denemesi (3.3). G3a önkoşul denemesi (4.4). Ayrı kaydedilir.
5. Geniş finans sözlüğünü yaz ve dondur (Bölüm 4.3, maddede 2). **Baseline sözlüğünden önce veya birlikte**, sinyal kodundan önce.
6. Kümeleri hesapla ve dondur (7.5).
7. Liste üretim betiği ve manifest. Tüm gruplar dondurulur. Taahhüt kökü yayımlanır.
8. F tarihi belirlenir, kurallar donar (Hafta 8).

---

## 10. Kadircan'ın onayı gereken kararlar

1. **Bölme kesim noktası:** 2026/18 (123 ayar / 48 değerlendirme) mi, 2026/12 (115 / 56) mi?
2. **Kilitli test penceresi:** F + 28 gün ve 10'dan az pozitifte yalnızca ham sayı: Kabul mü? F tarihi?
3. **Tarafsız örneklem akışı** (alan adı özetinin %1'i): Altyapıya isteğe izin ve oranın ölçüm sonrası ayarlanması.
4. **Geniş finans sözlüğü** (baseline'dan ayrı): Kim yazacak, ne zaman donacak?
5. **G1:** Kripto "faaliyette bulunanlar" listesi G1'e girmesin, ayrı ve "yetki değil" etiketiyle mi raporlansın, yoksa tamamen dışarıda mı kalsın? Web adresi resmi kaynakta yoksa kurum dışarıda kalsın mı?
6. **G2 kaynağı:** A (Tranco + Radar kategorisi, koşullu) mı, B (Wikidata) mı? Önkoşul denemesi eşiği %80 uygun mu?
7. **G3a** (crt.sh ile geçmiş zor negatifler) denemesi: Yapılsın mı? Başarısızsa geçmiş testte zor negatif yanlış alarm olmadığını kabul ediyor musun?
8. **Boyutlar:** G3 1.000, G4 300 + 700, G2 300, G1 sayım. Haftalık DNS yaklaşık 14 saat. Kabul mü?
9. **Pozitifle bağlı kontroller** (7.5): Ana yanlış alarmdan çıkıp ayrı tabakada mı yazılsın?
10. Bu belge ne zaman "önceden kayıtlı" sayılsın (commit tarihi ve zaman damgası)?

### 10.1 Onaylanan kararlar (2026-11-15)

Kadircan bu bölümdeki 10 kararı aşağıdaki şekliyle onayladı. Belge, bu bölümün eklendiği commit ile **önceden kayıtlı** sayılır. Bundan sonraki her değişiklik "Değişiklik kaydı" bölümüne tarihi ve gerekçesiyle yazılır. Sonuç görüldükten sonra yapılan bir değişiklik raporda ayrıca belirtilir.

1. **Bölme:** 2026/1–2026/18 bültenleri ayar verisi (123 alan adı), 2026/20–2026/77 bültenleri geçmiş veri testi (48 alan adı).
2. **Kilitli test:** Pencere F'den F + 28 güne kadardır. F, Hafta 8'de kuralların dondurulduğu commit'in tarihidir. 10'dan az pozitif varsa yalnızca ham sayı verilir.
3. **Tarafsız örneklem akışı:** Kabul edildi, hemen başlar. Akış, alan adı özetinin yaklaşık %1'ini, içindeki kelimeye bakmadan saklar. Oran, ölçülen hacme göre yalnızca dondurmadan önce bir kez ayarlanabilir.
4. **Geniş finans sözlüğü:** Hafta 7 baseline'ından ayrı bir sözlüktür. Şimdi yazılır ve baseline sonuçlarına bakılmadan dondurulur.
5. **G1:** Kripto "faaliyette bulunanlar" listesi G1'e girmez. Ayrı bir grup olarak, "yetki listesi değildir" notuyla raporlanır. Resmi kaynakta web adresi yazmayan kurum dışarıda kalır. Adres elle aranmaz.
6. **G2:** A seçeneği (Tranco + Cloudflare Radar kategorisi) kullanılır. Önkoşul denemesinin eşiği %80'dir. Deneme bu eşiği geçmezse B seçeneğine (Wikidata) geçilir.
7. **G3a:** Deneme yapılır. Başarısız olursa geçmiş veri testinde zaman eşleşmeli zor negatif kullanılmaz. Bu durum raporda açıkça yazılır.
8. **Boyutlar:** G1 sayım, G2 300, G3 1.000, G4 300 + 700. Kontrol gruplarının DNS ölçümü haftada bir yapılır (yaklaşık 14 saat).
9. **Pozitifle bağlı kontroller:** Ana yanlış alarm oranına girmez, ayrı bir tabakada raporlanır.
10. **Önceden kayıt:** Bu bölümün eklendiği commit'in tarihi geçerlidir. İsteğe bağlı olarak OpenTimestamps zaman damgası da eklenebilir.

---

## 11. Doğrulayamadıklarım

- Aracı kurum ve portföy yönetim şirketi listelerinin satırları ve web adresi alanı (SPK sayfaları satırları vermedi, TSPB Excel dosyası açılmadı).
- BDDK, SPK ve TSPB'nin yeniden kullanım şartları, `robots.txt` durumu, resmi hız sınırları.
- Cloudflare Radar kategori adları, hız sınırı ve şartı. Wikidata seçeneğinin boyutu.
- Tranco'nun lisans metni ve atıf zorunluluğu (sayfada yok).
- crt.sh joker karakterli kimlik aramasının ve doğrudan veritabanı erişiminin bugünkü durumu ve hız sınırı. Alan adına göre toplu "ilk sertifika" sorgusunun maliyeti.
- Günlük aday sayısı (üç rakam çelişiyor), Let's Encrypt kapsama oranı, `p_kw`.
- Yapılandırmada yalnızca iki Sycamore diliminin olması: Willow ve başka CA'ların logları için kapsama.

## 12. Kaynaklar (erişim 2026-11-15)

- BDDK kuruluş listeleri: <https://www.bddk.org.tr/Kurulus>, bankalar <https://www.bddk.org.tr/Kurulus/Liste/77>, meslek birlikleri <https://www.bddk.org.tr/Kurulus/Liste/87> [doğrulandı]
- SPK kurumlar: <https://spk.gov.tr/kurumlar>, aracı kurumlar <https://spk.gov.tr/kurumlar/yatirim-kuruluslari/araci-kurumlar>, faaliyet izinleri <https://spk.gov.tr/faaliyet-izinleri>, <https://spk.gov.tr/faaliyet-izinleri/araci-kurumlar>, portföy yönetim şirketleri <https://spk.gov.tr/kurumlar/portfoy-yonetim-sirketleri>, kripto <https://spk.gov.tr/kurumlar/kripto-varlik-hizmet-saglayicilar>, <https://spk.gov.tr/kurumlar/kripto-varlik-hizmet-saglayicilar/faaliyette-bulunanlar-listesi> [doğrulandı, satır verisi için bkz. tablo]
- SPK 2025 Faaliyet Raporu (aracı kurum sayısı): <https://spk.gov.tr/data/61e48f651b41c60d1404d687/Faaliyet%20Raporu_2025.pdf> [özet]
- TSPB: <https://www.tspb.org.tr/uye-bilgileri/> [doğrulandı, Excel açılmadı]
- Tranco: <https://tranco-list.eu/>, <https://tranco-list.eu/api_documentation> [doğrulandı]. Makale: <https://lepoch.at/publication/tranco> [özet]
- Cloudflare Radar sıralama API: <https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get> [özet]
- crt.sh veritabanı erişimi duyurusu (2017): <https://groups.google.com/g/crtsh/c/sUmV0mBz8bQ> [doğrulandı]. Hız sınırı (2020): <https://groups.google.com/g/crtsh/c/NZJntKrBdmg> ([DATA_SOURCES.md](../DATA_SOURCES.md) Bölüm 2'de okundu)
- Yerel: [ROADMAP.md](../ROADMAP.md), [DECISIONS.md](../DECISIONS.md), [DATA_SOURCES.md](../DATA_SOURCES.md), [ASAMA0_SONUC.md](../phase0/ASAMA0_SONUC.md), [HAFTA4_ILK_OLCUM.md](../HAFTA4_ILK_OLCUM.md), [NETWORK_PLAN.md](../NETWORK_PLAN.md), [STATIC_CT_HACIM.md](../phase0/STATIC_CT_HACIM.md)

## Değişiklik kaydı

- 2026-11-20 (Kadircan onayı, hiçbir sonuç görülmeden). Kurulabilirlik incelemesinin ([KONTROL_LISTESI_KURULUM.md](KONTROL_LISTESI_KURULUM.md), Bölüm 5.4 ve 7) ardından şu değişiklikler kabul edildi:
  1. **G1:** Resmi kaynakta web adresi yalnızca BDDK banka listesinde var. Bu yüzden G1 yaklaşık 66 kurumdan oluşur. TSPB üye listesi web adresi içeriyorsa aracı kurumlar da G1'e girer. Bu durum kontrol edilir ve sonucu buraya yazılır.
  2. **crt.sh:** İki istek arasında en az 15 saniye beklenir. crt.sh işletmecisi dakikada 5 istek sınırı bildirdi. ROADMAP'teki "5 saniyede 1" hızı bu sınırı aşıyordu.
  3. **G3 ve G4b:** Bu gruplar haftalık olarak ekleme yapılarak dondurulur. Her hafta eklenen üyeler o haftanın tarihini taşıyan bir taahhütle kayda girer. Kilitli test, F tarihinde dondurulmuş olan üyelerle çalışır. F'den sonra gelen üyeler yalnızca ileriye dönük teste girer. Raporda hedef boyut değil, ulaşılan boyut yazılır.
  4. **DNS:** Kontrol gruplarının haftalık DNS ölçümü iki ardışık güne yayılabilir. Gruplar iki güne karışık dağıtılır. Hız saniyede en fazla 1 sorgu olarak kalır. 1.5'teki "aynı gün" kuralı "aynı parti" olarak okunur.
  5. **G3a:** Ölçüt somutlaştırıldı: Üç denemeden en az ikisi başarılı olmalı. Ölçütün tam tanımı kurulum belgesinin 4. bölümünde. Deneme geçmezse geçmiş veri testinde zaman eşleşmeli zor negatif kullanılmaz.
  6. **G2:** Kategori kümesi ve "boş olmayan liste" tanımı kurulum belgesinin 3. bölümündeki gibi uygulanır.
  7. **Hafta tabakası:** Haftalık gruplama `first_logged_at` sütununa göre yapılır.
  8. **Kota yuvarlama:** G3 ve G4b kotaları kurulum belgesindeki kurala göre yuvarlanır.
  9. **G3 ile G4b arasındaki ilişki:** Sözlükle eşleştiği halde G3'e seçilmeyen alan adları G4b'de kalabilir.
  10. **Ana veritabanı:** Ana veritabanı Ubuntu'dur (WSL). Pozitiflerin crt.sh ve Wayback verisi orada, aynı toplayıcılarla yeniden toplanır. Windows'taki veritabanı karşılaştırma ve yedek için kalır.
  11. **G2 kaynağı:** A seçeneği (Tranco ve Cloudflare Radar) kalır. Cloudflare Radar token'ını Kadircan alır. Token repoya, loglara ve hiçbir çıktıya yazılmaz.

- 2026-11-15 (önceden kayıttan sonra, hiçbir sonuç görülmeden): Tarafsız örneklem (4.5) uygulanırken iki uygulama farkı oluştu.
  1. **Seçim kuralı:** "Özet mod 100 = 0" yerine "SHA-256(tuz || alan adı) değerinin ilk 8 baytı < oran × 2^64" kuralı kullanıldı. İkisi de yaklaşık %1 seçer. Yeni kuralın farkı şu: Oran ileride artırılırsa, daha önce seçilmiş alan adları yine seçilmiş kalır.
  2. **Saklanan veri:** Alan adının bütün sertifikaları değil, alan adı başına tek satır saklanır: ilk görülme zamanı, ilk sertifikanın not_before değeri ve görülme sayısı. Bu yüzden "sertifika yenileme düzeni" gibi sertifika başına hesaplanan sinyaller tarafsız kontrol çerçevesinde (N) hesaplanamaz. Bu sinyaller yalnızca K çerçevesinde ya da crt.sh verisiyle raporlanır.
  
  PSL'nin özel bölümündeki adlar (paylaşımlı barındırma alt alanları) örnekte kalır, çünkü N_tüm sayımı için gerekirler. Kontrol grubu seçilirken 1.1'deki kurala göre elenirler.

- 2026-11-15: İlk sürüm (öneri). Henüz onaylanmadı ve donmadı.
