# DNS kodu okuma rehberi (Hafta 4)

DNS paketini kuran ve çözen kodu hazır kütüphane kullanmadan, kendimiz yazdık. Bu rehber, kodu mülakatta anlatabilecek kadar anlamak için hangi sırayla okuman gerektiğini gösterir. Her maddede açman gereken satır ve kendine sorman gereken bir soru var. Soruyu koda bakmadan cevaplayabiliyorsan o maddeyi tamamlamış sayılırsın.

Kaynak: [RFC 1035](https://www.rfc-editor.org/rfc/rfc1035) Bölüm 4. Paket biçimini bilmeden kodu okumaya başlama: 12 baytlık başlık, soru bölümü, cevap kayıtları.

## 1. Sorgu paketini kurmak

| Yer | Soru |
|---|---|
| [DnsQueryBuilder.cs:32](../src/Fraud.Collectors/Dns/DnsQueryBuilder.cs) | İşlem kimliği neden `Random` ile değil de `RandomNumberGenerator` ile üretiliyor? Tahmin edilebilen bir kimlik nasıl sahte cevaba yol açar? (RFC 5452) |
| DnsQueryBuilder.cs:45 | Bayraklarda yalnızca RD biti 1. EDNS neden bilerek eklenmedi? Bu karar 512 baytlık sınırı ve TC yolunu nasıl çalışır hale getiriyor? |
| [DnsName.cs:41, :78](../src/Fraud.Collectors/Dns/DnsName.cs) | Türkçe karakterli bir ad pakete nasıl yazılır (Punycode)? Bir adın paketteki uzunluğu neden "karakter sayısı + 2" ediyor? |

## 2. Cevabı çözmek: En çok soru gelecek kısım

| Yer | Soru |
|---|---|
| [DnsMessageReader.cs:107](../src/Fraud.Collectors/Dns/DnsMessageReader.cs) | Uzunluk baytının üst iki biti ne anlatır? 00, 11, 01 ve 10 değerleri ne demek? |
| DnsMessageReader.cs:125 | Sıkıştırma işaretçisi neden yalnızca geriye gösterebiliyor? Bu kural sonsuz döngüyü nasıl imkânsız kılıyor? |
| DnsMessageReader.cs:131, :163 | Geriye gösterme kuralı varken neden ayrıca "en fazla 32 atlama" ve "en fazla 255 bayt" sınırları da konmuş? |
| DnsMessageReader.cs:136 | İlk işaretçiden sonra okumaya nereden devam edilir (`continueAt`)? Bu yanlış yapılırsa ne bozulur? |
| [DnsMessageParser.cs:39](../src/Fraud.Collectors/Dns/DnsMessageParser.cs) | Ayrıştırıcı dışarıya neden tek bir hata türü (`DnsFormatException`) veriyor? |
| DnsMessageParser.cs:72, :97, :128, :176 | Soru sayısı en fazla 1 olabilir (RFC 9619), RDLENGTH paketin içinde kalmalı, kayıt tam RDLENGTH'te bitmeli, sonda fazladan bayt olmamalı. Bu kontrollerden biri olmasaydı saldırgan ne yapabilirdi? |
| DnsMessageParser.cs:163, :193 | SOA kaydındaki RNAME neden okunduktan sonra atılıyor (ipucu: Kural 5)? TTL'in üst biti 1 ise neden 0 sayılıyor (RFC 2181 Bölüm 8)? |

## 3. Cevaba güvenmek

| Yer | Soru |
|---|---|
| [DnsResponseValidator.cs:23, :29, :42](../src/Fraud.Collectors/Dns/DnsResponseValidator.cs) | Bir cevap hangi üç koşulu sağlarsa kabul ediliyor? Soru neden büyük/küçük harfe duyarsız karşılaştırılıyor? |
| [DnsQueryClient.cs:178, :201, :206](../src/Fraud.Collectors/Dns/DnsQueryClient.cs) | CNAME zinciri halka halka nasıl izleniyor? Zincirin dışındaki kayıtlar neden atılıyor ("bailiwick")? Döngü ve 16 halka sınırı nasıl yakalanıyor? |
| DnsQueryClient.cs:135, :236 | NXDOMAIN ile "kayıt yok" (NOERROR ve boş cevap) arasındaki fark ne? Olumsuz TTL nasıl hesaplanıyor (RFC 2308 Bölüm 5)? |
| DnsQueryClient.cs:149 | Cevap bölümü dolu ama içindeki kayıtlar sorumuza ait değilse sonuç neden "kayıt yok" değil de hata (`UnrelatedAnswer`) sayılıyor? |

## 4. Taşıma: UDP, TCP ve DoH

| Yer | Soru |
|---|---|
| [UdpDnsTransport.cs:45–46, :57, :62](../src/Fraud.Collectors/Dns/UdpDnsTransport.cs) | Port 0'a bağlanmak neden rastgele bir kaynak portu verir? Bağlantısız bir protokol olan UDP'de `Connect` ne işe yarar? Yanlış adresten gelen bir paket neden hata sayılmıyor da yok sayılıp beklemeye devam ediliyor? |
| [TcpDnsTransport.cs:40, :50](../src/Fraud.Collectors/Dns/TcpDnsTransport.cs) | DNS TCP üzerinden giderken pakete neden 2 baytlık bir uzunluk öneki ekleniyor? `ReadExactlyAsync` hangi hatayı önlüyor? |
| DnsQueryClient.cs:83, :249 | TC biti geldiğinde ne yapılıyor? Ortasından kesilmiş, ayrıştırılamayan bir paket neden yine de kesik sayılabiliyor? |
| [DohDnsTransport.cs:50, :59, :75, :81](../src/Fraud.Collectors/Dns/DohDnsTransport.cs) | DoH aynı paketi HTTPS içinde taşıyor. Neden yalnızca 200 durum kodu ve `application/dns-message` içerik türü kabul ediliyor? Neden `RequestVersionOrLower` kullanılıyor, HTTP/3'e geçilseydi hangi denetim atlanırdı? Kimlik neden 0 (DnsQueryClient.cs:273, RFC 8484 Bölüm 4.1)? |
| DnsQueryClient.cs:276, :283 | Hız sınırı neden her pakette ayrı ayrı bekleniyor? Hangi hatalar tekrar deneniyor, hangileri denenmiyor ve neden? |

## 5. Projeye bağlanan yerler

| Yer | Soru |
|---|---|
| [DnsResolverCatalog.cs:37](../src/Fraud.Collectors/Dns/DnsResolverCatalog.cs) | Çözücü listesi neden ayar dosyasında değil de koda gömülü? |
| [DnsCollector.cs:137](../src/Fraud.Collectors/Dns/DnsCollector.cs) | "Engel işareti" nedir? Böyle işaretlenen bir cevabın IP'si neden RIPEstat'a gönderilmiyor? |
| DnsCollector.cs:172, :78 | Aynı gün ikinci kez çalışınca neden yalnızca hatalı satırın üzerine yazılıyor? Sorgu başına konan genel `catch` hangi veri kaybını önlüyor? |

| [NetworkSourceLock.cs:96](../src/Fraud.Collectors/Network/NetworkSourceLock.cs) | Veritabanı kilidi (`pg_try_advisory_lock`) tur ortasında neden kaybolabilir? Kaybolduğu nasıl fark ediliyor, ve iki süreç aynı anda sorgu atarsa hangi kuralımız bozulur? |

## Kendini sına

Testler, kodun ne yapması gerektiğini anlatan en iyi belge:

- `tests/Fraud.Integration.Tests/Dns/DnsWireTests.cs`: Elle kurulmuş paketler. Birini seç, baytları RFC 1035'teki tabloyla tek tek eşleştir.
- `tests/Fraud.Integration.Tests/Dns/DnsFuzzTests.cs`: Rastgele ve bozulmuş paketlerle yapılan fuzz testi. Neden yalnızca "ya çözülür ya da `DnsFormatException` fırlatır" diye kontrol ettiğini açıkla.

Mülakatta sorulabilecek bir soru: "DNS önbellek zehirlemesine karşı kodunda neler var?" Cevabında rastgele kimlik, rastgele kaynak port, soru eşleşmesi, bailiwick kontrolü ve DoH geçmeli.
