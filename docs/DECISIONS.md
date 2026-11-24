# Kararlar

Her kayıt: tarih, karar, neden, alternatif.

## 2026-11-12: Etiket verisi (Aşama 0)

**Karar:** Pozitif etiketler, 2026 SPK bültenlerindeki erişim engeli listelerinden şu adımlarla çıkarıldı:

1. `scripts/phase0/extract_candidates.py`, 78 bültenden aday adresleri çıkardı. Sayfa altlığındaki adres ifadesi ve SPK'nın kendi adresi elendi, böylece 24 bültenden 201 alan adı ve bülten çifti kaldı.
2. Her çift, iki bağımsız ve yazılım destekli incelemeyle bültenin kendi metninden kontrol edildi. İki inceleme birbirinin sonucunu görmedi. Karar, tarih ve kategori 201 satırın tamamında aynı çıktı.
3. Üçüncü kontrol, basit bir bölüm başlığı kuralıyla yapıldı. Kuralın uyuşmadığı 27 satırın hepsi elle açıklandı: Satır sonunda bölünmüş başlık, uygulama mağazası bağlantısı ve kısaltma servisi.
4. Sonuç olarak 21 bültenden 175 kayıt ve 171 farklı alan adı elde edildi. Bu kayıtlar `data/private/phase0/engellenenler.csv` dosyasında tutulur ve git dışındadır.

**Neden:** Etiket, ölçümün temeli. Tek bir geçişte yapılan hata bütün sonuçlara taşınır.

**Alternatif:** Tamamen elle doğrulama. Daha yavaş olurdu, ama kontrol bir kişiye bağlı olurdu.

**Elle örnek kontrolü (2026-11-15):** `scripts/phase0/make_manual_check.py` 175 kayıttan 10 tanesini sabit tohumla (`elle-kontrol-2026-11-15`) rastgele seçti. Seçilen kayıtlar 6 farklı bültenden geliyor. Kadircan her kaydı bültenin PDF'inde açtı ve dört soruya baktı: Adres bültende geçiyor mu, erişim engeli listesinde mi, karar tarihi doğru mu, kategori doğru mu? 10 kaydın 10'unda dört soruya da "doğru" sonucunu bildirdi. Sonuç tek tek hücrelere değil, toplu olarak bildirildi. 10 kayıtlık bir örnek büyük hataları yakalar ama küçük bir hata oranını dışlamaz: 0/10 hata için %95 üst sınır yaklaşık %26'dır.

**Sınır:** İncelemeler bülten metnine dayanır. Bültende yanlış yazılmış bir adres, veride de yanlış kalır.

### Ayrıntı kuralları

| Durum | Karar | Sayı |
|---|---|---|
| Adres suç duyurusu veya suç ihbarı tablosunda | Pozitif değil. Ayrı bir karar türü | 18 |
| Mevzuat ifadesinin alan adı sanılması | Pozitif değil | 3 |
| Uygulama mağazasında tek uygulama sayfası | Pozitif değil. Mağazanın alan adı engelli değil | 2 |
| Kısaltma servisinde tek bir yol | Belirsiz, veriye alınmadı | 1 |
| Listede yalnızca alt alan adı veya yol geçiyor | Kayıtlı alan adı pozitif sayıldı, gerekçe notu tutuldu | 74 |
| Ortak barındırma alanındaki tek bir alt alan adı | Veriye alınmadı. Kök alan adı başka siteleri de barındırıyor olabilir | 2 |
| Aynı alan adı farklı bültenlerde farklı bölümlerde | Karar bülten bazında verildi | |

- **Karar tarihi:** Bültenin ilk sayfasındaki tarih. Kurul toplantı tarihi kullanılmadı.
- **Kategori:** Liste girişi yalnızca kripto veya yalnızca kaldıraçlı işlem diyorsa o kategori. İkisini birlikte sayıyorsa ya da tür yazmıyorsa (99/3 listeleri) `diğer`. Bu tutucu bir seçim ve `diğer` sayısını artırır.
- **Kişi adına benzeyen alan adları:** Listede açıkça geçtikleri için veride kalırlar, ama panelde ve raporda adlarıyla gösterilmezler.
- **Etiketin anlamı:** "SPK erişim engeli listesinde" demektir, "dolandırıcı" demek değildir.

## 2026-11-13: DNS ölçüm konumu ve DNS kodu (Hafta 4)

**Karar:** DNS ölçümü Ubuntu makinesinden yapılır (ölçüm konumu `tr-ev-ubuntu`). Her alan adı aynı çözücüye üç yoldan sorulur: Düz UDP, TCP ve şifreli DoH (RFC 8484). Sonuçları birbirinden farklı çıkan sorgular "araya girme şüphesi" olarak işaretlenir. Cevapta bilinen bir engel sayfası IP'si varsa sorgu "engel işareti" alır ve barındırma bilgisi sayılmaz. Bütün gruplar (SPK listesi, adaylar, kontrol grubu) aynı konumdan ve aynı çözücülerden ölçülür.

**DNS paket kodu:** DNS paketini kuran ve çözen kodu (RFC 1035) proje içinde yazıyoruz, hazır kütüphane kullanmıyoruz. Kodun açıklamasını Kadircan satır satır okuyup anlayacak.

**Neden:** Türkiye'de erişim engelleri DNS cevabı değiştirilerek uygulanabiliyor. Güncel OONI bulguları büyük platformlar için bunu gösteriyor. Açık çözücülere giden UDP 53 trafiğinin bugün yakalanıp yakalanmadığına dair 2024–2026 tarihli bir ölçüm bulunamadı (bkz. [research/HAFTA4_AG_KAYNAKLARI.md](research/HAFTA4_AG_KAYNAKLARI.md)). Ev bağlantısından yapılan ölçüm bu yüzden kendiliğinden geçerli sayılmaz, karşılaştırmayla doğrulanır. DoH, servis sağlayıcının cevabı değiştirmesine karşı korur.

**Alternatif:** Yurt dışında bir sanal sunucu. Engelden etkilenmez, ama aylık ücreti var ve yönetilmesi gereken ikinci bir makine demek. Karşılaştırma araya girmenin yaygın olduğunu gösterirse bu seçenek yeniden ele alınır.

**Sınır:**
- Ölçüm tek konumdan ve tek servis sağlayıcıdan yapılıyor.
- Kullanılan herkese açık çözücüler sorgulanan alan adlarını görür.
- Sonuç, "bu servis sağlayıcıdan, bu zamanda görünen cevap" olarak yazılır.
- Geçmiş tarihlerin DNS verisine erişimimiz yok. Ölçüm ancak bugünden itibaren yapılabiliyor.

## 2026-11-13: Ağ verisinin saklanması

**Karar:** DNS, RIPEstat ve InternetDB kayıtları yerel PostgreSQL veritabanında tutulur. Veritabanı ve `pg_dump` yedekleri `data/private/` ile aynı gizlilik düzeyindedir: Repoya girmez, paylaşılmaz, rapora yalnızca toplu sayı olarak çıkar. InternetDB'nin `vulns` ve `hostnames` alanları saklanmaz.

**Neden:** Kural 8 şüpheli listenin ve port verisinin repoya girmemesini ister. Veritabanı da repo dışında, bu yüzden aynı korumayı sağlıyor. Veriyi ayrıca dosyaya yazmak, sızabilecek ikinci bir kopya yaratırdı.

## 2026-11-15: Sinyal ve ölçüm kodunu kim yazar

**Karar:** Hafta 6'dan itibaren `Fraud.Signals`, `Fraud.Evaluation` ve `Fraud.Claims` kodunu Kadircan yazmaz. Bu kod, yapay zekâ destekli olarak geliştirilir. Kadircan'ın kendi ifadesi: "hafta 6 dan itibaren kodları ben yazamayacağım bu senin için ben çalışıp öğrenirim sen başla".

**Şart:** Her modülle birlikte `docs/learn/` altında satır satır bir okuma rehberi ve sınama soruları yazılır. Kadircan, kodun her satırını mülakatta açıklayabilecek düzeye gelene kadar bu rehberle çalışır. Ölçüm kuralları değişmez:
- Geleceğe ait veri kullanılmaz.
- Ayar ve test verisi ayrılır.
- Kilitli test bir kez çalışır.
- Sonuca göre kural değiştirilmez.

**Neden:** Projenin takvimi ile Kadircan'ın ders ve staj başvuru yükü bir arada yürütülmüyor. Öğrenme hedefi kodu yazmaktan, kodu anlayıp savunabilmeye kaydırıldı.

**Alternatif:** Kodun tamamını Kadircan'ın yazması (önceki kural). Daha fazla öğrenme sağlar, ama zaman planına sığmıyor.

**Sınır:** Kod Kadircan'ın elinden çıkmadığı için mülakatta "yapay zekâ destekli geliştirildi, her satırını anlıyor ve savunabiliyorum" şeklinde anlatılmalıdır.

## 2026-11-16: Ölçüm varsayılanları (Hafta 6)

Bu kararlar Kadircan tarafından onaylandı. Hiçbir sinyal sonucu görülmeden alındı.

| Konu | Karar |
|---|---|
| Alarm eşiği | Ayar bölümündeki G3 ve G4 kontrollerinde en fazla %1 yanlış alarm. Eşik yalnızca ayar verisinde seçilir. |
| Taban oran (π) | 0,001. Bu bir varsayımdır, ölçülmüş değer değildir. Tarafsız örneklem birikince gerçek oran ölçülür ve kurallar dondurulmadan önce bir kez güncellenir. Bu güncelleme de buraya yazılır. |
| Kilitli testteki kontroller | Bir kontrol, referans zamanı [F, F+28] penceresine düşüyorsa teste girer. Pozitiflerle aynı kural kullanılır. Bu, KONTROL_GRUBU_YONTEMI.md 7.3 madde 4'teki "ilk görülme" kuralından bilinçli bir sapmadır. |
| Küçük sayılar | 5'ten küçük hücreler "<5" olarak yazılır. 10'dan küçük tabakalarda yüzde verilmez, yalnızca ham sayı verilir. Erken uyarı ve örneklem içi precision için de aynı kural geçerlidir. |

**Teknik varsayılanlar:** Aşağıdaki değerler yapılandırmadan değiştirilebilir. Değiştirilirse bu bölüme yazılır.

| Konu | Varsayılan |
|---|---|
| Bootstrap | 2000 tekrar. Yeniden örnekleme küme ve bülten düzeyinde yapılır. |
| Kontrolün referans zamanı | Eşleşmiş gecikme: kontrolün ilk sertifikasına, ayar pozitiflerinin "ilk sertifika → karar" dağılımından tohumlu olarak çekilen bir gecikme eklenir. |
| Arşiv kaydının bilinir sayıldığı an | Kayıt zamanı + 7 gün. İleriye dönük testte bizim kaydı gördüğümüz an. |
| Sertifikanın bilinir sayıldığı an | Geçmiş testinde not_before. Bu bir varsayımdır. İleriye dönük testte bizim sertifikayı gördüğümüz an. |
| DNS, RIPEstat ve InternetDB | Sorgu zamanı. Geçmiş veri testinde kullanılmaz. |
| Karar anı | Bültenin tarihi, Türkiye saatiyle gün başı. |

## 2026-11-16: Denetim sonrası ölçüm kuralı düzeltmeleri (Hafta 6)

Ölçüm kodunu iki bağımsız denetim inceledi: kodun öğretilmesi sırasında yapılan bir okuma ve bir güvenlik denetimi. Aşağıdaki kurallar bu denetimlerde bulunan sorunlar yüzünden değişti. Hiçbir sinyal sonucu görülmeden, kurallar dondurulmadan önce değiştiler.

1. **Gün ızgarası:** Bütün görünüm anları ve referans zamanları Türkiye saatiyle gün başına hizalanır. Önceki halde pozitifler hep gece yarısından bir an önce değerlendiriliyordu, kontroller ise günün rastgele bir saatinde. Yalnızca saate bakan sahte bir sinyal iki grubu kusursuz ayırdı.
2. **Günlük değerlendirme:** Sinyal, ilk gözlemden referanstan önceki son güne kadar her gün değerlendirilir. İlk alarm gün düzeyinde kaydedilir.
3. **İleriye dönük test:**
   - Alarm kümesi taahhüt edilmiş listeden okunur. Sonradan yeniden hesaplanmaz.
   - Test kümesi, referans zamanı taahhüt anından sonra olan ve ayar verisiyle bağlantısı olmayan birimlerden oluşur.
   - Bu testte sertifika, ancak bizim onu gördüğümüz andan itibaren bilinir sayılır.
4. **Geleceğe düşen kontroller:** Referans zamanı "şimdi"den sonraya düşen kontrol dışarıda kalır. F'den sonraya düşen kontrol yalnızca kilitli pencereye girebilir. Dışarıda kalanların sayısı rapora yazılır.
5. **Sertifikası bilinmeyen pozitif:** İlk sertifikası bilinmeyen pozitif ölçümden çıkarılır. Önceden "eski" sayılıyordu. Kontrol tarafında aynı kural zaten uygulanıyordu. İki sayı da raporlanır.
6. **Yaşı bilinmeyen alt grup:** İlk sertifikası karardan sonra olan pozitif "yeni" sayılmaz, "yaşı bilinmiyor" alt grubuna girer.
7. **Ayara bağlı birimler:** Ayar verisindeki bir birimle aynı bülten ya da küme bileşenine giren hiçbir birim ileriye dönük teste girmez.
8. **Küçük sayılar:** Ham sayı kuralı erken uyarıya ve örneklem içi precision'a da uygulanır. 5'ten küçük her hücre "<5" yazılır.
9. **Taahhüt kayıtları:** Kayıtlar, taahhüt edilmeden önce kayıtlı alan adı biçimine çevrilir: küçük harf, punycode, alt alan adı atılmış.
10. **Kilitli test şartları:** Kilitli test ancak şu durumda çalışır: Çalışma ağacı temiz, verilen commit HEAD ile aynı ve yapılandırma dosyası git'te izleniyor. Yapılandırma hatası kilidi harcamaz. Kilit dosyasının yeri yapılandırmayla değiştirilemez.
11. **Kapsama uyarısı:** Bir kontrol grubunda sertifika, arşiv ya da DNS kaydı olanların payı pozitiflerden 0,20'den fazla farklıysa rapor en üstte uyarı verir. Bu durumda "veri var mı" bilgisi tek başına etiketi ele verebilir.

**Ek düzeltmeler (2026-11-16, ikinci denetim turu, sonuç görülmeden):**

12. **İleriye dönük test zamanı:** Taahhüdün zamanı artık dosyadaki beyandan alınmıyor. Bağımsız bir zaman damgası servisinin imzalı RFC 3161 cevabındaki `genTime` kullanılıyor. Cevap doğrulanamazsa sonuç basılmaz. Zaman damgası servisinin kök sertifikası yapılandırmada zorunludur.
13. **İleriye dönük testte sertifika şartı:** İlk sertifikası bilinmeyen birim, pozitif ya da kontrol fark etmeksizin dışarıda kalır.
14. **Küçük hücreler:** Bir hücredeki isabet sayısı ya da tümleyeni 5'ten küçükse yalnızca aralık yazılır. Oran, güven aralığı ve o sayıdan geri hesaplanabilecek her değer (precision, düzeltilmiş precision, günlük alarm) gizlenir.
15. **Kapsama uyarısı:** Yalnızca referans zamanından önce bilinen kayıtları sayar. Böylece sinyalin gerçekten görebildiği veri ölçülür.
16. **Kilitli test:** Kilit dosyası git klasöründe tutulur. Aynı depodan açılan bütün çalışma kopyaları aynı kilidi paylaşır. Çalışan programın derlendiği commit HEAD ile aynı olmalıdır. Kilitli test çalışınca komut, kilit kaydının özetini commit etmek ve zaman damgası almak için gereken adımları yazdırır. Bu adımlar elle yapılır ve zorunludur. Depo başka bir yere kopyalanırsa yerel kilit işe yaramaz. Kopyalamaya karşı tek koruma bu dış kayıttır.

**Ek düzeltmeler (2026-11-20, üçüncü denetim turu, sonuç görülmeden):**

17. **Kilitli test temiz kopyada çalışır.** Kilitli test yalnızca HEAD'den açılmış, temiz ve geçici bir git worktree'de derlenen programla çalışır. `src/` ya da `tests/` altında git'in yok saydığı bir dosya varsa çalıştırma reddedilir. Programın derlendiği commit HEAD ile aynı olmak zorundadır. Bilinen sınır: Aynı makinede elle açılmış bir worktree bu otomatik açılandan ayırt edilemez. Tam kanıt için programın CI'da derlenmesi gerekir.
18. **İleriye dönük test yalnızca sabitlenmiş TSA köklerini kabul eder.** Kabul edilen kökler `src/Fraud.Collectors/Evaluation/tsa_roots.txt` dosyasında tutulur. Liste boşken ileriye dönük test çalışmaz. Bir kök, taahhütten önce bu listeye eklenip commit edilmelidir. Zaman damgasının iptal edilip edilmediği (CRL/OCSP) denetlenmez.
19. **Eski konumdaki kilit yeni konuma taşınır.** Kilit kaydı eski konumda bulunursa yeni konuma taşınır. O kayıt varken alınan her yeni bilet "yeniden çalıştırma" sayılır.
20. **İleriye dönük test de temiz kopyada çalışır.** İleriye dönük test de yalnızca HEAD'den açılmış temiz ve geçici bir worktree'de derlenen programla çalışır. `--commit` zorunludur. Kullanılan TSA kökünün `tsa_roots.txt` dosyasına eklendiği commit, damga zamanından önce olmalıdır. Değilse ya da böyle bir commit bulunamazsa sonuç basılmaz. Bilinen sınır: Commit tarihi, commit'i yapanın kendi beyanıdır. Kasıtlı geriye tarihlemeye karşı tek koruma, kökü ekleyen commit'in taahhütten önce uzak depoya itilmesi ya da damgalanmasıdır.

## 2026-11-20: Kontrol grubu kurulumunda verilen kararlar

| Konu | Karar |
|---|---|
| Tek grup kuralı | Bir alan adı yalnızca bir kontrol grubunda bulunabilir. Gruplar sırayla kurulur. Daha önce başka bir gruba girmiş bir ad sonraki gruba alınmaz ve E02 koduyla elenir. Bu, KONTROL_GRUBU_YONTEMI.md 4.2 madde 6'dan bilinçli bir sapmadır. Belgeye göre sonradan G3'e giren bir ad G4a'dan çıkarılmıyordu. Seçilen yol, sonuçların daha sade okunmasını sağlar. |
| K çerçevesi | G3'ün K çerçevesine de N çerçevesindeki kurallar uygulanır: donmuş sözlük, "yeni" şartı (ilk sertifika en fazla 28 gün önce) ve çerçeve başına haftada en fazla 60 ad. Bir adın ilk görülme zamanı, `ct_candidates` tablosundaki en eski kayıt zamanıdır. |
| Kişi adı deseni (E04) | Belge yalnızca sayım yapılmasını istiyor ve desen tanımlamıyor. Bu yüzden şimdilik uygulanmadı. Desen tanımlanırsa uygulanması buraya yazılır. |
| Kontrol komutlarının çalıştığı yer | Kontrol komutları Ubuntu'da Docker Compose'un `tools` profilindeki servisle çalışır. Servis .NET SDK imajını kullanır ve veritabanına iç ağ üzerinden bağlanır. Veritabanı portu dışarı açılmaz. |

## 2026-11-21: Hafta 7 sinyal kuralları

Aşağıdaki kurallar gerçek veri görülmeden, kod yazılırken belirlendi. Eşiklerin hiçbiri ayar verisinde ya da sonuca bakılarak seçilmedi.

| Konu | Karar |
|---|---|
| Baseline | Alan adının en soldaki etiketinde, aday süzgecindeki 8 terimden biri geçiyor mu? Terimler tek bir kaynaktan okunur. K çerçevesinde baseline tanım gereği alarm verir, bu yüzden sonuçlar çerçeve başına ayrı raporlanır. |
| Yeni sertifika + sözlük | Alan adının ilk sertifikası, görünüm anından en fazla 28 gün önce alınmış olmalı. Ayrıca alan adı geniş finans sözlüğüyle eşleşmeli. 28 gün, kilitli test penceresiyle aynı uzunlukta. Bilinen sınır: Geçmiş veri testinde bu sinyal pratikte "sözlük eşleşmesi + en az bir sertifika" anlamına gelir. |
| Sertifika patlaması | Görünüm anından önceki 30 günde en az 5 farklı sertifika. Gerekçe: Let's Encrypt sertifikası 90 gün geçerlidir ve 60. günde yenilenir. Normal bir alan adı 30 günde 1–2 sertifika alır. |
| Numaralı ad | Etiket bir rakamla bitiyorsa ya da etikette art arda iki rakam varsa. |
| Marka benzerliği | Yerli liste G1'deki BDDK bankalarının adlarından türetilir. Bu liste özeldir ve `data/private` altında tutulur. Yabancı liste FSB'nin G-SIB listesinden alınır ve donmuş bir dosyada durur. Bu listenin resmi kaynakla doğrulanması sürüyor. 5 karakterden kısa markalar listeye alınmaz. Eşleşme kuralı: Görsel olarak benzer karakterler dönüştürülür; sonra etiket markayı içeriyorsa ya da Levenshtein uzaklığı küçükse eşleşme sayılır. Uzaklık sınırı 8 karakterden kısa markalarda 1, daha uzun markalarda 2'dir. Markanın kendisi alarm vermez. |
| Birleşik skor | Sinyallerin ağırlıksız toplamı kullanılır, mantıksal VEYA kullanılmaz. Gerekçe: Eşik, ayar kontrollerinde en fazla %1 yanlış alarm verecek şekilde seçiliyor. VEYA kullanılsaydı, kontrollerin %1'inden fazlasında 1 veren tek bir parça yüzünden birleşik sinyal hiç alarm veremezdi. Toplam kullanıldığında eşik 1 seçilirse sonuç VEYA ile aynıdır. |
| Ablasyon | Her sinyal için "TOPLAM − x" ile karşılaştırma yapılır. ML.NET modeli ancak baseline'ı geçen bir sinyal görülürse denenir. |

## 2026-11-21: Eşik seçimi yalnızca tarafsız kontrollerden yapılır (sonuç görülmeden)

**Sorun:** Kod okunurken şu fark edildi: Eşik, G3 ve G4'teki ayar kontrollerinden seçiliyordu. G3 finans sözlüğüyle, K çerçevesi de aday kelime süzgeciyle seçildiği için bu birimler bazı sinyallerde tanım gereği 1 puan alır. Bu birimler eşik seçimine girerse, eşik %1 yanlış alarm sınırını tutturmak için 1'in üstüne çıkar ve o sinyal hiç alarm vermez. Örneğin baseline ölçüm yapılmadan susturulmuş olur. Ayrıca eşik, bizim belirlediğimiz G3:G4 ve K:N oranlarına bağımlı hale gelir.

**Karar:** Eşik yalnızca G4'ün (G4a ve G4b) ayar bölümündeki kontrollerden seçilir. G4, alan adındaki kelimeye bakılmadan rastgele seçilir. G1, G2 ve G3 eşik seçimine girmez. Bu gruplar, seçilen eşikte ne kadar yanlış alarm ürettiklerini göstermek için grup ve çerçeve başına ayrı raporlanır.

**Ek düzeltmeler:**
- **Patlama sinyali:** Bir sertifika, yayıncı ve seri numarası çiftine göre bir kez sayılır. Böylece aynı sertifikanın ön sertifikası (precertificate) ve son hali iki sertifika sayılmaz.
- **Ablasyon raporu:** Her satır kendi eşiğini seçtiği için "TOPLAM − x" farkı, eşiğin değişmesinden gelen etkiyi de içerir. Rapor bunu açıkça yazar. Ayrıca her satır için TOPLAM'ın eşiği sabit tutularak hesaplanan farkı da gösterir.

## 2026-11-24: Eşik kümesi ve ablasyon raporunda dört küçük dürüstlük düzeltmesi

Bu düzeltmeler kod okunurken bulundu. Hiçbiri bir sonuca bakılarak yapılmadı. Kilitli test ve taahhüt kodu değişmedi. Bugünkü veride ilk ikisi sonucu değiştirmez, çünkü G4'te K çerçevesinden gelen ya da tanınmayan gruptan bir birim yok. Bu iki düzeltme ileride yapılabilecek bir hatanın sessizce ölçüme karışmasını engeller.

1. **K çerçevesi eşik kümesine girmez.** Eşik kümesi önceden yalnızca gruba bakıyordu ("G4 mü?"). G4b bugün yalnızca N çerçevesinden kuruluyor. Ama ileride K çerçevesinden bir G4 birimi gelirse, bu birim eşik kümesine sessizce girerdi. K birimleri kelime süzgecinden geçtiği için eşiği yukarı iter. Artık süzgeç K çerçevesini de açıkça dışarıda bırakıyor. Baseline açıklamasındaki "G4b'nin K kısmı" ifadesi de yanlıştı. G4b yalnızca N çerçevesinden kurulduğu için bu ifade düzeltildi.
2. **Tanınmayan kontrol grubu hata verir.** Kontrol grubunu ölçüm grubuna çeviren kod, tanımadığı her grubu G4 sayıyordu. Bu durumda eklenecek yeni bir grup kendiliğinden eşik kümesine girerdi. Artık G4a ve G4b açıkça yazılıyor. Tanınmayan bir grup olursa çalıştırma durur. Hata mesajında yalnızca grubun sayısal değeri yazar, alan adı yazmaz.
3. **Rapor eşik kümesinin nereden geldiğini yazar.** Rapor önceden eşik kümesinin yalnızca toplam sayısını ("n=") veriyordu. Artık ayar bölümündeki G4a ve G4b sayıları ayrı ayrı yazılır. Küçük hücre kuralı uygulanır: 5'ten küçük bir sayı "<5" diye yazılır. Böyle bir durumda öteki sayı da "-" olarak gizlenir, çünkü toplam basılı olduğundan çıkarma yapılarak gizli sayı bulunabilirdi.
4. **Ablasyonda anlamsız fark basılmaz.** Tek bir parçanın puanı yalnızca 0 ya da 1 olabilir. TOPLAM'ın eşiği 1'in üstündeyse tek parça bu eşiğe hiçbir zaman ulaşamaz. Bu yüzden "TOPLAM eşiğiyle" farkı her seferinde TOPLAM recall'un eksi değeri çıkar ve sinyal hakkında bilgi vermez. Bu durumda tek parçalı satırlarda artık sayı yerine "uygulanmaz" ve kısa bir gerekçe yazılır. Bu kural yalnızca eşiğe bakar, sonuca bakmaz. "TOPLAM − x" satırlarında sabit eşikli fark eskisi gibi hesaplanır, çünkü orada sorulan soru tam olarak budur: Sinyal çıkarılırsa aynı eşikte ne kaybedilir?

## 2026-11-24: G2 kuralları raporda uygulanır, ablasyonda tek parça kuralı düzeltildi

Bu iki eksik kod okunurken bulundu. Hiçbiri bir sonuca bakılarak yapılmadı. Kilitli test, taahhüt, bölme ve eşik seçme kodu değişmedi. G2 eşik kümesine eskisi gibi girmez.

1. **G2 "yeterli örnek yok" kuralı artık raporda uygulanıyor.** 2026-11-21 kaydında şu kural yazılıydı: G2 kurulumunda 100'den az ad kalırsa grup "yeterli örnek yok" diye raporlanır ve oran verilmez. Kurulum komutu bu durumda yalnızca bir uyarı basıyor ve manifest dosyasına bir işaret yazıyordu. Değerlendirme raporu bu işareti hiç okumuyordu. Artık rapor, veritabanındaki G2 kurulum kaydına (seçilen üye sayısı) bakıyor. Bu sayı 100'den azsa G2 satırında yalnızca "yeterli örnek yok" ve birim sayısı yazıyor. Oran, aralık, isabet sayısı ve precision yazılmıyor. Manifest yerine veritabanı seçildi, çünkü kurulum kaydı üyelerle aynı işlemde yazılıyor ve ikisi her zaman birbirini tutuyor. Manifest ise bu işlemden sonra ayrı bir dosyaya yazılıyor, eksik kalabilir ya da elle değişebilir. G2 üyesi olduğu halde kurulum kaydı yoksa rapor yine oran vermiyor, çünkü kuralın geçtiği gösterilemiyor.
2. **G2 sonucu Türkiye'ye bağlı alt küme için de yazılıyor.** 2026-11-21 kaydında Türkiye bağı bir etiket olarak tanımlandı ve sonuçların hem tamamı hem bu alt küme için yazılacağı söylendi. Etiket veritabanında vardı ama ölçüme taşınmıyordu. Artık her G2 biriminde bu etiket var ve rapor G2 satırının hemen altına "Türkiye bağlantılı alt küme" satırını ekliyor. Küçük hücre kuralı burada da geçerli: Alt küme ya da tümleyeni (tamamı eksi alt küme) küçükse, alt küme satırında hiçbir sonuç sayısı yazılmıyor. Tamamı zaten basılı olduğu için, çıkarma yapılarak tümleyenin küçük sayısı bulunabilirdi. Alt küme satırında precision ve günlük alarm gibi türetilmiş değerler hiç yazılmıyor.
3. **Ablasyonda tek parça kuralı artık parça sayısına bakıyor.** Bir önceki kayıtta (madde 4) tek parçalı satırlarda "TOPLAM eşiğiyle" farkının basılmaması kararlaştırılmıştı. Ama "tek parça" bilgisi yalnızca tek sinyal satırlarına veriliyordu. Yalnızca iki parça varsa, "TOPLAM − x" satırında da tek parça kalır ve aynı sorun oluşur: Fark her seferinde TOPLAM recall'un eksi değeri çıkar. Artık her satırın kaç parçadan oluştuğu sayılıyor. Tek parça kalan her satırda, TOPLAM eşiği 1'in üstündeyse sayı yerine "uygulanmaz" yazılıyor. Bu kural yalnızca satırın yapısına ve eşiğe bakar, sonuca bakmaz. Bu nedenle önceki kaydın "TOPLAM − x satırlarında fark eskisi gibi hesaplanır" cümlesi artık yalnızca birden çok parça kalan satırlar için geçerli.
