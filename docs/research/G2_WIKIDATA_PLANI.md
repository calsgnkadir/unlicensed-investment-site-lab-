# G2 için B Seçeneği (Wikidata): Hata İncelemesi ve Kurulum Planı

Tarih: 21 Kasım 2026. Aksi yazılmadıkça erişim tarihi 2026-11-21. Bu belge liste **kurmaz**, hiçbir sorgu çalıştırmaz ve Radar'a ya da başka bir API'ye istek atmaz. Gerçek alan adı yok, örnekler `.example`. Bu bir yatırım tavsiyesi değildir.

İşaretler: **[doğrulandı]** sayfa bu çalışmada açılıp ilgili yer okundu (araç özetiyle). **[özet]** yalnızca arama özeti. **[yerel]** repodaki kod ya da belge. **[varsayım]** ölçülmemiş tahmin.

---

## 1. Radar önkoşul denemesi: uygulama hatası var mı?

Bu inceleme **denemeyi geri almaz**. Deneme kullanılmış sayılır ve tekrarlanmaz (karar 6). Sonuç yalnızca raporda dürüstçe yazılır.

### Sonuç: (b) büyük olasılıkla gerçek düşük kapsama, ama kesin değil

Kodda belgeyle çelişen bir satır **bulunamadı**. Ancak tek bir cevap ham olarak saklanmadığı için "kesin gerçek düşük kapsama" demek de mümkün değil.

| Soru | Bulgu |
|---|---|
| Uç nokta doğru mu | Evet. Kod `GET https://api.cloudflare.com/client/v4/radar/ranking/domain/{alan_adı}` çağırıyor [yerel] `ControlSourceClients.cs` satır 86. Resmi belge aynı yolu veriyor [doğrulandı] <https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get/> |
| Kategori yolu doğru mu | Evet. Ayrıştırıcı `success` -> `result` -> `details_0` -> `categories[].name` okuyor [yerel] `ControlSourceParsers.cs` satır 159-173. Belge şeması: `result.details_0.categories` (`id`, `name`, `superCategoryId`) [doğrulandı] aynı sayfa |
| Parametreler | Kod hiç sorgu parametresi göndermiyor. Belgede `date`, `rankingType` (POPULAR, TRENDING_RISE, TRENDING_STEADY), `limit`, `name`, `format`, `includeTopLocations` var, hepsi isteğe bağlı [doğrulandı] aynı sayfa. Varsayılan davranış (en güncel POPULAR) belgede açıkça yazılmadı, bu bir [doğrulanamadı] |
| "Kategorisiz" cevap başka şemada mı geliyor | Kodda bu ayrım var: `details_0` yoksa cevap geçersiz sayılıyor ve **hata** (`null`) olur, "kategorisiz" olmaz (satır 161-163). `details_0` var ama `categories` dizisi yoksa ya da boşsa "kategorisiz" sayılıyor (satır 166-169). Yani şema değişse bile `details_0` kaybolursa hata sayılırdı. Ama `details_0` var olup kategoriler başka bir adla gelseydi (ör. camelCase farkı ya da iç içe başka alan) bu **sessizce "kategorisiz"** olurdu. Denemede hata sayısı 0 olduğu için (kayıt "Failed", "Errored" değil) şema bütünüyle bozuk değildi |
| Belge ne diyor | Uç nokta "domain rank details" olarak tanımlı, kategorilendirme ucu olarak anlatılmıyor. İlk 100 alan adına sıralı rank, diğerlerine yalnızca kova (bucket) veriliyor [doğrulandı] aynı sayfa. Belge, kategorilerin hangi alan adları için dolu olacağını **yazmıyor** [doğrulanamadı]. Ayrı bir alan adı kategori ucu bu sayfada anılmıyor, varlığı ya da yokluğu doğrulanamadı |
| %14 makul mü | Bilinmiyor. Belge kapsama oranı vermiyor. "İlk 1.000 sıranın %80'inde kategori olur" varsayımı kurulum belgesinde de doğrulanmamış bir eşikti [yerel] `KONTROL_LISTESI_KURULUM.md` Bölüm 2.4. Düşük çıkması, kategorilerin yalnızca belirli bir sıra dilimine ya da ayrı bir veri kümesine bağlı olması ile uyumlu, ama bunu destekleyen belge bulunamadı |

### Kodun kendisiyle ilgili bulgular (hata değil, gözlem kaybı)

1. **Ham cevap saklanmıyor.** Önkoşul kaydı yalnızca dört sayı tutuyor (sorulan, kategorili, kategorisiz, hata) [yerel] `ControlCommands.cs` satır 274 ve 301. Kategorisiz cevabın içinde `bucket` ya da `rank` olup olmadığına artık bakılamaz. Bu yüzden (a) ihtimali tamamen kapatılamıyor.
2. **`categories` boş ile `categories` yok aynı sayılıyor** (`ControlSourceParsers.cs` satır 166-169). Bu, şema sapmasını gizleyebilecek tek yer.
3. **Ön sorgu** (`cloudflare.com`, satır 259) yalnızca token ve izni denetliyor, kategori dolu mu diye değil. Bu bilinçli bir tasarım, hata değil.

### Sonuç cümlesi (rapor için)

"Radar kategori kapsamı ilk 1.000 sıradan tohumlu 100 alan adında 14/100 çıktı (eşik 80). Hata sayısı 0. Kod belgedeki yolu okuyor, belgeyle çelişen satır bulunamadı. Ham cevap saklanmadığı için şema sapması ihtimali tam elenemedi. Deneme kullanıldı, B seçeneğine geçildi." Tek örnek, 100 alan adı, popüler ve Türkiye'ye özgü olmayan bir küme; genellenemez.

Kodu değiştirmek (örn. ham cevap kaydı) **yeni bir deneme açmaz**. Böyle bir değişiklik yalnızca gelecekteki bir araştırma için; Kadircan ayrıca karar verir.

---

## 2. Wikidata ile G2: seçim

### 2.1 Türkiye mi, genel finans medyası mı

Önceden kayıtlı metin: B seçeneği "haber sitesi, gazete ve finans basını örnekleri, resmi web sitesi alanı, tarih sabitli". Risk olarak "kapsam Türkiye'ye ve büyük yayınlara kayar, boyutu bilinmiyor" yazılmış [yerel] `KONTROL_GRUBU_YONTEMI.md` satır 141. G2'nin amacı: "finans konuşan ama meşru siteler" ve yalnızca bir **alt sınır** [yerel] aynı belge satır 166. Metin Türkiye ile sınırlamıyor ve G4a'nın "çoğu yabancı" sınırını zaten yazıyor (satır 264).

**Öneri: genel finans medyası ve eğitimi, Türkiye'ye bağlı olanı bir etiket sütunu olarak işaretle, süzgeç yapma.**

Nedenleri:

1. Önceden kayıtlı metin "Türkiye" demiyor. Türkiye ile sınırlamak, ölçütü sonuç (düşük boyut) görüldükten sonra değiştirmek olmaz ama ayrıca yeni bir kural eklemek olur.
2. Yalnızca Türkiye ile ilgili kayıtlar [varsayım] birkaç düzine olur ve 100 altı "yeterli örnek yok" kuralını tetikler.
3. Türkiye etiketi tutulursa, rapor "Türkiye'ye bağlı alt küme" için ayrıca yanlış alarm sayabilir. Bu hem yerel kapsamı gösterir hem sonucu şişirmez.

Karar Kadircan'ın: Alternatif, yalnızca Türkiye süzgeci, açıkça "küçük örnek" riskiyle.

### 2.2 Sorgu

Öğe kimlikleri: Q11032 gazete, Q41298 dergi, Q106651372 iş gazetesi, Q8134 iktisat, Q43015 finans, Q43 Türkiye, P31, P279, P856, P921 [doğrulandı, 2026-11-20 ve 2026-11-21; Q106651372 etiketi "business newspaper" <https://www.wikidata.org/wiki/Special:EntityData/Q106651372.json>]. Eğitim sınıfı için **doğrulanmış kimlik yok**: aşağıda `EDU_CLASS` yer tutucu. Çalıştırmadan önce `wbgetentities` ile bir kez doğrulanır ve sorgu metnine yazılır (sorgu metni, hash alınmadan önce son hâline getirilir).

```sparql
SELECT DISTINCT ?item ?site ?tr WHERE {
  {
    ?item wdt:P31 wd:Q106651372 .
  } UNION {
    VALUES ?cls { wd:Q11032 wd:Q41298 }
    ?item wdt:P31 ?cls .
    VALUES ?topic { wd:Q8134 wd:Q43015 }
    ?item wdt:P921 ?topic .
  } UNION {
    ?item wdt:P31 wd:EDU_CLASS .
    VALUES ?topic { wd:Q8134 wd:Q43015 }
    ?item wdt:P921 ?topic .
  }
  ?item p:P856 ?st .
  ?st ps:P856 ?site .
  ?st wikibase:rank ?rank .
  FILTER(?rank != wikibase:DeprecatedRank)
  FILTER NOT EXISTS { ?st pq:P582 ?ended }
  BIND(EXISTS { ?item wdt:P17 wd:Q43 } AS ?tr)
}
```

Tasarım notları:

- Alt sınıf zinciri (`P279*`) **yok**: 60 saniye sınırını aşma riski (Bölüm 2.3) ve kapsam büyümesi. Bu, bazı gerçek finans yayınlarını dışarıda bırakır, kabul edilen bir sınır.
- `FILTER NOT EXISTS pq:P582`: bitiş zamanı niteleyicisi olan (kapanmış) adresler elenir. Kullanımdan kalkmış (deprecated) değerler elenir.
- `?tr` yalnızca etiket: ülke (P17) Türkiye ise doğru. Süzgeç değil.
- Yinelenen öğe ve alan adı: `DISTINCT`. Bir öğenin birden çok adresi olabilir, ikisi de tabloda kalır, kayıtlı alan adına indirgenince tekilleşir.
- `SELECT` çıktısı JSON (`Accept: application/sparql-results+json`), satırlar `item` kimliğine göre sıralanmaz (sunucu sırası garanti değil), ham dosya bu hâliyle saklanır. Seçimde kendi sıralamamız (tohum) var.

### 2.3 Sorgu sınırları ve nezaket

[doğrulandı, 2026-11-20] <https://www.mediawiki.org/wiki/Wikidata_Query_Service/User_Manual>: Kesin 60 saniye sorgu süresi sınırı. İstemci başına 60 saniyelik pencerede 60 saniye işlem zamanı, dakikada 30 hatalı sorgu, IP başına en çok 5 paralel sorgu. Aşılırsa 429 ve `Retry-After`. Sınırlar değişebilir.

Bizim kullanımımız:

- Toplam en çok **3 sorgu isteği**: (1) isteğe bağlı sözdizimi denemesi (`LIMIT 5`, sonucu karar için kullanılmaz), (2) gerçek çalıştırma, (3) yalnızca ağ hatasında yeniden deneme. Paralel istek yok. Her istek arasında en az 60 saniye. 429 gelirse `Retry-After` kadar bekle, atlama yok.
- Kimlik doğrulama (`wbgetentities`) için ayrı küçük istek: Wikidata API'sine, en çok 1-2 istek.
- Zaman aşımı olursa sorgu **sonuca bakarak** değiştirilmez. Sorgu kuralı: Bölüm 2.5.

### 2.4 User-Agent politikası

[doğrulandı] <https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy>: İstemci adını ve iletişim bilgisini içermeli. İletişim bilgisi e-posta, web sitesi ya da vikide kullanıcı adı olabilir. Biçim: `<istemci adı>/<sürüm> (<iletişim bilgisi>) <kütüphane>/<sürüm>`.

Bir **web sitesi** kabul edildiği için projenin GitHub URL'si **yeterli görünüyor**; kişisel e-posta gerekmez. Örnek biçim:

```
alan-adi-kontrol-laboratuvari/0.1 (https://github.example/proje-adresi; arastirma) dotnet/10
```

Değerlendirme: GitHub deposu herkese açık ve "issue" açılabilir, yani operatörün ulaşma yolu var. Ama bu **repo gizli ya da silinmiş** ise iletişim yolu kırılır. Repo adresi hazır değilse (ya da herkese açık değilse) bu yol çalışmaz. Aynı kural Radar ve diğer kaynaklar için de uyarılmalı. Politikanın "yalnızca URL yeterli mi" sorusuna açık bir cevabı okunan özette yok; "e-posta, web sitesi ya da wiki kullanıcısı" ifadesi var. Kadircan'ın seçeceği şey: GitHub URL'si mi, ayrı bir proje e-postası mı.

### 2.5 Beklenen sonuç sayısı

Hepsi **[varsayım]**, ölçülmedi:

- Ham satır (üç sınıf birlikte, P856 var): yaklaşık 200-1.500.
- Alan adına indirgeme, PSL özel bölüm elemesi (E06), pozitiflerle çakışma (E01), G1 elemesi (E02) sonrası: yaklaşık 150-1.000.
- Türkiye'ye bağlı alt küme (`?tr`): yaklaşık 10-150.
- 300'ü dolduramama ihtimali orta. **100'ün altı** ihtimali düşük ama sıfır değil (özellikle `EDU_CLASS` boş ya da çok dar çıkarsa). Önceden kayıtlı kural: 100'den az ise "yeterli örnek yok", oran verilmez.

Popülerlik ve haber/eğitim yanlılığı: Wikidata'daki kayıtlar tanınmış yayınlara kayar, yeni küçük finans bloglarını temsil etmez. Bu G2'nin amacı (finans konuşan meşru site) için uygun ama raporda "popüler yanlılığı" yazılır.

---

## 3. Seçim, tohum ve taahhüt (G4a'daki gibi)

Aşağıdaki sıra [yerel] `KONTROL_LISTESI_KURULUM.md` Bölüm 4.2 (G4a) ile paralel. Her adımda hiçbir alan adı ekrana, log'a ya da repoya yazılmaz; yalnızca sayı ve tuzlu kimlik.

1. **Tohum.** `kontrol-g2-wikidata-YYYY-MM-DD` biçiminde metin. Sorgu çalıştırılmadan **önce** yazılır ve commit edilir. Sorgu metni de (son hâli) ayrı commit'lenir; hash'i commit mesajına ya da manifest şablonuna yazılır.
2. **Sorguyu bir kez çalıştır.** Ham JSON cevap `data/private/control/src/` altına yazılır (commit edilmez, Kural 8). Kaydedilen: UTC çalıştırma anı, HTTP durumu, cevap dosyasının SHA-256'sı, sorgu metninin SHA-256'sı, kullanılan User-Agent (iletişim URL'si dahil).
3. **Ayrıştır.** Her `site` değerinden ana bilgisayarı çıkar, yalnızca `http(s)`; kayıtlı alan adına indirge (Hafta 1 normalleştirmesi, PSL sürümü manifestoda). Ayrıştırılamayanlar `E00` olarak sayılır.
4. **E06 (PSL özel bölümü).** Tam liste ile yalnızca ICANN bölümüyle iki kez indirge, farklıysa ele. Bu adım burada özellikle önemli: Wikidata'da sosyal medya ve barındırma alt alanlarına bağlı resmi adresler olur.
5. **E01 (pozitiflerle çakışma)**, **E02 (G1'de olanlar)**. G2, G1'den sonra seçildiği için G1 dondurulmuş listesine göre.
6. **E04 (kişi adına benzer desen).** Yalnızca say. Kişisel blog ya da kişi adına yayın olabilir; G2'ye kişi blogu girmemesi için kayıt yalnızca kuruluş/yayın sınıfından geliyor (P31 ile yayın, gazete, eğitim kuruluşu).
7. **Sırala.** `SHA-256(tohum + "|" + kayıtlı_alan_adı)` artan. İlk 300. Rastgele sayı üreteci yok.
8. **100 altı kuralı.** 100'den az ise "yeterli örnek yok", oran verilmez.
9. **Taahhüt.** Tuzlu kimlik listesi ve Merkle kökü, G4a ile aynı kod yolu. Repoya yalnızca alan adsız manifest girer: Girdi satır sayısı, eleme kodu sayıları, seçilen sayı, `?tr` olan sayısı, PSL sürümü, tohum, betik commit'i, sorgu ve cevap SHA-256'sı, çalıştırma anı.
10. **Ayar ve değerlendirme ayrımı.** Zaman eşleşmesi yok; tohumlu özetle küme düzeyinde %70/%30 [yerel] `KONTROL_GRUBU_YONTEMI.md` satır 361.

### 3.1 Wikidata'nın durumu nasıl sabitlenir

Sorgu hizmeti canlı veri sorar; tarihli anlık görüntü sunmaz [yerel] `KONTROL_LISTESI_KURULUM.md` Bölüm 2.4 (aynı sayfa 2026-11-20'de okundu). Bu yüzden **aynı sonuç sonradan yeniden üretilemeyebilir**. Sabitleme:

- Ham cevap dosyası saklanır, tek doğru kaynak odur. SHA-256'sı ve UTC anı manifestoda.
- Sorgu metninin SHA-256'sı.
- İsteğe bağlı ek: Cevap başlıklarındaki zaman bilgisi ve (varsa) sorgu hizmetinin veri tazelik zamanı. Bu alanın adı ve sorgusu bu çalışmada **doğrulanmadı**, bu yüzden zorunlu değil.
- Verinin lisansı CC0, atıf gerekmiyor [doğrulandı, 2026-11-20] <https://www.wikidata.org/wiki/Wikidata:Licensing>.
- Sınır: Hash yalnızca dosyanın değişmediğini kanıtlar. Wikidata'nın o anki içeriğinin doğru olduğunu kanıtlamaz. Kayıtlar topluluk tarafından düzenlenir ve yanlış ya da eski P856 olabilir.

### 3.2 Hata kuralları

Hata, zaman aşımı, 429 ve boş cevap "kategori yok" ya da "sonuç yok" sayılmaz; ayrı sınıflanır (üst belge 3.2). Sorgu **sonuca bakarak** değiştirilmez: Çalıştırmadan önce dondurulan metin tek geçerlidir. Sözdizimi ya da zaman aşımı yüzünden çalışmazsa değişiklik yalnızca Değişiklik kaydı ile yapılır ve kaydedilen yeni metin yeniden hash'lenir.

---

## 4. Kadircan'ın onaylaması gereken noktalar

1. **Türkiye süzgeci mi etiket mi.** Öneri: Etiket. Alternatif: Süzgeç (küçük örnek riski).
2. **Eğitim sınıfı kimliği.** Doğrulanmış kimlik yok; seçimden önce kimlik belirlenir ya da eğitim kolu sorgudan çıkarılır (yalnızca gazete, dergi, iş gazetesi). Karar sorgu çalışmadan önce.
3. **User-Agent iletişim yolu.** GitHub URL'si yeterli mi; repo herkese açık mı; yoksa ayrı bir proje e-postası mı.
4. **Değişiklik kaydı** (sonuç görülmeden): G2 kaynağı B'ye geçti, önkoşul sonucu 14/100, sorgu metni ve etiket kuralı, 3 sorgu sınırı, 100 altı kuralı.
5. **Radar kodu:** Ham cevap kaydı eklemek isteniyorsa ayrı karar (yeni deneme açmaz).
6. **Sınır cümlesi raporda:** "G2 Wikidata'dan gelir; popüler ve tanınmış yayınlara kayar, Türkiye ile sınırlı değildir; yanlış alarm oranı alt sınırdır."

## 5. Kaynaklar (erişim 2026-11-21, aksi yazılmadıkça)

- Radar alan adı ucu: <https://developers.cloudflare.com/api/resources/radar/subresources/ranking/subresources/domain/methods/get/> [doğrulandı]
- Radar ranking genel arama özeti: <https://developers.cloudflare.com/radar/investigate/domain-ranking-datasets/> [özet, sayfa açılmadı]
- Wikimedia User-Agent politikası: <https://foundation.wikimedia.org/wiki/Policy:Wikimedia_Foundation_User-Agent_Policy> [doğrulandı]
- Wikidata öğesi Q106651372: <https://www.wikidata.org/wiki/Special:EntityData/Q106651372.json> [doğrulandı]
- Sorgu hizmeti kılavuzu ve lisans: <https://www.mediawiki.org/wiki/Wikidata_Query_Service/User_Manual>, <https://www.wikidata.org/wiki/Wikidata:Licensing> [doğrulandı 2026-11-20, bu oturumda yeniden açılmadı]
- Yerel: `KONTROL_GRUBU_YONTEMI.md`, `KONTROL_LISTESI_KURULUM.md`, `src/Fraud.Collectors/Control/` (`ControlSourceClients.cs`, `ControlSourceParsers.cs`, `ControlCommands.cs`)
