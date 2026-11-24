# Sinyal kodu okuma rehberi (Hafta 7)

Hafta 6'da sınavı yazdık. Hafta 7'de ilk gerçek öğrenciler sınava giriyor: Ada ve sertifikaya bakan dört sinyal, bir isteğe bağlı sinyal ve bunları birleştiren toplam. Kadircan, senin işin her satırı mülakatta anlatabilecek hale gelmek.

Nasıl çalışacaksın:

1. Önce Bölüm 1'i oku. "Baseline'ı geçmek" ve "tanım gereği alarm" kafanda oturmadan sinyallere bakma.
2. Bölüm 2'deki tabloları sırayla izle. Her satırda dosyayı aç, o satırı oku, soruyu **koda bakmadan** cevapla.
3. Bölüm 3'teki alıştırmaları kâğıt kalemle çöz. Cevaplar en altta.
4. Bölüm 4'teki mülakat sorularını sesli cevapla.
5. Bölüm 5'i oku: Bilinen sınırlar, kapanan maddeler ve kodda hâlâ açık noktalar.
6. Bölüm 6'daki sınama sorularını cevapları görmeden çöz. Cevaplar en altta, ayrı bir başlıkta.

Bağlantılar bu dosyanın yerine göre yazıldı (`docs/learn/`). Satır numaraları 2026-11-24 tarihli main koduna göre (9e8461f, 6792d9f, 831c5ff, 43ef5ca ve 148e358 commit'leri dahil). Kod değişirse fonksiyon adıyla ara. Hafta 6 rehberini ([HAFTA6_OLCUM_OKUMA.md](HAFTA6_OLCUM_OKUMA.md)) bitirmeden buna başlama. `AsOfView`, `Predictor`, `ThresholdSelector` ve küçük hücre kuralı burada bilindiği varsayılıyor.

Ön okuma: [FINANS_SOZLUGU.md](../research/FINANS_SOZLUGU.md) (sözlük ve baseline terimleri), [KONTROL_GRUBU_YONTEMI.md](../research/KONTROL_GRUBU_YONTEMI.md) Bölüm 2–5 (G1–G4 nasıl kuruldu), [DECISIONS.md](../DECISIONS.md) içindeki iki 2026-11-21 kaydı.

### Bu sürümde ne değişti (2026-11-24)

| Madde | Durum | Nerede okunur |
|---|---|---|
| Eşik, sinyalin tanım gereği 1 aldığı kontrolleri de içeriyordu | **Kapandı, 2026-11-21.** Eşik artık yalnızca G4 ayar kontrollerinden seçiliyor. | Bölüm 2.9 |
| Patlama sinyali aynı sertifikayı iki kez sayabiliyordu | **Kapandı, 2026-11-21.** Sayım (yayıncı, seri) çiftinin özetine göre. | Bölüm 2.6 |
| Ablasyon farkı eşik kaymasını da taşıyordu, raporda yazmıyordu | **Kapandı, 2026-11-21.** Rapor iki fark basıyor: kendi eşiğiyle ve TOPLAM eşiğiyle. | Bölüm 2.10 |
| Kilitli kipte marka listesi kayda bağlı değildi (S1) | **Kapandı.** Liste G1 taahhüdünden üretiliyor, özeti yapılandırmada ve kilit kaydında. | Bölüm 2.12 |
| G2 kaynağı | Radar yerine Wikidata. | Bölüm 2.13 |
| Dört küçük dürüstlük düzeltmesi: K çerçevesi eşikten açıkça dışarıda, tanınmayan grup hata veriyor, rapor eşik kümesini G4a/G4b diye döküyor, tek parça satırlarında sabit eşikli fark "uygulanmaz" | **Kapandı, 2026-11-24** ([DECISIONS.md](../DECISIONS.md), "2026-11-24: Eşik kümesi ve ablasyon raporunda dört küçük dürüstlük düzeltmesi"). | Bölüm 2.9, 2.10, 2.14, 5.2 |
| G2 kuralları raporda: "yeterli örnek yok" bayrağı, Türkiye alt kümesi satırı. Ablasyonda "uygulanmaz" kuralı tek parçadan "parça sayısı < TOPLAM eşiği"ne genişletildi | **Kapandı, 2026-11-24** ([DECISIONS.md](../DECISIONS.md), "2026-11-24: G2 kuralları raporda uygulanır, ablasyonda tek parça kuralı düzeltildi"). Genişletmeyi Kadircan hiçbir sonuç görülmeden onayladı. | Bölüm 2.10, 2.15, 5.2 |

---

## 1. Büyük resim

**Baseline neden en önemli rakip?** Bir ilacı test ederken ilacı "hiçbir şey vermemek" ile değil, piyasadaki en ucuz ilaçla karşılaştırırsın. Yeni ilaç ondan iyi değilse, pahalı ilacı kimse almaz. Bizim ucuz ilacımız baseline: "Adın soldaki parçasında 8 finans kelimesinden biri geçiyor mu?" Tek satırlık bu kuralı kimse yazmakta zorlanmaz. Bizim marka, sertifika ve patlama sinyallerimiz bundan daha iyi değilse, bütün o kodun bir değeri yok. Bu yüzden her sinyalin değerini "baseline'ı geçiyor mu" sorusuyla ölçeriz. "Recall %40" tek başına hiçbir şey söylemez. Baseline aynı yanlış alarm düzeyinde %38 veriyorsa, kazancımız 2 puan.

**Ablasyon nedir?** Bir yemeğin tadını neyin verdiğini anlamak için malzemeleri tek tek çıkarıp yeniden pişirirsin. Tuzu çıkarınca tat bozuluyorsa tuz önemli. Kekik çıkınca bir şey değişmiyorsa kekik süs. Ablasyon da aynısı: Dört sinyalin toplamını ölçüyoruz, sonra her birini tek tek çıkarıp yeniden ölçüyoruz. "TOPLAM − marka" satırı, toplamdan belirgin kötüyse marka sinyali işe yarıyor demektir. Değilse yük.

**"Tanım gereği" alarm neden sahte başarıdır?** Bir sınavın sorularını "kırmızı kalemle yazılmış kâğıtlar" arasından seçtiysen, "kırmızı kalem kullanan öğrenci" kuralı o kâğıtlarda her zaman doğru çıkar. Bu kuralın zekâsını değil, senin seçim yöntemini ölçer. Bizde üç örnek var:

1. **Baseline, Çerçeve K'de.** Çerçeve K'deki kontroller CT akışından, tam olarak baseline'ın 8 terimiyle süzülerek alındı. Bu kontrollerin hepsinin adında bir terim var, yani baseline orada her zaman 1 verir. K'deki yanlış alarm oranı baseline için bir ölçüm değil, kurgudur.
2. **Marka sinyali, G1'de.** Marka listesi G1'deki lisanslı kurumların adlarından üretildi. G1'deki bir ad, listedeki bir markanın kendisi olduğu için sinyal ona bilerek 0 verir. G1'de "hiç yanlış alarm yok" sonucu da bir başarı değil, kurgudur.
3. **Sözlük sinyali, G3'te.** G3 (zor negatifler) "aynı dönemde ilk sertifikasını almış ve sözlükle eşleşen adlar" diye kuruldu. Yeni sertifika + sözlük sinyali G3'te tanım gereği 1 verir.

Kural: Bir sinyalin kendi kurgusunun içinde ölçüldüğü tabaka, o sinyal için hiçbir şey kanıtlamaz. Gerçek sınav, kurguya dokunmayan tabakalardır: pozitifler, Çerçeve N ve tanım gereği alarmın olmadığı gruplar. Rapor bu notları kendisi basıyor ([Ablation.cs:108–124](../../src/Fraud.Evaluation/Running/Ablation.cs)).

Bu kuralın bir de ikinci yüzü var: Kurgu tabakası yalnızca **ölçüm** olarak sayılmamakla kalmamalı, **eşiği de belirlememeli**. Eskiden belirleyebiliyordu. 2026-11-21'de kapandı. Ayrıntısı Bölüm 2.9'da.

**Neden makine öğrenmesi yok?** Etiketli veri az: yaklaşık 175 pozitif kayıt. Dört sinyale ağırlık öğrenmek ayar verisine aşırı uyar ve ablasyonu okunmaz yapar. Yol haritası da "baseline geçilmeden model yok" diyor. Bu yüzden birleştirme en basit haliyle yapılıyor: ağırlıksız toplam.

---

## 2. Okuma sırası

### 2.1 Ad bir gözlemdir

| Yer | Soru |
|---|---|
| [Observation.cs:12–16](../../src/Fraud.Evaluation/Model/Observation.cs) | `ObservationKinds.Name` ne taşıyor? Neden "hiçbir çıktıya yazılmaz" deniyor (Kural 4)? |
| Observation.cs:27–29 | `Value` alanı sertifikada ve ad gözleminde neyi tutuyor? |
| [EvaluationDataLoader.cs:166–172](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Ad gözleminin `KnownAt`'i neden `DateTimeOffset.MinValue`? Bu bir gelecek sızıntısı mı? (İpucu: Ad zamanla değişmez. Karar günü öğrenilen bir bilgi değil.) |
| EvaluationDataLoader.cs:132, :147 | Ad gözlemi hem pozitife hem kontrole aynı yoldan ekleniyor. Bu simetri neden önemli? |
| EvaluationDataLoader.cs:102–107 | Sertifika gözleminin `Value`'su artık bir **veriliş kimliği**. Bu kimlik nasıl üretiliyor, patlama sinyalinde nasıl kullanılıyor? (Bölüm 2.6.) |
| [SubjectName.cs:10–13](../../src/Fraud.Signals/SubjectName.cs) | Ad neden `AsOfView`'a ayrı bir özellik olarak eklenmedi? Görünüm sözleşmesi hangi üç şeyi içeriyor (Hafta 6, AsOfView)? |
| SubjectName.cs:18–22, :28–29 | `Registrable` ve `FoldedLeftLabel` ne döndürüyor? Ad gözlemi yoksa ne oluyor? Neden yalnızca soldaki etiket? |
| [Predictor.cs:30–32](../../src/Fraud.Evaluation/Running/Predictor.cs) | Ad gözlemi değerlendirme başlangıcından neden dışlanıyor? Dışlanmasaydı `timed.Min(o => o.KnownAt)` ne olurdu ve döngü kaç gün dönerdi? |

### 2.2 Baseline ve tek terim kaynağı

| Yer | Soru |
|---|---|
| [BaselineTerms.cs:4–10, :13](../../src/Fraud.Domain/Signals/BaselineTerms.cs) | 8 terim hangileri? Bu liste neden "tek kaynak"? Dört farklı yer bu listeyi okuyor. Say. |
| [BaselineSignal.cs:11–13](../../src/Fraud.Signals/BaselineSignal.cs) | Filtre ve sinyal ayrı listelerden okusaydı ne bozulurdu? "K tanım gereği alarm" iddiası nasıl sessizce yanlış olurdu? |
| BaselineSignal.cs:14–17 | Filtre terimi bütün DNS adında arıyor, sinyal yalnızca soldaki etikette. K'de neden %100'ün biraz altı görülebilir? Satır 14 bugün K'nin yalnızca G3'te olduğunu söylüyor. Eskiden "G4b'nin K kısmı" diyordu ve yanlıştı. 2026-11-24'te düzeltildi (Bölüm 5.2, madde 5). |
| BaselineSignal.cs:18 | Hariç tutma dizeleri baseline'da neden uygulanmıyor? |
| BaselineSignal.cs:24–33 | Kuralı tek cümleyle söyle. Puan hangi değerleri alabilir? |
| [FinanceLexicon.cs:114–154](../../src/Fraud.Domain/Signals/FinanceLexicon.cs) | `Fold` hangi adımlardan geçiyor? `xn--` neden önce Unicode'a çevriliyor? NFD ve birleştirici işaret atma neyi çözüyor? Sondaki `ToLowerInvariant` neden kültürden bağımsız (I/ı sorunu)? |
| FinanceLexicon.cs:132 | Büyük `I` neden `i`'ye çevriliyor, `ı`'ya değil? Türkçe kültürde `ToLower("I")` ne verirdi? |
| [NameSignalTests.cs:9–17, :20–26](../../tests/Fraud.Signals.Tests/NameSignalTests.cs) | Altı elle örneği tek tek açıkla. `ornek.borsa.example` neden 0? Punycode'lu Türkçe ad nasıl yakalanıyor? |
| NameSignalTests.cs:36–44 | Bu test "K tanım gereği" iddiasını nasıl sabitliyor? |

### 2.3 Marka listesi ve görsel benzer karakterler

| Yer | Soru |
|---|---|
| [BrandList.cs:11–15](../../src/Fraud.Signals/BrandList.cs) | Yerli liste nereden geliyor, neden gizli? Yabancı liste nerede duruyor? (Kilitli kipte yerli listenin nereden geldiği değişti: Bölüm 2.12.) |
| BrandList.cs:19–20 | En kısa belirteç neden 5 karakter? 3–4 harflik bir marka alt dize olarak neye takılırdı? |
| BrandList.cs:38–42 | `Plain` ile `Tokens` farkı ne? `Plain`'de neden görsel benzer dönüşüm yapılmıyor? |
| BrandList.cs:49–63 | Liste nasıl kuruluyor? Neden sıralanıyor? `Plain` kısa markaları da içeriyor mu (BrandSimilarityTests.cs:77–83)? |
| BrandList.cs:89–100 | **Görsel benzer normalleştirme (homoglyph).** Önce hangi çok harfli dönüşümler yapılıyor? `rn → m` neden tek harflerden önce? |
| BrandList.cs:101–121 | Tek harf eşlemelerini say: rakamlar ve Kiril harfleri. `l → i` gerçek `l`'yi de değiştiriyor. Bu neden sorun değil? (İpucu: Belirteç de aynı fonksiyondan geçiyor.) |
| BrandList.cs:124–129 | Kısa belirteç normalleştirmeden **sonra** mı ölçülüyor, önce mi? `rn → m` bir markayı kısaltabilir mi? |
| [foreign_brands.txt:1–13](../../src/Fraud.Signals/foreign_brands.txt) | Yabancı listenin kaynağı ve durumu ne? "[doğrulanmadı]" neden önemli? Kısa adlı ve kripto kurumları neden yok? |
| [BrandSimilarityTests.cs:18–24](../../tests/Fraud.Signals.Tests/BrandSimilarityTests.cs) | Dört normalleştirme örneğini kâğıtta tekrar et. `0rnekbank` neden `omekbank` oluyor? |
| BrandSimilarityTests.cs:60–75 | Yabancı liste kuralları nasıl testle zorlanıyor? |

### 2.4 Marka benzerliği ve Levenshtein

| Yer | Soru |
|---|---|
| [BrandSimilaritySignal.cs:12–23](../../src/Fraud.Signals/BrandSimilaritySignal.cs) | Üç kuralı sırasıyla söyle. Etiket markanın kendisiyse neden 0? Bunun bedeli ne (hangi taklit kaçıyor)? |
| BrandSimilaritySignal.cs:24, :28, :35–40 | Sonuç neden önbellekte tutuluyor? Predictor aynı adı kaç kez sorar? Önbellek sonucu değiştirebilir mi? |
| BrandSimilaritySignal.cs:30–31, :49–53 | Belirteçler neden uzundan kısaya sıralı? "En uzun eşleşme" puanı değiştiriyor mu, yoksa yalnızca "hangi marka" sorusunun cevabını mı? |
| BrandSimilaritySignal.cs:58–63 | `plain` ve `normalized` neden ikisi birden hesaplanıyor? Çevrilemeyen IDN ne alıyor? |
| BrandSimilaritySignal.cs:65–72 | Önce alt dize, sonra uzaklık. Neden bu sıra? Uzaklık sınırı belirtecin uzunluğuna göre nasıl seçiliyor? Kısa belirteçte 2 harf fark neden fazla? |
| BrandSimilaritySignal.cs:83–88 | **Levenshtein, iki satırlık tablo.** `previous` ve `current` dizileri ne tutuyor? İlk satır neden 0, 1, 2, ..., n? |
| BrandSimilaritySignal.cs:90–98 | Her hücre üç yoldan birini seçiyor: değiştirme, silme, ekleme. Her birinin formülünü söyle. `current[0] = i` neden? |
| BrandSimilaritySignal.cs:101, :104 | Satırlar neden yer değiştiriyor? Döngü bittiğinde cevap neden `previous[b.Length]`'te, `current`'ta değil? Tam tablo yerine iki satır tutmanın bellek kazancı ne? |
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

### 2.6 Sertifika patlaması (veriliş sayımı, 2026-11-21)

**Ne değişti?** Eskiden "farklı sertifika" demek, sertifika tablosundaki farklı **satır** demekti. Tablo her log kaydına bir satır açıyor. Bir sertifika verilirken CT loglarına iki kez girer: önce **ön sertifika** (precertificate, logun imzalı makbuzunu almak için), sonra **son hal**. Bu ikisi iki satır olur. Eski kod, 30 günde 3 kez sertifika alan normal bir adı 6 sertifika sayıp alarm verebiliyordu. Yanlış alarm sayısını şişiren, sinyali kötü gösteren bir ölçüm hatasıydı.

Şimdi sayılan şey **veriliştir**: (yayıncı, seri numarası) çifti. Bir yayıncı aynı seri numarasını iki farklı sertifikaya veremez. Ön sertifika ile son hal aynı yayıncı ve aynı seriyi taşır. Yani aynı veriliştir.

| Yer | Soru |
|---|---|
| [EvaluationDataLoader.cs:88–91](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Sorgu sertifikadan hangi alanları çekiyor? Eskiden çekilen satır kimliği neden artık yok? |
| EvaluationDataLoader.cs:102–107 | Yorumu oku. Aynı sertifika birden çok alan adı satırına bağlıysa ne oluyor? "Yayıncı metni gözleme girmez" neden önemli? (İpucu: Kural 5 ve gözlem değerinin kısa kalması.) |
| EvaluationDataLoader.cs:154–164 | **`IssuanceId`, adım adım.** Seri numarası hangi dört adımdan geçiyor: `:` silme, kırpma, küçük harf, baştaki sıfırları atma. Her biri hangi yazım farkını yok ediyor? Neden `ToLowerInvariant` (I/ı)? Yayıncı adında yalnızca `Trim` var. Neden? Özet neden 16 onaltılık karakter (64 bit)? |
| [CertificateBurstSignal.cs:10–11](../../src/Fraud.Signals/CertificateBurstSignal.cs) | Pencere `(AsOf − 30 gün, AsOf]`. Üst sınırı kim koyuyor, alt sınırı kim? Alt sınır neden açık (`>`)? |
| CertificateBurstSignal.cs:12–14 | "Farklı sertifika" artık nasıl tanımlanıyor? Ön sertifika ve son hal neden bir sayılıyor? Kimliği olmayan gözlem nasıl sayılıyor ve bu bugün hâlâ olur mu? |
| CertificateBurstSignal.cs:15–17 | 30 gün ve 5 sertifika nereden geliyor? Normal bir ad 30 günde kaç sertifika alır? |
| CertificateBurstSignal.cs:26–32 | Sayımı satır satır izle. `withId` neden `Distinct(StringComparer.Ordinal)` kullanıyor? `withoutId` neden ayrı? |
| [CertificateSignalTests.cs:45–55](../../tests/Fraud.Signals.Tests/CertificateSignalTests.cs) | Aynı kimlik iki kez yüklenirse ne oluyor? |
| CertificateSignalTests.cs:57–66, :69–76 | Pencere dışındaki sertifika ve görünüm anından sonra bilinen sertifika nasıl dışlanıyor? İkinci test hangi gelecek sızıntısını deniyor? |
| [EvaluationLoaderTests.cs:356–395](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs) | Test iki satır ekliyor: biri ön sertifika, seri `00:ab:12`; öteki son hal, seri `AB12`. Neden iki gözlem ama tek kimlik çıkıyor? Son satırda eşik neden bilerek 2 seçilmiş? |
| EvaluationLoaderTests.cs:397–408 | Hangi yazım farkları aynı kimliği vermeli, hangi farklar farklı kimlik vermeli? |

**Neden ölçüm dürüstlüğü için önemli?** Patlama sinyali "kısa sürede çok sertifika" der. Bir kaydın nasıl loglandığı (bir kez mi, iki kez mi) sitenin davranışı değil, CT altyapısının ayrıntısıdır. Bunu saymak, sinyale sitenin değil, logun özelliğini ölçtürür. Ayrıca bu hata kontrol ve pozitifte eşit dağılmayabilir: Hangi yayıncının ön sertifikayı ayrı logladığı, sitenin hangi yayıncıyı seçtiğine bağlı.

### 2.7 Numaralı ad (isteğe bağlı)

| Yer | Soru |
|---|---|
| [NumberedNameSignal.cs:9–11](../../src/Fraud.Signals/NumberedNameSignal.cs) | Bu sinyalin arkasındaki gözlem ne? Tek rakam ortada neden sayılmıyor? |
| NumberedNameSignal.cs:25–36 | İki kuralı söyle: sonda rakam, art arda iki rakam. Döngü neden 1'den başlıyor? |
| [NameSignalTests.cs:46–55](../../tests/Fraud.Signals.Tests/NameSignalTests.cs) | Yedi örneği tek tek açıkla. `ornek.v2.example` neden 0? |

### 2.8 Birleştirme: Neden VEYA değil de toplam?

| Yer | Soru |
|---|---|
| [Ablation.cs:8–18](../../src/Fraud.Evaluation/Running/Ablation.cs) | Birleşik puan neyi sayıyor? Ağırlık neden öğrenilmiyor? |
| Ablation.cs:14–17 | **%1 eşik argümanı.** Eşik ayar kontrollerinde %1 yanlış alarma göre seçiliyor. VEYA (en büyük parça) kullanılsaydı, kontrollerin %3'ünde 1 veren tek bir gürültülü parça ne yapardı? Toplamda hangi eşikler seçilebilir? Toplam neden VEYA'yı da kapsıyor? |
| Ablation.cs:19–24 | `SumSignal` ne kadar kısa? Adı neden parçaları sırayla yazıyor? |
| [Predictor.cs:65–76](../../src/Fraud.Evaluation/Running/Predictor.cs) | 0/1 sinyalde eşik seçimi nasıl işliyor? Kontrollerin α'dan fazlası 1 alırsa eşik ne olur, sinyal ne yapar? |
| [AblationTests.cs:85–92](../../tests/Fraud.Signals.Tests/AblationTests.cs) | `ornekyatirim24.example` neden 2 alıyor? |

### 2.9 Eşik yalnızca tarafsız kontrollerden (KAPANDI, 2026-11-21)

**Eski sorun.** Eşik, `CalibrationGroups` içindeki (G3 + G4) ayar kontrollerinden seçiliyordu. Çerçeve K'deki her kontrol baseline'da 1 alır. G3'ün her üyesi new-cert-lexicon'da 1 alır. Bu "tanım gereği 1" birimleri ayar kontrollerinin %1'inden fazlasıysa, `ThresholdSelector` eşiği 1'in üstüne iter ve o sinyal **hiç alarm vermez**. Yani baseline ölçülmeden susturulabilirdi. Ardından "sinyallerimiz baseline'ı geçti" demek sahte bir zafer olurdu. Üstelik eşik, bizim keyfi seçtiğimiz G3:G4 ve K:N oranlarına bağlı kalıyordu. G3'e 300 değil 1.000 kişi koysaydık eşik değişirdi.

**Karar** ([DECISIONS.md](../DECISIONS.md), "2026-11-21: Eşik seçimi yalnızca tarafsız kontrollerden yapılır"): Eşik yalnızca G4'ün (G4a ve G4b) ayar bölümündeki kontrollerden seçilir. G4, alan adındaki kelimeye bakılmadan seçilir. G1, G2, G3 eşiğe girmez. Seçilen eşikte grup ve çerçeve başına ayrı raporlanır. Karar sonuç görülmeden, kod okunurken verildi.

**Kod bunu üç yerde zorluyor.** Biri asıl süzgeç. Biri yanlış yapılandırmaya karşı kilit. Biri de yükleyicide, tanınmayan bir grubun G4'e karışmasına karşı kilit.

| Yer | Ne yapıyor, ne soracaksın |
|---|---|
| [EvaluationRunner.cs:311–314](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | Yorum iki kararı ve tarihlerini anıyor: 2026-11-21 (yalnızca G4) ve 2026-11-24 (K çerçevesi ayrıca dışarıda). Kod ile karar kaydı birbirine bağlı. |
| EvaluationRunner.cs:315–318 | **Asıl süzgeç.** Beş koşul var (:316–317). Her birini tek tek söyle: (1) `split.PartOf(s.Id) == SplitPart.Tuning`: Test birimi eşiğe giremez. (2) `!s.IsPositive`: Pozitif eşiğe giremez, eşik yalnızca yanlış alarma göre seçilir. (3) `!s.LinkedToPositive`: Bir pozitifle aynı kümedeki kontrol (E05) eşiğe giremez. (4) `s.Group == SubjectGroup.ControlRandom`: Yalnızca G4. (5) `s.Subgroup != Subgroup.FrameTermConditioned`: G4 olsa bile K çerçevesinden gelen birim giremez. Soru: Bugün G4'te K birimi yok. Beşinci koşul o zaman neden yazıldı? Grup burada sabit yazılı ve runner `config.CalibrationGroups`'u hiç okumuyor. Bunun iyi ve kötü yanı ne? |
| EvaluationRunner.cs:319 | Puanlar `threshold: null` ile hesaplanıyor. Neden? (Eşik daha yok. Yalnızca en yüksek puan lazım.) |
| EvaluationRunner.cs:320 | `fixedThreshold ?? ThresholdSelector.Select(...)`. Sabit eşik ne zaman verilir? (Yalnızca ablasyon, Bölüm 2.10.) |
| EvaluationRunner.cs:323–325 | Test tahminleri eşik sabitlendikten **sonra**. Sıra neden önemli? |
| EvaluationRunner.cs:327–328 | Rapor kuruluyor, sonra `with { CalibrationComposition = ... }` ile eşik kümesinin dökümü ekleniyor. Bu söz dizimi Bölüm 2.14, not 1'de. |
| EvaluationRunner.cs:331–340 | `CompositionOf`: Eşik kümesi G4a ve G4b diye sayılıyor. İkisi aynı ölçüm grubunda. Onları ne ayırıyor? (Çerçeve: G4a çerçevesiz, `Subgroup.None`. G4b Çerçeve N.) Bir birim ne G4a ne G4b sayılırsa ne olur? (İki sayının toplamı `n`'den az çıkar. Test bunu denetliyor: ThresholdTests.cs:185.) |
| EvaluationRunner.cs:46–47, :67–74 | Rapor eşiği, hedef oranı ve `n=` değerini yazıyor. Altına "Eşik kümesi (ayar): G4a=..., G4b=..." satırı geliyor. İleriye dönük testte bu satır neden basılmıyor? (Eşik yok, liste boş kalıyor.) |
| EvaluationRunner.cs:147–156 | `CompositionText`: küçük hücre ve tümleyen kuralı. İç içe üçlü işleç (`? :`) Bölüm 2.14, not 2'de. |
| [EvaluationConfig.cs:153–158](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Varsayılan artık `[SubjectGroup.ControlRandom]`. Alan neden silinmedi? (Eski yapılandırma dosyaları okunabilsin.) |
| EvaluationConfig.cs:242 | `CalibrationGroups is not [SubjectGroup.ControlRandom]`. Bu bir **liste deseni**: Liste tam olarak tek elemanlı ve o eleman `ControlRandom` değilse yapılandırma reddedilir. Soru: Runner bu alanı okumuyorsa bu satır neden var? |
| [EvaluationDataLoader.cs:237–254](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Her kontrol grubu açıkça yazılı. G4a ve G4b ikisi de `ControlRandom`. Son kol (:251–253) artık tanınmayan grupta hata fırlatıyor. Eskiden `_ => SubjectGroup.ControlRandom` vardı. Fark ne? Hata mesajında neden yalnızca sayısal değer var? |
| EvaluationDataLoader.cs:256–261 | `FrameOf`: K, N ya da yok. Burada `_ => Subgroup.None` hâlâ duruyor. Neden burada sorun değil? (Çerçevesi olmayan grup zaten G4a gibi çerçevesiz sayılmalı. Üstelik K'yi eşikten dışlayan koşul ayrı.) |
| [ControlCommands.cs:385](../../src/Fraud.Collectors/Control/ControlCommands.cs) | G3 hem N hem K çerçevesinden kuruluyor, G4b yalnızca N'den. Bu satır, "bugün G4'te tanım gereği 1 alan birim yok" iddiasının dayanağı. Beşinci koşul bu satır yarın değişirse diye var. |
| [ThresholdTests.cs:60–78](../../tests/Fraud.Signals.Tests/ThresholdTests.cs) | 1.200 Çerçeve K birimi var (G3'te), hepsi baseline'da 1. Eski kuralda ne olurdu? Yeni kuralda eşik nereden geliyor? K satırı neden %100 ve neden "ölçüm değil"? |
| ThresholdTests.cs:80–94 | G3'te 300 birim hep 1 ya da 900 birim hep 0. Eşik ve recall neden aynı çıkmalı? Bu test hangi bağımlılığı kırdığını kanıtlıyor? |
| ThresholdTests.cs:96–101 | Hangi üç yapılandırma reddediliyor? |
| ThresholdTests.cs:138–165 | Bu kez K birimleri **G4'ün içinde** (:151). Sonuç neden K birimleri hiç yokmuş gibi çıkmalı (:160–162)? K birimleri raporda yine görünüyor mu (:164)? |
| ThresholdTests.cs:167–188 | Döküm testi. :184 neden iki sayının da 5 ya da üstü olmasını şart koşuyor? (Yoksa metin "<5" ya da "-" olurdu ve :186'daki karşılaştırma tutmazdı.) |
| ThresholdTests.cs:190–197 | Üç örneği kâğıtta tekrar et: (3, 200), (0, 4), (5, 200). |
| [G2ReportTests.cs:39–51](../../tests/Fraud.Evaluation.Tests/G2ReportTests.cs) | 600 G2 birimi eklenince eşik, `n` ve döküm neden hiç değişmemeli? (G2 eşiğe girmez.) |
| [EvaluationLoaderTests.cs:410–421, :423–437](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs) | İlk test her grubun eşlemesini tek tek denetliyor. İkincisi 99 değerinin reddedildiğini ve bütün grupların hatasız eşlendiğini denetliyor. :433–436'daki döngü neden bilerek kırılgan? (Bölüm 2.14, not 4.) |
| [SafetyTests.cs:191–201, :203–220](../../tests/Fraud.Evaluation.Tests/SafetyTests.cs) | Repodaki örnek yapılandırma neden varsayılanla aynı `CalibrationGroups`'u vermeli? |

**`Fraud.Signals` tarafında ne var?** Dürüst cevap: Sinyal kodu eşiği bilmez, yalnızca puan verir. Eşik kuralını zorlayan kod `Fraud.Evaluation`'da (runner ve yapılandırma). `Fraud.Signals`'ta yalnızca her sinyalin hangi tabakada ölçüm sayılmadığını yazan notlar var: [BaselineSignal.cs:14–15](../../src/Fraud.Signals/BaselineSignal.cs), [BrandSimilaritySignal.cs:16–18](../../src/Fraud.Signals/BrandSimilaritySignal.cs), [NewCertificateLexiconSignal.cs:17–18](../../src/Fraud.Signals/NewCertificateLexiconSignal.cs). Kuralın testleri ise `tests/Fraud.Signals.Tests/ThresholdTests.cs` içinde, çünkü gerçek sinyallerle denenmesi gerekiyor.

**Neden ölçüm dürüstlüğü için önemli?**

1. Bir sinyalin kurgu tabakası artık onun eşiğini belirleyemez. Baseline'ı susturarak "geçtik" denemez.
2. Eşik, G3 ve K'nin büyüklüğüne bağlı değil. Haftalık kurulumda G3 büyüdükçe eşik kaymaz.
3. "%1 yanlış alarm" cümlesi artık net bir anlam taşıyor: "Rastgele seçilmiş normal sitelerin ayar kısmında en çok %1." G3, K ve G1'deki oranlar daha yüksek olabilir. Onlar ayrı satırlarda, kendi notlarıyla yazılır.
4. Bedeli var: Eşik yalnızca G4'e dayanıyor. G4'ün ayar kısmı büyük olasılıkla çoğunlukla G4a, yani popüler ve köklü siteler. Bu sitelerde gürültü az olabilir. Böyle olursa eşik düşük seçilir ve yeni alan adlarında (G4b, Çerçeve N) yanlış alarm %1'in üstüne çıkabilir. Rapor bunu G4b / N satırında gösterir. 2026-11-24'ten beri "Eşik kümesi (ayar): G4a=..., G4b=..." satırı da basılıyor. Okuyan eşiğin hangi sitelere dayandığını görebiliyor. Bedel ortadan kalkmadı, ama artık gizli değil.

### 2.10 Ablasyon (iki fark, 2026-11-21)

| Yer | Soru |
|---|---|
| [Ablation.cs:26–32](../../src/Fraud.Evaluation/Running/Ablation.cs) | `AblationRow` üç şey taşıyor: `Report` (satırın kendi eşiği), `AtFullThreshold` (TOPLAM(hepsi)'nin eşiği) ve `AtFullThresholdSkipped` (sabit eşikli sürüm uygulanmadıysa gerekçe). TOPLAM satırında ikisi neden `null`? Bir satırda ikisi birden dolu olabilir mi? |
| Ablation.cs:34–42 | Ablasyon hangi satırları üretiyor? Neden yalnızca geçmiş veri testinde? |
| Ablation.cs:53 | "Şimdi" neden bir kez alınıp bütün satırlara veriliyor? Her satır kendi saatini alsaydı ne değişebilirdi? |
| Ablation.cs:54–65 | **"TOPLAM − x" ve `PartCount`.** Her satır artık kaç parçadan oluştuğunu taşıyor: Tek sinyal satırı 1, TOPLAM `parts.Count`, "TOPLAM − x" `parts.Count − 1`. Bu sayı yalnızca yapıdan gelir. Aynı zamanda satırın alabileceği en yüksek puandır, çünkü her parça 0 ya da 1 verir (:57). Çıkarılmış toplamlar nasıl kuruluyor? `ReferenceEquals` (:64) neden ada göre karşılaştırmaktan daha güvenli? Tek parçada neden "TOPLAM − x" satırı yok? |
| Ablation.cs:67–69 | TOPLAM(hepsi) neden önce çalışıyor? |
| Ablation.cs:71–72 | **`SkipReason`** artık yalnızca `FixedThresholdSkipReason(partCount, full.Threshold)` çağırıyor. |
| Ablation.cs:84–98 | **`FixedThresholdSkipReason`.** Kural: `partCount < fullThreshold` ise gerekçe, değilse `null`. Neden `<`, neden `≤` değil? (Bölüm 2.15, not 5.) Neden yalnızca parça sayısına ve eşiğe bakıyor? (Bölüm 2.14, not 3.) `public static` olması neyi kolaylaştırıyor? (Doğrudan test edilebiliyor: ThresholdTests.cs:248–266.) |
| Ablation.cs:74–81 | Her satır kendi eşiğiyle bir kez çalışıyor. Sabit eşikli ikinci çalıştırma yalnızca gerekçe `null` ise yapılıyor. Yoksa rapor yerine gerekçe saklanıyor. TOPLAM satırı bir kez çalışıyor. Aynı bölme, aynı "şimdi", aynı veri. |
| [EvaluationRunner.cs:273–274, :288–291](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | Sabit eşik neden yalnızca geçmiş veri testinde kabul ediliyor? ([ThresholdTests.cs:298–301](../../tests/Fraud.Signals.Tests/ThresholdTests.cs)) |
| Ablation.cs:108–124 | Tablo hangi "tanım gereği" notlarını basıyor ve hangi koşulda? |
| Ablation.cs:126 | "Eşik 1'den büyükse sinyal hiç alarm vermez" notu ne demek? Notta neden "G4 ayar kontrolleri" yazıyor? |
| Ablation.cs:127–128 | **İki farkın açıklaması.** Kendi cümlelerinle yeniden yaz. |
| Ablation.cs:137–149 | İki fark nasıl basılıyor? İkinci farkın yanında neden ayrıca recall yazılıyor? Sabit eşikli rapor yoksa ne basılıyor (:145–148)? |
| Ablation.cs:152–157 | Tabaka satırları ana raporla **aynı biçimleyiciyi** (`StratumLabel`, `FormatStratum`) kullanıyor. Neden önemli? (Ablasyon tablosu G2'nin "yeterli örnek yok" kuralını ve Türkiye alt kümesinin tümleyen kuralını delmesin. Test: G2ReportTests.cs:102–106.) |
| Ablation.cs:164–179 | Fark nasıl hesaplanıyor? Küçük hücrede neden fark da yazılmıyor? Fark bir sayıyı nasıl açığa çıkarabilirdi? |
| [EvaluationConfig.cs:268–272](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Ablasyon kilitli kipte neden reddediliyor? Kilitli testte birden çok sinyal denemek hangi kuralı bozardı? |
| [ThresholdTests.cs:103–128](../../tests/Fraud.Signals.Tests/ThresholdTests.cs) | İki parça, TOPLAM eşiği 1'in üstünde. Şimdi "TOPLAM − x" satırlarında da gerekçe bekleniyor (:118–122). Eskiden bu test tam tersini bekliyordu. :123–124'teki `^` (özel veya) neyi şart koşuyor? (Her satırda ya rapor ya gerekçe var, ikisi birden ya da hiçbiri değil.) |
| ThresholdTests.cs:130–136 | `ConstSignal` her birime aynı puanı veriyor. TOPLAM eşiğini istenen yere koymak için neden işe yarıyor? |
| ThresholdTests.cs:199–221 | İki sabit 1 + baseline: G4'ün tamamı 2 alıyor, eşik `BitIncrement(2)` (:214). Neden hem tek parça hem 2 parçalı satırlar "uygulanmaz"? |
| ThresholdTests.cs:223–246 | Dört parça, aynı eşik: "TOPLAM − x" satırlarında 3 parça kalıyor ve çalışıyor. Tek parça satırları yine "uygulanmaz". |
| ThresholdTests.cs:248–266 | Sınır örnekleri: (3, 3.0) çalışır, (2, 3.0) atlanır, (2, `BitIncrement(2)`) atlanır, (3, `BitIncrement(2)`) çalışır. Her birini kâğıtta doğrula. |
| ThresholdTests.cs:276–296 | İki parça ama TOPLAM eşiği 1 ya da altında: Bu kez "TOPLAM − x" satırları çalışmalı. Bu test neyi kanıtlıyor? ("Uygulanmaz" kararı iki parça olmaktan değil, eşikten geliyor.) |
| [AblationTests.cs:53–67, :69–83](../../tests/Fraud.Signals.Tests/AblationTests.cs) | Ablasyon çıktısında neden hiçbir ad geçmiyor? Satır sırası nasıl sabitleniyor? Baseline recall'u neden yaklaşık %50 bekleniyor? |

**İki fark neden gerekli?** Önce iki soruyu ayır:

- **Kendi eşiğiyle fark.** Soru: "Bu sinyal olmasaydı, **aynı yanlış alarm bütçesiyle** (G4'te %1) ne kadar yakalardık?" Bu adil karşılaştırmadır. Yol haritası sonuçları sabit bir yanlış alarm düzeyinde istiyor. Ama fark iki şeyi birlikte taşır: sinyalin kendi katkısı ve eşiğin kayması. Gürültülü bir sinyali çıkarınca eşik düşebilir, öteki sinyallerin alarmları sayılmaya başlar ve recall **artabilir**. Fark pozitif çıkar.
- **TOPLAM eşiğiyle fark.** Soru: "Eşik yerinde dursa, bu sinyalin puanı çıkınca kaç alarm kaybolur?" Bu, sinyalin alarmlara doğrudan katkısıdır. "TOPLAM − x" her birimde puanı ya aynı bırakır ya 1 düşürür. Eşik sabitse alarm sayısı yalnızca azalabilir. Bu fark **hiçbir zaman pozitif olamaz**. Ama bu satırın yanlış alarm oranı artık %1 değil, daha düşük. Yani farklı yanlış alarm düzeyinde bir karşılaştırma bu.

Tek başına hiçbiri doğru hikâyeyi anlatmaz. Yalnızca birincisine bakarsan, eşik kaymasını sinyalin katkısı sanırsın. Yalnızca ikincisine bakarsan, her sinyal "katkı veriyor" görünür, çünkü fark negatiften başka bir şey olamaz. İkisi birlikte okununca ayrım yapılabilir: Sabit eşikte büyük kayıp, kendi eşiğinde küçük kayıp ya da kazanç varsa, sinyal alarmlara katkı veriyor ama gürültüsü eşiği yukarı itiyor demektir. Alıştırma B9 bunu sayılarla gösteriyor.

**Bir satır ne zaman "uygulanmaz" alır?** (2026-11-24, iki adımda.) k parçalı bir satır en çok k puan alır. TOPLAM eşiği k'den büyükse satır o eşiğe hiçbir zaman ulaşamaz. Recall her durumda 0 çıkar, fark da her durumda "−TOPLAM recall" olur. Bu sayı sinyal hakkında hiçbir şey söylemez. Basılsaydı okuyan onu "bu sinyali çıkarmak 40 puan kaybettiriyor" diye yanlış okurdu. Bu yüzden rapor sayı yerine gerekçe yazıyor, örneğin "TOPLAM eşiğiyle uygulanmaz: 2 parçalı satır en çok 2 puan alır, TOPLAM eşiğine ulaşamaz".

İlk düzeltme yalnızca tek parça satırlarını kapsıyordu. İki parçalı bir koşuda "TOPLAM − x" satırı da aslında tek parçaydı, ama elle verilen bir bayrak yüzünden sayı yine basılıyordu. İkinci düzeltme kuralı "parça sayısı < TOPLAM eşiği" olarak genişletti. Kadircan bu genişletmeyi **hiçbir sonuç görülmeden** onayladı ([DECISIONS.md](../DECISIONS.md), "2026-11-24: G2 kuralları raporda uygulanır, ablasyonda tek parça kuralı düzeltildi", madde 4). Bu önemli: Bir sunum kuralı sonuca bakılarak değişseydi, kötü görünen sayıları gizlemenin bir yolu olurdu.

### 2.11 Yapılandırma, katalog ve komutlar

| Yer | Soru |
|---|---|
| [EvaluationConfig.cs:47–80](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Bilinen sinyal adları ve varsayılan parçalar hangileri? Numaralı ad neden varsayılan toplamda yok? `DomesticBrandsSha256` ne için var (Bölüm 2.12)? |
| EvaluationConfig.cs:82–83 | `UsesBrand` ne zaman doğru? |
| EvaluationConfig.cs:85–100 | `sum` ve `random` neden parça olamaz? Aynı parça iki kez neden yasak? Sayı sınırları ne? Özet neden tam 64 küçük harf onaltılık karakter olmalı (:94–95)? |
| [SignalCatalog.cs:9–12, :17–20](../../src/Fraud.Signals/SignalCatalog.cs) | Katalog neden dosya okumuyor? Hangi sinyal hangi kaynağa ihtiyaç duyuyor? |
| SignalCatalog.cs:23–47 | Ad nesneye nasıl çevriliyor? Kaynak yoksa ne oluyor? |
| SignalCatalog.cs:49–59 | Parçalar ne zaman okunuyor? Tek bir `baseline` çalıştırması neden marka listesini istemiyor ([AblationTests.cs:95–108](../../tests/Fraud.Signals.Tests/AblationTests.cs))? |
| [EvaluationCommands.cs:58–59, :71–76](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs) | Yapılandırma ve kaynaklar neden kilitten **önce** okunuyor? |
| [SignalCommands.cs:42–56](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs) | Sözlük donmuş sürümle aynı değilse ne oluyor? |
| SignalCommands.cs:166–207 | `signals-brands` listeyi nereden üretiyor? Aynı veriyle iki kez çalışınca neden aynı dosya çıkıyor? Geçici dosya neden kullanılıyor? Bu komut kilitli testi etkileyebilir mi? (Artık hayır. Bölüm 2.12.) |
| EvaluationCommands.cs:130–136 | Ablasyon çalıştırması normal çalıştırmadan nerede ayrılıyor? |

### 2.12 Marka listesi kilide bağlı (S1, kapandı)

**Hangi saldırıyı engelliyor?** Yerli marka listesi `data/private/signals/brands.txt` içinde duruyor. Git dışında (Kural 8). Eskiden kilitli test bu dosyayı okuyordu. Şu senaryoyu düşün: Kurallar dondurulur (F). Sonra biri, sen dahil, test dönemindeki pozitif adları görür ve dosyaya onlara benzeyen bir belirteç ekler. Ya da `signals-brands`'i farklı bir G1 ile yeniden çalıştırır. Kod commit'i aynı, yapılandırma aynı, kilit kaydı aynı görünür. Kilitli test sonucu şişer ve hiçbir kayıt bunu göstermez. Bu, "sonuca bakıp kural değiştirmek"in **görünmez** halidir. Marka listesi sinyal kuralının bir parçasıdır, kodun kendisi kadar.

Engellemek için iki şey gerekiyor: Liste dondurma anındaki listeyle aynı olmalı ve kullanılan liste kayda geçmeli.

| Yer | Ne yapıyor, ne soracaksın |
|---|---|
| [SignalCommands.cs:25–34](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs) | Yorumu oku. "Taahhüt değişirse açık dosya değişir; liste değişirse özet tutmaz." Bu iki yarım cümle hangi iki yolu kapatıyor? |
| SignalCommands.cs:63–74 | Geçmiş veri testinde liste hâlâ dosyadan okunuyor. Neden bu kabul edilebilir? |
| SignalCommands.cs:75–83 | Kilitli ve ileriye dönük kipte dosya **hiç okunmuyor**. Liste `CommittedG1LabelsAsync`'ten geliyor. |
| SignalCommands.cs:124–130 | İki dosya gerekiyor: repodaki açık taahhüt dosyası ve `data/private` altındaki açıklama dosyası. Biri yoksa ne oluyor? |
| SignalCommands.cs:132–147 | Açıklama dosyası açık taahhüde karşı doğrulanıyor (Hafta 6: Merkle kökü). Açıklama dosyasında bir ad değiştirilirse ne olur? (Test: EvaluationLoaderTests.cs:331–333.) |
| SignalCommands.cs:149–159 | Taahhütteki kayıtlar, veritabanındaki taahhütlü G1 üyeleriyle **birebir** aynı olmalı (`SetEquals`). Neden "içeriyor" değil de "eşit"? |
| SignalCommands.cs:161, :164 | Etiketler nasıl çıkarılıyor? Neden sıralı ve tekrarsız? |
| SignalCommands.cs:102–112 | **Kurallı özet.** Kırpma, boş ve `#` satırlarını atma, tekrarsız, sıralı, her satır `\n` ile. Bu kurallar neden var? Dosyanın düz SHA-256'sını almak neden yetmezdi? |
| SignalCommands.cs:85–91 | Kilitli kipte özet, yapılandırmadaki `signal.domesticBrandsSha256` ile aynı değilse çalışma durur. |
| [EvaluationConfig.cs:75–80, :274–278](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Marka sinyali kullanılan kilitli ya da ileriye dönük testte bu alan zorunlu. Yapılandırma dosyasının kendi SHA-256'sı da kilit kaydına yazılıyor ([EvaluationCommands.cs:94–97](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)). Bu, özeti iki kez bağlamak demek. Neden? |
| [LockedRunGuard.cs:6–9, :95, :118](../../src/Fraud.Evaluation/Running/LockedRunGuard.cs) | `BrandListSha256` hem ilk çalıştırmanın hem yeniden çalıştırmanın kaydına giriyor. Bir yeniden çalıştırma farklı listeyle yapılırsa kayıtta nasıl görünür? |
| EvaluationCommands.cs:71–76 | Bütün bu denetimler kilitten **önce**. Tutmayan özet neden tek hakkı harcamamalı? |
| [EvaluationLoaderTests.cs:275–335](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs) | Saldırı satırını bul (:301–304). Kilitli kipte eklenen marka neden listeye girmiyor (:310–313)? Taahhüt dışı bir G1 üyesi eklenince ne oluyor (:324–329)? Son satırdaki Kural 4 denetimi ne arıyor? |
| EvaluationLoaderTests.cs:337–354 | Özet olmadan kilitli kip neden başlamıyor? Geçmiş veri testinde neden zorunlu değil? |

**Neden ölçüm dürüstlüğü için önemli?** "Kilitli test bir kez çalışır, sonuca göre kural değişmez" kuralı, kuralın bütün parçaları kayıtlıysa anlamlıdır. Kod commit'e bağlıydı, yapılandırma SHA'sı kayda bağlıydı, ama liste değildi. Şimdi listenin kaynağı (G1 taahhüdü) dondurmadan önce yayımlanmış ve değiştirilemez durumda. Kullanılan listenin özeti de kayıtta.

**Hâlâ kapsamadığı şeyler:** G1 taahhüdünün gerçek BDDK listesini doğru yansıtıp yansıtmadığı ayrı bir sorudur. Bu kilit yalnızca "dondurmadan sonra değişmedi" der. Yabancı liste derlemeye gömülü. Onu kod commit'i bağlıyor, bu özet değil.

### 2.13 G2: Radar'dan Wikidata'ya

**Neden taşındı?** G2'nin (finans konuşan meşru siteler) ilk yolu bir trafik sıralaması ile Cloudflare Radar kategorisiydi. Önceden yazılmış bir önkoşul vardı: Tohumlu 100 alan adının en az %80'inde kategori dönmeli. Sonuç 14/100 çıktı, hata 0. Deneme kullanılmış sayıldı ve tekrarlanmadı. Önceden kayıtlı B seçeneğine, Wikidata'ya geçildi ([KONTROL_GRUBU_YONTEMI.md](../research/KONTROL_GRUBU_YONTEMI.md), 2026-11-21 değişiklik kayıtları; [G2_WIKIDATA_PLANI.md](../research/G2_WIKIDATA_PLANI.md) Bölüm 1). G2 eşiğe girmez (Bölüm 2.9). Yalnızca seçilen eşikte ayrı bir yanlış alarm satırı verir.

| Yer | Ne yapıyor, ne soracaksın |
|---|---|
| [g2_wikidata_query.rq:1](../evaluation/g2_wikidata_query.rq) | İlk satır tohum: `#tohum: kontrol-g2-wikidata-2026-11-21`. Tohum neden dosyanın **içinde**? |
| g2_wikidata_query.rq:5–7, :8–23 | Kapsam: konusu iktisat ya da finans olan gazete ve dergiler, iş gazeteleri, resmi web sitesi. Eğitim kolu neden çıkarıldı? Bitmiş ve kullanımdan kalkmış adresler nasıl eleniyor (:20–21)? |
| g2_wikidata_query.rq:22 | `BIND(EXISTS { ... } AS ?tr)`: Türkiye bağı bir **sütun**, `FILTER` değil. Fark ne? |
| [G2Wikidata.cs:24–48](../../src/Fraud.Collectors/Control/G2Wikidata.cs) | Dosya nasıl ayrıştırılıyor? SHA-256 dosyanın **bütün baytları** üzerinden (yorumlar ve tohum dahil). Sorguyu ya da tohumu değiştirmek özeti ne yapar? |
| [.gitattributes:14–15](../../.gitattributes) | Dosya her makinede LF satır sonuyla çıkıyor. Windows'ta CRLF olsaydı özet ne olurdu? |
| [ControlSources.cs:95–129](../../src/Fraud.Collectors/Control/ControlSources.cs) | Sorgu dosyası git'te izlenmeli ve çalışma ağacı temiz olmalı. Neden? |
| Commit 9e8461f mesajı | Sabitlenen sorgu özeti: `4b6f1ed975d84c59ef55233c979fd6e74e5b91692112642d49ca2aab6b79ab92`. Kendin doğrula: PowerShell'de `Get-FileHash docs/evaluation/g2_wikidata_query.rq -Algorithm SHA256` (çıktı büyük harfle gelir, karşılaştırırken harf farkını yok say). Program da her çalıştırmada bunu basar (G2Wikidata.cs:440–442). |
| G2Wikidata.cs:203–213 | **Sabitler.** En çok 3 deneme. Denemeler arası en az 60 sn. 429 (hız sınırı) sonrası 5 dk. 100'den az üye "yeterli örnek yok". |
| G2Wikidata.cs:248–262 | Başarılı bir cevap varsa yeni istek **atılmıyor**. Mesajı oku: "sonuç beğenilmedi diye yeniden sorulmaz". 3 deneme dolduysa? |
| G2Wikidata.cs:264–276 | Bekleme süresi neye göre hesaplanıyor? Son deneme 429 aldıysa neden daha uzun? |
| G2Wikidata.cs:278–291 | **"Başladı" kaydı istekten önce**, `CreateNew` ile. Yarıda kesilen deneme neden "kullanıldı" sayılıyor? İki süreç aynı anda başlarsa ne olur? (Hafta 6'daki kilit dosyasıyla aynı fikir.) |
| G2Wikidata.cs:293–315 | Tek istek. Ham cevap **her durumda** saklanıyor, SHA-256'sı alınıyor. Radar denemesinden hangi ders çıkarıldı? |
| G2Wikidata.cs:446–472 | Okunamayan deneme kaydı ne oluyor? Hak neden geri gelmiyor? |
| G2Wikidata.cs:340–365 | `control-build-g2-wikidata` ağa çıkmıyor. Girdi dosyasının SHA-256'sı başarılı bir denemenin kaydındaki özetle **ve** o denemenin sorgu özeti donmuş sorguyla aynı olmalı. Elle düzenlenmiş bir cevap neden seçime giremez? |
| G2Wikidata.cs:57–115 | Cevap güvenilmez veri (Kural 10). Boyut, derinlik ve satır sınırları neler? Geçersiz satır ne oluyor? |
| G2Wikidata.cs:117–133 | `HostOf` hangi adresleri reddediyor? Neden IP adresi ve kullanıcı bilgili adres yok? |
| G2Wikidata.cs:373–390 | Türkiye etiketi kayıtlı alan adı başına VEYA'lanıyor. Neden? |
| G2Wikidata.cs:392–397, [ControlSelection.cs:14–23](../../src/Fraud.Collectors/Control/ControlSelection.cs) | Seçim: elemeler, sonra `SHA-256(tohum + "|" + ad)` sırası, ilk 300. Neden rastgele sayı üreteci yok? |
| G2Wikidata.cs:408–421 | 100'den az kalırsa ne oluyor? Grup yine kuruluyor mu? `enough_for_rates` nereye yazılıyor? Değerlendirme raporu bu manifest bayrağını okuyor mu? (Hayır, bilerek okumuyor. Aynı kuralı veritabanındaki kurulum kaydından okuyor. Bölüm 2.15, not 1.) |
| G2Wikidata.cs:168–184 | İstemci: otomatik yeniden deneme yok, yönlendirme izlenmiyor, User-Agent'ta kişisel e-posta değil projenin açık depo adresi var. Neden? |
| [G2WikidataTests.cs:89, :151, :178, :205](../../tests/Fraud.Integration.Tests/Control/G2WikidataTests.cs) | Dört testin adını oku. Her biri hangi kuralı sabitliyor? |

**Neden ölçüm dürüstlüğü için önemli?** Wikidata canlı veridir. Aynı sorgu yarın başka sonuç verebilir. Bu yüzden "hangi listeyle çalıştık" sorusunun tek dürüst cevabı: donmuş sorgu özeti + tohum + ham cevabın özeti + UTC anı. 3 deneme sınırı ve "başarılı cevaptan sonra yeni istek yok" kuralı, "beğenmediğim listeyi yeniden çek" yolunu kapatır. Türkiye bağının süzgeç değil etiket olması, önceden yazılmış kapsamı sonradan daraltmamak içindir. 100 altı kuralı da küçük bir gruptan oran çıkarıp sonucu abartmamak içindir. Sınır: Özet dosyanın değişmediğini kanıtlar, Wikidata'daki bilginin doğru olduğunu kanıtlamaz.

### 2.14 Dört C# okuma notu (2026-11-24 düzeltmeleri)

**Not 1. `record with { }` ile kopya.** [EvaluationRunner.cs:327–328](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)

```csharp
return Report(...)
    with { CalibrationComposition = CompositionOf(calibration) };
```

`EvaluationReport` bir `record`. `with` ifadesi var olan nesneyi **değiştirmez**. Onun bir kopyasını çıkarır ve süslü parantezdeki özellikleri kopyada başka değerle doldurur. `CalibrationComposition` yalnızca `init` ile atanabiliyor (:47). Yani nesne kurulduktan sonra `rapor.CalibrationComposition = ...` yazamazsın, derleyici izin vermez. `with` bunu yapmanın tek yolu.

Neden böyle yazılmış: `Report(...)` yardımcısını hem `Run` hem `RunProspective` kullanıyor. İleriye dönük testte eşik yok, dolayısıyla eşik kümesi de yok. Orada döküm varsayılan boş listede kalıyor (:47, `= []`). Yardımcının imzasına yeni bir parametre eklemek yerine, yalnızca `Run` kopyaya dökümü ekliyor.

Dikkat: Kopya **sığdır** (shallow). Listeler kopyalanmaz, iki nesne aynı listeyi gösterir. Burada sorun değil, çünkü listeler `IReadOnlyList` ve kimse onları değiştirmiyor.

**Not 2. `CompositionText` içindeki iç içe üçlü işleç.** [EvaluationRunner.cs:151–156](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)

```csharp
p.Count < SmallCellLimit ? Cell(p.Count) : anySmall ? "-" : Cell(p.Count)
```

Üçlü işleç sağdan gruplanır. Yani bu satır `a ? b : (c ? d : e)` diye okunur. Bunu küçük bir tablo gibi oku:

| Bu sayı 5'ten küçük mü? | Öteki sayılardan biri 5'ten küçük mü (`anySmall`)? | Yazılan |
|---|---|---|
| Evet | (fark etmez) | `<5` (`Cell` bunu basar) |
| Hayır | Evet | `-` |
| Hayır | Hayır | sayının kendisi |

Neden ikinci satır var: Toplam `n` raporda zaten açık yazılı (:70). G4a "<5" diye gizlenip G4b açık yazılsaydı, okuyan `n − G4b` işlemiyle gizli sayıyı bulurdu. Buna **tümleyen kuralı** denir. Recall'daki `IsSmallCell` (:223–227) aynı fikri izliyor: "11/12" de tek kalan birimi ele verir. Kendini sına: (3, 200) için çıktı ne? Cevap: `G4a=<5, G4b=-` (ThresholdTests.cs:194).

Okunabilirlik notu: İç içe üçlü işleç kısa ama okunması zor. Mülakatta "bunu nasıl daha okunur yazardın" diye sorulursa bir `switch` ifadesi ya da yukarıdaki tablo gibi üç kollu bir `if` öner. Aynı kalıp, Türkiye alt kümesinin birim sayısında da var ([EvaluationRunner.cs:196–198](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Bölüm 2.15, not 3.

**Not 3. "Uygulanmaz" kararı neden yalnızca eşiğe ve parça sayısına bakıyor?** [Ablation.cs:71–72, :84–98](../../src/Fraud.Evaluation/Running/Ablation.cs)

`SkipReason` bir yerel fonksiyon. Dışarıdaki `full` değişkenini kullanıyor ve işi `FixedThresholdSkipReason(partCount, full.Threshold)`'a bırakıyor. O da yalnızca iki sayıya bakıyor: satırın parça sayısı ve TOPLAM eşiği. Hiçbir recall'a, hiçbir yanlış alarm sayısına bakmıyor. Bunun iki nedeni var:

1. **Mantık gereği kesin.** k parçalı satır en çok k puan alır. k eşikten küçükse satır eşiğe asla ulaşamaz. Bunu bilmek için sonuca bakmaya gerek yok.
2. **Sonuca göre sunum seçilmesin.** Koşul recall'a bakan bir kural olsaydı ("recall 0 ise yazma" gibi), neyin yazılıp neyin gizleneceği sonucu gördükten sonra seçilmiş olurdu. Bu da "sonuca göre kural değiştirme"nin raporlamadaki halidir. Eşik ise yalnızca G4 ayar kontrollerinden seçiliyor. Test verisi ona hiç dokunmuyor. Yorum bunu açıkça yazıyor: "Yalnızca parça sayısına ve eşiğe bakar, sonuca bakmaz" (:85–86).

Not: `SkipReason` satır başına iki kez çağrılıyor (:78 ve :81). Saf bir fonksiyon olduğu için iki çağrı hep aynı cevabı verir. Sonucu bir değişkene atamak daha temiz olurdu, ama doğruluk değişmez. `<` ile `≤` farkı Bölüm 2.15, not 5'te.

**Not 4. Yükleyici testindeki enum döngüsü neden bilerek kırılır?** [EvaluationLoaderTests.cs:427–437](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs)

```csharp
foreach (var group in Enum.GetValues<Fraud.Domain.Control.ControlGroup>())
{
    _ = EvaluationDataLoader.GroupOf(group);
}
```

`Enum.GetValues` o an tanımlı **bütün** grupları verir. Bugün hepsi `GroupOf` içinde açıkça eşlendiği için döngü sessizce geçer. Yarın biri `ControlGroup`'a yeni bir grup ekleyip `GroupOf`'a eklemeyi unutursa, `GroupOf` o grup için hata fırlatır ve bu test düşer. Bu bir **tuzak teli**: Yeni grubu ekleyen kişiyi "bu grup ölçümde hangi gruba girecek, eşiğe girecek mi" sorusunu açıkça cevaplamaya zorluyor. Eskiden cevap kendiliğinden "G4" oluyordu. Yeni grup da sessizce eşik kümesine giriyordu.

Aynı testin ilk kısmı (:430–431), tanımlı olmayan bir değerin (99) reddedildiğini ve hata mesajında yalnızca sayının geçtiğini denetliyor. Mesajda alan adı yok (Kural 4).

Neden derleyici yakalamıyor: `_ =>` kolu yoksa C# derleyicisi eksik durum için yalnızca uyarı verir. Burada ise `_ =>` kolu bilerek var, çünkü tanınmayan değerde hata fırlatıyor. Bu kol varken derleyici yeni bir grubun unutulduğunu hiç fark edemez. Emniyeti bu yüzden test ve çalışma anındaki hata sağlıyor.

### 2.15 G2 kuralları raporda ve eşik sınırı (2026-11-24, ikinci kayıt)

Önce bağlam: G2 kurulumunda iki önceden kayıtlı kural vardı. "100'den az üye kalırsa oran verilmez" ve "Türkiye bağı süzgeç değil etiket, alt küme ayrıca raporlanır." Bunlar kurulumda yazılıyordu ama değerlendirme raporu ikisini de uygulamıyordu. Artık uyguluyor ([DECISIONS.md](../DECISIONS.md), "2026-11-24: G2 kuralları raporda uygulanır, ablasyonda tek parça kuralı düzeltildi"). G2 eşiğe eskisi gibi girmiyor.

**Not 1. "Yeterli örnek yok" bayrağı veritabanından, manifestten değil.** [EvaluationDataLoader.cs:174–193](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs), kullanıldığı yer :64 ve :74–76.

`G2BelowMinimumAsync`, `control_builds` tablosundaki G2 kurulum kayıtlarının `Selected` (seçilen üye) sayısını okuyor. Kural iki durumda "yetersiz" diyor (:192): Hiç kurulum kaydı yoksa ya da herhangi bir kayıtta seçilen 100'den azsa. Neden manifest dosyası değil? Yorumda (:179–184) üç neden var:

1. Kurulum kaydı üyelerle **aynı veritabanı işleminde** yazılır. Üye varsa kayıt da vardır. İkisi birbirini tutar.
2. Manifest bu işlemden **sonra** ayrı bir dosyaya yazılır. Arada süreç durursa manifest hiç olmayabilir.
3. Manifest `data/private` altında bir dosya. Elle değiştirilebilir.

Kayıt yoksa ama G2 üyesi varsa (elle eklenmiş üye), kural "yetersiz" der. Bu **tutucu taraf**: Kuralın geçtiği kanıtlanamıyorsa oran basılmaz. Eşik sabiti de tek yerden geliyor: `G2WikidataCommands.MinimumForRates` (G2Wikidata.cs:213). Kurulum ile rapor aynı sayıyı kullanıyor. Test: [EvaluationLoaderTests.cs:439–479](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs), üç durum: 99 (yetersiz), 100 (yeterli), kayıt yok (yetersiz). Aynı test G4 biriminin bayraktan etkilenmediğini de denetliyor (:476–478).

**Not 2. Oran kaynağında hiç üretilmiyor.** [MetricsCalculator.cs:94–102, :144–152](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs)

Tabakadaki tek bir birim bile işaretliyse (`members.Any(...)`, :97), tabaka `Withheld` ile kuruluyor. `Withheld` ham sayıları tutuyor ama `Rate`, aralıklar, precision ve günlük alarm `null` (:148). Neden raporda gizlemek yerine burada? Çünkü oran bir kez hesaplanırsa, onu okuyan her yer (ana rapor, ablasyon tablosu, ileride bir panel ya da dışa aktarma) gizlemeyi ayrı ayrı hatırlamak zorunda kalır. Biri unutursa oran sızar. Kaynakta hiç üretilmeyen bir sayıyı kimse yanlışlıkla basamaz. Rapor tarafı da ayrıca bu satırlarda yalnızca "yeterli örnek yok" ve birim sayısını yazıyor ([EvaluationRunner.cs:103–107, :185–187](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Test: [G2ReportTests.cs:79–107](../../tests/Fraud.Evaluation.Tests/G2ReportTests.cs). Bu test ablasyon tablosunu da denetliyor.

Ham isabet sayısı (`Hits`) kayıtta duruyor ama raporda basılmıyor. Yalnızca birim sayısı basılıyor, o da küçük hücre kuralıyla (`Cell`).

**Not 3. Türkiye alt kümesi: tümleyen kuralı ve ayrı tohum eki.** [MetricsCalculator.cs:120–142](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs), [EvaluationRunner.cs:169–210](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)

G2 tabakasından hemen sonra bir satır daha geliyor: yalnızca `TurkeyLinked == true` olan birimler. Bu satırda precision ve günlük alarm yok (:141). Alt küme bir tahmin tabanı değil, yalnızca kapsamı gösteriyor.

*Tümleyen kuralı.* Raporda G2'nin tamamı zaten yazılı. Alt küme de yazılınca okuyan çıkarma yapabilir: "tamamı − alt küme = Türkiye'ye bağlı olmayanlar". Alt küme büyük görünse bile tümleyen küçük olabilir. `FormatStratum` bunu şöyle engelliyor:
- Tümleyeni kuruyor: isabet = tamamın isabeti − alt kümenin isabeti, birim = tamamın birimi − alt kümenin birimi (:191–194).
- Alt küme **ya da** tümleyen küçük hücreyse hiçbir sonuç sayısı yazmıyor, yalnızca birim sayısı (:204–207). "≥36/40" biçimindeki kuyruk da yazılmıyor, çünkü o da tümleyenin küçük olduğunu ele verir.
- Birim sayısı da aynı kurala uyuyor (:196–198). Tümleyenin birim sayısı 5'ten azsa alt kümenin birim sayısı "-" yazılıyor. Burada da Bölüm 2.14, not 2'deki iç içe üçlü işleç var.

Elle örnek (G2ReportTests.cs:121–126): Tamamı 30/100, alt küme 27/60. Tümleyen 3/40. Alt küme "27/60" yazılsaydı 30 − 27 = 3 geri bulunurdu. Bu yüzden yalnızca "test birimi=60" yazılıyor.

*Ayrı tohum eki.* Alt kümenin bootstrap tohumu, tamamın tohumuna `-tr` eklenerek kuruluyor (:139–140). Neden? Tohum, `Rate` içinde `BootstrapSeed + "|" + seedSuffix` diye birleşiyor (:188). Alt küme kendi ekini almasaydı iki seçenek vardı: Ya tamamla aynı tohumu kullanırdı (iki satırın yeniden örneklemesi birbirine bağlanırdı), ya da tamamın tohumu değiştirilirdi. Bu da daha önce hesaplanmış bütün G2 aralıklarını değiştirirdi. Yeni bir ek vermek, eski sonuçları bayt bayt aynı bırakıyor. Yorum da bunu söylüyor: "Tamamının tohumu değişmez, eski sonuçlar aynı kalır."

**Not 4. `Subject` içinde `Group`'u okuyan `init` erişimcisi.** [Subject.cs:149–185](../../src/Fraud.Evaluation/Model/Subject.cs)

```csharp
public bool? TurkeyLinked
{
    get => _turkeyLinked;
    init
    {
        if (value is not null && Group != SubjectGroup.ControlFinanceNews) throw ...;
        _turkeyLinked = value;
    }
}
```

`init`, özelliğin yalnızca nesne kurulurken atanabileceği anlamına geliyor: kurucuda ya da `new Subject(...) { TurkeyLinked = true }` gibi bir nesne başlatıcısında. Burada `init` bloğu bir doğrulama yapıyor ve `Group`'u okuyor. Bu neden güvenli? Çünkü nesne başlatıcısı (süslü parantezli kısım) kurucudan **sonra** çalışır. Kurucu `Group`'u zaten doldurmuştur (:110). Yorum bunu açıkça yazıyor (:158).

Ne kazanıyoruz: Türkiye etiketi G2 dışında bir birime **hiç** konamıyor. Yanlış etiketli bir birim ölçüme giremiyor (Türkiye alt kümesi yanlışlıkla G4'te bir satır üretemez). `BuildBelowMinimum` aynı kalıbı kullanıyor: Pozitif bir birime konamıyor (:176–181). Testler: [G2ReportTests.cs:144–151](../../tests/Fraud.Evaluation.Tests/G2ReportTests.cs).

Dikkat edilecek iki ayrıntı. (1) Değer bir alanda (`_turkeyLinked`) tutuluyor, çünkü `init` bloğu olan bir özellik otomatik özellik olamaz. (2) `with` ile kopyalarken de `init` çalışır. Yani `subject with { TurkeyLinked = true }` bir G4 biriminde de hata fırlatır. Doğrulama kopyada da korunuyor.

**Not 5. Neden `<`, neden `≤` değil?** [Ablation.cs:88–96](../../src/Fraud.Evaluation/Running/Ablation.cs)

Alarm kuralı "puan ≥ eşik" ([Predictor.cs:40](../../src/Fraud.Evaluation/Running/Predictor.cs)). `ThresholdSelector` iki türlü eşik döndürebiliyor (Predictor.cs:75): Ya ayardaki bir puanın kendisi (ör. tam 3) ya da bir puanın hemen üstündeki `double` (`Math.BitIncrement(2)` ≈ 2,0000000000000004, yani "en az 3 parça"). k parçalı satır en çok k puan alır. Satır eşiğe ancak k ≥ eşik ise ulaşabilir. "Ulaşamaz" demek k < eşik demek.

| k | Eşik | `k < eşik`? | Sonuç |
|---|---|---|---|
| 3 | 3,0 | Hayır | Çalışır. 3 puanlık birim 3 ≥ 3 ile alarm verir. |
| 2 | 3,0 | Evet | Uygulanmaz. |
| 2 | `BitIncrement(2)` | Evet | Uygulanmaz. 2, 2,0000000000000004'ten küçük. |
| 3 | `BitIncrement(2)` | Hayır | Çalışır. |
| 1 | 1,0 | Hayır | Çalışır. |

`≤` kullanılsaydı, k = 3 ve eşik tam 3 olan satır "uygulanmaz" sayılırdı. Oysa o satır eşiğe ulaşabilir ve anlamlı bir fark verir. Bilgi taşıyan bir sayı yanlışlıkla gizlenirdi. Testler: [ThresholdTests.cs:248–266](../../tests/Fraud.Signals.Tests/ThresholdTests.cs). Kayan nokta karşılaştırması burada güvenli, çünkü k bir tam sayı ve `BitIncrement` en küçük adımı atıyor. Hiçbir tam sayı 2 ile `BitIncrement(2)` arasına düşemez.

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

**B5. Patlama sayımı.** Görünüm 10 Nisan sonunda. Sertifika gözlemleri (bilinme günü, veriliş kimliği): 1 Mart c-1; 20 Mart c-2; 25 Mart c-3; 2 Nisan c-4; 5 Nisan c-5; 6 Nisan c-4 (aynı veriliş ikinci kez yüklenmiş).
(a) Pencerenin alt sınırı hangi an? Pencerede kaç farklı veriliş var? Sinyal kaç verir?
(b) 9 Nisan'da c-6 eklense?
(c) Normal bir ad 30 günde 3 kez sertifika alıyor. Her veriliş logda iki kayıtla (ön sertifika ve son hal) görünüyor. Eski kod (satır kimliği) kaç "farklı sertifika" sayardı? Yeni kod (yayıncı, seri) kaç sayar? Sinyal ikisinde kaç verir?

**B6. Toplam skorda eşik seçimi.** 100 G4 ayar kontrolünün toplam puanları: 80 tane 0, 15 tane 1, 4 tane 2, 1 tane 3. 50 test pozitifinin puanları: 10 tane 0, 15 tane 1, 15 tane 2, 10 tane 3.
(a) α = %1 için eşik ne olur? Kaç kontrol, kaç pozitif alarm verir?
(b) Aynı veride VEYA kullanılsaydı (her birimin puanı en büyük parçası, yani toplam ≥ 1 ise 1), eşik ve recall ne olurdu?
(c) α = %5 için toplamda eşik ve recall?

**B7. Ablasyon farkı.** Geçmiş veri testinde 100 test pozitifi var. Kendi eşiğiyle recall: TOPLAM(hepsi) 40/100, TOPLAM − brand 31/100, TOPLAM − cert-burst 40/100, TOPLAM − baseline 44/100, TOPLAM − new-cert-lexicon 3/100.
(a) Raporda her satırın yanında yazan "kendi eşiğiyle" fark kaç puan?
(b) Hangi sinyal en çok katkı veriyor, hangisi hiç vermiyor?
(c) Baseline'ı çıkarınca recall neden **artabilir**? Bunu raporda hangi ikinci sayıya bakarak doğrularsın?
(d) Son satır için fark neden yazılmıyor?

**B8. Numaralı ad.** Şu etiketlerin puanı kaç: `ornek7`, `ornek-2a`, `o12nek`, `4ornek`, `ornek.2026.example`?

**B9. İki fark.** İki parça var: baseline (b) ve brand (m). α = %1.
100 G4 ayar kontrolü: 3 tanesi yalnızca b = 1, 1 tanesi yalnızca m = 1, 96 tanesi ikisi de 0.
50 test pozitifi: 10 tanesi ikisi de 1, 15 tanesi yalnızca b = 1, 5 tanesi yalnızca m = 1, 20 tanesi ikisi de 0.
(a) TOPLAM(hepsi)'nin eşiği ve recall'u?
(b) "TOPLAM − brand" (yani yalnızca b) ve "TOPLAM − baseline" (yalnızca m) için kendi eşikleri ve recall'ları?
(c) Her iki satır için iki farkı hesapla: kendi eşiğiyle ve TOPLAM eşiğiyle.
(d) Raporda hangi farklar "-" görünür, hangileri "uygulanmaz" yazar? Neden?
(e) Sonucu iki cümleyle yorumla: Baseline bu toplamda yardım mı ediyor, zarar mı?

**B10. Veriliş kimliği.** Aynı ad için pencerede altı sertifika satırı var (yayıncı, seri, tür):
1. (CA-1, `00:AB:12`, ön sertifika) 2. (CA-1, `AB12`, son hal) 3. (CA-1, `0C:01`, ön sertifika) 4. (CA-1, `C01`, son hal) 5. (CA-2, `AB12`, son hal) 6. (CA-1, `AB:12:00`, son hal)
(a) Her serinin `IssuanceId` içindeki normal biçimini yaz.
(b) Kaç farklı veriliş var? Eşik 5 ise patlama kaç verir? Eski kod kaç sayardı?
(c) Yayıncı adı bir kayıtta `C=US, O=Test CA`, ötekinde `C=US,O=Test CA` yazılsaydı ne olurdu?

**B11. Eski ve yeni eşik kuralı.** Ayar bölmesinde 100 G4 kontrolü (birinin adında baseline terimi var) ve 300 G3 Çerçeve K kontrolü var (hepsinin adında terim var). 50 test pozitifinin 25'inde terim var. Sinyal: baseline. α = %1.
(a) Eski kural (G3 + G4, 400 kontrol): eşik ve recall?
(b) Yeni kural (yalnızca G4): eşik, recall, G4 ve G3-K satırları raporda nasıl yazılır?
(c) G4'te 1 değil 3 kontrolün adında terim olsaydı yeni kuralda ne olurdu? Bu sonuç dürüst mü?

---

## 4. Mülakat soruları

**S1. Baseline'ı neden yazdın?**
Taslak: Her sinyalin değerini ölçmek için bir rakip gerekiyor. "Recall %40" tek başına anlamsız. Akla gelen ilk basit kural aynı yanlış alarmda %38 veriyorsa kazancım 2 puan. Baseline 8 finans kelimesine bakıyor ve CT akışını süzen filtreyle aynı listeyi okuyor. Böylece ikisi asla ayrışmıyor.

**S2. Baseline'ı geçtin mi?**
Taslak: Dürüst cevap: Geçmiş veri testi bunu söyleyecek, kilitli test kesinleştirecek. Ölçerken iki tuzağa dikkat ediyorum. Birincisi, baseline'ın kendi kurgusu olan K çerçevesinde ölçmemek. İkincisi, baseline'ın eşik seçiminde susturulmaması. Eskiden eşik, baseline'ın tanım gereği 1 aldığı kontrolleri de içeriyordu. 2026-11-21'de, sonuç görmeden, eşiği yalnızca rastgele G4 kontrollerinden seçmeye geçtim. Geçemezsem bunu yazarım. Yol haritası da sonucu gizleme diyor.

**S3. Feature ablation nedir?**
Taslak: Bir özelliğin katkısını, onu çıkarıp sonucun ne kadar kötüleştiğine bakarak ölçmek. Ben her sinyali tek başına, hepsinin toplamını ve her biri çıkarılmış toplamı aynı bölmeyle ölçüyorum. Her satır için iki fark yazıyorum. Kendi eşiğiyle fark, aynı yanlış alarm bütçesinde karşılaştırır ama eşik kaymasını da taşır. TOPLAM'ın eşiği sabitken fark, yalnızca o sinyalin alarmlara doğrudan katkısını gösterir ama yanlış alarm düzeyi farklıdır. İkisini birlikte okuyorum.

**S4. Typosquatting'i nasıl yakalarsın, sınırları ne?**
Taslak: İki adımda. Önce görsel benzer karakterleri tek biçime indiriyorum: 0 → o, 1 ve l → i, rn → m, bazı Kiril harfleri Latin karşılığına. Etiketi ve marka listesini aynı fonksiyondan geçiriyorum. Sonra etiket markayı içeriyor mu ya da Levenshtein uzaklığı küçük mü diye bakıyorum: 5–7 harfte en çok 1, 8 ve üstünde en çok 2. Sınırlar: 5 harften kısa markalar listede yok. Markanın birebir adını başka uzantıda kullanan taklit kaçıyor. Yunan harfleri gibi eşlenmemiş benzer karakterler kaçıyor. Yabancı liste doğrulanmadı.

**S5. Levenshtein'ı nasıl hesaplıyorsun? Neden iki satır?**
Taslak: Dinamik programlama. Hücre (i, j), a'nın ilk i harfini b'nin ilk j harfine çevirmenin en az maliyeti. Üç yoldan en küçüğü alınıyor: değiştirme (çapraz + 0 ya da 1), silme (üst + 1), ekleme (sol + 1). Her satır yalnızca bir önceki satıra baktığı için iki satır yeterli. Bellek O(n·m) yerine O(m).

**S6. Neden ML modeli kurmadın?**
Taslak: Etiketli veri az, yaklaşık 175 pozitif kayıt. Dört sinyale ağırlık öğrenmek ayar verisine aşırı uyar, ablasyonu da okunmaz yapar. Yol haritası "baseline geçilmeden model yok" diyor. Önce basit, açıklanabilir sinyallerin baseline'ı geçip geçmediğini gösteriyorum. Geçerse ve veri büyürse model katmanı mantıklı olur.

**S7. Sinyalleri neden VEYA ile değil de toplamla birleştirdin?**
Taslak: Eşik ayar kontrollerinde %1 yanlış alarma göre seçiliyor. VEYA'da tek bir gürültülü sinyal kontrollerin %1'inden fazlasında 1 verirse eşik 1'in üstüne çıkar ve birleşik sinyal hiç alarm vermez. Toplamda eşik "en az k sinyal" olarak seçilebilir. Eşik 1 seçilirse sonuç VEYA'yla aynı, yani toplam VEYA'yı da kapsıyor.

**S8. "Tanım gereği alarm" nedir, projende nerede var?**
Taslak: Bir tabaka o sinyalin kendi kuralıyla kurulduysa, sinyalin orada aldığı sonuç ölçüm değil, kurgudur. Üç örnek: Baseline, kendi terimleriyle süzülmüş K çerçevesinde hep 1 verir. Marka sinyali, listesinin kaynağı olan G1'de hep 0 verir. Sözlük sinyali, sözlükle kurulmuş G3'te hep 1 verir. Rapor bu tabakaları ayrıca işaretliyor. Bu tabakalar eşik seçimine de girmiyor.

**S9. Yeni sertifika sinyali gerçekten "yeni"yi ölçüyor mu?**
Taslak: Kısmen. Predictor her birimi gün gün puanlayıp en yüksek puanı tuttuğu için, ilk sertifikanın görüldüğü gün her ad "yeni"dir. Geçmiş veri testinde sinyal pratikte "sözlük eşleşmesi + en az bir sertifika" kuralına dönüşüyor. "Yeni" şartı alarmın **ne zaman** verildiğini belirliyor, verilip verilmediğini değil. Canlı sistemde de böyle olurdu. Bunu kod yorumunda ve raporda açıkça yazıyorum.

**S10. Adı sinyale nasıl verdin? Gelecek sızıntısı yaratmadı mı?**
Taslak: Ad ayrı bir gözlem türü. `KnownAt`'i `MinValue`, çünkü ad zamanla değişmiyor ve karardan sonra öğrenilen bir bilgi değil. Böylece görünüm sözleşmesi (kimlik, an, gözlemler) bozulmadı. Predictor ad gözlemini değerlendirme başlangıcından dışlıyor, yoksa döngü 1. yıldan başlardı. Ad yalnızca bellekte, hiçbir çıktıya yazılmıyor.

**S11. Sertifika patlaması neyi yakalıyor, nerede yanılır?**
Taslak: 30 günde en az 5 farklı veriliş. Aynı adın kısa sürede tekrar tekrar kurulduğunu düşündürüyor. Verilişi (yayıncı, seri numarası) çiftiyle sayıyorum. Böylece aynı sertifikanın ön sertifikası ve son hali iki kez sayılmıyor. Yanılabileceği yerler: Her alt alan adına ayrı sertifika alan normal siteler. Sık yenileme yapan barındırma ve CDN hizmetleri. Ayrıca yalnızca tek bir kaynaktaki yayıncı metnine güveniyorum. Yayıncı adını farklı yazan ikinci bir kaynak eklenirse aynı veriliş iki kez sayılabilir.

**S12. Marka listesi nereden geliyor, neden gizli? Kilitli testte değiştirilemeyeceğini nasıl gösterirsin?**
Taslak: Yerli liste, G1'deki lisanslı bankaların alan adlarının soldaki etiketlerinden üretiliyor. Gizli, çünkü kontrol grubunun içeriğini açık ederdi (Kural 8). Geçmiş veri testinde `data/private` altındaki dosyadan okunuyor. Kilitli testte dosya hiç okunmuyor. Liste, daha önce yayımlanmış G1 taahhüdünden yeniden üretiliyor. Taahhüt doğrulanıyor ve veritabanındaki üyelerle birebir aynı olmalı. Listenin kurallı SHA-256'sı dondurulan yapılandırmadakiyle aynı olmalı, ayrıca kilit kaydına yazılıyor. Böylece dondurmadan sonra listeye marka eklemek ya imkânsız ya da kayıtta görünür.

**S13. Bir sinyalin ayar verisinde seçilmediğini nasıl gösterirsin?**
Taslak: Bütün parametreler (28 gün, 30 gün, 5 sertifika, 5 harf, uzaklık sınırları) kodda sabit ve gerekçesi yorumda yazılı. Hiçbiri sonuca bakılarak ayarlanmadı. Değişirse `docs/DECISIONS.md`'ye tarihli kayıt girer. Eşik kuralındaki değişiklik bile öyle girdi. Kilitli testte tek, dondurulmuş sinyal çalışır. Ablasyon ve sabit eşik orada reddedilir. Sinyal kuralının parçası olan marka listesinin özeti de kayda bağlı.

**S14. Bu koddaki hangi kararlar senin, hangi satırları anlatabiliyorsun?**
Taslak: Hafta 6 rehberi 5.5'teki kalıp aynen geçerli. Sinyalleri hangi gözleme dayandıracağımı, hangi tabakada ölçüm sayılmayacaklarını ve eşiğin yalnızca G4'ten seçilmesini ben belirledim. İsterseniz Levenshtein tablosunu, VEYA'nın neden işe yaramadığını ya da iki ablasyon farkını şimdi tahtada göstereyim.

---

## 5. Bilinen sınırlar, kapanan maddeler, açık noktalar

### 5.1 Bilinen sınırlar

- **Yabancı marka listesi doğrulanmadı.** Dosyanın başında `[doğrulanmadı]` yazıyor ([foreign_brands.txt:3–5](../../src/Fraud.Signals/foreign_brands.txt)). Liste ağa erişilmeden yazıldı. Kullanmadan önce kaynağın resmi yayınıyla karşılaştırılmalı. Fark varsa yeni sürüm açılmalı.
- **5 karakterden kısa markalar listede yok** ([BrandList.cs:19–20](../../src/Fraud.Signals/BrandList.cs)). Alt dize ve uzaklık olarak çok sayıda ilgisiz ada takılırlardı. Bedeli: Kısa adlı bir kurumun taklidi kaçıyor.
- **Yeni sertifika şartı geçmiş veri testinde pratikte "sözlük + en az bir sertifika" kuralına dönüşüyor** ([NewCertificateLexiconSignal.cs:13–16](../../src/Fraud.Signals/NewCertificateLexiconSignal.cs)). "Yeni" şartı yalnızca alarmın zamanını belirliyor.
- **Görsel benzer dönüşüm yalnızca marka sinyalinde.** Baseline ve sözlük `Fold`'u kullanıyor, `Normalize`'ı değil. `yat1r1m` baseline'a takılmaz. Bu bilinçli: Baseline en basit kural olarak kalmalı.
- **Eşlenmemiş benzer karakterler.** Yunan harfleri ve birçok Unicode benzeri eşlenmedi. Yalnızca rakamlar, `rn`, `vv` ve sekiz Kiril harfi var.
- **Marka adının birebir başka uzantıda kullanılması kaçıyor.** Etiket markanın kendisiyse puan 0 ([BrandSimilaritySignal.cs:60–63](../../src/Fraud.Signals/BrandSimilaritySignal.cs)).
- **G2 Wikidata'dan geliyor.** Popüler ve tanınmış yayınlara kayar, Türkiye ile sınırlı değil, eğitim kolu yok. G2'deki yanlış alarm oranı bir alt sınırdır ([G2_WIKIDATA_PLANI.md](../research/G2_WIKIDATA_PLANI.md) Bölüm 4, madde 6).

### 5.2 Kapanan maddeler (2026-11-21 ve 2026-11-24)

1. **Eşik seçimi tanım gereği 1 alan kontrolleri içeriyordu.** KAPANDI, 2026-11-21. Eşik yalnızca G4 ayar kontrollerinden seçiliyor ([EvaluationRunner.cs:311–320](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Başka bir grup listesi yapılandırmada reddediliyor ([EvaluationConfig.cs:242](../../src/Fraud.Evaluation/EvaluationConfig.cs)). Karar sonuç görülmeden [DECISIONS.md](../DECISIONS.md)'ye tarihli girdi. Bölüm 2.9.
2. **Patlama sinyalinde çift sayım riski.** KAPANDI, 2026-11-21. Sayım (yayıncı, seri) çiftinin özetine göre ([EvaluationDataLoader.cs:154–164](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs), [CertificateBurstSignal.cs:12–14](../../src/Fraud.Signals/CertificateBurstSignal.cs)). Bölüm 2.6.
3. **Ablasyonda her satır kendi eşiğini seçiyordu, raporda yazmıyordu.** KAPANDI, 2026-11-21. Rapor bunu açıkça yazıyor ve ikinci bir fark, TOPLAM'ın eşiği sabitken fark da basıyor ([Ablation.cs:67–81, :127–128, :137–149](../../src/Fraud.Evaluation/Running/Ablation.cs)). Bölüm 2.10.
4. **Kilitli kipte marka listesi kayda bağlı değildi (S1).** KAPANDI. Liste G1 taahhüdünden üretiliyor. Özeti yapılandırmada dondurulmuş ve kilit kaydına yazılıyor ([SignalCommands.cs:75–91](../../src/Fraud.Collectors/Evaluation/SignalCommands.cs), [LockedRunGuard.cs:6–9](../../src/Fraud.Evaluation/Running/LockedRunGuard.cs)). Bölüm 2.12.
5. **Eski yorum yanlış iddia ediyordu, süzgeç yalnızca gruba bakıyordu.** KAPANDI, 2026-11-24. [BaselineSignal.cs:14](../../src/Fraud.Signals/BaselineSignal.cs) artık "bugün yalnızca G3'ün K kısmı; G4b yalnızca N'den kurulur" diyor. Eşik süzgecinde beşinci koşul var: `s.Subgroup != Subgroup.FrameTermConditioned` ([EvaluationRunner.cs:316–317](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Test: [ThresholdTests.cs:138–165](../../tests/Fraud.Signals.Tests/ThresholdTests.cs). Bölüm 2.9.
6. **Bilinmeyen grup eşiğe düşüyordu.** KAPANDI, 2026-11-24. `GroupOf` her grubu açıkça yazıyor, tanınmayan grupta hata fırlatıyor ([EvaluationDataLoader.cs:243–254](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs)). Test: [EvaluationLoaderTests.cs:423–437](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs). Bölüm 2.14, not 4.
7. **Eşik kümesinin bileşimi yazılmıyordu.** KAPANDI, 2026-11-24. Rapor "Eşik kümesi (ayar): G4a=..., G4b=..." satırını küçük hücre ve tümleyen kuralıyla basıyor ([EvaluationRunner.cs:71–74, :147–156, :331–340](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Testler: [ThresholdTests.cs:167–197](../../tests/Fraud.Signals.Tests/ThresholdTests.cs). Bölüm 2.14, not 2. Not: Döküm yalnızca bileşimi gösteriyor. "Eşik çoğunlukla köklü sitelere dayanıyor olabilir" sınırı hâlâ geçerli, artık görünür durumda.
8. **Tek parça satırlarında "TOPLAM eşiğiyle" farkı anlamsızdı.** KAPANDI, 2026-11-24. TOPLAM eşiği 1'in üstündeyse tek parça satırında sayı yerine "uygulanmaz" ve gerekçe yazılıyor. Karar yalnızca eşikten veriliyor, sonuçtan değil. Madde 10 bu kuralı genişletti. Bölüm 2.14, not 3.
9. **G2'nin "100 altı" kuralı ve Türkiye alt kümesi raporda uygulanmıyordu.** KAPANDI, 2026-11-24 (ikinci kayıt). Bayrak veritabanındaki kurulum kaydından okunuyor ([EvaluationDataLoader.cs:174–193](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs)). Oran kaynağında hiç üretilmiyor ([MetricsCalculator.cs:94–102](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs)). Türkiye alt kümesi tümleyen kuralıyla ayrı satırda ([MetricsCalculator.cs:120–142](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs), [EvaluationRunner.cs:169–210](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Testler: [G2ReportTests.cs](../../tests/Fraud.Evaluation.Tests/G2ReportTests.cs), [EvaluationLoaderTests.cs:439–479](../../tests/Fraud.Integration.Tests/Evaluation/EvaluationLoaderTests.cs). Bölüm 2.15, not 1–4.
10. **İki parçalı ablasyonda "TOPLAM − x" satırı da tek parçaydı ama sayı basılıyordu.** KAPANDI, 2026-11-24 (ikinci kayıt). Kural "parça sayısı < TOPLAM eşiği" olarak genişletildi. Kadircan bunu **hiçbir sonuç görülmeden** onayladı ([DECISIONS.md](../DECISIONS.md), ikinci 2026-11-24 kaydı, madde 4). Kod: [Ablation.cs:54–65, :84–98](../../src/Fraud.Evaluation/Running/Ablation.cs). Testler: [ThresholdTests.cs:103–128, :199–296](../../tests/Fraud.Signals.Tests/ThresholdTests.cs). Bölüm 2.10 ve 2.15, not 5.

### 5.3 Kodda hâlâ açık noktalar (kod tarafına iletilecek)

Kadircan, bu listenin bütün eski maddeleri kapandı (Bölüm 5.2, madde 5–10). Okurken bir küçük nokta daha buldum. Bugünkü sonucu bozmuyor.

1. **Türkiye alt kümesinin tümleyen kuralı yalnızca biçimleyicide.** "Yeterli örnek yok" oranı kaynağında hiç üretilmiyor (Bölüm 2.15, not 2). Türkiye alt kümesinin oranı ise üretiliyor (`Rate` dolu, [MetricsCalculator.cs:140–141](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs)). Tümleyen kuralı yalnızca `FormatStratum` içinde uygulanıyor ([EvaluationRunner.cs:204–207](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). Bugün iki çıktı da (ana rapor ve ablasyon tablosu) `FormatStratum`'u kullanıyor, yani sızıntı yok. Ama ileride `StratumResult`'ı başka bir yerden basan biri (panel, JSON dışa aktarma) bu kuralı atlayabilir. Öneri: Tümleyen küçükse alt küme oranını da kaynağında `null` yapmak ya da bunu sınayan bir test eklemek.

---

## 6. Yeni maddeler için kendini sına

Cevapları görmeden, sesli ya da yazılı cevapla. Sonra en alttaki "Bölüm 6 cevapları" ile karşılaştır.

### 6.1 Eşik yalnızca G4'ten

- **Y1.** Eskiden eşik nereden seçiliyordu? Bu, baseline'ı nasıl susturabilirdi? Sayılı bir örnekle anlat.
- **Y2.** EvaluationRunner.cs:316–317'deki beş koşulu söyle. Her biri hangi hatayı engelliyor?
- **Y3.** Runner `CalibrationGroups` alanını okumuyor. O zaman EvaluationConfig.cs:242 neden var?
- **Y4.** Yeni kuralda G3 ve Çerçeve K'deki yanlış alarm oranı nereye yazılıyor? Neden ana sayı değil?
- **Y5.** Yeni kuralın bedeli ne? "%1 yanlış alarm" cümlesini raporda nasıl yazarsın?

### 6.2 Patlama ve veriliş kimliği

- **P1.** Ön sertifika nedir? Aynı sertifika neden CT'de iki kayıt olarak görünür?
- **P2.** `IssuanceId` seri numarasını hangi adımlardan geçiriyor? Her biri neden var?
- **P3.** Anahtarda neden yalnızca seri değil, yayıncı da var?
- **P4.** Kimlik neden ham metin değil de SHA-256 özeti? 16 onaltılık karakter neden yetiyor?

### 6.3 İki ablasyon farkı

- **A1.** "Kendi eşiğiyle" fark neyi ölçer, neyi karıştırır?
- **A2.** "TOPLAM eşiğiyle" fark neden hiçbir zaman pozitif olamaz?
- **A3.** Neden ikisi birden gerekli? Biri yetmez mi?
- **A4.** Sabit eşik kilitli testte neden reddediliyor?

### 6.4 Marka listesi kilidi (S1)

- **M1.** S1 hangi saldırıyı engelliyor? Saldırgan kim olabilir?
- **M2.** Kilitli kipte liste nereden geliyor? Hangi denetimlerden geçiyor?
- **M3.** Kurallı özet neden dosyanın düz SHA-256'sı değil?
- **M4.** Özet neden hem yapılandırmada hem kilit kaydında? İkisi aynı işi yapmıyor mu?
- **M5.** Bu denetimler neden kilitten önce yapılıyor?

### 6.5 G2 ve Wikidata

- **W1.** G2 neden Radar'dan Wikidata'ya taşındı? "Deneme kullanıldı" ne demek?
- **W2.** Sorgu dosyasının SHA-256'sı neyi kapsıyor? Tohum neden dosyanın içinde? Satır sonu neden önemli?
- **W3.** En çok 3 deneme ve en az 60 sn neden? Yarıda kesilen deneme neden sayılıyor?
- **W4.** Ham cevabın SHA-256'sı ne işe yarıyor? Neyi kanıtlamaz?
- **W5.** Türkiye bağı neden süzgeç değil etiket? 100'den az üye kalırsa ne olur? Bu kural kodda ne kadar zorlanıyor?

### 6.6 C# okuma notları (2026-11-24)

- **C1.** `Report(...) with { CalibrationComposition = ... }` satırı neyi yapıyor? Özgün rapor nesnesi değişiyor mu? Neden doğrudan atama yapılmıyor?
- **C2.** `CompositionText` şu girdiler için ne yazar: (G4a=2, G4b=150), (G4a=40, G4b=150), (G4a=0, G4b=3)? İkinci sayı neden bazen "-" oluyor?
- **C3.** `SkipReason` recall'a bakıp "recall 0 ise yazma" deseydi ne kötü olurdu? Neden yalnızca eşiğe bakmak yeterli ve güvenli?
- **C4.** Biri `ControlGroup`'a `G5` ekler ama `GroupOf`'a eklemeyi unutursa ne olur? Hangi test düşer, neden bu iyi bir şey?

### 6.7 G2 raporu ve eşik sınırı (2026-11-24, ikinci kayıt)

- **R1.** "Yeterli örnek yok" bayrağı neden manifestteki `enough_for_rates` alanından değil, `control_builds` tablosundan okunuyor? Kurulum kaydı hiç yoksa ne oluyor, neden?
- **R2.** Bu kuralda oran raporda gizlenmek yerine neden `MetricsCalculator` içinde hiç hesaplanmıyor?
- **R3.** G2 tamamı 30/100, Türkiye alt kümesi 27/60. Raporda alt küme satırında ne yazar? Neden? Alt küme 20/60 olsaydı?
- **R4.** Türkiye alt kümesinin bootstrap tohumuna neden `-tr` eki konuyor? Alt küme tamamla aynı tohumu kullansaydı ya da tamamın tohumu değiştirilseydi ne olurdu?
- **R5.** `new Subject(... SubjectGroup.ControlRandom ...) { TurkeyLinked = true }` ne yapar? `init` bloğu `Group`'u okurken `Group` neden zaten doludur?
- **R6.** `FixedThresholdSkipReason` neden `partCount <= fullThreshold` değil de `partCount < fullThreshold` kullanıyor? (3, 3.0) ve (2, `BitIncrement(2)`) için ne döner?

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
(c) Eski kod: 3 veriliş × 2 kayıt = 6 farklı satır kimliği ≥ 5. Puan 1, yani normal bir ada yanlış alarm. Yeni kod: Ön sertifika ve son hal aynı (yayıncı, seri) çiftini taşır. 3 veriliş = 3 kimlik < 5. Puan 0.

**B6.**
(a) n = 100, izin verilen alarm = floor(0,01 × 100) = 1. Büyükten küçüğe: 0. sıra 3, 1. sıra 2. Eşik 2'nin hemen üstü, yani yalnızca puan 3 alarm verir. Kontrol: 1/100 = %1. Pozitif: 10/50 = %20.
(b) VEYA'da 20 kontrolün puanı 1 (toplamı ≥ 1 olanlar), 80'inin 0. 1. sıra 1. Eşik 1'in hemen üstü, hiçbir puan 1'den büyük değil. Hiç alarm yok. Recall 0. Toplamın neden seçildiğinin cevabı bu.
(c) İzin verilen 5. Sıralı: 3, 2, 2, 2, 2, **1**. Eşik 1'in hemen üstü, yani puan ≥ 2 alarm verir. Kontrol: 5/100 = %5. Pozitif: 25/50 = %50.

**B7.**
(a) TOPLAM − brand: 31 − 40 = −9,0 puan. TOPLAM − cert-burst: 0,0. TOPLAM − baseline: +4,0. TOPLAM − new-cert-lexicon: yazılmaz.
(b) En çok katkı markadan geliyor (çıkınca 9 puan düşüyor). Patlama hiçbir katkı vermiyor.
(c) Baseline, G4 ayar kontrollerinin bir kısmında da 1 veriyorsa toplam puanı orada yükseltir. Bu da toplamda seçilen eşiği yukarı iter. Baseline çıkınca eşik düşebilir ve öteki sinyallerin alarmları sayılmaya başlar. Doğrulamak için aynı satırdaki "TOPLAM eşiğiyle" farkına bak. O fark 0 ya da negatif olmak zorunda. Negatifse baseline alarmlara gerçekten katkı veriyor, ama gürültüsü eşiği yukarı itiyor.
(d) 3/100'de isabet 5'ten küçük, yani küçük hücre. Fark yazılsaydı (3 − 40 = −37) gizlenen 3 geri hesaplanırdı.

**B8.** `ornek7` → 1 (sonda rakam). `ornek-2a` → 0 (tek rakam, sonda değil). `o12nek` → 1 (art arda iki rakam). `4ornek` → 0 (tek rakam başta). `ornek.2026.example` → 0 (yalnızca soldaki etiket `ornek`'e bakılır).

**B9.**
(a) TOPLAM puanları (kontrol): 4 tane 1, 96 tane 0. İzin verilen alarm 1. Sıralı listede 1. sıra 1. Eşik 1'in hemen üstü: Yalnızca puan 2 alarm verir. Kontrolde 0 alarm. Pozitif: 10/50 = %20.
(b) "TOPLAM − brand" (yalnızca b): Kontrolde 3 tane 1. 1. sıra 1, eşik 1'in hemen üstü. Hiç alarm yok, recall 0/50. "TOPLAM − baseline" (yalnızca m): Kontrolde 1 tane 1. 1. sıra 0, eşik 0'ın hemen üstü. Puan 1 alarm verir. Kontrolde 1/100. Pozitif: 10 + 5 = 15/50 = %30.
(c) TOPLAM eşiği (yalnızca puan 2) sabitken iki satır da hiç alarm veremez, çünkü tek parçanın puanı en çok 1. Recall 0/50.
- TOPLAM − brand: kendi eşiğiyle 0 − 20 = −20 puan. TOPLAM eşiğiyle 0 − 20 = −20 puan.
- TOPLAM − baseline: kendi eşiğiyle 30 − 20 = **+10** puan. TOPLAM eşiğiyle 0 − 20 = −20 puan.
(d) Kendi eşiğiyle farklarda: 0/50 küçük hücre (isabet < 5). Delta bu hücrede `null` döner, "-" basılır. "TOPLAM − brand" satırında "-", "TOPLAM − baseline" satırında "+10,0 puan" görünür. TOPLAM eşiğiyle farklarda sayı hiç basılmaz. TOPLAM eşiği `BitIncrement(1)` ("en az 2 parça"). İki parça olduğu için "TOPLAM − x" satırları 1 parçalı, tek parça satırları da öyle. 1 < eşik olduğu için bütün satırlarda "uygulanmaz: 1 parçalı satır en çok 1 puan alır, TOPLAM eşiğine ulaşamaz" yazar (Bölüm 2.15, not 5). Kâğıttaki −20, kodun neden basmadığını gösteriyor: Bu sayı her durumda "−TOPLAM recall" olurdu, sinyal hakkında bilgi taşımazdı. Kâğıtta hesaplaman bu yüzden önemli.
(e) TOPLAM eşiğinde her alarm baseline'a muhtaç: Baseline çıkarsa bütün alarmlar kaybolur. Ama baseline G4'te %3 alarm verdiği için toplamın eşiğini 2'ye itiyor. Aynı %1 bütçesiyle marka tek başına daha çok yakalıyor. Yani baseline alarmlara katkı veriyor, ama bu birleşimde gürültüsüyle zarar veriyor. Tek farka bakan biri ya "baseline zararlı" ya da "baseline vazgeçilmez" derdi. İkisi birden doğru, ama farklı soruların cevabı.

**B10.**
(a) `00:AB:12` → `:` sil `00AB12` → küçük harf `00ab12` → baştaki sıfırlar `ab12`. `AB12` → `ab12`. `0C:01` → `0C01` → `0c01` → `c01`. `C01` → `c01`. `AB:12:00` → `ab1200` (yalnızca **baştaki** sıfırlar atılır).
(b) Kimlikler: CA-1|ab12 (1 ve 2), CA-1|c01 (3 ve 4), CA-2|ab12 (5), CA-1|ab1200 (6). 4 farklı veriliş. 4 < 5, puan 0. Eski kod 6 satırı 6 sertifika sayardı, puan 1.
(c) Yayıncı adına yalnızca `Trim` uygulanıyor. İçerideki boşluk farkı iki ayrı kimlik verir ve aynı veriliş iki kez sayılır. Bugün tek bir kaynağımız var ve aynı yayıncı orada hep aynı metinle geliyor, o yüzden pratikte sorun yok. İkinci bir kaynak eklenirse bu bir tuzaktır.

**B11.**
(a) 400 kontrolün 301'i 1 (300 K + 1 G4). İzin verilen alarm floor(4) = 4. Sıralı listede 4. sıra 1. Eşik 1'in hemen üstü. Baseline hiç alarm vermez, recall 0/50. Baseline ölçülmeden susturuldu.
(b) 100 G4, 1'i 1. İzin verilen 1. 1. sıra 0. Eşik 0'ın hemen üstü: Puan 1 alarm verir. Recall 25/50 = %50. G4 satırı: 1/100, küçük hücre, "<5/100". G3-K satırı: 300/300. İsabet 5'ten büyük ama tümleyen 0 < 5, yani o da küçük hücre: "≥296/300". Yanında "tanım gereği, ölçüm değil" notu var.
(c) 3 tane 1 olsaydı 1. sıra 1 olurdu ve eşik yine 1'in üstüne çıkardı. Baseline yeni kuralda da susardı. Ama bu sefer sonuç dürüst: Baseline rastgele normal sitelerin %3'ünde alarm veriyor ve %1 bütçeye sığmıyor. Raporda "baseline %1 yanlış alarm düzeyinde çalışamıyor" yazılır. α sonuca bakılarak değiştirilmez.

### Bölüm 6 cevapları

**Y1.** G3 + G4 ayar kontrollerinden. K'deki her birim baseline'da 1 alır. Örnek: 100 G4 kontrolünden 1'i, 300 K kontrolünün hepsi 1 ise, 400 kontrolde izin verilen alarm 4. Ama 301 birim 1. Eşik 1'in üstüne çıkar ve baseline hiç alarm vermez (B11a).

**Y2.** (1) Yalnızca ayar bölmesi: Test verisi eşiğe sızmaz. (2) Pozitif değil: Eşik yalnızca yanlış alarma göre seçilir, etiket görülmez. (3) Pozitife bağlı değil: Bir pozitifle aynı kümedeki kontrol (E05) aslında pozitifin akrabasıdır, eşiği bozar. (4) Yalnızca G4: Kurgu tabakası (G3, K) ve tanım gereği 0 tabakası (G1) eşiği belirlemez. (5) K çerçevesi değil: G4'ün içine bir gün kelime süzgecinden geçmiş bir birim girerse, grup koşulu onu durdurmaz, bu koşul durdurur. Bugün böyle bir birim yok. Koşul, yarın sessizce oluşabilecek bir hataya karşı.

**Y3.** Kullanıcının ne çalıştığını sandığı ile gerçekte ne çalıştığı aynı olsun diye. Eski bir yapılandırma "G3 de dahil" diyorsa runner bunu sessizce yok sayardı. Sonuç ile yapılandırma dosyası çelişirdi. Satır 242 bunu bir hataya çeviriyor. Kemer ve askı: Biri asıl kural, öteki yanlış ayara karşı kilit.

**Y4.** Seçilen eşikte grup ve çerçeve başına ayrı tabaka satırlarında, "tanım gereği" notlarıyla. Ana sayı değil, çünkü o tabakalar ilgili sinyalin kuralıyla kuruldu. Oradaki oran sinyalin kalitesini değil, bizim seçim yöntemimizi ölçer.

**Y5.** Bedel: Eşik tek bir gruba dayanıyor ve G4'ün ayar kısmı büyük olasılıkla çoğunlukla köklü sitelerden oluşuyor. Raporda şöyle yazarım: "Eşik, G4 (rastgele normal siteler) ayar kontrollerinde en çok %1 yanlış alarm verecek şekilde seçildi. n = .... Bu oran yeni alan adlarında ve zor negatiflerde daha yüksek olabilir, ayrı satırlarda verildi."

**P1.** Sertifika yayıncısı, sertifikayı vermeden önce bir ön sertifikayı CT loguna yazar ve logun imzalı makbuzunu alır. Sonra makbuzu içeren son sertifikayı verir. Son hal de loglanabilir. Yani bir veriliş iki kayıt olur. Bu sitenin davranışı değil, altyapının ayrıntısıdır.

**P2.** `:` silme (`00:ab:12` ile `00ab12` aynı olsun). Kırpma (boşluk). `ToLowerInvariant` (`AB` ile `ab` aynı olsun, Türkçe I/ı kültür hatası olmasın). Baştaki sıfırları atma (`00ab12` ile `ab12` aynı olsun, aynı sayının farklı yazımları).

**P3.** Seri numarası yalnızca bir yayıncının içinde tekildir. İki farklı yayıncı aynı seriyi tesadüfen verebilir. Yalnızca seriye bakılsaydı iki farklı veriliş tek sayılırdı (B10'da 1 ve 5).

**P4.** Gözlemin değeri kısa kalsın ve yayıncının metni gözlem değerine girmesin. Değer yalnızca eşitlik için kullanılıyor, metnin kendisine gerek yok. 16 onaltılık karakter 64 bittir. Bir adın 30 günlük penceresinde birkaç düzine verilişte çakışma olasılığı yok denecek kadar küçük.

**A1.** Aynı yanlış alarm hedefinde (G4'te %1) sinyalsiz ne kadar yakalardık sorusunu ölçer. Adil karşılaştırma budur. Ama sinyalin kendi katkısını eşiğin kaymasıyla karıştırır. Gürültülü sinyal çıkınca eşik düşer ve fark pozitif çıkabilir.

**A2.** "TOPLAM − x"in puanı her birimde TOPLAM'ınkine eşit ya da 1 eksik. Eşik sabitse, TOPLAM'da alarm vermeyen bir birim "TOPLAM − x"te alarm veremez. Alarm kümesi yalnızca küçülebilir, recall yalnızca düşebilir ya da aynı kalabilir.

**A3.** Biri yetmez. Yalnızca ilki eşik kaymasını katkı sanar. Yalnızca ikincisi her sinyali "katkılı" gösterir, çünkü negatiften başka olamaz. Üstelik farklı bir yanlış alarm düzeyinde ölçer. İkisi birlikte "katkı veriyor ama gürültüsü eşiği yukarı itiyor" gibi bir durumu görünür kılar (B9).

**A4.** Kilitli testte eşik yalnızca kurala göre, ayar verisinden seçilmeli. Dışarıdan verilen bir eşik, bir yol açar: Önceki çalıştırmalara bakıp "işe yarayan" eşiği elle vermek. Bu da sonuca göre kural değiştirmektir. Ablasyon da kilitli testte zaten reddediliyor.

**M1.** Dondurmadan sonra, test pozitiflerinin adlarını gördükten sonra gizli marka dosyasına belirteç eklemek ya da listeyi başka bir G1 ile yeniden üretmek. Kilitli test recall'u şişer, hiçbir kayıtta görünmez. Saldırgan dışarıdan biri olmak zorunda değil. En olası kişi, sonucu beğenmeyen proje sahibi, yani sen. Kilit seni de kendinden korumak için var.

**M2.** Daha önce yayımlanmış G1 taahhüdünden. Denetimler: (1) Açık taahhüt dosyası ve açıklama dosyası var. (2) Açıklama taahhüde karşı doğrulanıyor. (3) Veritabanındaki taahhütlü G1 üyeleri taahhütle birebir aynı. (4) Üretilen listenin kurallı özeti, dondurulan yapılandırmadakiyle aynı. Herhangi biri tutmazsa çalışma kilitten önce durur.

**M3.** Düz özet yorum satırı, satır sırası, tekrar ya da sondaki boşluk değişince de değişir. O zaman anlamsız bir fark "liste değişti" alarmı verirdi. Kurallı biçim yalnızca anlamlı içeriği, yani etiket kümesini özetler. Tek bir etiket değişirse özet değişir.

**M4.** Farklı işler yapıyorlar. Yapılandırmadaki değer bir **ön koşul**: Liste dondurulan listeyle aynı değilse çalışma başlamaz. Kilit kaydındaki değer bir **tanık**: Hangi listeyle çalışıldığını, yeniden çalıştırmalar dahil, sonradan gösterir. Yapılandırma ileride değişse bile kayıt kalır.

**M5.** Kilitli testin tek hakkı var. Kilit alındıktan sonra bir dosya eksik ya da özet yanlış çıksaydı, hak hiçbir sonuç üretmeden harcanırdı. Önce bütün kontroller, sonra kilit, sonra veri.

**W1.** Önceden yazılmış önkoşul (100 alan adının en az %80'inde kategori) 14/100 ile geçmedi. Hata 0'dı. Kural deneme tekrarlanmaz diyordu. "Kullanıldı" demek: Kodda bir hata bulunsa bile o deneme yeniden yapılmaz, sonuç olduğu gibi raporlanır. Önceden kayıtlı B seçeneğine geçildi.

**W2.** Dosyanın bütün baytlarını: sorgu metni, yorumlar ve ilk satırdaki tohum. Tohum içeride olduğu için tohumu değiştirmek de özeti değiştirir. "Aynı sorgu, başka tohum" denemesi kayıtta görünür. Satır sonu: Windows'ta dosya CRLF ile çıksaydı baytlar, dolayısıyla özet değişirdi. Aynı dosya iki makinede iki farklı özet verirdi. `.gitattributes` dosyayı her yerde LF'ye sabitliyor.

**W3.** Hizmetin kullanım kurallarına uymak ve "beğenene kadar sor" yolunu kapatmak için. Başarılı bir cevaptan sonra yeni istek hiç atılmaz. Yarıda kesilen deneme sayılıyor, çünkü "başladı" kaydı istekten önce yazılıyor. Yoksa biri programı cevabı gördükten sonra durdurup denemeyi "olmamış" sayabilirdi. Hafta 6'daki kilit dosyasıyla aynı fikir.

**W4.** Seçimin hangi cevaptan yapıldığını sabitler. Kurulum komutu yalnızca kaydı olan başarılı bir denemenin cevabını kabul eder. Elle düzenlenmiş ya da başka sorguyla alınmış dosya giremez. Kanıtlamadığı şey: Wikidata'daki bilginin doğru ve güncel olduğu. Kayıtlar topluluk tarafından düzenlenir.

**W5.** Önceden kayıtlı metin Türkiye ile sınırlamıyordu. Süzgeç eklemek sonradan yeni bir kural eklemek olurdu, ayrıca grubu 100'ün altına düşürebilirdi. Etiket, Türkiye'ye bağlı alt kümeyi ayrıca göstermeye izin verir. 100'den az kalırsa grup yine kurulur, ekrana uyarı basılır ve manifeste `enough_for_rates=false` yazılır. 2026-11-24'ten beri değerlendirme raporu da kuralı uyguluyor. Bayrağı manifestten değil, veritabanındaki kurulum kaydından okuyor. O tabakada oran hiç hesaplanmıyor, yalnızca "yeterli örnek yok" yazıyor (Bölüm 2.15, not 1–2).

**C1.** `with`, rapor kaydının bir kopyasını çıkarır ve kopyada yalnızca `CalibrationComposition`'ı doldurur. Özgün nesne değişmez. Doğrudan atama olmaz, çünkü özellik `init` ile tanımlı: Yalnızca nesne kurulurken atanabilir. `Report(...)` yardımcısı ileriye dönük testte de kullanılıyor. Orada eşik olmadığı için döküm boş kalmalı. Bu yüzden döküm yalnızca `Run` içinde, kopyaya ekleniyor. Kopya sığdır, listeler paylaşılır. Listeler salt okunur olduğu için bu sorun yaratmaz.

**C2.** (2, 150) → `G4a=<5, G4b=-`. (40, 150) → `G4a=40, G4b=150`. (0, 3) → `G4a=<5, G4b=<5`. İkinci sayı "-" oluyor, çünkü toplam `n` raporda açık yazılı. G4b=148 yazılsaydı `n − 148` gizlenen 2'yi geri verirdi. Bu tümleyen kuralı. Kendi sayısı küçük olan her zaman "<5" yazar. Küçük olmayan ise yalnızca başka bir küçük sayı varsa "-" yazar.

**C3.** O zaman neyin yazılıp neyin gizleneceği sonuca bakılarak seçilmiş olurdu. Bu da kötü görünen sayıları saklama yolu açar ve "sonuca göre kural değiştirme"nin raporlamadaki hali olur. Eşiğe bakmak yeterli, çünkü sorun mantık gereği kesin: 0/1 puanlı bir parça 1'in üstündeki bir eşiğe hiç ulaşamaz. Güvenli, çünkü eşik yalnızca G4 ayar kontrollerinden seçiliyor ve test verisine hiç dokunmuyor.

**C4.** `GroupOf` `G5` için hata fırlatır, değerlendirme durur. `EvaluationLoaderTests.cs:433–436`'daki döngü bütün enum değerlerini dolaştığı için o test düşer. Bu iyi bir şey, çünkü eskiden `G5` sessizce G4 sayılır ve eşik kümesine girerdi. Şimdi grubu ekleyen kişi, `G5`'in ölçümde hangi gruba gireceğine ve eşiğe girip girmeyeceğine açıkça karar vermek zorunda. Derleyici bunu yakalayamaz, çünkü `switch` ifadesinde `_ =>` kolu var.

**R1.** Kurulum kaydı üyelerle aynı veritabanı işleminde yazılır. Üye varsa kayıt da vardır. Manifest ise bu işlemden sonra ayrı bir dosyaya yazılır. Arada süreç durursa hiç olmayabilir. `data/private` altında durduğu için elle de değiştirilebilir. Kayıt hiç yoksa (G2 üyesi elle eklenmişse) kural "yetersiz" der. Kuralın geçtiği kanıtlanamıyorsa oran basılmaz. Bu tutucu taraf: Yanlışlıkla gizlenen bir oran, yanlışlıkla basılan bir orandan daha az zarar verir.

**R2.** Bir kez hesaplanan oranı okuyan her yer (ana rapor, ablasyon tablosu, ileride panel ya da dışa aktarma) gizlemeyi ayrı ayrı hatırlamak zorunda kalırdı. Biri unutursa oran sızardı. Kaynakta hiç üretilmeyen bir sayıyı kimse yanlışlıkla basamaz.

**R3.** Yalnızca "test birimi=60" ve küçük hücre notu. Tümleyen 3/40: isabeti 5'ten küçük. Alt küme "27/60" yazılsaydı 30 − 27 = 3 geri bulunurdu. 20/60'ta tümleyen 10/40. İkisi de küçük değil, normal biçim basılır: "20/60 = 33.33%" (kültürden bağımsız biçim) (G2ReportTests.cs:139–141).

**R4.** Tohum `BootstrapSeed + "|" + ek` diye kuruluyor. Aynı tohum iki satırın yeniden örneklemesini birbirine bağlardı. Tamamın tohumunu değiştirmek ise daha önce hesaplanmış bütün G2 aralıklarını değiştirirdi. Yeni ek, alt kümeye kendi tohumunu veriyor ve eski sonuçları bayt bayt aynı bırakıyor.

**R5.** `ArgumentException` fırlatır. Türkiye etiketi yalnızca G2 için anlamlı. Nesne başlatıcısı (süslü parantez) kurucudan sonra çalışır. Kurucu `Group`'u zaten atamıştır. Aynı doğrulama `with` ile kopyalarken de çalışır.

**R6.** Alarm kuralı "puan ≥ eşik". k parçalı satır eşiğe ancak k ≥ eşik ise ulaşır, yani "ulaşamaz" demek k < eşik demek. (3, 3.0) için `null` döner: Satır çalışır, çünkü 3 puanlık birim 3 ≥ 3 ile alarm verir. `≤` olsaydı bilgi taşıyan bu sayı yanlışlıkla gizlenirdi. (2, `BitIncrement(2)`) için gerekçe döner: 2, 2,0000000000000004'ten küçük, satır eşiğe ulaşamaz.
