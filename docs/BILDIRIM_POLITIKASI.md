# Kurumlara Bildirim Politikası

Durum: Ana kararlar Kadircan tarafından 2026-11-24 tarihinde verildi (Bölüm 6). Bildirim kanalı henüz doğrulanmadı; doğrulanana kadar bildirim yapılmaz. Politika, ileriye dönük test başlamadan yazıldı (ROADMAP Hafta 8).

İlgili kurallar: Proje kuralları 2, 3, 4, 7, 8. Arka plan: [ROADMAP_REVIEW.md](ROADMAP_REVIEW.md) Bölüm 3.7, [THREAT_MODEL.md](THREAT_MODEL.md) Bölüm 3.7 ve 3.8.

## 1. Sorun

İleriye dönük test, dondurulmuş kurallarla canlı akıştan gelen alan adlarını işaretler. İşaretlenen bir alan adı SPK veya USOM listesinde olmayabilir. Üç tehlike var:

- **Yanlış suçlama:** Listede olmayan bir siteyi adıyla yayımlamak veya kuruma "şüpheli" diye bildirmek, o sitenin sahibine haksızlık olabilir. İşaret bir kanıt değil, bir ölçüm sinyalidir. Yanlış alarm oranı sıfır değildir ([N] ölçülecek).
- **Zararın sürmesi:** Gerçekten zararlı bir site görüp susmak, mağdur olabilecek kişilerin zarar görmesine izin verebilir.
- **Ölçüm bozulması:** Bildirdiğimiz site sonra engellenirse, bunun nedeni sistemin doğru tahmini mi, bizim bildirimimiz mi, ayırt edilemez. İleriye dönük testin "SPK ne zaman bakmış" ölçüsü kirlenir ([ROADMAP_REVIEW.md](ROADMAP_REVIEW.md) 3.7).

## 2. Resmi kanallar: doğrulama durumu

Repoda ve ona bağlı belgelerde şunlar var, başka bir şey yok:

| Konu | Durum |
|---|---|
| SPK'ya bildirim yolu (form, e-posta, başvuru süreci) | [doğrulanmadı]. Repoda SPK bülten kaynağı var, bildirim kanalı yok. |
| USOM'a bildirim yolu | [doğrulanmadı]. DATA_SOURCES.md Bölüm 8: USOM ana sayfası okundu, ama bildirim ve API başvuru yolu sayfada bulunamadı. Zararlı bağlantı listesi yeni bir devlet siber güvenlik portalına taşınıyor. |
| Bildirimin hukuki sonucu, kurumun geri dönüş süresi | [doğrulanmadı] |
| Kuruma gönderilecek bilginin biçimi | [doğrulanmadı] |

Kanal doğrulanmadan hiçbir seçenek uygulanamaz. Kadircan, kurumların kendi resmi sayfalarından (`spk.gov.tr`, `usom.gov.tr`) kanalı elle bulup doğrulamalı ve adresi bu belgeye kaynak göstererek eklemelidir. Bu taslak adres, e-posta veya prosedür uydurmaz.

## 3. Üç seçenek

| | A: Hiç bildirme | B: Kanıt eşiğini geçenleri bildir | C: Yalnızca sonradan resmi listeye girenleri kaydet |
|---|---|---|---|
| Ne yapılır | Hiçbir kuruma bildirim yok. Yalnızca gizli listenin taahhüdü var. | Açıkça yazılmış kanıt eşiğini geçen adaylar, resmi kanala elle bildirilir. Dil: "resmi kayıtla uyuşmuyor", asla "dolandırıcı". | Bildirim yok. Aday sonradan SPK veya USOM listesine girerse, bu bir erken uyarı süresi ölçümü olarak raporlanır. |
| Fayda | Test tamamen temiz. Yanlış suçlama riski en düşük. En az iş. | Açık bir zarar görürsek susmuyoruz. Etik olarak savunması en kolay. | Test temiz. Rapor "ne kadar önce fark ettik" sorusuna doğrudan cevap verir. |
| Risk | Gerçek zarar sürerken sessiz kalmak. | Yanlış alarm kuruma yük olur ve bir siteye haksız işlem başlatabilir. Kurumun işlemi bizim bildirimimizden mi sonuçtan mı, belli olmaz. | A ile aynı: sessizlik. Ayrıca "sonradan listeye girenler" zaten bizim görmediğimiz bir süreçle girdi. |
| Ölçüm dürüstlüğü | Bozmaz. | **Bozabilir.** Bildirilen ve sonra engellenen siteler başarı sayılamaz. Bu siteler ölçümden ayrılmalı veya bayraklanmalı (Bölüm 5). | Bozmaz. |
| İş yükü | Yok. | Orta: Her bildirim için kanıt dosyası, elle gönderim, kayıt, takip, düzeltme. | Düşük: Yalnızca sonradan eşleşmeyi kaydetmek. |

## 4. Öneri (Kadircan 2026-11-24'te onayladı, bkz. Bölüm 6)

**Öneri: B'nin dar sürümü, C'nin ölçüm kuralıyla birlikte.**

Gerekçe:

1. A ve C, açık bir mağdur riskinde sessiz kalmayı seçiyor. ROADMAP_REVIEW 3.7 bunun etik olarak savunulamayabileceğini söylüyor.
2. B'nin sakıncası (ölçümü kirletmek) kayıtla çözülebilir: Bildirilen her aday kayıt altına alınır ve ileriye dönük test analizinde **ayrı raporlanır**. Ana sonuç bildirilmemiş adaylar üzerinden hesaplanır, bildirilenler ayrı satırda gösterilir.
3. İş yükünü sınırlamak için eşik yüksek tutulur ve bildirim sayısı küçük kalır. Eşik, dondurulmuş sinyal kurallarının dışında, test başlamadan **tarihli** bir not olarak yazılır. Eşiği testin sonucuna göre değiştirmek yasak (kural 7).
4. Eşik tutturulamıyorsa veya kanal doğrulanamıyorsa, varsayılan A'dır (bildirme). Bu sonucu raporda açıkça yazılır.

Önerilen eşik (taslak, Kadircan karar verecek): Aday, dondurulmuş kurallarla işaretlenmiş olmalı **ve** elle, pasif kaynaklardan (arşiv kaydı, sertifika kaydı) bakıldığında içerikte karar verici bir kanıt görülmeli. Yalnızca skor tek başına yetmez. Eşik sayısı: [N], henüz ölçülmedi.

## 5. Her seçimde zorunlu önlemler

1. **Adlar kamuya açık çıktıda yok.** Rapor, sayfa, log, metrik ve README'de listede olmayan hiçbir alan adı görünmez (kural 4). Bildirim kuruma gider, kamuya değil.
2. **Gönderen yalnızca Kadircan, elle.** Hiçbir betik, worker veya zamanlanmış iş bildirim göndermez. Kodda bildirim gönderen bir yol olmaz.
3. **En az kanıt alanları** (kuruma giden dosyada bunlar vardır, fazlası yoktur):
   - alan adı
   - işaretlendiği tarih ve kural sürümü (commit)
   - hangi sinyaller tetiklendi (kısa, sayısal)
   - kanıt kaynağı türü (sertifika kaydı, arşiv kaydı) ve tarihi
   - ölçüm sınırı: Yanlış alarm oranı [N], veri sınırı ve "bu bir kesinlik değil, ölçüm sinyalidir" ifadesi
   - kişi adı, e-posta, telefon yok (kural 5)
4. **Dil:** "Resmi kayıtla uyuşmuyor" veya "ölçüm sinyali verdi". "Dolandırıcı", "yalan", "sahte" yok.
5. **Gönderim kaydı `data/private/` altında.** Her bildirim için: tarih, kanal, kısa kimlik (opak), gönderilen dosyanın özeti (SHA-256), alınan yanıt. Bu dosya commit edilmez (kural 8).
6. **Testten ayırma:** Her bildirilen aday, ileriye dönük test analizinde `bildirildi` bayrağıyla ve bildirim tarihiyle işaretlenir. Bildirimden **sonra** olan resmi karar, erken uyarı ölçümüne girmez. Ana rakamlar bildirilmemiş adaylardan hesaplanır, bildirilenler ayrı tabloda verilir. Bayrak, kapanış raporu öncesinde taahhütle birlikte sabitlenir. [yöntemin kod tarafı henüz yok, ayrıca karar gerekir]
7. **Düzeltme ve geri çekme süreci:** Bir site sahibi veya kurum, işaretin yanlış olduğunu bildirirse: (a) kayıt `data/private/` içinde "itiraz edildi" diye işaretlenir, (b) ölçümde yanlış alarm olarak sayılır, kaldırılmaz, (c) kuruma doğrulanmış bir geri çekme notu elle gönderilir, (d) rapordaki ilgili rakamlar güncellenir ve değişiklik tarihi yazılır. Hangi kanaldan itirazın alınacağı [doğrulanmadı], README'ye eklenecek iletişim kanalı Kadircan'ın kararı.
8. **Yatırım tavsiyesi değildir** ifadesi bildirim notunda da yer alır (kural 6).
9. **Rakam kuralı:** Bildirim sayısı ve her oran yanında veri sınırı ve yanlış alarm oranıyla yazılır (kural 7).

## 6. Kadircan'ın kararları (2026-11-24, hiçbir ileriye dönük test sonucu görülmeden)

1. **Seçenek:** B, yani eşiği geçen bildirilir. Bildirilenler ana sonuçtan ayrı satırda gösterilir.
2. **Zaman:** Açık mağdur riski varsa hemen bildirilir. Diğerleri test dönemi bitince topluca bildirilir.
3. **Eşik:** Dondurulmuş kurallar işaretlemiş olmalı, ayrıca Kadircan pasif kaynaklarda (arşiv ve sertifika kaydı) karar verici bir kanıt görmeli. Skor tek başına yetmez.
4. **İtiraz kanalı:** GitHub issue. Issue şablonunda alan adı yazılmaması istenir.
5. **Henüz cevaplanmayanlar, o zamana kadar varsayılanlar:**
   - Kanal doğrulanmazsa A uygulanır (bildirim yok).
   - Haftalık bildirim sınırı yok.
   - Eşik, kuralların dondurulduğu commit ile aynı gün tarihlenir.

## 7. Kadircan'ın cevaplaması gereken sorular (ilk liste)

1. **Seçenek hangisi?**
   - a) A: Hiç bildirme
   - b) B: Eşiği geçenleri bildir (öneri)
   - c) C: Yalnızca sonradan listeye girenleri kaydet
   - d) Başka: ...
2. **Bildirim zamanı (B seçilirse):**
   - a) Hemen, eşik geçilince
   - b) Test dönemi (ek rapor) bitince topluca
   - c) Yalnızca açık mağdur riski varsa hemen, diğerleri sonra
3. **Kanal:** SPK ve USOM'un resmi bildirim yollarını kim ve ne zamana kadar doğrulayacak? Doğrulanmazsa varsayılan A olsun mu? (evet / hayır)
4. **Eşik:** Yalnızca skor mu, skor ve elle pasif kanıt incelemesi mi? (öneri: ikincisi)
5. **Bildirim sınırı:** Haftada en fazla [N] bildirim kuralı konsun mu? (evet / hayır, sayı?)
6. **İtiraz kanalı:** README'de hangi iletişim yolu yazılsın? (GitHub issue / üniversite e-postası / yazılmasın)
7. **Ana sonuç:** Bildirilen adaylar ana rakamlardan tamamen çıkarılsın mı, yoksa ayrı satırda mı gösterilsin? (öneri: ayrı satır)
8. **Eşiği ne zaman yazıp tarihleyeceksin?** (kuralların dondurulduğu commit ile aynı gün / önce)
