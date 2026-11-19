# Ölçüm altyapısı okuma rehberi (Hafta 6)

Bu kodu sen yazmadın. Yapay zekâ destekli geliştirildi. Ama mülakatta "bunu sen mi yazdın?" sorusundan sonra gelecek ikinci soru hep aynı olacak: "Peki şu satır ne yapıyor?" Bu rehberin amacı o soruya her satır için cevap verebilmen.

Nasıl çalışacaksın:

1. Önce Bölüm 1'i oku. Büyük resim kafanda oturmadan satırlara geçme.
2. Bölüm 2'deki tabloları sırayla izle. Her satırda dosyayı aç, o satırı oku, soruyu **koda bakmadan** cevapla. Cevaplayamazsan işaretle, bir sonraki oturumda birlikte bakarız.
3. Bölüm 3'teki alıştırmaları kâğıt kalemle çöz. Cevaplar en altta. Önce kendin çöz, sonra bak.
4. Bölüm 4'teki mülakat sorularını sesli cevapla. Taslak cevaplar yardım içindir, ezberlemek için değil.
5. Bölüm 5'i en son oku: Bulunan hatalar ve nasıl kapatıldıkları. Mülakatta en çok iz bırakacak hikâyeler orada.

Bağlantılar bu dosyanın yerine göre yazıldı (`docs/learn/`). Satır numaraları 2026-11-16 tarihli koda göre. Bu tarihte E1–E10 ve mentör listesindeki bütün maddeler kapatıldı ve kod satır satır yeniden okundu. Kod değişirse numaralar kayar, o zaman fonksiyon adıyla ara.

Ön okuma: [KONTROL_GRUBU_YONTEMI.md](../research/KONTROL_GRUBU_YONTEMI.md) Bölüm 7 (ayırma) ve 8 (metrikler). Kod bu belgedeki kuralları uyguluyor. Kuralı bilmeden kodu okursan "neden böyle yapılmış" sorusunun cevabını bulamazsın.

---

## 1. Büyük resim

**Neden sinyalden önce sınav yazılır?** Öğretmen sınav sorularını sınavdan önce yazar ve zarfa koyar. Öğrencinin cevaplarını gördükten sonra soruyu değiştirirse, sınav artık öğrenciyi değil öğretmenin isteğini ölçer. Biz de önce ölçme kurallarını (hangi veri, hangi bölme, hangi metrik) yazıyoruz, sonra sinyali. Böylece "sonuç kötü çıktı, kuralı biraz değiştireyim" kapısı kapanıyor. Hafta 6'da tek sinyal sahte sinyal (rastgele puan). Amaç sınavın kendisinin doğru çalıştığını göstermek.

**Geleceği sızdırmak:** Maç bittikten sonra skoru bilen biri "ben bu maçı tahmin ettim" derse buna tahmin denmez. Bir siteyi SPK karar gününde puanlıyorsak, sinyal yalnızca o gün herkesin bilebileceği bilgiyi görmeli. Bugün sorduğumuz DNS kaydını Mayıs ayındaki bir karara uygularsak, "dünkü tahmini yarının gazetesiyle" yapmış oluruz. Kodda bunu `KnownAt` alanı ve `AsOfView` sınıfı engelliyor.

**Saat de etiket olabilir:** Sınav kâğıtlarında yalnızca öğrenci numarası olsun. Ama başarılı öğrencilerin hepsi mavi, kalanların hepsi siyah kalem kullanmış olsun. Notu kâğıda bakmadan kalem renginden tahmin edebilirsin. Bizde "kalem rengi" görünüm anının saatiydi. Pozitifler hep aynı saatte, kontroller günün herhangi bir saatinde puanlanıyordu. Bu hata bulundu ve kapatıldı (Bölüm 5.3, E4). Ders şu: Etiket yalnızca açık alanlardan değil, kimsenin düşünmediği yan bilgilerden de sızar.

**Ayar ve test ayrımı:** Deneme sınavında çıkan soru gerçek sınavda da çıkarsa, gerçek sınavdaki not şişer. Eşiği ayar verisinde seçiyoruz, test verisinde yalnızca ölçüyoruz. Ama tek tek siteleri ayırmak yetmez. Aynı bültende toplu engellenen siteler ya da aynı operatörün siteleri birbirine çok benzer. Biri ayarda, öteki testte olursa, aynı soru iki sınavda çıkmış gibi olur. Bu yüzden bülten ve küme bütün olarak tek tarafa gider.

**Kilitli test:** Mühürlü bir zarf gibi. Kurallar dondurulduktan sonra yayımlanan bültenler bir kez açılır. Sonuç beğenilmese bile yeniden açılamaz. Açılırsa rapor bunu büyük harflerle yazar. Kilidin kendisi de kimsenin kolayca atlatamayacağı biçimde kuruldu: sabit yer, doğrulanmış commit, temiz kod, dış çapa.

**Taahhüt:** Tahminini bir kâğıda yazıp zarfa koyuyorsun, zarfı noterde mühürletiyorsun. Maçtan sonra zarfı açınca "bu tahmin maçtan önce yapılmıştı" diye kanıtlayabilirsin. Ama zarfı açmadan kimse içinde ne yazdığını bilemez. Bizde zarf = Merkle kökü, noter = RFC 3161 zaman damgası. Kural 8 şüpheli listenin repoya girmesini yasaklıyor. Taahhüt bize "listeyi gizli tutarken o tarihte bu listeye sahiptik" deme imkânı veriyor. İleriye dönük test alarmları yeniden hesaplamıyor, doğrudan bu zarftan okuyor. Tarihi de kendi yazdığımız alandan değil, noterin imzalı damgasından alıyor.

Bütün hat tek bir yerde, sırasıyla yazılı: [EvaluationRunner.cs:195](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs). Referans zamanı ata, böl, eşiği yalnızca ayarda seç, testte tahmin yap, metrikleri hesapla. İleriye dönük test ayrı bir yoldan gidiyor: [EvaluationRunner.cs:254](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs). Okumaya bu iki fonksiyona bir kez göz atarak başla, sonra aşağıdaki sırayla ayrıntıya in.

---

## 2. Okuma sırası

### 2.1 Birim ve gözlem: "Bu bilgi ne zaman bilinirdi?"

| Yer | Soru |
|---|---|
| [Subject.cs:4, :26](../../src/Fraud.Evaluation/Model/Subject.cs) | Pozitif, G1, G2, G3, G4 ne demek? Çerçeve K ile Çerçeve N arasındaki fark ne? Baseline (kelime kuralı) Çerçeve K'de neden "tanım gereği" alarm verir? |
| Subject.cs:75–104 | Kurucu hangi yanlış etiketleri reddediyor? Kontrolün bülteni neden olamaz? Bu kontroller neden veriyi yükleyen kodda değil de kurucuda? |
| Subject.cs:117, :133 | Kimlik neden alan adı değil? "Pozitife bağlı" kontrol (E05) ana yanlış alarm oranına neden girmez? |
| Subject.cs:43–47, :144–153 | Pozitifin yaş sınıfı nasıl seçiliyor? İlk bilinen sertifika karardan **sonraysa** neden "yeni" değil de "yaşı bilinmiyor"? Fark negatif çıkınca eski kod ne yapıyordu? |
| Subject.cs:155–156 | Çerçeveli kontrol ne demek? Hangi alt gruplar kilitli teste girebilir? |
| [BulletinNumber.cs:6–9, :26](../../src/Fraud.Evaluation/Model/BulletinNumber.cs) | "2026/12" ile "2026/9" metin olarak sıralanınca hangisi önce gelir? Bu hata bölmeyi nasıl bozardı? |
| [Observation.cs:22](../../src/Fraud.Evaluation/Model/Observation.cs) | `KnownAt` ile `ObservedAt` farkı ne? Filtre neden `ObservedAt`'e değil de `KnownAt`'e bakıyor? |
| Observation.cs:28–29 | "Emin değilsek geç say" kuralı neden tutucu? Erken saymak sonucu neden şişirir, geç saymak neden yalnızca zayıflatır? |
| Observation.cs:41 | Sertifika için `KnownAt = not_before`. Bu bir **varsayım**. Hangi durumda yanlış olur (geriye tarihli sertifika, geç loglanan sertifika)? |
| Observation.cs:47–48 | İleriye dönük testte sertifika neden `observed_at`? Taahhütten sonra çektiğimiz bir sertifikanın `not_before`'u taahhütten önceyse, eski kural ne derdi? |
| Observation.cs:54–55 | Arşiv kaydı geçmiş testinde `captured_at + 7 gün`, ileriye dönük testte `observed_at`. Neden iki farklı kural var? |
| Observation.cs:61 | DNS ve ağ kaydı için `KnownAt = queried_at`. Bu kural sayesinde geçmiş testinde DNS neden "kendiliğinden" kullanılamaz? |

### 2.2 Gün ızgarası ve AsOfView: Sinyalin gördüğü tek şey

| Yer | Soru |
|---|---|
| [DayGrid.cs:8–12](../../src/Fraud.Evaluation/Model/DayGrid.cs) | Izgara neden var (E4)? "Izgara noktası" tam olarak hangi an? |
| DayGrid.cs:16, :19–23 | Gün başı neden Türkiye saatiyle (UTC+3) alınıyor? `ToOffset` ne yapıyor? |
| DayGrid.cs:27 | `DayStart(t.AddTicks(1)).AddTicks(-1)`. Neden önce bir tick ekleyip sonra çıkarıyor? t zaten bir günün son anıysa sonuç ne olur? |
| DayGrid.cs:30–34, :40, :43 | `EndOfDayAtOrAfter`, `NextDayStartAfter`, `DayStartAtOrAfter` arasındaki fark ne? `NextDayStartAfter`'a tam gün başı verilirse neden **ertesi** gün başı döner? ([FindingsTests.cs:20](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| [AsOfView.cs:7–19](../../src/Fraud.Evaluation/Model/AsOfView.cs) | Sızıntıyı engelleyen üç yol ne? Sinyale grup, bülten ve küme neden verilmiyor? |
| AsOfView.cs:23 | Kurucu neden `private`? Dışarıdan biri `KnownAt > AsOf` olan bir gözlemi görünüme nasıl sokabilirdi, bu tasarım bunu nasıl imkânsız kılıyor? |
| AsOfView.cs:48 | `asOf >= reference` ise hata. Neden `>` değil de `>=`? Karar günü neden görünmez? |
| AsOfView.cs:53–58 | Görünüm anı aşağı yuvarlanıyor ve süzme yuvarlanmış ana göre yapılıyor. Aşağı yuvarlamak neden tutucu? Saat 15:00'te istenen görünümde aynı gün 10:00'da bilinen kayıt görünür mü? |
| AsOfView.cs:67, :81–84 | `ISignal` neden deterministik olmalı? `Prediction.Score` neden "en yüksek puan"? "Canlı sistem bir kez alarm verdiyse verilmiştir" cümlesini kendi kelimelerinle anlat. |
| [SafetyTests.cs:18, :37, :53](../../tests/Fraud.Evaluation.Tests/SafetyTests.cs) | Üç test üç farklı sızıntıyı deniyor: gelecek tarihli DNS, başka birimin gözlemi, karar günü. Hangisi hangisi? `SpySignal` neden işe yarar bir test aracı? |
| SafetyTests.cs:61 | Bu test `KnownAtPolicy` kurallarını sabitliyor. Biri arşiv payını 7'den 0'a indirse hangi satır kırılır? |
| [FindingsTests.cs:34, :306](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs) | `ClockSpy` neyi topluyor? `Assert.Single(spy.TimesOfDay)` neden E4'ün kapandığının kanıtı? |

### 2.3 Eşleşmiş gecikme: Sertifika yaşı sızıntısını kapatmak

Bu bölüm mülakatta en çok fark yaratacak kısım. Sorun şu: Pozitifi karar gününde puanlıyoruz. Pozitiflerin karar günü, ilk sertifikalarından ortalama aylar sonra geliyor. Kontrolün karar günü yok. Kontrolü ilk sertifika gününde puanlarsak, "sertifika kaç günlük" diye soran aptal bir sinyal bile pozitifleri kusursuz ayırır. Çünkü pozitif hep "yaşlı", kontrol hep "sıfır günlük" puanlanır. Sinyal dolandırıcılığı değil, bizim puanlama anı seçimimizi öğrenmiş olur.

| Yer | Soru |
|---|---|
| [ReferenceTimeAssigner.cs:12–36](../../src/Fraud.Evaluation/Splitting/ReferenceTimeAssigner.cs) | Kontrolün puanlama anı nasıl seçiliyor? Formülü kâğıda yaz. "Şimdi" sınırı ne? |
| ReferenceTimeAssigner.cs:44–49 | Gecikme havuzu **yalnızca ayar pozitiflerinden** kuruluyor. Test pozitifleri de havuza girseydi hangi bilgi teste sızardı? Negatif gecikmeler neden atılıyor? |
| ReferenceTimeAssigner.cs:55–59 | Pozitife neden dokunulmuyor? |
| ReferenceTimeAssigner.cs:61 | Yükleyici bir referans verdiyse bile neden ızgaraya yukarı yuvarlanıyor? |
| ReferenceTimeAssigner.cs:67 | `SeededHash.Unit(...) * lags.Count` neden `lags.Count`'a eşit bir indeks veremez? (İpucu: `Unit` hiçbir zaman 1 olmaz.) |
| ReferenceTimeAssigner.cs:70 | Referans "ilk sertifika + gecikme"den **sonraki** ilk TR gün başı. Neden "sonraki"? Gecikme sıfırsa kontrol kendi ilk sertifikasını görebiliyor mu? |
| ReferenceTimeAssigner.cs:72–75 | İlk sertifikası bilinmeyen kontrol neden ölçümden çıkıyor? Aynı kural pozitifte nerede uygulanıyor? (Splitter.cs:74–79) |
| ReferenceTimeAssigner.cs:79–83 | Referansı "şimdi"den sonraya düşen kontrol neden çıkarılıyor? Çıkarılmasaydı bugünkü DNS kaydı ne yapardı? ([FindingsTests.cs:148](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| [EvaluationConfig.cs:21–34](../../src/Fraud.Evaluation/EvaluationConfig.cs) | `FirstCertificate` seçeneği neden hâlâ kodda duruyor? Kim, ne için kullanır? |
| [RandomSignal.cs:27–36](../../src/Fraud.Evaluation/Signals/RandomSignal.cs) | `CertificateAgeSignal` puanı nedir? Bu sinyal neden bilerek "aptal" yazılmış? |
| [PipelineTests.cs:50, :61, :71](../../tests/Fraud.Evaluation.Tests/PipelineTests.cs) | İki test aynı veriyi, aynı sinyali kullanıyor, yalnızca kontrol referans kuralı değişiyor. Biri recall'u şansa yakın (%3–%18), öteki **%90'ın üstünde** buluyor. Bu iki sonuç birlikte neyi kanıtlıyor? |
| PipelineTests.cs:75 | Bu test test pozitiflerinin ilk sertifikasını 1000 gün geri atıyor ve kontrol referanslarının **hiç** değişmediğini kontrol ediyor. Bu neden "havuz yalnızca ayar pozitiflerinden" kuralının kanıtı? |

%90 sonucunu kendin izle: `FirstCertificate` düzeninde kontrolün referansı, ilk sertifikasından sonraki ilk gün başı. Son görünüm o günün son anı, yani her kontrolün yaş puanı 1 günden küçük. Pozitiflerin gecikmesi en az 10 gün. Eşik kontrol puanlarına göre seçildiği için 1 günün altında kalır, bütün pozitifler eşiği geçer. Recall neredeyse %100 çıkar. Test yalnızca "> %90" diye gevşek kontrol ediyor. Mülakatta şunu söyle: "Sonuç bu kadar mükemmel çıkınca ilk düşünmem gereken şey sızıntı."

### 2.4 Tohumlu rastgelelik

| Yer | Soru |
|---|---|
| [SeededHash.cs:8–9](../../src/Fraud.Evaluation/SeededHash.cs) | .NET'in `Random` sınıfı neden kullanılmıyor? Başka bir makinede, başka bir .NET sürümünde aynı bölmeyi elde etmek neden önemli? |
| SeededHash.cs:24–26 | Neden ilk 8 bayt, neden `>> 11`, neden 2^53'e bölme? `double` kaç bitlik tam sayıyı kayıpsız tutar? |
| SeededHash.cs:42–53 | SplitMix64 neden bootstrap'ta, SHA-256 neden bölmede? Bu üreteç kriptografik değil. Burada bu neden sorun değil? |
| SeededHash.cs:59–63 | Mod almak hangi küçük eğilimi yaratır? Neden ihmal edilebilir? |

### 2.5 Bölme: Bülten, küme, union-find ve `TiedToTuning`

| Yer | Soru |
|---|---|
| [DisjointSet.cs:15, :17–35](../../src/Fraud.Evaluation/Splitting/DisjointSet.cs) | Kök nedir? `Find` iki döngüden oluşuyor. Birinci döngü ne buluyor, ikinci döngü ne yapıyor? Yol sıkıştırma sonucu değiştirir mi? |
| DisjointSet.cs:38–55 | `Union`'da kök neden `CompareOrdinal` ile seçiliyor? Kenarlar farklı sırayla gelse küme kimlikleri değişseydi hangi tekrar edilebilirlik bozulurdu? |
| DisjointSet.cs:59–72 | Üç kenar türü ne? `ViaCdnOrCloud` doğruysa kenar neden atılıyor? |
| DisjointSet.cs:95 | CDN kenarı kullanılsaydı küme yapısına ne olurdu? "Aynı ağ" ile "aynı operatör" neden aynı şey değil? |
| [Splitter.cs:5–19](../../src/Fraud.Evaluation/Splitting/Splitter.cs) | Enum sayıları neden 1, 2, 3 sırasında? "Çakışmada küçük olan kazanır" kuralı bu sıraya nasıl dayanıyor? |
| Splitter.cs:24–33 | `SplitResult` içinde üç alan var: `TiedToTuning`, `PositivesWithoutCertificate`, `ControlsAfterFreeze`. Her biri hangi sorunu kapatıyor? |
| Splitter.cs:74–79 | İlk sertifikası bilinmeyen pozitif neden dışarıda? "Sertifika var mı" bilgisi grubu nasıl ele verebilirdi? |
| Splitter.cs:104–118 | Pozitif bülten numarasına göre bölünüyor: 1–18 ayar, 20–77 geçmiş test. 19 nereye gidiyor? ([SplitTests.cs:22](../../tests/Fraud.Evaluation.Tests/SplitTests.cs)) |
| Splitter.cs:129 | Kontrolün tarafı neden kendi kimliğiyle değil de **küme** kimliğiyle seçiliyor? |
| Splitter.cs:130–134 | F'den sonraki kontrol nereye gidiyor? Zamanla eşleşmeyen kontrol (G1, G2, G4a) F'den sonra neden hiçbir bölmeye girmiyor? ([SplitTests.cs:69, :91](../../tests/Fraud.Evaluation.Tests/SplitTests.cs)) |
| Splitter.cs:146–155 | Çerçeveli kontrol, ilk sertifika ayındaki pozitiflerin ayar/test oranına göre bölünüyor. Bu neden "aynı dönemden kontrol" sağlıyor? O ayda hiç pozitif yoksa kontrol neden yalnızca ayara gidiyor? |
| Splitter.cs:158–159 | Kilitli pencere iki uç dahil [F, F+28]. Bunu hangi test sabitliyor? |
| Splitter.cs:169–180 | Bütünlük kuralı. `u:`, `k:`, `b:` önekleri neden var? Önek olmasaydı küme kimliği "2026/30" olan bir küme ile 2026/30 bülteni yanlışlıkla birleşebilir miydi? |
| Splitter.cs:184–191 | **`TiedToTuning`.** Dışarıda kalan (Excluded) birimler de bileşene giriyor. Ayardaki bir birimle aynı bileşende olan herkes "ayara bağlı" işaretleniyor. Bu liste nerede kullanılıyor? ([EvaluationRunner.cs:269](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)) Eski kodda ayardaki bir operatörün sonraki sitesi ileriye dönük teste nasıl girebiliyordu? |
| Splitter.cs:193–204 | Bileşen iki tarafa düştüyse **bütünü** en erken bölmeye (ayar) taşınıyor. Neden teste değil de ayara? Dışarıda kalanlar neden taşınmıyor ama yine de "bağlı" sayılıyor? |
| SplitTests.cs:33, :48 | İlk test elle kurulmuş küçük bir örnek, ikincisi 1.400 birimlik rastgele bir örnek. İkisi de neden gerekli? |
| SplitTests.cs:107 | CDN testi: a–b sertifika, b–c CDN, c–d ad gövdesi. Sonuç kaç küme? |
| [FindingsTests.cs:123](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs) | `p-ayar` ve `p-sonra` aynı kümede, `p-bagimsiz` başka kümede. İleriye dönük testte neden yalnızca bir pozitif sayılıyor? |

### 2.6 Eşik: Yalnızca ayar verisinde, gün gün değerlendirme

| Yer | Soru |
|---|---|
| [EvaluationRunner.cs:184–190](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | "Eşik test verisi görülmeden seçilir" kuralı kodun neresinde, hangi sırayla zorlanıyor? |
| EvaluationRunner.cs:203–206, :213 | `Run` ileriye dönük kipi neden reddediyor? Yapılandırma neden burada bir kez daha denetleniyor? |
| EvaluationRunner.cs:227–232 | Eşiğe giren kontroller hangi dört koşulu sağlıyor? G1 (lisanslı kurumlar) neden eşiğe girmiyor? ([EvaluationConfig.cs:105](../../src/Fraud.Evaluation/EvaluationConfig.cs)) |
| [Predictor.cs:8–16](../../src/Fraud.Evaluation/Running/Predictor.cs) | Eski kod yalnızca gözlem anlarında değerlendiriyordu. Bu hangi varsayıma dayanıyordu, hangi sinyalde ilk alarmı geç buluyordu? |
| Predictor.cs:28 | Son değerlendirme anı nasıl bulunuyor? `reference.AddTicks(-1)` neden önce yapılıyor, sonra ızgaraya yuvarlanıyor? |
| Predictor.cs:29–30 | İlk değerlendirme günü ne? Birimin hiç gözlemi yoksa kaç kez değerlendirilir? |
| Predictor.cs:34–42 | Döngü her gün bir görünüm kuruyor. 200 günlük bir birim için sinyal kaç kez çağrılır? Bu maliyet neden kabul edilmiş? |
| [FindingsTests.cs:52](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs) | Sertifika 10 Mart 05:00'te (TR) bilinir, eşik 30 gün. Beklenen ilk alarm neden `Tr0.AddDays(31).AddTicks(-1)`? Kâğıtta say. |
| Predictor.cs:63–74 | Eşik seçme kuralını adım adım anlat. `BitIncrement` neden gerekli? Eşitlik olursa yanlış alarm oranı hedefin üstüne mi çıkar, altında mı kalır? |
| Predictor.cs:72 | `1e-9` neden ekleniyor? 0,29 · 100 kayan noktada kaç eder? |
| [MetricsTests.cs:112](../../tests/Fraud.Evaluation.Tests/MetricsTests.cs) | Üç elle örnek: normal, eşitlik, kayan nokta. Her birini kâğıtta tekrar et. |
| EvaluationRunner.cs:235–237 | Geçmiş ve kilitli kipte test kümesi nasıl seçiliyor? Ayar bölmesi hangi kipte teste girer? (Cevap: hiçbirinde. Satırı göster.) |

### 2.7 Kilitli test bir kez çalışır

| Yer | Soru |
|---|---|
| [LockedRunGuard.cs:35–46](../../src/Fraud.Evaluation/Running/LockedRunGuard.cs) | Kilit dosyası neden çalıştırmadan **önce** yazılıyor? Sonra yazılsaydı hile nasıl yapılırdı? |
| LockedRunGuard.cs:53–54, :62–67 | Commit kimliği neden tam 40 küçük harf onaltılık karakter olmalı? "son", "HEAD" ya da 7 karakterlik kısa kimlik neden kabul edilmiyor? |
| LockedRunGuard.cs:74 | `FileMode.CreateNew` ne yapar? İki süreç aynı anda başlarsa ne olur? Önce `File.Exists` sorup sonra yazmak neden aynı güvenceyi vermez (yarış durumu)? |
| LockedRunGuard.cs:78 | `catch (IOException) when (File.Exists(lockFile))`: `when` filtresi neden var? Disk dolu ya da izin hatası bu dala düşer mi? |
| LockedRunGuard.cs:86–97 | Yeniden çalıştırma hangi iki şartla mümkün? İlk kayıt neden silinmiyor, listeye ekleme yapılıyor? |
| LockedRunGuard.cs:12–18 | `LockedRunTicket` kurucusu neden `internal`? [EvaluationRunner.cs:208](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) ile birlikte bu tasarım neyi garanti ediyor? |
| [EvaluationConfig.cs:139–145](../../src/Fraud.Evaluation/EvaluationConfig.cs) | Kilit yolu neden yapılandırmadan okunmuyor? JSON'a `lockFile` yazılırsa ne olur? ([FindingsTests.cs:103](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) Bu eski yol bugün yalnızca hangi denetim için duruyor? |
| [EvaluationCommands.cs:46–47](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs) | `ValidateFor` neden kilitten **önce**? Eski sırada eksik bir `freezeDate` ne yapıyordu? |
| EvaluationCommands.cs:289–307 | Donmuş durum denetiminin ilk dört adımını say: commit biçimi, HEAD ile eşleşme, temiz çalışma ağacı, yapılandırmanın git'te izlenmesi. Her biri olmasaydı hangi hile mümkün olurdu? |
| EvaluationCommands.cs:308–317 | **İkili commit kontrolü.** Kaynak ağacı temiz ve HEAD doğru olsa bile çalışan program hangi commit'ten derlenmiş olabilir? `BuildCommit` bu bilgiyi nereden okuyor ([RepoContext.cs:133–138](../../src/Fraud.Collectors/Evaluation/RepoContext.cs))? Bilgi yoksa neden durmuyor da uyarıyor? |
| EvaluationCommands.cs:58–66 | **Kilit `git-common-dir` altında.** Kilit artık depo klasöründe değil, git'in ortak klasöründe. Eski konumda bir kilit varsa ne oluyor? |
| [RepoContext.cs:86–100](../../src/Fraud.Collectors/Evaluation/RepoContext.cs) | `git rev-parse --git-common-dir` ne döndürür? `git worktree add` ile açılan ikinci çalışma ağacı neden aynı kilidi görür? Başka bir klasöre `git clone` neden yine yeni bir kilit açar? |
| EvaluationCommands.cs:67–72, :392–414 | **Dış çapa.** Kilit alındıktan hemen sonra ne yazdırılıyor? Kilit kaydının özetini commit edip zaman damgalamak, "klonlayıp yeniden çalıştırma" hilesini nasıl görünür kılar? |
| EvaluationRunner.cs:50–55 | Yeniden çalıştırmada rapor neyle başlıyor? Bunu hangi test sabitliyor? ([SafetyTests.cs:106](../../tests/Fraud.Evaluation.Tests/SafetyTests.cs)) |

### 2.8 Depo kökü, güvenilir git ve gizli yollar (RepoContext)

| Yer | Soru |
|---|---|
| [RepoContext.cs:10–19](../../src/Fraud.Collectors/Evaluation/RepoContext.cs) | Göreli bir yol neye göre çözülür? Komut başka bir klasörden çalıştırılınca gizli dosya nereye düşerdi (E2)? Yukarı doğru bulunan ilk `FraudLab.slnx` neden yetmiyor (E8)? |
| RepoContext.cs:42–64 | Kök nasıl bulunuyor? Bulunan klasör neden `git rev-parse --show-toplevel` ile **aynı** olmalı? `data/private/x/` içine konan sahte bir işaret dosyası eskiden ne yapardı? |
| RepoContext.cs:67–68 | `At` neden yalnızca testler için? Git'i neden hiç çağırmıyor? |
| RepoContext.cs:73–84 | `PrivatePath` neyi garanti ediyor? `../` ile `data/private` dışına çıkmaya çalışan bir ad ne olur? Sonuna neden `DirectorySeparatorChar` ekleniyor? (İpucu: `data/private-kopya` klasörü.) |
| RepoContext.cs:103, :126–127 | HEAD nasıl okunuyor? Bir dosyanın git'te izlendiği nasıl soruluyor? |
| RepoContext.cs:110–123 | **`IsClean`.** İki denetim var. Birincisi `git status --porcelain`. İkincisi `git ls-files -v`: Satır başı küçük harfse ya da `S` ise neden "kirli"? `git update-index --assume-unchanged` ve `--skip-worktree` ne işe yarar? Değiştirilmiş bir dosyayı `git status`'tan nasıl gizler? |
| RepoContext.cs:140–145 | Yol karşılaştırması neden Windows'ta büyük/küçük harfe duyarsız, Linux'ta duyarlı? |
| RepoContext.cs:147–191 | Git neden `ArgumentList` ile, argümanlar tek tek verilerek çalıştırılıyor? Boşluklu bir yol tek metne gömülünce ne olurdu? stdout ve stderr ikisi de neden okunuyor (tampon dolarsa ne olur)? Süre aşılınca neden süreç öldürülüyor ve sonuç "başarısız" sayılıyor? |
| RepoContext.cs:194–247 | **`GitExecutable`.** Windows bir programı yalnızca adıyla (`git`) başlatırken önce hangi klasörlere bakar? Çalışma klasörüne bakması neden tehlikeli? Depo klasörüne başka bir hesabın koyduğu `git.exe` hangi iki şeyi yapabilir? |
| RepoContext.cs:215–220, :225–244 | Çözümleme sırası ne? Ayarla verilen yol neden tam yol olmalı? PATH'teki göreli girdiler ve çalışma klasörünün altındaki girdiler neden atlanıyor? |
| [.gitignore:2, :4–5](../../.gitignore) | `data/private/` ile `**/data/private/` farkı ne? Ortasında `/` olan bir kalıp neden yalnızca depo köküne bağlanır? `*.opening.json` neden ayrıca eklendi? |

### 2.9 Metrikler, küçük hücre ve veri kapsaması

| Yer | Soru |
|---|---|
| [MetricsCalculator.cs:11](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs) | `RateResult` içinde ham sayı her zaman var, oran neden bazen `null`? |
| MetricsCalculator.cs:121–128 | 10'dan az birimde neden yüzde verilmiyor? 2/6 = %33 yazmak okuyana ne yanlış izlenim verir? |
| MetricsCalculator.cs:66–71 | Recall hem toplam hem alt grup (yeni, eski, yaşı bilinmiyor) için hesaplanıyor. Neden ayrı ayrı? |
| MetricsCalculator.cs:74–91 | Yanlış alarm oranı neden kontrol grubu × alt grup × pozitife bağlı tabakalarında ayrı ayrı? Tek bir FPR neden yanıltır? |
| MetricsCalculator.cs:81–84 | Örneklem içi precision neden "gerçek dünyayı göstermez"? Tabaka 10'dan küçükse neden hiç yazılmıyor? ([FindingsTests.cs:77](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| MetricsCalculator.cs:38–59 | Bayes formülü: `PPV = r·π / (r·π + f·(1 − π))`. Her harfi tanımla. f = 0 ise neden precision yazılmıyor? |
| MetricsCalculator.cs:86–88 | Günlük beklenen yanlış alarm neden yalnızca Çerçeve N için? Çerçeve K için hesaplasaydık neden anlamsız olurdu? |
| MetricsCalculator.cs:101–119 | Erken uyarı = karar − ilk alarm. Yakalanan pozitif 10'dan azsa neden ortanca yazılmıyor? Çift sayıda değerde ortanca nasıl alınıyor? |
| [EvaluationRunner.cs:127–132](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | `Cell`: `<5` kuralı (E6). Neden 0 da bastırılıyor? "1/1" yazmak Kural 4'ü nasıl dolaylı yoldan bozabilir? |
| EvaluationRunner.cs:140–149 | **Küçük hücre.** Bir hücre ne zaman "küçük" sayılıyor? Neden yalnızca isabet değil, **tümleyen** (toplam − isabet) de bakılıyor? |
| EvaluationRunner.cs:151–161 | Küçük hücrede neden oran ve aralık da yazılmıyor? "<5/40 = %7,5" yazısından isabet nasıl geri hesaplanırdı? "≥8/12" ne demek, 11/12 neden böyle yazılıyor? ([FindingsTests.cs:237](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| EvaluationRunner.cs:94–103 | Precision ve günlük alarm, küçük hücrede neden "-" yazılıyor? Bunlardan isabet sayısı nasıl geri hesaplanabilirdi? ([FindingsTests.cs:257](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| EvaluationRunner.cs:108–111 | Erken uyarı satırı 10'dan az yakalanan pozitifte ne yazıyor? Recall hücresi küçükse yakalanan sayısı neden "-" oluyor? |
| [CoverageCheck.cs:10–14](../../src/Fraud.Evaluation/Metrics/CoverageCheck.cs) | "Veri var mı?" bilgisi neden tek başına bir sinyal olabilir? Toplayıcılar şu an kimin için veri topluyor? |
| CoverageCheck.cs:27–36 | Yalnızca hangi kayıtlar sayılıyor? Son görünüm anı nasıl hesaplanıyor, Predictor'daki hesapla aynı mı? Karardan sonra sorulmuş DNS neden artık "veri var" sayılmıyor? ([FindingsTests.cs:289](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| CoverageCheck.cs:38–42, :51–63 | Pay nasıl hesaplanıyor? Uyarı hangi koşulda yazılıyor? Uyarı metninde neden birim kimliği yok? |
| [EvaluationRunner.cs:292, :299–306](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | Kapsama denetimine hangi birimler giriyor? "Dışarıda kalan" satırı hangi dört nedeni sayıyor ve neden önemli? |
| [EvaluationConfig.cs:110–114](../../src/Fraud.Evaluation/EvaluationConfig.cs) | π = 0,001. Bu sayı nereden geldi? (Cevap: ölçülmedi, varsayım.) Rapor bunu nerede yazıyor? ([EvaluationRunner.cs:115](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)) |
| [MetricsTests.cs:24, :48, :84](../../tests/Fraud.Evaluation.Tests/MetricsTests.cs) | Testin yorumundaki hesabı kâğıtta tekrar et. 0,0244140625 nereden geliyor? |
| [FindingsTests.cs:182](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs) | Arşiv için uyarı var, sertifika için yok. Neden? |

### 2.10 Bootstrap: Küme ve bülten düzeyinde

| Yer | Soru |
|---|---|
| [Bootstrap.cs:15–17](../../src/Fraud.Evaluation/Metrics/Bootstrap.cs) | Aynı kümedeki ya da bültendeki alan adları neden bağımsız değil? Tek tek alan adını yeniden örneklemek aralığı neden yapay olarak daraltır? |
| Bootstrap.cs:33–37 | Satırlar birime göre toplanıyor. Birimler neden sıralanıyor? Sıralanmasaydı aynı tohum aynı aralığı verir miydi? |
| Bootstrap.cs:41–52 | Bir tekrarda kaç birim çekiliyor? "Yerine koyarak" ne demek? Oran neden Σisabet / Σtoplam, birim oranlarının ortalaması değil? |
| Bootstrap.cs:54–59 | 2000 tekrarda %2,5 ve %97,5 hangi sıraya düşüyor? Kendin hesapla. |
| [MetricsCalculator.cs:131–137](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs) | Recall için iki aralık var (küme, bülten), yanlış alarm için bir. Neden kontrolde bülten aralığı yok? |
| [SafetyTests.cs:149](../../tests/Fraud.Evaluation.Tests/SafetyTests.cs) | Bütün satırlar tek kümede ise aralık neden tek bir noktaya çöküyor? Bu test neyi kanıtlıyor? |

### 2.11 Merkle ağacı ve taahhüt

Ön bilgi: RFC 6962 Bölüm 2.1. CT istemcisinde aynı ağacı zaten gördün. Burada aynı fikir kendi listemiz için kullanılıyor.

| Yer | Soru |
|---|---|
| [MerkleTree.cs:18–33](../../src/Fraud.Evaluation/Commitments/MerkleTree.cs) | Yaprak `0x00`, iç düğüm `0x01` önekiyle özetleniyor. Önek olmasaydı bir iç düğüm nasıl yaprak gibi gösterilebilirdi (ikinci ön görüntü saldırısı)? |
| MerkleTree.cs:77–86 | `SplitPoint(5)`, `SplitPoint(8)`, `SplitPoint(9)` kaç? "n'den küçük en büyük ikinin kuvveti" tanımında neden "küçük eşit" değil de "küçük"? |
| MerkleTree.cs:88–97 | Tek sayıda yaprakta yaprak kopyalanıyor mu? Bitcoin'deki kopyalama hangi sorunu yaratır? |
| MerkleTree.cs:99–118 | Kanıt yolu neden alttan üste sıralı? Yaprak soldaysa hangi özet ekleniyor, sağdaysa hangisi? |
| MerkleTree.cs:124–146 | Doğrulama, kanıt üretimini tersten izliyor. Kanıtın son elemanı neden en üstteki kardeş? Yaprağa inildiğinde kanıt tükenmemişse neden `null`? |
| MerkleTree.cs:67, :73 | Kanıt elemanı 32 bayt değilse neden hemen `false`? `FixedTimeEquals` burada gerçekten gerekli mi, yoksa iyi alışkanlık mı? |
| [Commitment.cs:33–43](../../src/Fraud.Evaluation/Commitments/Commitment.cs) | Taahhüt şemasını kâğıda yaz: tuz, c, yaprak, kök. Neden kayıt başına ayrı tuz? Neden yapraklar c'ye göre sıralı? |
| Commitment.cs:55–60 | Tuz olmasaydı biri "acaba listede kayit.test var mı?" sorusunu nasıl cevaplardı? |
| Commitment.cs:67–76 | Boş satır ve tekrar eden kayıt neden reddediliyor? |
| Commitment.cs:91–97 | `Previous` alanı nedir? Önceki dosya neden önce `ParsePublic` ile denetleniyor (E5)? Rastgele bir dosyanın özetiyle zincir neden anlamsız olur? |
| Commitment.cs:120–145 | Doğrulayan kişi neye sahip, neye sahip değil? Kanıt dosyası diğer kayıtlar hakkında ne söyler? |
| Commitment.cs:151–180 | **`VerifyOpening`.** Açıklama dosyasının açık taahhütle tuttuğu nasıl denetleniyor? Üç adımı say: sayı, kayıt kayıt yeniden hesaplama, kökün yeniden kurulması. Son satırdaki `Distinct` hangi hileyi yakalıyor? ([FindingsTests.cs:223](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| Commitment.cs:186–193, :196–210 | `VerifyPrevious` neye bakıyor? `ParsePublic` hangi alanları denetliyor, kök neden 64 küçük harf onaltılık karakter olmalı? ([FindingsTests.cs:210](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| Commitment.cs:12, :229–253 | `CreatedAt` neden tek başına kanıt değil? RFC 3161 zaman damgası isteği (.tsq) hangi alanları taşıyor? Kod neden ağa bağlanmıyor? |
| [EvaluationCommands.cs:113–142](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs) | Kayıtlar taahhütten **önce** normalleştiriliyor. Neden? Normalleştirince iki satır aynı çıkarsa ne oluyor? |
| EvaluationCommands.cs:155–161, :416–439 | Aynı adla ikinci taahhüt neden reddediliyor (E3)? `WriteNewFileAsync` neden önce geçici dosyaya yazıp sonra taşıyor? |
| EvaluationCommands.cs:238–248 | `commit-verify --record` ve `--previous` ne ekliyor? |
| [CommitmentTests.cs:29, :66, :84](../../tests/Fraud.Evaluation.Tests/CommitmentTests.cs) | RFC vektörleri neden önemli? Bozulmuş kanıt testindeki beş durumu tek tek say. Yorumdaki "7 ve 8 yaprakta yol şekli aynı" notunu açıkla. |

### 2.12 İleriye dönük test: Taahhüt edilmiş liste ve doğrulanmış zaman

**Benzetme: Noter onaylı tarih.** Bir belgeyi notere götürüyorsun. Noter belgenin içeriğini okumuyor, yalnızca belgenin **parmak izini** (SHA-256 özeti) alıyor. Üstüne tarih yazıp mühürlüyor. RFC 3161'de beş parça var:
- *messageImprint* = notere verdiğin parmak izi. Damgadaki parmak izi senin dosyanınkiyle aynı değilse damga başka bir belgeye aittir.
- *nonce* = notere verdiğin rastgele sıra numarası. Cevapta aynı numara yoksa bu cevap senin isteğine değil, başkasının isteğine verilmiştir.
- *imza* = noterin mührü. Mühür sahte değilse damgadaki hiçbir alan değiştirilmemiştir.
- *zincir* = noterin gerçekten yetkili olduğu. Mührün sahibi, güvendiğimiz bir kuruluşa (kök sertifika) bağlanmalı. Herkes kendi kendine "noter" olamaz.
- *genTime* = noterin yazdığı tarih. Taahhüt anı artık bizim yazdığımız `CreatedAt` değil, bu tarih.

| Yer | Soru |
|---|---|
| [EvaluationRunner.cs:242–253](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs) | Alarm kümesi neden **yeniden hesaplanmıyor**? Sinyali bugün yeniden çalıştırmak neden geleceği sızdırırdı? |
| EvaluationRunner.cs:262–263 | `RunProspective` neden `ValidateFor` değil de yalnızca `Validate` çağırıyor? TSA kökü burada neden gerekmiyor? |
| EvaluationRunner.cs:268–275 | Test kümesi hangi koşulları sağlıyor? `TiedToTuning` ne işe yarıyor? Neden `Excluded`'ın tamamı değil de yalnızca sertifikası bilinmeyen birimler dışlanıyor? (Yorumu oku: Yeni pozitifler de `Excluded` etiketi taşıyor.) ([FindingsTests.cs:273](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)) |
| EvaluationRunner.cs:276–279 | Taahhütte olan birim "alarm", olmayan "alarm yok". İlk alarm anı neden taahhüt anı? Erken uyarı süresi o zaman neyi ölçer? |
| EvaluationRunner.cs:64–67 | Raporda eşik neden "yok" yazıyor? |
| [EvaluationCommands.cs:76–85](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs) | Zaman damgası doğrulanmazsa neden veri bile yüklenmiyor? |
| EvaluationCommands.cs:328–359 | `CreatedAt` neden taahhüt anı olarak kullanılmıyor? .tsq, .tsr ve TSA kökü nereden okunuyor? Biri eksikse ne oluyor? |
| EvaluationCommands.cs:361–371 | Doğrulama başarısız olursa çıktıda ne yazıyor? Neden "yine de sonuç bas, altına not düş" seçeneği yok? |
| EvaluationCommands.cs:373–389 | Zaman doğrulandıktan sonra açıklama dosyası nasıl denetleniyor? Kayıtlar opak kimliğe nasıl çevriliyor? |
| [TimestampVerifier.cs:13–25](../../src/Fraud.Collectors/Evaluation/TimestampVerifier.cs) | Doğrulamanın dört adımını kendi kelimelerinle say. Neden ağa bağlanılmıyor? |
| TimestampVerifier.cs:39–47 | **messageImprint (istek).** İstekteki özet neden açık dosyanın SHA-256'sı olmalı? Algoritma neden ayrıca kontrol ediliyor? |
| TimestampVerifier.cs:49–57 | **nonce ve durum.** `ProcessResponse` neyi denetliyor? Başka bir isteğe verilmiş geçerli bir cevabı buraya koymak neden işe yaramaz? |
| TimestampVerifier.cs:59–62 | **messageImprint (damga).** İstekteki özet doğru olsa bile damgadaki özet neden ayrıca karşılaştırılıyor? |
| TimestampVerifier.cs:64–67 | **İmza.** İmza neyi kanıtlar? İmzalayan sertifikanın "zaman damgası için yetkili" (EKU) olması neden önemli? |
| TimestampVerifier.cs:69–87 | **Zincir.** İşletim sisteminin güven deposu neden kullanılmıyor, yapılandırmadaki kök neden tek güven noktası? `RevocationMode.NoCheck` neyi kabul ediyor? `VerificationTime` neden şimdiki an değil de damga anı? (İpucu: TSA sertifikası bugün süresi dolmuş olabilir.) |
| TimestampVerifier.cs:89 | **genTime.** Fonksiyon sonunda hangi zamanı döndürüyor? |
| [EvaluationConfig.cs:133–137, :207–210](../../src/Fraud.Evaluation/EvaluationConfig.cs) | TSA kök dosyası nerede ayarlanıyor? İleriye dönük kipte yoksa neden en baştan duruluyor? |
| [EvaluationDataLoader.cs:158–159](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | `SubjectIdOf` neden yalnızca bu iş için dışarı açıldı? |

### 2.13 Yapılandırma: Güvenilmeyen girdi

| Yer | Soru |
|---|---|
| [EvaluationConfig.cs:63–71](../../src/Fraud.Evaluation/EvaluationConfig.cs) | `allowIntegerValues: false` ve `UnmappedMemberHandling.Disallow` neyi engelliyor? Yanlış yazılmış bir ayar adı eskiden ne olurdu? |
| EvaluationConfig.cs:74–79, :151–163 | Dosya boyutu ve bootstrap/pencere üst sınırı neden var (E7)? Kilitli kipte hiç bitmeyen bir çalıştırma neden tehlikeli? |
| EvaluationConfig.cs:165–193 | Doğrulamada hangi alanların hem alt hem üst sınırı var? `Enum.IsDefined` neden ayrıca çağrılıyor? |
| EvaluationConfig.cs:199–217 | `ValidateFor` neden `Validate`'ten ayrı? Hangi üç kipe özel kural var? Bilinmeyen sinyal adı neden kilitten önce reddediliyor? |
| [FindingsTests.cs:91–100](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs) | Reddedilen yedi yapılandırmayı tek tek açıkla. `prospectiveCommitmentAt` neden artık bilinmeyen alan? |

### 2.14 Veri yükleyici ve opak kimlik

| Yer | Soru |
|---|---|
| [EvaluationDataLoader.cs:35–39](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs) | Karar tarihi neden Türkiye saatiyle gün başı? UTC gün başı neden 3 saat geç ve neden sızıntı riski? Bu kural `DayGrid` ile nasıl uyuşuyor? |
| EvaluationDataLoader.cs:51–54 | Aynı alan adı birden fazla bültende geçiyorsa hangisi kullanılıyor? Neden en erken? |
| EvaluationDataLoader.cs:69–72 | E01: Pozitif listesindeki bir ad neden kontrol olamaz? |
| EvaluationDataLoader.cs:103–109 | Üç gözlem türü hangi `KnownAtPolicy` kuralından geçiyor? Satır satır eşleştir. Sertifika satırında `prospective` neden var? |
| EvaluationDataLoader.cs:123–125 | Yaş sınıfı nereden geliyor? İlk sertifikası hiç bilinmeyen pozitif hangi sınıfa giriyor ve bölmede ne oluyor? |
| EvaluationDataLoader.cs:161–162 | Opak kimlik `HMAC-SHA256(anahtar, ad)` ilk 16 karakter. Anahtarsız düz SHA-256 olsaydı biri kimliği gördüğünde ne yapabilirdi? 16 onaltılık karakter kaç bit? Çakışma riski var mı? |
| EvaluationDataLoader.cs:144–156 | Anahtar nerede tutuluyor? Kaybolursa ne olur? |
| EvaluationDataLoader.cs:164–167 | Küme kimliği neden ayrı bir `"cluster|"` önekiyle özetleniyor? |
| EvaluationDataLoader.cs:186–204 | Geçersiz satırın içeriği neden hiçbir yere yazılmıyor, yalnızca sayılıyor? (Kural 4.) |
| EvaluationDataLoader.cs:206–223 | `TryParseName` neden yalnızca harf kabul ediyor? `Enum.TryParse` "7" ya da "1,2" için ne yapardı? `TryParseBool` "evet" gördüğünde neden satırı reddediyor? |

### 2.15 Tarafsız örneklem (NeutralSampler)

| Yer | Soru |
|---|---|
| [NeutralSampler.cs:78–102](../../src/Fraud.Collectors/StaticCt/NeutralSampler.cs) | Kuralı kâğıda yaz: h, x, eşik. Neden "kelimeye bakmadan" seçmek baseline'ın adil ölçülmesi için şart? |
| NeutralSampler.cs:122–132 | Eşik = oran × 2^64. x düzgün dağılıyorsa x'in eşikten küçük olma olasılığı neden tam olarak oran? 2^64 neden `double` olarak tam yazılabilir? |
| NeutralSampler.cs:135–146 | Saf fonksiyon: Girdi tuz, ad, eşik. Neden ayrı ve `static`? Tuz ile ad arasına ayraç konmamış. Bu neden sorun değil? (İpucu: tuzun boyu sabit.) |
| NeutralSampler.cs:94–100 | Neden sertifika değil de kayıtlı alan adı düzeyinde? Neden tuz? Tuz bilinseydi biri ne yapabilirdi? |
| NeutralSampler.cs:159 | `*.ornek.example` joker adı nasıl ele alınıyor? |
| NeutralSampler.cs:28–31 | Oran yükseltilirse eski seçilenlerin hepsi neden yine seçilir? ([NeutralSampleTests.cs:67](../../tests/Fraud.Integration.Tests/StaticCt/NeutralSampleTests.cs)) |
| NeutralSampleTests.cs:47, :58 | 100.000 adda %1 seçimin standart sapması neden yaklaşık 31,5? 0,01 × 2^64 neden 184467440737095520 çıkıyor (tam değer ...516,16 iken)? |

### 2.16 Hepsini birleştiren testler

| Yer | Soru |
|---|---|
| [PipelineTests.cs:20–36](../../tests/Fraud.Evaluation.Tests/PipelineTests.cs) | "Bitti sayılır" ölçütü bu test. Rastgele sinyalle recall neden yaklaşık α (hedef yanlış alarm) çıkmalı? Çıkmasaydı ilk nereye bakardın? |
| PipelineTests.cs:24 | `now: Synthetic.Now` neden veriliyor? Verilmeseydi test yarın farklı sonuç verebilir miydi? |
| PipelineTests.cs:35 | `"p0"` metninin raporda geçmemesi neden kontrol ediliyor? |
| [Synthetic.cs:14–19](../../tests/Fraud.Evaluation.Tests/Synthetic.cs) | Uydurma karar tarihi neden TR gün başı? Uydurma "şimdi" neden 2028? |
| [TimestampAndRepoTests.cs:28, :40](../../tests/Fraud.Integration.Tests/Evaluation/TimestampAndRepoTests.cs) | Geçerli damga hangi zamanı veriyor, beyan edilen zamanı mı, TSA zamanını mı? Yanlış kök, değişmiş dosya ve başka isteğe ait cevap neden hiç zaman vermiyor? |
| TimestampAndRepoTests.cs:111, :122, :141 | Kök neden git kökü olmalı? Yok sayılan klasördeki işaret dosyası neden kök değil? Git hangi PATH girdilerinden çözülüyor? |

---

## 3. Elle hesaplama alıştırmaları

Kâğıt kalem kullan. Cevaplar en altta.

**A1. Recall ve FPR.** Test kümesinde 20 pozitif var, sinyal 7'sinde alarm veriyor. Çerçeve N'de 400 kontrol var, 8'inde alarm var. Recall, yanlış alarm oranı (FPR) ve örneklem içi precision kaç?

**A2. Taban oran düzeltmesi.** A1'deki r ve f ile π = 0,001 için düzeltilmiş precision kaç? π = 0,01 olsaydı kaç olurdu? Bir de günde 50.000 yeni alan adı görüldüğünü varsay. Günde kaç yanlış alarm beklenir?

**A3. Eşik seçimi.** Ayar kontrollerinin puanları: 0,95 0,40 0,72 0,88 0,15 0,60 0,33 0,91 0,50 0,05. Hedef α = 0,2. Eşik ne olur? Sonra test pozitiflerinin puanları 0,89 0,88 0,97 0,30. Kaçı alarm verir? Raporda recall nasıl yazılır?

**A4. Ham sayı kuralı.** Kilitli testte 6 pozitif var, 2'si yakalandı. Raporda ne yazar? "%33" yazmak neden yanıltıcı? (Aralığın kabaca ne kadar geniş olduğunu tahmin et.)

**A5. 4 yapraklı Merkle kökü (kavramsal).** Kayıtların c değerleri c0, c1, c2, c3 (sıralı). Yaprak özetlerini, iki iç düğümü ve kökü `H(...)` biçiminde yaz. c2'nin kanıt yolu hangi iki özetten oluşur? Doğrulayan kişi kökü hangi sırayla yeniden kurar? Bonus: 3 yaprakta kök nasıl yazılır?

**A6. Bootstrap neden küme düzeyinde?** 10 pozitif var. 6'sı aynı operatörün (tek küme K), hepsi yakalandı. Kalan 4'ü tek başına birer küme, hiçbiri yakalanmadı. Recall kaç? Küme düzeyinde bootstrap'ta bir tekrarda recall'un 0 çıkma olasılığı kaç? Alan adı düzeyinde bootstrap'ta kaç? Hangi aralık daha dürüst?

**A7. Eşleşmiş gecikme.** Ayar pozitiflerinin gecikme havuzu (sıralı): 10, 40, 100, 200 gün. Bir kontrolün ilk sertifikası 2026-03-01 00:00 UTC ve `SeededHash.Unit` 0,62 veriyor. Kontrolün referans zamanı (TR ve UTC olarak) ne olur? Sinyal kontrolü en son hangi anda görür?

**A8. KnownAt.** Karar 2026-04-05 (TR gün başı). Bir arşiv kaydı 2026-04-01'de alınmış. Geçmiş testinde bu kayıt sinyale görünür mü? Aynı kayıt 2026-03-20'de alınmış olsaydı? Bugün (2026-11-16) sorulan bir DNS kaydı görünür mü?

**A9. Bütünlük kuralı.** Pozitifler: p1 (bülten 2026/3, küme A), p2 (2026/25, küme B), p3 (2026/25, küme C), p4 (2026/50, küme C), p5 (2026/60, küme D). Başlangıçta her biri hangi bölmede? Bileşenler ne? Kaç birim taşınır? Sonra p1'in kümesini C yap ve tekrar hesapla. Bonus: 2026/95 bülteninde, küme C'de bir p6 eklensin (hiçbir aralıkta değil). İleriye dönük teste girer mi?

**A10. Tarafsız örneklem eşiği.** 0,01 × 2^64 onaltılık olarak yaklaşık `0x028F5C28F5C28F5C`. Bir adın özetinin ilk iki baytı `01 FF` ise seçilir mi? `02 10`? `02 A0`? `03 00`?

**A11. Gün ızgarası.** Bütün saatler Türkiye saati (UTC+3).
(a) `DayGrid.EndOfDayAtOrBefore` şu anlar için ne verir: 10 Mart 14:00; 9 Mart 23:59:59,9999999; 10 Mart 00:00:00 tam?
(b) Pozitifin kararı 10 Mart (referans 10 Mart 00:00). Son görünüm anı ne? Aynı pozitifin bir arşiv kaydının `KnownAt`'i 9 Mart 22:00. Son görünümde görünür mü?
(c) `AsOfView.Create`'e 9 Mart 15:00 verilirse görünümün `AsOf`'u ne olur? `KnownAt`'i 9 Mart 10:00 olan kayıt görünür mü?
(d) E4'ten önce kontrolün referansı "ilk sertifika + gecikme + 1 sn" idi. İlk sertifikası 2 Şubat 14:37 olan, gecikmesi 30 gün çekilen bir kontrolün son görünümü saat kaçtaydı? Pozitifinki saat kaçtaydı (UTC olarak yaz)? Şimdi ikisi saat kaçta?

**A12. Gizlenen sayıyı geri hesaplamak.** Eski raporda bir kontrol tabakası şöyle yazıyordu: `<5/40 = 7.5%`. Gizlenen isabet sayısını bul. Sonra aynı yöntemi şuna uygula: `<5/12 = 0%`. Son olarak: Bu iki örnek, yalnızca sayıyı gizleyip oranı yazmanın neden yetmediğini nasıl gösteriyor?

**A13. Yeni küçük hücre kuralı.** `IsSmallCell` ve `Format` ile aşağıdaki dört hücre için raporda ne yazar? Her birinde okuyucu gerçek sayı hakkında en fazla neyi bilebilir?
(a) 3 birim, 1 alarm. (b) 40 birim, 3 alarm. (c) 12 birim, 11 alarm. (d) 40 birim, 20 alarm.

---

## 4. Mülakat soruları

Her cevap kısa olsun: bir cümle ana fikir, bir cümle projeden somut örnek, varsa bir cümle sınır.

**S1. Neden accuracy değil de precision ve recall?**
Taslak: Veride pozitifler çok az. Gerçek dünyada yeni alan adlarının belki binde biri. Her şeye "temiz" diyen bir model %99,9 accuracy alır ama hiçbir şey yakalamaz. Recall "yakalanması gerekenlerin kaçını yakaladık", yanlış alarm oranı "temizlerin kaçına boşuna alarm verdik" sorusunu ayrı ayrı cevaplıyor. Precision'ı da örneklemdeki oranla değil, taban oranla düzeltip veriyorum.

**S2. Data leakage'ı projende nasıl önledin?**
Taslak: Dört katman var. (1) Her gözlemin "ne zaman bilinirdi" anı (`KnownAt`) var. Sinyal yalnızca `AsOfView` üzerinden veri görüyor, o da karar anından önce bilinenleri içeriyor. Bugünkü DNS geçmiş bir karara ulaşamıyor. (2) Eşik yalnızca ayar verisinde seçiliyor. Bülten ve küme, union-find ile tek parça halinde tek tarafa gidiyor. (3) Puanlama anı: Kontrolleri ilk sertifika gününde puanlasaydık "sertifika yaşı" tek başına %90'ın üstünde recall veriyordu. Eşleşmiş gecikmeyle bu şansa indi. (4) Yan kanallar: Görünüm anının saati ve "veri var mı" bilgisi de etiketi taşıyabiliyordu. Gün ızgarası ve kapsama uyarısı bunları kapatıyor.

**S3. Taahhüt şeması nasıl çalışıyor?**
Taslak: Kayıtları önce normalleştiriyorum: kayıtlı alan adı, küçük harf, punycode. Her kayda 32 baytlık ayrı bir rastgele tuz veriyorum. c = SHA-256(tuz ‖ kayıt). Yapraklar c'ye göre sıralanıyor, RFC 6962 Merkle ağacı kuruluyor. Repoya yalnızca kök, kayıt sayısı ve bir önceki taahhüdün özeti giriyor. Açık dosya RFC 3161 ile damgalanıyor. Sonradan tek bir kaydı, tuzu ve kanıt yoluyla açıyorum. Diğer kayıtlar kapalı kalıyor.

**S4. Neden tek bir özet değil de tuzlu Merkle taahhüdü?** (ROADMAP 8.13)
Taslak: Tek özetle bir kaydı kanıtlamak için bütün listeyi açmam gerekirdi. Merkle ile log(n) özetlik bir yol yetiyor. Tuz olmasaydı biri tahmin ettiği adları özetleyip listede olup olmadığını deneyebilirdi. Tuz kayıt başına ayrı, çünkü ortak tuz bir kayıtla birlikte açılınca diğerleri de denenebilir hale gelirdi.

**S5. Eşleşmiş gecikme nedir, neden gerekti?**
Taslak: Pozitif karar gününde puanlanıyor, kontrolün karar günü yok. Kontrolün puanlama anını "ilk sertifika + ayar pozitiflerinden çekilmiş bir gecikme"den sonraki ilk gün başı olarak seçiyorum. Böylece iki grup ilk sertifikalarından aynı dağılımda uzaklıkta, aynı saatte puanlanıyor. Havuz yalnızca ayar pozitiflerinden kuruluyor, test pozitiflerinin bilgisi girmiyor.

**S6. Güven aralığını neden küme ve bülten düzeyinde bootstrap ile hesapladın?**
Taslak: Aynı bültende toplu engellenen ya da aynı operatöre ait siteler bağımsız değil. Tek tek alan adını yeniden örneklemek, gerçekte 5 bağımsız gözlem varken 50 varmış gibi davranır ve aralığı daraltır. Önce kümeleri çekip her kümenin bütün sitelerini birlikte alıyorum.

**S7. Taban oran düzeltmesini bir örnekle anlat.**
Taslak: Recall 0,5, yanlış alarm %2, gerçek yaygınlık binde 1 ise Bayes ile precision yaklaşık %2,4. Yani alarmların kabaca 40'ta biri SPK listesine girecek bir site. Örneklem içi precision bizim seçtiğimiz pozitif/kontrol oranına bağlı, gerçek dünyayı göstermez. π'yi henüz ölçmedim, bu yüzden raporda varsayım olarak yazıyor.

**S8. "Kilitli test bir kez çalışır" kuralını nasıl zorladın? Sınırı ne?**
Taslak: Kilit dosyası çalıştırmadan önce `FileMode.CreateNew` ile yazılıyor. Dosya varsa işletim sistemi açmayı reddediyor. Yarıda kesilen çalıştırma da kullanılmış sayılıyor. Kilit git'in ortak klasöründe duruyor, böylece worktree'ler aynı kilidi görüyor. Kilitten önce şunlar denetleniyor: commit kimliği 40 karakter, HEAD ile aynı, çalışma ağacı temiz (gizleme işaretleri dahil), yapılandırma git'te, çalışan program aynı commit'ten derlenmiş. Yapılandırma hatası kilitten önce yakalanıyor, yani tek hakkı harcamıyor. Sınır: Başka bir klasöre klonlayan biri yeni bir kilit açabilir. Buna karşı tek çare dış çapa: Kilit kaydının özeti commit ediliyor ve zaman damgası alınıyor.

**S9. Union-find neden, nasıl çalışıyor?**
Taslak: "Aynı bülten" ve "aynı küme" ilişkileri zincirleniyor. A ile B aynı bültende, B ile C aynı kümedeyse üçü tek parça. Union-find bu bileşenleri neredeyse doğrusal zamanda buluyor. Her öğenin bir ebeveyni var. İki öğe aynı köke çıkıyorsa aynı bileşendeler. Kökü sıralamada küçük olandan seçiyorum, böylece sonuç ekleme sırasından bağımsız ve tekrar edilebilir oluyor.

**S10. CDN kenarı neden sayılmıyor?** (ROADMAP 8.9)
Taslak: Binlerce ilgisiz site aynı CDN IP'sini paylaşır. Bu kenarı kullansaydım neredeyse her şey tek kümeye düşerdi, "aynı operatör" anlamı kalmazdı. Bölme de anlamsızlaşırdı. Bu yüzden CDN veya bulut üzerinden gelen ağ kenarları kümelemede atlanıyor.

**S11. Rastgele sinyali neden sisteme soktun?**
Taslak: Ölçüm hattının kendisini test etmek için. Rastgele bir sinyal şanstan iyi olamaz. Yanlış alarm %10'a ayarlanınca recall da yaklaşık %10 çıkmalı. Çıkmasaydı bölmede, eşikte ya da metrikte bir sızıntı olduğunu anlardım. Sınavın kendisi bozuksa gerçek sinyalin sonucu da anlamsız olur.

**S12. Merkle ağacında 0x00 ve 0x01 önekleri ne işe yarıyor?**
Taslak: Yaprak ile iç düğüm farklı önekle özetleniyor. Önek olmasaydı, bir iç düğümün iki çocuğunun birleşimini "yaprak verisi" diye sunup ağaçta olmayan bir kaydı var gibi gösterebilirdim. RFC 6962'deki kural bu. Kod RFC'nin test vektörleriyle doğrulanıyor.

**S13. Opak kimlik için neden HMAC, neden düz SHA-256 değil?**
Taslak: Alan adlarının uzayı tahmin edilebilir. Düz özeti gören biri aday adları özetleyip eşleştirebilir. HMAC'in anahtarı `data/private` altında. Anahtarı bilmeyen kimliği geri çeviremez, deneme de yapamaz. Böylece ölçüm kodu ve rapor alan adını yanlışlıkla bile basamaz.

**S14. Tarafsız örneklem nasıl çalışıyor, neden gerekli?**
Taslak: Baseline'ı (kelime kuralı) adil ölçmek için kelimeden bağımsız bir kontrol çerçevesi lazım. Kelime filtreli akıştan gelen kontrolde kural tanım gereği alarm verir. Her kayıtlı alan adı için SHA-256(tuz ‖ ad)'ın ilk 8 baytını sayı olarak okuyorum. Sayı oran × 2^64'ten küçükse seçiyorum. Karar deterministik ve kelimeye bakmıyor. Tuz gizli olduğu için kimse örneğe girmeyi ya da girmemeyi seçemiyor.

**S15. Eşiği nasıl seçtin? Eşit puanlarda ne oluyor?**
Taslak: Ayar kontrollerini büyükten küçüğe sıralıyorum. İzin verilen alarm sayısı floor(α·n). Eşik, o sıradaki puanın hemen üstündeki `double`. Eşitlikte eşit puanların hiçbiri alarm vermiyor, yani oran hedefin üstüne çıkmıyor, altında kalıyor. Test verisi bu adıma hiç girmiyor.

**S16. 10'dan az birimde neden yüzde vermiyorsun? Küçük sayıları neden gizliyorsun?**
Taslak: 2/6 için %95 aralık kabaca %4 ile %78 arası. "%33" yazmak sanki bir ölçüm yapılmış gibi gösterir. Küçük sayı ise başka bir sorun: Tek birimli bir tabakada "1/1" yazarsam, o birimin hangi kurum olduğunu bilen okuyucu sonucu adıyla çıkarır. İlk sürümde yalnızca sayıyı gizliyordum. Oran yazıldığı için sayı geri hesaplanabiliyordu ("<5/40 = %7,5" demek 3 demek). Tümleyen de açıktı: "11/12" alarm almayan tek kurumu ele veriyordu. Şimdi isabet ya da tümleyen 5'ten küçükse oran, aralık ve ondan türeyen precision da yazılmıyor.

**S17. Neden System.Random kullanmadın?**
Taslak: Algoritması .NET sürümleri arasında değişebilir. Bölmenin başka bir makinede aynen tekrar edilebilmesi gerekiyor. Bölme için SHA-256 tabanlı bir sayı, bootstrap için yayımlanmış sabitleri olan SplitMix64 kullandım. Tohumlar seçimden önce yazılı.

**S18. Projende bulduğun bir hatayı anlat.**
Taslak: Bölüm 5.3'teki E4 hikâyesini anlat. Kısa hali: "Saate bakan sahte bir sinyal 20 pozitife 20 kontrolde etiketi kusursuz ayırdı. Sebebi, pozitiflerin hep aynı saatte, kontrollerin rastgele saatlerde puanlanmasıydı. Bütün anları Türkiye günü sonuna hizaladım ve bunu sabitleyen bir test yazdım." Sonra şunu ekle: "Bu tür sızıntıyı bulmanın yolu, etiketi bilmemesi gereken aptal sinyaller yazıp sistemin onları ödüllendirmediğini göstermek."

**S19. Gizli dosyaların yanlışlıkla repoya girmesini nasıl önledin?**
Taslak: Bütün gizli yollar depo köküne bağlı ve `data/private` dışına yazmayı reddediyor. Kök de yalnızca bir işaret dosyasına göre değil, git'in kendi köküne göre doğrulanıyor. Eskiden göreli yol çalışma klasörüne göre çözülüyordu, iç içe bir `data/private` git tarafından yok sayılmıyordu. `.gitignore`'a `**/data/private/` ve `*.opening.json` eklendi. Yani iki kat koruma var.

**S20. Önceden kaydı (kilit ve taahhüt) neden kimsenin atlatamayacağı şekilde yapmak gerekir?**
Taslak: Önceden kayıt, "sonucu gördükten sonra kuralı değiştirmedim" sözünün kanıtı. Atlatılabiliyorsa kanıt değil, yalnızca bir iyi niyet beyanıdır. Bu yüzden her deliği bir saldırgan gibi düşünerek kapattık:
1. **E1:** Kilit yolu yapılandırmadan geliyordu, başka bir yol yazınca "ilk" çalıştırma yeniden alınabiliyordu. `--commit` serbest metindi. Yolu sabitledik, commit'i HEAD'e ve temiz ağaca bağladık.
2. **E8:** Yok sayılan bir klasöre konan sahte işaret dosyası yeni bir "kök" ve yeni bir kilit açıyordu. Kökü git'in köküne bağladık, kilidi worktree'lerin paylaştığı ortak git klasörüne taşıdık. Klonlamaya karşı dış çapa ekledik.
3. **E9:** "Temiz mi" sorusunu soran git'in kendisi sahte olabilirdi. Ayrıca `skip-worktree` işaretiyle değişmiş bir dosya `git status`'tan gizlenebiliyordu, çalışan program da başka bir commit'ten derlenmiş olabiliyordu. Git'i güvenilir bir yoldan başlatıyoruz, gizleme işaretlerini reddediyoruz, derleme commit'ini HEAD ile karşılaştırıyoruz.
4. **E10:** İleriye dönük testin tarihi kendi yazdığımız bir alandı, yani geriye tarihlenebilirdi. Artık tarih, imzası ve zinciri doğrulanmış bir RFC 3161 damgasından geliyor. Doğrulanamazsa sonuç basılmıyor.
Sınır: Kendi makinemde yönetici olan biri her şeyi değiştirebilir. Bu yüzden son güvence dış çapalar: commit geçmişi ve üçüncü tarafın zaman damgası.

**S21. RFC 3161 zaman damgası nedir?**
Taslak: Bir zaman damgası kuruluşunun (TSA) "bu özet, şu tarihte bana gösterildi" diye imzaladığı bir belge. Noter onaylı tarihe benzer: Noter belgeyi okumaz, yalnızca parmak izini mühürler. Ben açık taahhüt dosyasının SHA-256'sını bir rastgele sayıyla (nonce) gönderiyorum. Gelen cevapta beş şeyi kontrol ediyorum: Özet benim dosyamın özeti mi (messageImprint), nonce benim nonce'um mu, imza geçerli mi, imzalayan sertifika güvendiğim köke zincirleniyor mu, sertifika zaman damgası için yetkili mi. Hepsi tutarsa taahhüt anı olarak damgadaki `genTime`'ı kullanıyorum. Kendi yazdığım tarihe güvenmiyorum. Sınır: Sertifika iptal listesi çevrimdışı olduğu için kontrol edilmiyor, güven de yapılandırmadaki kök sertifikaya dayanıyor.

**S22. Yapay zekâyı nerede kullandın?** (ROADMAP 8.14) Bölüm 5.5'e bak.

---

## 5. Bilinen sınırlar, kapatılan bulgular ve dürüst cevaplar

Bir mülakatçıyı en çok etkileyen şey, sınırını kendisi söyleyen adaydır. Bu bölümü ezberle değil, anla.

### 5.1 Varsayımlar ve kalan sınırlar

- **KnownAt = not_before bir varsayım.** CT logları sertifikayı genelde hemen yayımlar, ama geriye tarihli ya da geç loglanmış sertifika küçük bir sızıntı payı bırakır. Daha sıkı kural: `max(not_before, logdaki giriş zamanı)`. İleriye dönük testte bu varsayım yok, orada `observed_at` kullanılıyor.
- **Arşiv payı 7 gün bir tahmin.** Dizine girme gecikmesi ölçülmedi. Onay bekliyor.
- **π = 0,001 ölçülmedi.** Raporda varsayım diye yazıyor. Gerçek değer tarafsız örneklemden ölçülünce değişecek. Düzeltilmiş precision π'ye çok duyarlı: A2'de π 10 kat artınca precision yaklaşık 9 kat artıyor.
- **SPK karar tarihi ilk tespit tarihi değil.** Erken uyarı süresi "karardan kaç gün önce", "siteden kaç gün sonra" değil.
- **Etiket "SPK listesinde" demek.** "Dolandırıcı" demek değil (Kural 3). Precision da "SPK etiketine göre".
- **Geçmiş veri testi temiz değil.** Aşama 0'da bu bültenlere bakıldı. Rapor bunu yazıyor ([EvaluationRunner.cs:118–121](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)).
- **Kilit yerel bir dosya.** Worktree'ler aynı kilidi görüyor, ama başka bir klasöre klonlamaya karşı kodda koruma yok. Güvence, kilit kaydının commit edilmiş özeti ve zaman damgası. Bu adım elle yapılıyor, yapılmazsa kanıt eksik kalır.
- **Zaman damgasında iptal kontrolü yok.** Ağa bağlanmadığımız için TSA sertifikasının iptal edilip edilmediğine bakılmıyor. Güven, yapılandırmadaki tek kök sertifikaya dayanıyor.
- **Kontroller için veri henüz toplanmıyor.** Kapsama uyarısı bu yüzden şu an büyük ihtimalle her çalıştırmada çıkacak. Uyarı çıktıkça sonuç "güvenilmez" diye okunmalı.

### 5.2 İlk okumada bulunan 10 ölçüm sorunu: Hepsi kapatıldı

| # | Sorun | Durum ve nasıl kapatıldı |
|---|---|---|
| 1 | İleriye dönük testte sertifika `not_before` ile biliniyor sayılıyordu, alarm sonradan yeniden hesaplanıyordu | **Kapatıldı.** Sertifika bu kipte `observed_at` ile bilinir ([Observation.cs:47–48](../../src/Fraud.Evaluation/Model/Observation.cs)) ve alarm kümesi `VerifyOpening`'den geçen taahhüt dosyasından okunur ([EvaluationRunner.cs:254–282](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). |
| 2 | `Excluded` birimler bütünlük kuralına girmiyordu, ayardaki bir operatör ileriye dönük teste girebiliyordu | **Kapatıldı.** Dışarıdakiler de bileşene giriyor, ayara bağlı olanlar `TiedToTuning` ile ileriye dönük testten çıkıyor ([Splitter.cs:184–191](../../src/Fraud.Evaluation/Splitting/Splitter.cs)). |
| 3 | Kontrol referansı bugünden sonraya düşüp bugünkü DNS'i görebiliyordu | **Kapatıldı.** Referansı "şimdi"den sonra olan kontrol çıkıyor ([ReferenceTimeAssigner.cs:79–83](../../src/Fraud.Evaluation/Splitting/ReferenceTimeAssigner.cs)), F'den sonrakiler yalnızca kilitli pencereye girebiliyor ([Splitter.cs:130–134](../../src/Fraud.Evaluation/Splitting/Splitter.cs)). |
| 4 | Tahmin yalnızca gözlem anlarında yapılıyordu, zamanla büyüyen puanda ilk alarm geç bulunuyordu | **Kapatıldı.** Predictor her günün son anında değerlendiriyor ([Predictor.cs:34–42](../../src/Fraud.Evaluation/Running/Predictor.cs)). |
| 5 | Kilit, yapılandırma denetiminden önce alınıyordu | **Kapatıldı.** `ValidateFor` ve donmuş durum denetimi kilitten önce çalışıyor ([EvaluationCommands.cs:47, :53](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)). |
| 6 | Erken uyarıda ve küçük tabakada ham sayı kuralı yoktu | **Kapatıldı.** 10'dan az yakalanan pozitifte süre ([MetricsCalculator.cs:111–114](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs)), 10'dan küçük tabakada precision ([MetricsCalculator.cs:82](../../src/Fraud.Evaluation/Metrics/MetricsCalculator.cs)) yazılmıyor. |
| 7 | İlk sertifikası karardan sonra olan pozitif "yeni" sayılıyordu | **Kapatıldı.** Yeni alt grup "yaşı bilinmiyor" ([Subject.cs:144–153](../../src/Fraud.Evaluation/Model/Subject.cs)). |
| 8 | Sertifikasız kontrol çıkıyordu, sertifikasız pozitif kalıyordu | **Kapatıldı.** Geçmiş ve kilitli testte bölme ([Splitter.cs:74–79](../../src/Fraud.Evaluation/Splitting/Splitter.cs)), ileriye dönük testte test filtresi ([EvaluationRunner.cs:273](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)) iki tarafa da aynı kuralı uyguluyor. |
| 9 | CSV'de tanımsız enum sayısı kabul ediliyor, "True" sessizce yanlış sayılıyordu | **Kapatıldı.** `TryParseName` yalnızca tanımlı adları, `TryParseBool` yalnızca true/false'u kabul ediyor ([EvaluationDataLoader.cs:206–223](../../src/Fraud.Collectors/Evaluation/EvaluationDataLoader.cs)). Yapılandırmada da sayısal enum reddediliyor. |
| 10 | Taahhüt kayıtları normalleştirilmiyordu | **Kapatıldı.** Kayıtlar taahhütten önce ve `--record` ile doğrulamada aynı kuraldan geçiyor ([EvaluationCommands.cs:132–142, :238–242](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)). |

### 5.3 Bulunup kapatılan güvenlik bulguları

Güvenlik denetimi bu modül için on bulgu yazdı (E1–E10, ayrıntı [THREAT_MODEL.md](../THREAT_MODEL.md) Bölüm 5.3). Denetimin yöntemi iyi bir mülakat konusu: Her bulgu için **önce kırmızı yanan bir test** yazıldı. Test düzeltmeden önce başarısız oldu, düzeltmeden sonra geçti. Yani "düzelttim" sözü bir testle kanıtlı.

Aşağıdaki hikâyeleri kendi kelimelerinle anlatabilmelisin. Kalıp hep aynı: **Sorun neydi, nasıl bulundu, nasıl düzeltildi, ne öğrendim?**

**E1: "Bir kez" kuralı atlatılabiliyordu.**
- *Sorun:* Kilit dosyasının yolu yapılandırmadan geliyordu ve çalışma klasörüne göre çözülüyordu. Yapılandırmaya başka bir yol yazan ya da komutu başka klasörden çalıştıran biri `CreateNew`'den "ilk" bileti yeniden alıyordu. Rapor da "YENİDEN ÇALIŞTIRMA" demiyordu. Ayrıca `--commit` serbest metindi, "son" yazmak bile kabul ediliyordu. Yani kayıt hangi kodla çalışıldığını kanıtlamıyordu.
- *Nasıl bulundu:* Denetim, kuralı atlatmayı deneyen testler yazdı. Biri yapılandırmayla kilidi taşıdı, öteki uydurma bir commit verdi. İkisi de geçti, yani kural delikti.
- *Nasıl düzeltildi:* Yol yapılandırmadan okunmuyor ([EvaluationConfig.cs:139–145](../../src/Fraud.Evaluation/EvaluationConfig.cs)), kilit git'in ortak klasöründe ([RepoContext.cs:86–100](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). Commit 40 karakterlik bir git kimliği olmalı ve HEAD ile eşleşmeli. Çalışma ağacı temiz olmalı. Yapılandırma dosyası git'te izlenmeli ([EvaluationCommands.cs:289–326](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)).
- *Ders:* Bir kuralı zorlayan kodun girdisini kuralı çiğnemek isteyen kişi veriyorsa, o kural zorlanmıyor demektir.

**E2: Gizli liste repoya girebiliyordu.**
- *Sorun:* `data/private/commitments` göreli bir yoldu. Komut `src/Fraud.Collectors` içinden çalıştırılırsa açıklama dosyası (bütün kayıtlar ve tuzları) `src/Fraud.Collectors/data/private/` altına yazılıyordu. `.gitignore`'daki `data/private/` kalıbının ortasında `/` olduğu için kalıp yalnızca depo köküne bağlanıyordu. İç içe klasör yok sayılmıyordu, yani bir `git add .` şüpheli listeyi repoya alırdı (Kural 8).
- *Nasıl bulundu:* Denetim komutu başka bir klasörden çalıştırıp dosyanın nereye düştüğüne baktı. Sonra `git check-ignore` ile git'in o dosyayı yok saymadığını doğruladı.
- *Nasıl düzeltildi:* Bütün gizli yollar `RepoContext.PrivatePath` ile depo köküne bağlandı ([RepoContext.cs:73–84](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). Kök bulunamazsa komut duruyor. `.gitignore`'a `**/data/private/` ve `*.opening.json` eklendi ([.gitignore:4–5](../../.gitignore)).
- *Ders:* Göreli yol "nereden çalıştırıldığına" bağlıdır. Güvenlik iki kat olmalı: Kod yanlış yere yazmasın, yazarsa da git almasın.

**E4: Saat etiketi ele veriyordu. En iyi hikâye bu.**
- *Sorun:* Pozitifin referansı karar günü 00:00 (TR) idi. Bu yüzden son görünüm anı her pozitifte UTC 20:59:59,9999999 oluyordu. Kontrolün referansı "ilk sertifika + gecikme + 1 sn" idi, yani günün herhangi bir saati. Sinyal `view.AsOf`'u görebiliyordu.
- *Nasıl bulundu:* Denetim yalnızca `view.AsOf`'un saatine bakan sahte bir sinyal yazdı. Bu sinyal hiçbir veriye bakmıyordu. 20 pozitif ve 20 kontrolde etiketi **kusursuz** ayırdı. Rastgele sinyal şansa yakın çıktığı için hat "temiz" görünüyordu. Hatayı ancak bilerek etiket arayan bir sinyal gösterdi.
- *Neden tehlikeli:* Hafta 7'de gerçek bir sinyal "gün içindeki saat" ya da "saat cinsinden yaş" gibi bir özellik kullansaydı, bu sızıntıyı kimse fark etmeden sonucu şişirirdi.
- *Nasıl düzeltildi:* Bütün anlar Türkiye gününün son anına hizalandı ([DayGrid.cs](../../src/Fraud.Evaluation/Model/DayGrid.cs)). `AsOfView` görünüm anını aşağı yuvarlıyor ([AsOfView.cs:53](../../src/Fraud.Evaluation/Model/AsOfView.cs)). Kontrol referansı da bir gün başına yuvarlanıyor ([ReferenceTimeAssigner.cs:70](../../src/Fraud.Evaluation/Splitting/ReferenceTimeAssigner.cs)). Test: Bütün görünümlerin saati tek bir değer ([FindingsTests.cs:34](../../tests/Fraud.Evaluation.Tests/FindingsTests.cs)).
- *Ders:* Rastgele sinyal hattın bozuk olmadığını gösterir, sızıntı olmadığını göstermez. Sızıntıyı bulmak için etiketi bulmaya **çalışan** aptal sinyaller yazılır: sertifika yaşı, saat, "veri var mı". Her biri şansta kalmalı.

**E8: Sahte kök ve ikinci kilit.**
- *Sorun:* Kök, yukarı doğru bulunan ilk `FraudLab.slnx` idi. Git'in yok saydığı `data/private/x/` gibi bir klasöre konan sahte bir işaret dosyası çalışma ağacını "temiz" bırakıyordu. Oradan çalıştırılan kilitli test, HEAD ve temizlik denetimlerini üst depodan geçiyor, ama kilidi yeni "kök"te ilk kez açıyordu. `git worktree add` ve başka klasöre klonlama da aynı sonucu veriyordu.
- *Nasıl düzeltildi:* Bulunan klasör `git rev-parse --show-toplevel` ile aynı olmalı ([RepoContext.cs:55–61](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). Kilit, worktree'lerin paylaştığı `git-common-dir` altında. Klonlamaya karşı dış çapa yazdırılıyor ([EvaluationCommands.cs:392–414](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)).

**E9: Sahte git ve gizlenmiş değişiklik.**
- *Sorun:* Git yalnızca adıyla başlatılıyordu. Windows böyle bir adı ararken PATH'ten önce çalışma klasörüne bakar. Depo klasörüne başka bir yerel hesabın koyduğu `git.exe`, hem senin adına kod çalıştırabilir hem de "HEAD şu, ağaç temiz" diye yalan söyleyebilirdi. Ayrıca `skip-worktree` ya da `assume-unchanged` işaretli bir dosya değişse bile `git status` boş görünüyordu. Çalışan programın hangi commit'ten derlendiği de denetlenmiyordu.
- *Nasıl düzeltildi:* Git tam yoldan, PATH'in yalnızca mutlak ve çalışma klasörü dışındaki girdilerinden çözülüyor, argümanlar tek tek veriliyor ([RepoContext.cs:147–247](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). `ls-files -v` ile gizleme işaretleri reddediliyor ([RepoContext.cs:110–123](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). Derleme commit'i HEAD ile karşılaştırılıyor ([EvaluationCommands.cs:308–312](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)).
- *Ders:* "Güvendiğin aracı nereden çalıştırdığın" da güvenlik sorusudur.

**E10: Geriye tarihlenebilen taahhüt.**
- *Sorun:* İleriye dönük testin taahhüt anı, açık dosyadaki `CreatedAt`'ti. Bu alanı biz yazıyoruz. Kararlar bilindikten sonra geriye tarihli bir taahhüt "ileriye dönük" sonuç verebilirdi.
- *Nasıl düzeltildi:* Zaman, imzası, özeti, nonce'u ve zinciri doğrulanmış RFC 3161 cevabından alınıyor ([TimestampVerifier.cs](../../src/Fraud.Collectors/Evaluation/TimestampVerifier.cs)). Doğrulanamazsa veri bile yüklenmiyor ([EvaluationCommands.cs:76–85](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)).

Diğer dördü kısaca:
- **E3:** Aynı adla ikinci taahhüt eski tuzları siliyordu. Artık var olan dosyanın üzerine yazılmıyor ve yazma atomik ([EvaluationCommands.cs:155–161, :416–439](../../src/Fraud.Collectors/Evaluation/EvaluationCommands.cs)).
- **E5:** `Previous` zinciri yazılıyor ama denetlenmiyordu. Artık `VerifyPrevious` ve `commit-verify --previous` var ([Commitment.cs:186–193](../../src/Fraud.Evaluation/Commitments/Commitment.cs)).
- **E6:** Küçük tabakada "1/1" gibi sayılar basılıyordu. Artık isabet ya da tümleyen 5'ten küçükse sayı, oran, aralık ve türetilen precision yazılmıyor ([EvaluationRunner.cs:94–103, :127–161](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)).
- **E7:** Yapılandırmanın üst sınırı ve boyut sınırı yoktu, bilinmeyen alanlar sessizce yok sayılıyordu. Artık hepsi reddediliyor ([EvaluationConfig.cs:63–79, :180–192](../../src/Fraud.Evaluation/EvaluationConfig.cs)).

### 5.4 İkinci okumada görülen 5 nokta: Hepsi kapatıldı

| # | Sorun | Durum ve nasıl kapatıldı |
|---|---|---|
| 1 | İleriye dönük testte sertifikasız pozitif giriyor, sertifikasız kontrol girmiyordu | **Kapatıldı.** İleriye dönük testte Excluded'ın tamamı değil, sertifikası bilinmeyen birimler iki tarafta da dışlanıyor. Çünkü yeni pozitifler de Excluded etiketi taşıyor ([EvaluationRunner.cs:268–275](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). |
| 2 | Oran, gizlenen küçük sayıyı ele veriyordu; tümleyen gizlenmiyordu | **Kapatıldı.** İsabet ya da tümleyen 5'ten küçükse yalnızca "<5/40" ya da "≥8/12" yazılıyor. Oran, aralık, precision ve günlük alarm da yazılmıyor ([EvaluationRunner.cs:94–103, :140–161](../../src/Fraud.Evaluation/Running/EvaluationRunner.cs)). |
| 3 | Kapsama denetimi kaydın ne zaman bilindiğine bakmıyordu | **Kapatıldı.** Yalnızca son görünüm anından önce bilinen kayıtlar sayılıyor ([CoverageCheck.cs:27–36](../../src/Fraud.Evaluation/Metrics/CoverageCheck.cs)). |
| 4 | `Subject.cs`'te belge yorumu kaymıştı | **Kapatıldı.** Her yorum kendi üyesinin üstünde ([Subject.cs:140–156](../../src/Fraud.Evaluation/Model/Subject.cs)). |
| 5 | Git çağrısı hata akışını okumuyordu, zaman aşımında çıkış koduna güveniliyordu | **Kapatıldı.** İki akış da okunuyor, süre aşılırsa süreç öldürülüyor ve sonuç başarısız sayılıyor ([RepoContext.cs:147–191](../../src/Fraud.Collectors/Evaluation/RepoContext.cs)). |

### 5.5 "Yapay zekâ destekli geliştirdim" cümlesi

Bunu saklama, savunmaya da geçme. Düz söyle, sonra hemen anladığını göster. Kalıp şu:

> "Ölçüm ve değerlendirme modülünü yapay zekâ destekli geliştirdim. Kuralları ben koydum: Hangi verinin ne zaman bilindiği, bölmenin bülten ve küme düzeyinde yapılması, kilitli testin bir kez çalışması. Bunlar tarihli karar kayıtlarında yazılı. Kodun her satırını okudum ve anlatabilirim. Denetimde bulunan sızıntıları da biliyorum. Mesela saate bakan bir sinyalin etiketi nasıl bulduğunu ve bunu nasıl kapattığımızı anlatabilirim."

Neden işe yarıyor: (1) Gerçeği söylüyorsun. (2) Senin kararın olan kısmı ayırıyorsun. (3) Sınanmaya davet ediyorsun. Mülakatçı davet ettiğin yeri sorar, sen de en iyi bildiğin yerden başlarsın.

Yapma: "Hepsini ben yazdım" deme. DNS istemcisi gibi gerçekten senin yazdığın kısımla bu modülü karıştırma. "Yapay zekâ yazdı, ben bakmadım" izlenimi de verme. Bir satırı açıklayamıyorsan "bunu şu an tam hatırlamıyorum, şu dosyada, mantığı şuydu" de. Uydurma.

CV'ye yalnızca açıklayabildiğin işi yaz. Bu rehberdeki tabloların hepsini koda bakmadan cevaplayabildiğin gün, bu modül CV'ye girer.

---

## Cevaplar

**A1.** Recall = 7/20 = 0,35. FPR = 8/400 = 0,02. Örneklem içi precision = 7/(7+8) = 7/15 ≈ 0,467. Dikkat: Bu son sayı bizim seçtiğimiz 20'ye 400 oranına bağlı, gerçek dünyayı göstermez.

**A2.** π = 0,001: pay = 0,35 × 0,001 = 0,00035. Payda = 0,00035 + 0,02 × 0,999 = 0,00035 + 0,01998 = 0,02033. PPV ≈ 0,0172, yani yaklaşık %1,7. Kabaca her 58 alarmdan biri pozitif. π = 0,01: 0,0035 / (0,0035 + 0,0198) = 0,0035 / 0,0233 ≈ 0,150, yani %15. Günlük yanlış alarm: 0,02 × 50.000 = 1.000. Bu sayı, sinyal %35 yakalasa bile bir insanın her gün 1.000 alarma bakamayacağını gösteriyor. Rakamı yazmanın önemi bu.

**A3.** n = 10, izin verilen alarm = floor(0,2 × 10) = 2. Büyükten küçüğe: 0,95 0,91 0,88 0,72 ... 0 tabanlı 2. sıra 0,88. Eşik = 0,88'in hemen üstü. Kontrollerde alarm: 0,95 ve 0,91, yani 2/10 = 0,2 ≤ α. Pozitifler: 0,89 alarm (0,88'den büyük), 0,88 alarm **değil** (eşik 0,88'in üstünde), 0,97 alarm, 0,30 değil. Recall 2/4. İsabet 2 < 5, yani küçük hücre. Rapor "<5/<5 (küçük hücre: sayı, oran ve aralık yazılmaz)" yazar.

**A4.** İsabet 2 < 5, küçük hücre: "<5/6 (küçük hücre: sayı, oran ve aralık yazılmaz)". 2/6 için kesin (Clopper–Pearson) %95 aralığı yaklaşık %4 ile %78 arası. "%33" yazmak bu belirsizliği gizler.

**A5.** Yapraklar: L0 = H(00 ‖ c0), L1 = H(00 ‖ c1), L2 = H(00 ‖ c2), L3 = H(00 ‖ c3). n = 4, k = 2. Sol iç düğüm N01 = H(01 ‖ L0 ‖ L1), sağ iç düğüm N23 = H(01 ‖ L2 ‖ L3). Kök = H(01 ‖ N01 ‖ N23). c2'nin kanıtı alttan üste [L3, N01]. Doğrulama: Önce L2'yi c2'den hesapla, sonra N23 = H(01 ‖ L2 ‖ L3), sonra kök = H(01 ‖ N01 ‖ N23), sonra verilen kökle karşılaştır. L2 solda olduğu için L3 sağa, N23 sağda olduğu için N01 sola konur. 3 yaprakta k = 2: kök = H(01 ‖ H(01 ‖ L0 ‖ L1) ‖ L2). L2 kopyalanmaz, tek başına sağ alt ağaçtır.

**A6.** Recall = 6/10 = 0,6. Küme düzeyinde 5 birim var (K ve 4 tekli küme). Her tekrarda yerine koyarak 5 birim çekilir. K hiç çekilmezse recall 0 olur. Olasılık (4/5)^5 ≈ 0,328, yani tekrarların yaklaşık üçte biri. Bu yüzden aralığın alt ucu 0'a iner. Alan adı düzeyinde 10 site tek tek çekilir. Recall'un 0 çıkması için 10 çekilişin hepsinin yakalanmamış 4 siteden gelmesi gerekir: (4/10)^10 ≈ 0,0001. Alan adı düzeyindeki aralık çok daha dar çıkar, ama yanlıştır. Gerçekte "6 başarı" tek bir operatörün başarısı. Bir operatörü ya yakalarsın ya kaçırırsın. Küme düzeyindeki geniş aralık dürüst olandır.

**A7.** i = floor(0,62 × 4) = floor(2,48) = 2. Havuz[2] = 100 gün. İlk sertifika 2026-03-01 00:00 UTC = 03:00 TR. Üstüne 100 gün eklenince 2026-06-09 03:00 TR olur. (Mart'ta 30 gün kalır → 31 Mart, +30 → 30 Nisan, +31 → 31 Mayıs, +9 → 9 Haziran.) Bundan **sonraki** ilk TR gün başı 2026-06-10 00:00 TR, yani 2026-06-09 21:00 UTC. Bu kontrolün referansı. Son görünüm referanstan önceki günün son anı: 2026-06-09 23:59:59,9999999 TR (20:59:59,9999999 UTC). Bu, bütün pozitiflerin son görünümüyle aynı saat.

**A8.** 04-01 + 7 gün = 04-08. Bu, karardan (04-05) sonra, yani görünmez. 03-20 + 7 = 03-27. Bu karardan önce, yani görünür. Bugün sorulan DNS'in `KnownAt`'i 2026-11-16, karardan çok sonra. Görünmez. Geçmiş testinde DNS sinyali bu yüzden kendiliğinden boş kalır.

**A9.** Başlangıç: p1 ayar (3 ≤ 18). p2, p3, p4, p5 geçmiş test. Bileşenler: {p1}, {p2, p3} (bülten 25), p3 ile p4 küme C ile bağlı, yani {p2, p3, p4} ve {p5}. Her bileşen zaten tek bölmede, taşınan = 0. p1'in kümesi C olunca: p1–p3–p4 küme C ile, p2–p3 bülten 25 ile bağlanır. Bileşen {p1, p2, p3, p4} olur. En erken bölme ayar. p2, p3, p4 ayara taşınır, taşınan = 3. Test kümesinde yalnızca p5 kalır. Değerlendirme havuzu küçülür ama yeniden bölme yapılmaz. Bonus: p6 `Excluded` olur, bu yüzden taşınmaz. Ama ayardaki p1 ile aynı bileşende olduğu için `TiedToTuning` listesine girer ve ileriye dönük teste **giremez**.

**A10.** Eşik `02 8F ...` ile başlıyor. `01 FF ...` < `02 ...`, yani seçilir. `02 10 ...` < `02 8F ...`, yani seçilir. `02 A0 ...` > `02 8F ...`, yani seçilmez. `03 00 ...` > eşik, yani seçilmez. Sezgi: İlk bayt 0x00 ya da 0x01 ise kesin seçilir (256'da 2), 0x02 ise ikinci bayta bakılır. Toplam olasılık yaklaşık (2 + 0x8F/256)/256 ≈ (2 + 0,56)/256 ≈ 0,01.

**A11.**
(a) 10 Mart 14:00 → 9 Mart 23:59:59,9999999. 9 Mart 23:59:59,9999999 → kendisi (zaten ızgara noktası). 10 Mart 00:00:00 → 9 Mart 23:59:59,9999999. Neden: Bir tick eklenince 10 Mart 00:00:00,0000001 olur. Onun gün başı 10 Mart 00:00, bir tick geri 9 Mart'ın son anı.
(b) Son görünüm = `EndOfDayAtOrBefore(10 Mart 00:00 − 1 tick)` = 9 Mart 23:59:59,9999999. Arşiv kaydı 9 Mart 22:00'de biliniyor, bu da ≤ son an olduğu için görünür.
(c) `AsOf` = 8 Mart 23:59:59,9999999. 9 Mart 10:00'daki kayıt bu andan sonra, görünmez. İstenen an 15:00 olsa da görünüm aşağı yuvarlandığı için aynı gün öğrenilen bilgi o günün görünümüne girmez. Bu tutucu bir seçim.
(d) Eski kural: 2 Şubat 14:37 + 30 gün + 1 sn = 4 Mart 14:37:01. Son görünüm bundan bir tick önce, saat 14:37:00,9999999 TR (11:37 UTC). Pozitifin son görünümü hep 23:59:59,9999999 TR, yani 20:59:59,9999999 UTC. Saate bakan bir sinyal "20:59 ise pozitif" kuralıyla kusursuz ayırır. Yeni kuralda kontrolün referansı 5 Mart 00:00 TR, son görünümü 4 Mart 23:59:59,9999999 TR. İkisi de UTC 20:59:59,9999999. Saat artık hiçbir şey söylemiyor.

**A12.** İsabet = oran × toplam = 0,075 × 40 = 3. "<5" yazmak hiçbir şey gizlememiş, çünkü toplam ve oran açıkta. İkinci örnek: 0 × 12 = 0. "12 birimin hiçbiri alarm almadı" bilgisi açıkça okunuyor. Bu, kodun yorumunda gizlenmek istenen "hiç alarm yok" bilgisinin ta kendisi. Ders: Bir sayıyı gizlemek, o sayının **hesaplanabildiği** her şeyi de gizlemeyi gerektirir: oran, aralık, precision, tümleyen. Gizlilikte buna "tamamlayıcı bastırma" denir.

**A13.**
(a) İsabet 1 < 5: "<5/<5 (küçük hücre: sayı, oran ve aralık yazılmaz)". Okuyucu yalnızca "isabet 5'ten az, toplam 5'ten az" bilir.
(b) İsabet 3 < 5: "<5/40 (küçük hücre: ...)". Okuyucu isabetin 0 ile 4 arasında olduğunu bilir. Oran yazılmadığı için 3'ü bulamaz.
(c) Tümleyen 12 − 11 = 1 < 5: "≥8/12 (küçük hücre: ...)". 12 − 5 + 1 = 8. Okuyucu isabetin 8 ile 12 arasında olduğunu bilir. "Alarm almayan tek kurum" bilgisi kaybolur.
(d) İsabet 20, tümleyen 20, ikisi de ≥ 5: "20/40 = 50%, küme %95 [...]". Normal yazılır.
