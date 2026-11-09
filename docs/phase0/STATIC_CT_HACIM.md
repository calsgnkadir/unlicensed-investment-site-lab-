# Static CT günlük hacim ölçümü (Aşama 0)

Tarih: 10 Kasım 2026 (ölçüm UTC 9 Kasım 21:21-21:33 arası, yani Türkiye saatiyle gece yarısından sonra)
Log: Let's Encrypt Sycamore 2026h2, `https://mon.sycamore.ct.letsencrypt.org/2026h2/`
Betik: `scripts/phase0/ct_volume_probe.py`

Etiketler: [ölçüldü] doğrudan okunan sayı, [hesap] ölçülenden türetilen sayı, [bilinmiyor] ölçülmedi.

## Yöntem

- Static CT API belgesi (c2sp.org/static-ct-api) okundu. Yollar oradan kuruldu: `<önek>/checkpoint` ve `<önek>/tile/data/<N>` (N üç haneli bölümler, sonuncu hariç `x` önekli). Tam tile 256 kayıt.
- Checkpoint iki kez okundu, arası 660 sn (11 dk). Fark, kayıt hızını verir.
- 10 tam veri tile'ı indirildi: en yeni 4 tam tile ve log boyunca dağıtılmış 6 tile (log boyunun %10, 25, 40, 55, 70, 85'i). Tile'lar diske yazılmadı, bellekte işlendi.
- Her tile'da bayt boyutu (gzip ve açılmış) ile kayıt sayısı ölçüldü. Sertifikaların DNS adlarında şu kelimeler arandı: yatirim, borsa, kripto, forex, trade, invest, capital, menkul. Yalnızca sayı tutuldu, hiçbir alan adı yazılmadı veya saklanmadı.
- İstek: bu logun monitoring adresine 12 istek (2 checkpoint + 10 tile), arada en az 1,1 sn. Genel User-Agent, e-posta yok. Belgeyi okumak için GitHub'dan ayrıca 3 küçük istek atıldı (log değil). Sertifikadaki alan adlarına, DNS'e, hedef sitelere istek yok.

## Ham rakamlar [ölçüldü]

| | Değer |
|---|---|
| Checkpoint 1 (21:21:59 UTC) ağaç boyutu | 1.092.832.238 |
| Checkpoint 2 (21:32:59 UTC) ağaç boyutu | 1.092.910.631 |
| Fark / süre | 78.393 kayıt / 660,4 sn |
| Tile başına kayıt | 256 (10 tilenin hepsinde) |
| Tile başına gzip bayt, ortalama (10 tile) | 167.522 (aralık 143.160 - 193.155) |
| Tile başına açılmış bayt, ortalama (10 tile) | 504.419 (aralık 352.977 - 637.594) |
| En yeni 4 tile, gzip ortalama | 182.948 |
| En yeni 4 tile, açılmış ortalama | 603.489 |
| Örneklenen sertifika | 2.560 |
| Finans kelimesi geçen sertifika | 2 |

Not: Dağıtılmış 6 tile, en yeni 4 tile'dan küçük çıktı. Ön sertifika payı da farklı (en yenilerde 201-233 / 256, dağıtılmışlarda 71-255 / 256). Nedeni araştırılmadı.

## Hesaplar [hesap]

- Kayıt hızı: 78.393 / 660,4 = 118,7 kayıt/sn, yani saatte yaklaşık 427 bin, günde yaklaşık **10,3 milyon**. Tek 11 dakikalık pencere. Günün saatine göre değişir, bu yüzden kaba bir tahmin. DATA_SOURCES.md'deki önceki tahmin (günde 4-6 milyon) bunun altında kalmış.
- Günlük tile: 10.255.468 / 256 = yaklaşık **40 bin tile**.
- Günlük bant (gzip ile indirme): 40.060 x 167.522 B = yaklaşık **6,7 GB**. En yeni tile'larla: yaklaşık 7,3 GB. İstek sayısı günde yaklaşık 40 bin.
- Günlük disk, hepsini saklarsak:
  - Açılmış: 40.060 x 504.419 B = yaklaşık **20 GB** (en yeni tile'larla yaklaşık 24 GB).
  - gzip halinde tutarsak: yaklaşık 6,7-7,3 GB.
- Aday oranı: 2 / 2.560 = %0,08. %95 güven aralığı (Wilson) yaklaşık %0,02 - %0,28. Günde yaklaşık 8 bin sertifika, aralık 2 bin - 29 bin.
- Günlük disk, yalnızca finans kelimeli adayları saklarsak: sertifika başına ortalama yaklaşık 2 KB (504.419 / 256 = 1.970 B). 8 bin x 2 KB = yaklaşık **16 MB/gün**, üst uçta (29 bin) yaklaşık 57 MB/gün. Bant yine de 6,7-7,3 GB/gün olur, çünkü eleme için tile'ın tamamı indirilmek zorunda.

## Makinedeki boş disk ile karşılaştırma

Görevde boş disk ~41 GB yazıyordu. Ölçüm sırasında `df` C: için 29 GB boş gösterdi [ölçüldü]. İki rakam uyuşmuyor, güvenli tarafta olmak için 29 GB'ı kullandım.

| Senaryo | Günlük | 29 GB'a oranı |
|---|---|---|
| Her şeyi açılmış sakla | 20-24 GB | Bir günü zor sığar, ikinci güne sığmaz |
| Her şeyi gzip sakla | 6,7-7,3 GB | Yaklaşık 4 gün |
| Yalnızca adayları sakla | 16-57 MB | Yıllarca sığar |

## Sonuç

- Tek log için günlük akışı okumak mümkün ama ağır: günde yaklaşık 40 bin istek ve 7 GB indirme. Tile'lar anında süzülüp atılmalı, ham veri saklanmamalı (ROADMAP 2.3 ile uyumlu).
- Yalnızca adayları saklamak makineye rahatça sığar. Hepsini saklamak sığmaz.
- Bu hacmin yeni logları ve Willow'u da kapsaması gerekirse bant ve iş yükü katlanır. Sürekli akışta 118 kayıt/sn'yi işleyecek bir istemci gerekir. Ayrıştırma hızı ölçülmedi.

## Bu logun toplam Static CT içindeki payı

[bilinmiyor]. DATA_SOURCES.md ve bu ölçüm yalnızca Sycamore'un boyutunu veriyor, diğer logların günlük hacmi ölçülmedi. Tek bilgi: Let's Encrypt her sertifikayı iki loga (Sycamore ve Willow) yazdığını söylüyor, bu yüzden Willow'un benzer hacimde olması beklenir [özet, doğrulanmadı].

## Sınırlar ve yanlış alarm

- Günlük hız tek 11 dakikalık pencereden çıktı. Gün içi ve haftalık değişim ölçülmedi.
- Bayt boyutları 10 tile'dan çıktı. Tile'lar arasında 1,8 kata kadar fark var, ortalama kaba.
- Dilim "2026h2" sertifikanın bitiş tarihine göre ayrılıyor. Bu logun tamamı, internetteki tüm yeni sertifikaları temsil etmez.
- Aday oranı yalnızca 2 eşleşmeye dayanıyor. Rakam çok belirsiz (yukarıdaki aralık). Örnek rastgele değil, 10 tile'dan oluşuyor.
- Kelime araması alt dize eşleşmesi: "trade", "capital", "invest" gibi kelimeler meşru sitelerde de geçer. 2 eşleşmeye elle bakılmadı. Bu oran "şüpheli site oranı" değil, yalnızca bir filtrenin ne kadar kayıt geçireceğinin tahminidir. Yanlış alarm oranı bilinmiyor [N].
- Yalnızca DNS adı (SAN dNSName) arandı, ortak ad (CN) ve IP alanları bakılmadı. Checkpoint imzası doğrulanmadı, tutarlılık kontrolü yapılmadı (yalnızca hacim ölçüldü, güvenlik doğrulaması istemci aşamasında yapılacak).
- Hiçbir alan adı yazılmadı veya saklanmadı (Kural 4). Sonuçlar yatırım tavsiyesi veya sitelerle ilgili bir yargı değildir.
