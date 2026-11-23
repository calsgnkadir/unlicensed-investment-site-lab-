# Sinyal kodu okuma rehberi (Hafta 7)

Hafta 6'da sınavı yazdık. Hafta 7'de ilk gerçek öğrenciler sınava giriyor: Ada ve sertifikaya bakan dört sinyal, bir isteğe bağlı sinyal ve bunları birleştiren toplam. Kod yapay zekâ destekli geliştirildi. Senin işin her satırı anlatabilecek hale gelmek.

Nasıl çalışacaksın:

1. Önce Bölüm 1'i oku. "Baseline'ı geçmek" ve "tanım gereği alarm" kafanda oturmadan sinyallere bakma.
2. Bölüm 2'deki tabloları sırayla izle. Her satırda dosyayı aç, o satırı oku, soruyu **koda bakmadan** cevapla.
3. Bölüm 3'teki alıştırmaları kâğıt kalemle çöz. Cevaplar en altta.
4. Bölüm 4'teki mülakat sorularını sesli cevapla.
5. Bölüm 5'i en son oku: Bilinen sınırlar ve kodda açık noktalar.

Bağlantılar bu dosyanın yerine göre yazıldı (`docs/learn/`). Satır numaraları 2026-11-21 tarihli koda göre. Kod değişirse fonksiyon adıyla ara. Hafta 6 rehberini ([HAFTA6_OLCUM_OKUMA.md](HAFTA6_OLCUM_OKUMA.md)) bitirmeden buna başlama. `AsOfView`, `Predictor`, `ThresholdSelector` ve küçük hücre kuralı burada bilindiği varsayılıyor.

Ön okuma: [FINANS_SOZLUGU.md](../research/FINANS_SOZLUGU.md) (sözlük ve baseline terimleri), [KONTROL_GRUBU_YONTEMI.md](../research/KONTROL_GRUBU_YONTEMI.md) Bölüm 2–5 (G1–G4 nasıl kuruldu).

---

## 1. Büyük resim

**Baseline neden en önemli rakip?** Bir ilacı test ederken ilacı "hiçbir şey vermemek" ile değil, piyasadaki en ucuz ilaçla karşılaştırırsın. Yeni ilaç ondan iyi değilse, pahalı ilacı kimse almaz. Bizim ucuz ilacımız baseline: "Adın soldaki parçasında 8 finans kelimesinden biri geçiyor mu?" Tek satırlık bu kuralı kimse yazmakta zorlanmaz. Bizim marka, sertifika ve patlama sinyallerimiz bundan daha iyi değilse, bütün o kodun bir değeri yok. Bu yüzden her sinyalin değerini "baseline'ı geçiyor mu" sorusuyla ölçeriz. "Recall %40" tek başına hiçbir şey söylemez. Baseline aynı yanlış alarm düzeyinde %38 veriyorsa, kazancımız 2 puan.

**Ablasyon nedir?** Bir yemeğin tadını neyin verdiğini anlamak için malzemeleri tek tek çıkarıp yeniden pişirirsin. Tuzu çıkarınca tat bozuluyorsa tuz önemli. Kekik çıkınca bir şey değişmiyorsa kekik süs. Ablasyon da aynısı: Dört sinyalin toplamını ölçüyoruz, sonra her birini tek tek çıkarıp yeniden ölçüyoruz. "TOPLAM − marka" satırı, toplamdan belirgin kötüyse marka sinyali işe yarıyor demektir. Değilse yük.

**"Tanım gereği" alarm neden sahte başarıdır?** Bir sınavın sorularını "kırmızı kalemle yazılmış kâğıtlar" arasından seçtiysen, "kırmızı kalem kullanan öğrenci" kuralı o kâğıtlarda her zaman doğru çıkar. Bu kuralın zekâsını değil, senin seçim yöntemini ölçer. Bizde üç örnek var:

1. **Baseline, Çerçeve K'de.** Çerçeve K'deki kontroller CT akışından, tam olarak baseline'ın 8 terimiyle süzülerek alındı. Bu kontrollerin hepsinin adında bir terim var, yani baseline orada her zaman 1 verir. K'deki yanlış alarm oranı baseline için bir ölçüm değil, kurgudur.
2. **Marka sinyali, G1'de.** Marka listesi G1'deki lisanslı kurumların adlarından üretildi. G1'deki bir ad, listedeki bir markanın kendisi olduğu için sinyal ona bilerek 0 verir. G1'de "hiç yanlış alarm yok" sonucu da bir başarı değil, kurgudur.
3. **Sözlük sinyali, G3'te.** G3 (zor negatifler) "aynı dönemde ilk sertifikasını almış ve sözlükle eşleşen adlar" diye kuruldu. Yeni sertifika + sözlük sinyali G3'te tanım gereği 1 verir.

Kural: Bir sinyalin kendi kurgusunun içinde ölçüldüğü tabaka, o sinyal için hiçbir şey kanıtlamaz. Gerçek sınav, kurguya dokunmayan tabakalardır: pozitifler, Çerçeve N ve tanım gereği alarmın olmadığı gruplar. Rapor bu notları kendisi basıyor ([Ablation.cs:67–83](../../src/Fraud.Evaluation/Running/Ablation.cs)). Bu kuralın eşik seçimine etkisi de var. Bölüm 5.2'nin 1. maddesini mutlaka oku.

**Neden makine öğrenmesi yok?** Etiketli veri az: yaklaşık 175 pozitif kayıt. Dört sinyale ağırlık öğrenmek ayar verisine aşırı uyar ve ablasyonu okunmaz yapar. Yol haritası da "baseline geçilmeden model yok" diyor. Bu yüzden birleştirme en basit haliyle yapılıyor: ağırlıksız toplam.

---

## 2. Okuma sırası

### 2.1 Ad bir gözlemdir

| Yer | Soru |
|---|---|
| [Observation.cs:12–16](../../src/Fraud.Evaluation/Model/Observation.cs) | `ObservationKinds.Name` ne taşıyor? Neden "hiçbir çıktıya yazılmaz" deniyor (Kural 4)? |
| Observation.cs:27–29 | `Value` alanı sertifikada ve ad gözleminde neyi tutuyor? |
| [EvaluationDataLoader.cs:148–154](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Ad gözleminin `KnownAt`'i neden `DateTimeOffset.MinValue`? Bu bir gelecek sızıntısı mı? (İpucu: Ad zamanla değişmez. Karar günü öğrenilen bir bilgi değil.) |
| EvaluationDataLoader.cs:128, :141 | Ad gözlemi hem pozitife hem kontrole aynı yoldan ekleniyor. Bu simetri neden önemli? |
| EvaluationDataLoader.cs:99–103 | Sertifika gözleminin `Value`'su artık sertifikanın iç kimliği. Bu kimlik patlama sinyalinde nasıl kullanılıyor? |
| [SubjectName.cs:10–13](../../src/Fraud.Signals/SubjectName.cs) | Ad neden `AsOfView`'a ayrı bir özellik olarak eklenmedi? Görünüm sözleşmesi hangi üç şeyi içeriyor (Hafta 6, AsOfView)? |
| SubjectName.cs:18–22, :28–29 | `Registrable` ve `FoldedLeftLabel` ne döndürüyor? Ad gözlemi yoksa ne oluyor? Neden yalnızca soldaki etiket? |
| [Predictor.cs:30–32](../../src/Fraud.Evaluation/Running/Predictor.cs) | Ad gözlemi değerlendirme başlangıcından neden dışlanıyor? Dışlanmasaydı `timed.Min(o => o.KnownAt)` ne olurdu ve döngü kaç gün dönerdi? |

### 2.2 Baseline ve tek terim kaynağı

| Yer | Soru |
|---|---|
| [BaselineTerms.cs:4–10, :13](../../src/Fraud.Domain/Signals/BaselineTerms.cs) | 8 terim hangileri? Bu liste neden "tek kaynak"? Dört farklı yer bu listeyi okuyor. Say. |
| [BaselineSignal.cs:11–13](../../src/Fraud.Signals/BaselineSignal.cs) | Filtre ve sinyal ayrı listelerden okusaydı ne bozulurdu? "K tanım gereği alarm" iddiası nasıl sessizce yanlış olurdu? |
| BaselineSignal.cs:14–17 | Filtre terimi bütün DNS adında arıyor, sinyal yalnızca soldaki etikette. K'de neden %100'ün biraz altı görülebilir? |
| BaselineSignal.cs:18 | Hariç tutma dizeleri baseline'da neden uygulanmıyor? |
| BaselineSignal.cs:24–33 | Kuralı tek cümleyle söyle. Puan hangi değerleri alabilir? |
| [FinanceLexicon.cs:114–154](../../src/Fraud.Domain/Signals/FinanceLexicon.cs) | `Fold` hangi adımlardan geçiyor? `xn--` neden önce Unicode'a çevriliyor? NFD ve birleştirici işaret atma neyi çözüyor? Sondaki `ToLowerInvariant` neden kültürden bağımsız (I/ı sorunu)? |
| FinanceLexicon.cs:132 | Büyük `I` neden `i`'ye çevriliyor, `ı`'ya değil? Türkçe kültürde `ToLower("I")` ne verirdi? |
| [NameSignalTests.cs:9–17, :20–26](../../tests/Fraud.Signals.Tests/NameSignalTests.cs) | Altı elle örneği tek tek açıkla. `ornek.borsa.example` neden 0? Punycode'lu Türkçe ad nasıl yakalanıyor? |
| NameSignalTests.cs:36–44 | Bu test "K tanım gereği" iddiasını nasıl sabitliyor? |

### 2.3 Marka listesi ve görsel benzer karakterler

| Yer | Soru |
|---|---|
| [BrandList.cs:11–15](../../src/Fraud.Signals/BrandList.cs) | Yerli liste nereden geliyor, neden gizli? Yabancı liste nerede duruyor? |
| BrandList.cs:19–20 | En kısa belirteç neden 5 karakter? 3–4 harflik bir marka alt dize olarak neye takılırdı? |
| BrandList.cs:38–42 | `Plain` ile `Tokens` farkı ne? `Plain`'de neden görsel benzer dönüşüm yapılmıyor? |
| BrandList.cs:49–63 | Liste nasıl kuruluyor? Neden sıralanıyor? `Plain` kısa markaları da içeriyor mu (BrandSimilarityTests.cs:77–83)? |
| BrandList.cs:84–100 | **Görsel benzer normalleştirme (homoglyph).** Önce hangi çok harfli dönüşümler yapılıyor? `rn → m` neden tek harflerden önce? |
| BrandList.cs:102–119 | Tek harf eşlemelerini say: rakamlar ve Kiril harfleri. `l → i` gerçek `l`'yi de değiştiriyor. Bu neden sorun değil? (İpucu: Belirteç de aynı fonksiyondan geçiyor.) |
| BrandList.cs:124–129 | Kısa belirteç normalleştirmeden **sonra** mı ölçülüyor, önce mi? `rn → m` bir markayı kısaltabilir mi? |
| [foreign_brands.txt:1–13](../../src/Fraud.Signals/foreign_brands.txt) | Yabancı listenin kaynağı ve durumu ne? "[doğrulanmadı]" neden önemli? Kısa adlı ve kripto kurumları neden yok? |
| [BrandSimilarityTests.cs:18–24](../../tests/Fraud.Signals.Tests/BrandSimilarityTests.cs) | Dört normalleştirme örneğini kâğıtta tekrar et. `0rnekbank` neden `omekbank` oluyor? |
| BrandSimilarityTests.cs:60–75 | Yabancı liste kuralları nasıl testle zorlanıyor? |

### 2.4 Marka benzerliği ve Levenshtein

| Yer | Soru |
|---|---|
| [BrandSimilaritySignal.cs:12–23](../../src/Fraud.Signals/BrandSimilaritySignal.cs) | Üç kuralı sırasıyla söyle. Etiket markanın kendisiyse neden 0? Bunun bedeli ne (hangi taklit kaçıyor)? |
| BrandSimilaritySignal.cs:24, :28, :36 | Sonuç neden önbellekte tutuluyor? Predictor aynı adı kaç kez sorar? Önbellek sonucu değiştirebilir mi? |
| BrandSimilaritySignal.cs:43–48 | `plain` ve `normalized` neden ikisi birden hesaplanıyor? Çevrilemeyen IDN ne alıyor? |
| BrandSimilaritySignal.cs:50–57 | Uzaklık sınırı belirtecin uzunluğuna göre nasıl seçiliyor? Kısa belirteçte 2 harf fark neden fazla? |
| BrandSimilaritySignal.cs:66–75 | **Levenshtein, iki satırlık tablo.** `previous` ve `current` dizileri ne tutuyor? İlk satır neden 0, 1, 2, ..., n? |
| BrandSimilaritySignal.cs:77–85 | Her hücre üç yoldan birini seçiyor: değiştirme, silme, ekleme. Her birinin formülünü söyle. `current[0] = i` neden? |
| BrandSimilaritySignal.cs:88, :91 | Satırlar neden yer değiştiriyor? Döngü bittiğinde cevap neden `previous[b.Length]`'te, `current`'ta değil? Tam tablo yerine iki satır tutmanın bellek kazancı ne? |
| BrandSimilarityTests.cs:9–16 | `kitten → sitting = 3` ve `ornekbnak → ornekbank = 2`. Yer değiştirme (transposition) neden 2 sayılıyor? |
| BrandSimilarityTests.cs:26–41 | On iki örneği tek tek açıkla. `ornxkbnak` neden 0? `deneme-katilim` neden 0, `denemekatilim` neden 1? |
| BrandSimilarityTests.cs:43–50 | 5 karakterli belirteçte uzaklık 1 ve 2 nasıl ayrılıyor? |

### 2.5 Yeni sertifika + sözlük

| Yer | Soru |
|---|---|
| [NewCertificateLexiconSignal.cs:11–12](../../src/Fraud.Signals/NewCertificateLexiconSignal.cs) | 28 gün nereden geliyor? Neden ayar verisinde seçilmedi? |
| NewCertificateLexiconSignal.cs:13–16 | **Okunması gereken sonuç.** Predictor gün gün puanlayıp en yüksek puanı tutunca bu sinyal pratikte neye dönüşüyor? "Yeni" şartı neyi belirliyor, neyi belirlemiyor? |
| NewCertificateLexiconSignal.cs:17–18 | G3'te bu sinyalin yanlış alarm oranı neden ölçüm değil? |
| NewCertificateLexiconSignal.cs:29 | Neden **ilk** sertifika? Görünüm sıralı olduğu için `FirstOrDefault` neyi garanti ediyor? |
| NewCertificateLexiconSignal.cs:35 | **28 günlük sınır.** `<=` neden `<` değil? Görünüm anı hep günün son anı. Sertifika gün içinde bir saatte bilinir. Bu, sınırı kaç gün kaydırıyor? |
| NewCertificateLexiconSignal.cs:36 | Sözlük eşleşmesi hangi kurallarla yapılıyor ([FinanceLexicon.cs:94–111](../../src/Fraud.Domain/Signals/FinanceLexicon.cs))? Hariç tutma dizeleri neden önce siliniyor? |
| FinanceLexicon.cs:55–64 | Sözlük dosyası neden SHA-256 ile denetleniyor? Biri bir kelime eklerse ne olur? |
| [CertificateSignalTests.cs:15–23](../../tests/Fraud.Signals.Tests/CertificateSignalTests.cs) | 27,5 ve 28,5 gün örneklerini kâğıtta hesapla. |
| CertificateSignalTests.cs:32–39 | Dün alınan bir sertifika eski bir adı neden "yeni" yapmıyor? |
| CertificateSignalTests.cs:82–93 | Karardan sonra alınan sertifika Predictor'dan nasıl geçiyor? Neden hiçbir günde görünmüyor? |

### 2.6 Sertifika patlaması

| Yer | Soru |
|---|---|
| [CertificateBurstSignal.cs:10–11](../../src/Fraud.Signals/CertificateBurstSignal.cs) | Pencere `(AsOf − 30 gün, AsOf]`. Üst sınırı kim koyuyor, alt sınırı kim? Alt sınır neden açık (`>`)? |
| CertificateBurstSignal.cs:12–13 | "Farklı sertifika" nasıl tanımlanıyor? Kimliği olmayan gözlem nasıl sayılıyor? |
| CertificateBurstSignal.cs:14–16 | 30 gün ve 5 sertifika nereden geliyor? Normal bir ad 30 günde kaç sertifika alır? |
| CertificateBurstSignal.cs:25–31 | Sayımı satır satır izle. `withId` ve `withoutId` neden ayrı? |
| [CertificateSignalTests.cs:45–55](../../tests/Fraud.Signals.Tests/CertificateSignalTests.cs) | Aynı kimlik iki kez yüklenirse ne oluyor? |
| CertificateSignalTests.cs:57–66, :69–76 | Pencere dışındaki sertifika ve görünüm anından sonra bilinen sertifika nasıl dışlanıyor? İkinci test hangi gelecek sızıntısını deniyor? |
| [EvaluationDataLoader.cs:99–103](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Sertifika kimliği hangi tablonun hangi alanından geliyor? Bir CT logunda aynı sertifikanın ön sertifikası (precertificate) ve son hali ayrı kayıtlarsa, bu kimlik onları bir mi sayar, iki mi? (Bölüm 5.2.) |

### 2.7 Numaralı ad (isteğe bağlı)

| Yer | Soru |
|---|---|
| [NumberedNameSignal.cs:9–11](../../src/Fraud.Signals/NumberedNameSignal.cs) | Bu sinyalin arkasındaki gözlem ne? Tek rakam ortada neden sayılmıyor? |
| NumberedNameSignal.cs:25–36 | İki kuralı söyle: sonda rakam, art arda iki rakam. Döngü neden 1'den başlıyor? |
| [NameSignalTests.cs:46–55](../../tests/Fraud.Signals.Tests/NameSignalTests.cs) | Yedi örneği tek tek açıkla. `ornek.v2.example` neden 0? |

### 2.8 Birleştirme: Neden VEYA değil de toplam?

| Yer | Soru |
|---|---|
| [Ablation.cs:8–17](../../src/Fraud.Evaluation/Running/Ablation.cs) | Birleşik puan neyi sayıyor? Ağırlık neden öğrenilmiyor? |
| Ablation.cs:14–17 | **%1 eşik argümanı.** Eşik ayar kontrollerinde %1 yanlış alarma göre seçiliyor. VEYA (en büyük parça) kullanılsaydı, kontrollerin %3'ünde 1 veren tek bir gürültülü parça ne yapardı? Toplamda hangi eşikler seçilebilir? Toplam neden VEYA'yı da kapsıyor? |
| Ablation.cs:19–24 | `SumSignal` ne kadar kısa? Adı neden parçaları sırayla yazıyor? |
| [Predictor.cs:65–76](../../src/Fraud.Evaluation/Running/Predictor.cs) | 0/1 sinyalde eşik seçimi nasıl işliyor? Kontrollerin α'dan fazlası 1 alırsa eşik ne olur, sinyal ne yapar? |
| [AblationTests.cs:85–92](../../tests/Fraud.Signals.Tests/AblationTests.cs) | `ornekyatirim24.example` neden 2 alıyor? |

### 2.9 Ablasyon

| Yer | Soru |
|---|---|
| [Ablation.cs:29–37](../../src/Fraud.Evaluation/Running/Ablation.cs) | Ablasyon hangi satırları üretiyor? Neden yalnızca geçmiş veri testinde? |
| Ablation.cs:46 | "Şimdi" neden bir kez alınıp bütün satırlara veriliyor? Her satır kendi saatini alsaydı ne değişebilirdi? |
| Ablation.cs:48–53 | **"TOPLAM − x".** Çıkarılmış toplamlar nasıl kuruluyor? `ReferenceEquals` neden ada göre karşılaştırmaktan daha güvenli? Tek parçada neden "TOPLAM − x" satırı yok? |
| Ablation.cs:55–56 | Her satır `EvaluationRunner.Run` ile ayrı çalışıyor. Bu, her satırın **kendi eşiğini** seçtiği demek. Fark yalnızca çıkarılan sinyalden mi geliyor, yoksa eşik değişiminden de mi? |
| Ablation.cs:62–85 | Tablo hangi uyarı notlarını basıyor ve hangi koşulda? "Eşik 1'den büyükse sinyal hiç alarm vermez" notu ne demek? |
| Ablation.cs:94–97, :112–122 | Fark nasıl hesaplanıyor? Küçük hücrede neden fark da yazılmıyor? Fark bir sayıyı nasıl açığa çıkarabilirdi? |
| [EvaluationConfig.cs:251–257](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Ablasyon kilitli kipte neden reddediliyor? Kilitli testte birden çok sinyal denemek hangi kuralı bozardı? |
| [AblationTests.cs:53–67, :69–83](../../tests/Fraud.Signals.Tests/AblationTests.cs) | Ablasyon çıktısında neden hiçbir ad geçmiyor? Satır sırası nasıl sabitleniyor? Baseline recall'u neden yaklaşık %50 bekleniyor? |

### 2.10 Yapılandırma, katalog ve komutlar

| Yer | Soru |
|---|---|
| [EvaluationConfig.cs:47–73](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Bilinen sinyal adları ve varsayılan parçalar hangileri? Numaralı ad neden varsayılan toplamda yok? |
| EvaluationConfig.cs:75–88 | `sum` ve `random` neden parça olamaz? Aynı parça iki kez neden yasak? Sayı sınırları ne? |
| [SignalCatalog.cs:12, :18–20](../../src/Fraud.Signals/SignalCatalog.cs) | Katalog neden dosya okumuyor? Hangi sinyal hangi kaynağa ihtiyaç duyuyor? |
| SignalCatalog.cs:23–47 | Ad nesneye nasıl çevriliyor? Kaynak yoksa ne oluyor? |
| SignalCatalog.cs:49–59 | Parçalar ne zaman okunuyor? Tek bir `baseline` çalıştırması neden marka listesini istemiyor ([AblationTests.cs:95–108](../../tests/Fraud.Signals.Tests/AblationTests.cs))? |
| [SignalCommands.cs:18–21](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs) | Kaynaklar neden kilitten **önce** okunuyor ([EvaluationCommands.cs:61–67](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs))? |
| SignalCommands.cs:29–43 | Sözlük donmuş sürümle aynı değilse ne oluyor? |
| SignalCommands.cs:45–61 | Marka listesi okunduktan sonra ekrana ne basılıyor? Neden ad değil de SHA-256? Bu özet bugün kilit kaydına bağlanıyor mu? |
| SignalCommands.cs:70–107 | `signals-brands` listeyi nereden üretiyor? Aynı veriyle iki kez çalışınca neden aynı dosya çıkıyor? Geçici dosya neden kullanılıyor? |
| [EvaluationCommands.cs:126–132](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs) | Ablasyon çalıştırması normal çalıştırmadan nerede ayrılıyor? |

---

## 3. Elle hesaplama alıştırmaları

Kâğıt kalem kullan. Cevaplar en altta.

**B1. Levenshtein, iki satırlı tablo.** `a = "ekin"`, `b = "kent"`. Kodun yaptığı gibi tabloyu satır satır doldur. İlk satır `0 1 2 3 4`. Her satırın sonunda `previous` ve `current` dizilerinin ne olduğunu yaz. Uzaklık kaç? Hangi üç işlemle `ekin` `kent` olur?

**B2. Görsel benzer dönüşüm.** `BrandList.Normalize` şu etiketler için ne döndürür?
(a) `0rnek-Bank` (b) `vvebornek` (c) `ka1em-giris`
Sonra şunu cevapla: Belirteç listesinde yalnızca `kalem` varsa (normalleşince `kaiem`), (c) etiketi marka sinyalinde kaç alır? Aynı etiket baseline'da da bir dönüşümden geçer mi?

**B3. Marka kuralının üç adımı.** Belirteç listesi: `ornekbank` (normalleşince `omekbank`, 8 karakter). Her etiket için hangi kuralın karar verdiğini ve puanı yaz:
(a) `ornekbank` (b) `ornekbank-giris` (c) `0rnekbamk` (d) `omxkbnak`

**B4. 28 günlük sınır.** İlk sertifika 1 Nisan 12:00'de (TR) biliniyor. Görünümler hep günün son anında (23:59:59,9999999).
(a) 28 Nisan sonunda yaş kaç gün, sinyal kaç verir?
(b) 29 Nisan sonunda?
(c) Sertifika 1 Nisan 23:59:59,9999999'da bilinseydi, 29 Nisan sonunda yaş kaç olur ve `<=` burada neyi değiştirir?
(d) Predictor en yüksek puanı tuttuğuna göre, bu adın sözlükle eşleştiğini varsayarsak birimin nihai puanı kaçtır?

**B5. Patlama sayımı.** Görünüm 10 Nisan sonunda. Sertifika gözlemleri (bilinme günü, kimlik): 1 Mart c-1; 20 Mart c-2; 25 Mart c-3; 2 Nisan c-4; 5 Nisan c-5; 6 Nisan c-4 (aynı sertifika ikinci kez yüklenmiş).
(a) Pencerenin alt sınırı hangi an? Pencerede kaç farklı sertifika var? Sinyal kaç verir?
(b) 9 Nisan'da c-6 eklense?
(c) Aynı ad için her sertifika verilişi logda iki ayrı kimlikle (ön sertifika ve son hal) görünseydi, 30 günde 3 kez sertifika alan normal bir ad kaç "farklı sertifika" sayılırdı?

**B6. Toplam skorda eşik seçimi.** 100 ayar kontrolünün toplam puanları: 80 tane 0, 15 tane 1, 4 tane 2, 1 tane 3. 50 test pozitifinin puanları: 10 tane 0, 15 tane 1, 15 tane 2, 10 tane 3.
(a) α = %1 için eşik ne olur? Kaç kontrol, kaç pozitif alarm verir?
(b) Aynı veride VEYA kullanılsaydı (her birimin puanı en büyük parçası, yani toplam ≥ 1 ise 1), eşik ve recall ne olurdu?
(c) α = %5 için toplamda eşik ve recall?

**B7. Ablasyon farkı.** Geçmiş veri testinde 100 test pozitifi var. Recall: TOPLAM(hepsi) 40/100, TOPLAM − brand 31/100, TOPLAM − cert-burst 40/100, TOPLAM − baseline 44/100, TOPLAM − new-cert-lexicon 3/100.
(a) Raporda her satırın yanında yazan fark kaç puan?
(b) Hangi sinyal en çok katkı veriyor, hangisi hiç vermiyor?
(c) Baseline'ı çıkarınca recall neden **artabilir**? (İpucu: Her satır kendi eşiğini seçiyor.)
(d) Son satır için fark neden yazılmıyor?

**B8. Numaralı ad.** Şu etiketlerin puanı kaç: `ornek7`, `ornek-2a`, `o12nek`, `4ornek`, `ornek.2026.example`?

---

## 4. Mülakat soruları

**S1. Baseline'ı neden yazdın?**
Taslak: Her sinyalin değerini ölçmek için bir rakip gerekiyor. "Recall %40" tek başına anlamsız. Akla gelen ilk basit kural aynı yanlış alarmda %38 veriyorsa kazancım 2 puan. Baseline 8 finans kelimesine bakıyor ve CT akışını süzen filtreyle aynı listeyi okuyor. Böylece ikisi asla ayrışmıyor.

**S2. Baseline'ı geçtin mi?**
Taslak: Dürüst cevap: Geçmiş veri testi bunu söyleyecek, kilitli test kesinleştirecek. Ölçerken iki tuzağa dikkat ediyorum. Birincisi, baseline'ın kendi kurgusu olan K çerçevesinde ölçmemek. İkincisi, eşiğin seçildiği kontrollerde tanım gereği 1 alan birimler varsa, baseline'ın yapay olarak susturulabilmesi (Bölüm 5.2). Geçemezsem bunu yazarım. Yol haritası da sonucu gizleme diyor.

**S3. Feature ablation nedir?**
Taslak: Bir özelliğin katkısını, onu çıkarıp sonucun ne kadar kötüleştiğine bakarak ölçmek. Ben her sinyali tek başına, hepsinin toplamını ve her biri çıkarılmış toplamı aynı bölmeyle ölçüyorum. "TOPLAM − marka" satırı toplamdan 9 puan düşükse markanın katkısı 9 puan. Sınırı: Her satır kendi eşiğini seçtiği için fark, eşik değişiminin etkisini de içeriyor.

**S4. Typosquatting'i nasıl yakalarsın, sınırları ne?**
Taslak: İki adımda. Önce görsel benzer karakterleri tek biçime indiriyorum: 0 → o, 1 ve l → i, rn → m, bazı Kiril harfleri Latin karşılığına. Etiketi ve marka listesini aynı fonksiyondan geçiriyorum. Sonra etiket markayı içeriyor mu ya da Levenshtein uzaklığı küçük mü diye bakıyorum: 5–7 harfte en çok 1, 8 ve üstünde en çok 2. Sınırlar: 5 harften kısa markalar listede yok. Markanın birebir adını başka uzantıda kullanan taklit kaçıyor. Yunan harfleri gibi eşlenmemiş benzer karakterler kaçıyor. Yabancı liste doğrulanmadı.

**S5. Levenshtein'ı nasıl hesaplıyorsun? Neden iki satır?**
Taslak: Dinamik programlama. Hücre (i, j), a'nın ilk i harfini b'nin ilk j harfine çevirmenin en az maliyeti. Üç yoldan en küçüğü alınıyor: değiştirme (çapraz + 0 ya da 1), silme (üst + 1), ekleme (sol + 1). Her satır yalnızca bir önceki satıra baktığı için iki satır yeterli. Bellek O(n·m) yerine O(m).

**S6. Neden ML modeli kurmadın?**
Taslak: Etiketli veri az, yaklaşık 175 pozitif kayıt. Dört sinyale ağırlık öğrenmek ayar verisine aşırı uyar, ablasyonu da okunmaz yapar. Yol haritası "baseline geçilmeden model yok" diyor. Önce basit, açıklanabilir sinyallerin baseline'ı geçip geçmediğini gösteriyorum. Geçerse ve veri büyürse model katmanı mantıklı olur.

**S7. Sinyalleri neden VEYA ile değil de toplamla birleştirdin?**
Taslak: Eşik ayar kontrollerinde %1 yanlış alarma göre seçiliyor. VEYA'da tek bir gürültülü sinyal kontrollerin %1'inden fazlasında 1 verirse eşik 1'in üstüne çıkar ve birleşik sinyal hiç alarm vermez. Toplamda eşik "en az k sinyal" olarak seçilebilir. Eşik 1 seçilirse sonuç VEYA'yla aynı, yani toplam VEYA'yı da kapsıyor.

**S8. "Tanım gereği alarm" nedir, projende nerede var?**
Taslak: Bir tabaka o sinyalin kendi kuralıyla kurulduysa, sinyalin orada aldığı sonuç ölçüm değil, kurgudur. Üç örnek: Baseline, kendi terimleriyle süzülmüş K çerçevesinde hep 1 verir. Marka sinyali, listesinin kaynağı olan G1'de hep 0 verir. Sözlük sinyali, sözlükle kurulmuş G3'te hep 1 verir. Rapor bu tabakaları ayrıca işaretliyor.

**S9. Yeni sertifika sinyali gerçekten "yeni"yi ölçüyor mu?**
Taslak: Kısmen. Predictor her birimi gün gün puanlayıp en yüksek puanı tuttuğu için, ilk sertifikanın görüldüğü gün her ad "yeni"dir. Geçmiş veri testinde sinyal pratikte "sözlük eşleşmesi + en az bir sertifika" kuralına dönüşüyor. "Yeni" şartı alarmın **ne zaman** verildiğini belirliyor, verilip verilmediğini değil. Canlı sistemde de böyle olurdu. Bunu kod yorumunda ve raporda açıkça yazıyorum.

**S10. Adı sinyale nasıl verdin? Gelecek sızıntısı yaratmadı mı?**
Taslak: Ad ayrı bir gözlem türü. `KnownAt`'i `MinValue`, çünkü ad zamanla değişmiyor ve karardan sonra öğrenilen bir bilgi değil. Böylece görünüm sözleşmesi (kimlik, an, gözlemler) bozulmadı. Predictor ad gözlemini değerlendirme başlangıcından dışlıyor, yoksa döngü 1. yıldan başlardı. Ad yalnızca bellekte, hiçbir çıktıya yazılmıyor.

**S11. Sertifika patlaması neyi yakalıyor, nerede yanılır?**
Taslak: 30 günde en az 5 farklı sertifika. Aynı adın kısa sürede tekrar tekrar kurulduğunu düşündürüyor. Yanılabileceği yerler: Her alt alan adına ayrı sertifika alan normal siteler. Bir CDN'in sık sertifika yenilemesi. Bir de kayıt düzeyinde bir tuzak var: Bir log aynı sertifikanın ön sertifikasını ve son halini ayrı kayıt olarak tutuyorsa, sayım ikiye katlanabilir (Bölüm 5.2).

**S12. Marka listesi nereden geliyor, neden gizli?**
Taslak: Yerli liste, G1'deki lisanslı bankaların alan adlarının soldaki etiketlerinden komutla üretiliyor ve `data/private` altında duruyor. Yabancı liste derlemeye gömülü ve sürümlü. Yerli liste gizli çünkü kontrol grubunun içeriğini açık ederdi (Kural 8). Repoya yalnızca üreten kod giriyor. Ekrana adlar değil, dosyanın SHA-256'sı basılıyor.

**S13. Bir sinyalin ayar verisinde seçilmediğini nasıl gösterirsin?**
Taslak: Bütün parametreler (28 gün, 30 gün, 5 sertifika, 5 harf, uzaklık sınırları) kodda sabit ve gerekçesi yorumda yazılı. Hiçbiri sonuca bakılarak ayarlanmadı. Değişirse `docs/DECISIONS.md`'ye tarihli kayıt girer. Kilitli testte tek, dondurulmuş sinyal çalışır, ablasyon orada reddedilir.

**S14. Yapay zekâyı nerede kullandın?**
Taslak: Hafta 6 rehberi 5.5'teki kalıp aynen geçerli. Sinyalleri hangi gözleme dayandıracağımı ve hangi tabakada ölçüm sayılmayacaklarını ben belirledim. Kod yapay zekâ destekli yazıldı, her satırını anlatabiliyorum. İsterseniz Levenshtein tablosunu ya da VEYA'nın neden işe yaramadığını şimdi göstereyim.

---

## 5. Bilinen sınırlar ve dürüst cevaplar

### 5.1 Bilinen sınırlar

- **Yabancı marka listesi doğrulanmadı.** Dosyanın başında `[doğrulanmadı]` yazıyor ([foreign_brands.txt:3–5](../../src/Fraud.Signals/foreign_brands.txt)). Liste ağa erişilmeden yazıldı. Kullanmadan önce kaynağın resmi yayınıyla karşılaştırılmalı. Fark varsa yeni sürüm açılmalı.
- **5 karakterden kısa markalar listede yok** ([BrandList.cs:19–20](../../src/Fraud.Signals/BrandList.cs)). Alt dize ve uzaklık olarak çok sayıda ilgisiz ada takılırlardı. Bedeli: Kısa adlı bir kurumun taklidi kaçıyor.
- **Yeni sertifika şartı geçmiş veri testinde pratikte "sözlük + en az bir sertifika" kuralına dönüşüyor** ([NewCertificateLexiconSignal.cs:13–16](../../src/Fraud.Signals/NewCertificateLexiconSignal.cs)). "Yeni" şartı yalnızca alarmın zamanını belirliyor.
- **Kilitli kipte marka listesi henüz kayda bağlı değil.** Yerli listenin SHA-256'sı ekrana basılıyor ([SignalCommands.cs:57–60](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs)), ama kilit kaydına yazılmıyor. Liste git dışında. Dondurmadan sonra `signals-brands` yeniden çalıştırılırsa ([SignalCommands.cs:98–100](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs) üzerine yazar) kilitli test farklı bir listeyle çalışabilir ve bu kayıtta görünmez.
- **Görsel benzer dönüşüm yalnızca marka sinyalinde.** Baseline ve sözlük `Fold`'u kullanıyor, `Normalize`'ı değil. `yat1r1m` baseline'a takılmaz. Bu bilinçli: Baseline en basit kural olarak kalmalı.
- **Eşlenmemiş benzer karakterler.** Yunan harfleri ve birçok Unicode benzeri eşlenmedi. Yalnızca rakamlar, `rn`, `vv` ve sekiz Kiril harfi var.
- **Marka adının birebir başka uzantıda kullanılması kaçıyor.** Etiket markanın kendisiyse puan 0 ([BrandSimilaritySignal.cs:45–48](../../src/Fraud.Signals/BrandSimilaritySignal.cs)).

### 5.2 Kodda açık noktalar (kod sahibine iletildi)

1. **Eşik seçimi, sinyalin tanım gereği 1 aldığı kontrolleri içeriyor.** Eşik `CalibrationGroups` (G3 + G4) içindeki ayar kontrollerinden seçiliyor ve alt grup süzülmüyor ([EvaluationRunner.cs:227–232](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs), [EvaluationConfig.cs:142](../../src/Fraud.Evaluation/EvaluationConfig.cs)). Çerçeve K'deki kontrollerin hepsi baseline'da 1 alır. G3'ün hepsi new-cert-lexicon'da 1 alır (ve G3'ün baseline alt kümesi baseline'da da). Bu birimler ayar kontrollerinin %1'inden fazlasıysa, eşik 1'in üstüne çıkar ve sinyal **hiç alarm vermez**. Sonuç: Baseline ölçümden önce susturulmuş olur. "Baseline'ı geçtik" sözü sahte bir zafer olur. Eşik de bizim seçtiğimiz G3:G4 ve K:N oranına bağlı kalır. Öneri: Eşiği yalnızca kurgusu olmayan kontrollerden (ör. Çerçeve N) seçmek ya da her sinyal için kendi kurgu tabakasını eşikten çıkarmak. Bu bir kural değişikliği olduğu için `docs/DECISIONS.md`'ye tarihli girmeli ve sonuçlara bakılmadan yapılmalı.
2. **Patlama sinyalinde çift sayım riski.** Sertifika kimliği, sertifika tablosunun iç satır numarası. Tablo her log kaydına bir satır açıyor (benzersiz alan kaynak kayıt numarası). Bir sertifikanın ön sertifikası ve son hali iki ayrı kayıtsa, sayım ikiye katlanır ve 30 günde 3 kez sertifika alan normal bir ad 5 eşiğini geçer (Alıştırma B5c). Öneri: Farklı sertifikayı (issuer, seri numarası) çiftiyle saymak. `IsPrecertificate` alanı şu an boş.
3. **Ablasyonda her satır kendi eşiğini seçiyor** ([Ablation.cs:55–56](../../src/Fraud.Evaluation/Running/Ablation.cs)). Bu bilinçli bir tasarım, ama "TOPLAM − x" farkı yalnızca x'in katkısı değil, eşik değişiminin etkisini de içeriyor. Raporda bu cümle yok, eklenmeli.

---

## Cevaplar

**B1.** `b = k e n t`.
- Başlangıç: `previous = [0 1 2 3 4]`.
- i = 1 (`e`): `current = [1 1 1 2 3]`. (j=1 `k`: değiştir 0+1=1. j=2 `e`: değiştir 1+0=1. j=3 `n`: ekle 1+1=2. j=4 `t`: ekle 2+1=3.) Yer değiştirme sonrası `previous = [1 1 1 2 3]`.
- i = 2 (`k`): `current = [2 1 2 2 3]`. (j=1 `k`: değiştir 1+0=1. j=2: üç yol da 2. j=3: değiştir 1+1=2. j=4: değiştir 2+1=3.) `previous = [2 1 2 2 3]`.
- i = 3 (`i`): `current = [3 2 2 3 3]`. (j=1: sil 1+1=2. j=2: değiştir 1+1=2. j=3: 3. j=4: değiştir 2+1=3.) `previous = [3 2 2 3 3]`.
- i = 4 (`n`): `current = [4 3 3 2 3]`. (j=3 `n`: değiştir 2+0=2. j=4: ekle 2+1=3.) `previous = [4 3 3 2 3]`.
- Cevap `previous[4] = 3`. İşlemler: `e`'yi sil (`kin`), `i`'yi `e` yap (`ken`), sona `t` ekle (`kent`).

**B2.**
(a) `Fold` → `0rnek-bank`. Tire silinir → `0rnekbank`. `rn → m` → `0mekbank`. `0 → o` → `omekbank`.
(b) `rn → m` → `vvebomek`. `vv → w` → `webomek`.
(c) Tire silinir → `ka1emgiris`. `1 → i` → `kaiemgiris`.
(c) etiketi `kaiem` belirtecini alt dize olarak içeriyor. Harf katlanmış hali (`ka1em-giris`) markanın kendisi değil. Puan 1. Baseline ve sözlük `Normalize`'dan geçmez, yalnızca `Fold`'dan geçer: Rakamlar harfe çevrilmez.

**B3.**
(a) `ornekbank`: Harf katlanmış hali `Plain`'de. Kural 1 karar verir, puan 0 (markanın kendisi).
(b) `ornekbank-giris`: `Plain`'de değil. Normalleşince `omekbankgiris`, `omekbank`'ı içeriyor. Kural 2, puan 1.
(c) `0rnekbamk`: `Plain`'de değil. Normalleşince `omekbamk`, belirteci içermiyor. Uzaklık 1 ≤ 2 (8 karakter). Kural 3, puan 1.
(d) `omxkbnak`: Normalleşince değişmez. `omekbank` ile uzaklık 3 (e→x, n↔a yer değiştirmesi 2). 3 > 2, puan 0.

**B4.**
(a) 28 Nisan 23:59:59,9999999 − 1 Nisan 12:00 ≈ 27,5 gün ≤ 28. Puan 1.
(b) 29 Nisan sonu − 1 Nisan 12:00 ≈ 28,5 gün > 28. Puan 0.
(c) 29 Nisan 23:59:59,9999999 − 1 Nisan 23:59:59,9999999 = tam 28 gün. `<=` olduğu için 1. `<` olsaydı 0 olurdu.
(d) 1. İlk değerlendirme günü (1 Nisan sonu) yaş yarım gün, yani "yeni". Predictor en yüksek puanı tuttuğu için birim 1 alır. Bu, Bölüm 5.1'deki "pratikte sözlük + bir sertifika" sınırının ta kendisi.

**B5.**
(a) Alt sınır: 10 Nisan sonu − 30 gün = 11 Mart sonu (açık uç). c-1 (1 Mart) dışarıda. İçeride c-2, c-3, c-4, c-5, c-4. Farklı kimlik 4. 4 < 5, puan 0.
(b) c-6 ile 5 farklı kimlik. Puan 1.
(c) Her veriliş iki kimlikle görünürse 3 veriliş = 6 kimlik ≥ 5. Normal bir ad yanlış alarm alır. Bölüm 5.2 madde 2.

**B6.**
(a) n = 100, izin verilen alarm = floor(0,01 × 100) = 1. Büyükten küçüğe: 0. sıra 3, 1. sıra 2. Eşik 2'nin hemen üstü, yani yalnızca puan 3 alarm verir. Kontrol: 1/100 = %1. Pozitif: 10/50 = %20.
(b) VEYA'da 20 kontrolün puanı 1 (toplamı ≥ 1 olanlar), 80'inin 0. 1. sıra 1. Eşik 1'in hemen üstü, hiçbir puan 1'den büyük değil. Hiç alarm yok. Recall 0. Toplamın neden seçildiğinin cevabı bu.
(c) İzin verilen 5. Sıralı: 3, 2, 2, 2, 2, **1**. Eşik 1'in hemen üstü, yani puan ≥ 2 alarm verir. Kontrol: 5/100 = %5. Pozitif: 25/50 = %50.

**B7.**
(a) TOPLAM − brand: 31 − 40 = −9,0 puan. TOPLAM − cert-burst: 0,0. TOPLAM − baseline: +4,0. TOPLAM − new-cert-lexicon: yazılmaz.
(b) En çok katkı markadan geliyor (çıkınca 9 puan düşüyor). Patlama hiçbir katkı vermiyor.
(c) Baseline, ayar kontrollerinin bir kısmında (özellikle tanım gereği 1 aldığı K çerçevesinde) puanı yükseltiyor. Bu da toplamda seçilen eşiği yukarı itiyor. Baseline çıkınca eşik düşebilir ve diğer sinyallerin alarmları sayılmaya başlar. Fark, eşik değişiminin etkisini de taşıyor (Bölüm 5.2 madde 1 ve 3).
(d) 3/100'de isabet 5'ten küçük, yani küçük hücre. Fark yazılsaydı (3 − 40 = −37) gizlenen 3 geri hesaplanırdı.

**B8.** `ornek7` → 1 (sonda rakam). `ornek-2a` → 0 (tek rakam, sonda değil). `o12nek` → 1 (art arda iki rakam). `4ornek` → 0 (tek rakam başta). `ornek.2026.example` → 0 (yalnızca soldaki etiket `ornek`'e bakılır).
