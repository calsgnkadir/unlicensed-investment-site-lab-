# Yabancı marka listesi doğrulaması (foreign_brands.txt)

Erişim tarihi: 2026-11-21. Hiçbir alan adına istek atılmadı. Dosya değiştirilmedi; düzeltme kod sahibine bırakıldı.

## Kaynak ve sürüm

| Ne | Değer | Okuma durumu |
|---|---|---|
| FSB, "2025 List of Global Systemically Important Banks (G-SIBs)" | https://www.fsb.org/2025/11/2025-list-of-global-systemically-important-banks-g-sibs/ ve PDF: https://www.fsb.org/uploads/P271125.pdf | Sayfa okundu: yayın tarihi 27 Kasım 2025, 29 banka, 2024 ile aynı bankalar. PDF'in metni sıkıştırılmış olduğu için çözülemedi. |
| FSB, aynı liste, kova bazında isimler | FSB arama özeti (fsb.org PDF'inden) kova 4, 3 ve 2'yi adıyla verdi | Arama özeti, doğrudan okuma değil |
| Kova 1'in 15 ismi | https://en.wikipedia.org/wiki/List_of_systemically_important_banks | İkincil kaynak. Sayı (1+4+9+15 = 29) FSB özetiyle tutuyor. |
| FSB 2024 listesi | https://www.fsb.org/uploads/P261124.pdf (26 Kasım 2024) | PDF çözülemedi, okunamadı |

- Güncel liste 2025 sürümüdür (27 Kasım 2025). 2026 sürümü Kasım 2026'da bekleniyor; arama sonuçlarında bulunamadı, yayımlandığı doğrulanmadı. Yayımlanırsa bu belge yeniden kontrol edilmeli.
- 2024'ten 2025'e banka kümesi değişmedi, yalnızca kova değişti (Bank of America ve ICBC kova 3'e çıktı, Deutsche Bank kova 1'e indi). Marka benzerliği sinyali için kova önemsiz; yalnızca isim kümesi önemli. Dosyanın "Kasım 2024" etiketi bu yüzden isim açısından geçerli kalır, ama kaynak satırı 2025 olarak güncellenmeli.
- Kısa uyarı: Kova 1 isimleri doğrudan FSB belgesinden okunamadı. Doğrulama için FSB PDF'inin ek tablosuna bir kez elle bakılması önerilir.

## Resmi liste (2025, 29 banka)

- Kova 4: JPMorgan Chase
- Kova 3: Bank of America, Citigroup, HSBC, ICBC
- Kova 2: Agricultural Bank of China, Bank of China, Barclays, BNP Paribas, China Construction Bank, Goldman Sachs, Groupe Crédit Agricole, Mitsubishi UFJ FG, UBS
- Kova 1: Bank of Communications, Bank of New York Mellon, Deutsche Bank, Groupe BPCE, ING, Mizuho FG, Morgan Stanley, Royal Bank of Canada, Banco Santander, Société Générale, Standard Chartered, State Street, Sumitomo Mitsui FG, Toronto-Dominion Bank, Wells Fargo

## Marka adı alan adı etiketine nasıl indirgenir

1. Küçük harfe çevrilir.
2. Aksanlar ASCII'ye indirilir (Crédit becomes credit, Société becomes societe).
3. Boşluk, tire, nokta ve "&" silinir.
4. Hukuki ek ve genel sözler (AG, Group, FG, Financial) atılır; ayırt edici çekirdek kalır. Kısa ad ile uzun ad ayrı satır olabilir.
5. Aday alan adının etiketi de aynı biçimde tiresiz yazılır, çünkü taklit alan adlarında tire sık kullanılır (bank-of-america gibi).

## Karşılaştırma tablosu

5 karakter kuralı: dosyadaki hiçbir satır kurala aykırı değil (en kısa satır "mizuho", 6 karakter). Kurala uymayan, yani kasıtlı dışarıda bırakılan adlar: hsbc, icbc, ubs, ing, bnp, mufg (dosyanın yorumunda zaten bildirilmiş).

| Dosyada | Resmi listede | Öneri |
|---|---|---|
| jpmorgan | JPMorgan Chase | Kalsın. İsteğe bağlı: jpmorganchase ekle |
| citigroup | Citigroup | Kalsın |
| citibank | Yok (Citigroup'un alt markası) | Resmi listede yok. Ya çıkar ya da "alt marka, listede yok" diye yorumla işaretle |
| bankofamerica | Bank of America | Kalsın |
| bankofchina | Bank of China | Kalsın |
| barclays | Barclays | Kalsın |
| bnpparibas | BNP Paribas | Kalsın |
| deutschebank | Deutsche Bank | Kalsın |
| goldmansachs | Goldman Sachs | Kalsın |
| morganstanley | Morgan Stanley | Kalsın |
| santander | Banco Santander | Kalsın |
| societegenerale | Société Générale | Kalsın |
| standardchartered | Standard Chartered | Kalsın |
| statestreet | State Street | Kalsın |
| wellsfargo | Wells Fargo | Kalsın |
| creditagricole | Groupe Crédit Agricole | Kalsın. İsteğe bağlı: groupecreditagricole |
| mizuho | Mizuho FG | Kalsın |
| sumitomomitsui | Sumitomo Mitsui FG | Kalsın |
| bnymellon | Bank of New York Mellon | Kalsın. İsteğe bağlı: bankofnewyorkmellon |
| royalbankofcanada | Royal Bank of Canada | Kalsın |
| torontodominion | Toronto-Dominion Bank | Kalsın |
| agriculturalbankofchina | Agricultural Bank of China | Kalsın |
| chinaconstructionbank | China Construction Bank | Kalsın |
| groupebpce | Groupe BPCE | Kalsın |
| (yok) | Bank of Communications | EKLE: bankofcommunications |
| (yok) | Mitsubishi UFJ FG | EKLE: mitsubishiufj (kısa ad mufg 4 karakter, dışarıda) |
| (yok) | Industrial and Commercial Bank of China | EKLE: industrialandcommercialbankofchina (kısa ad icbc dışarıda). Uzun, düşük değerli; kod sahibi karar verir |
| (yok) | HSBC, UBS, ING | Kısa (4, 3, 3 karakter), kural gereği alınmaz. Bilinen sınır olarak kalır |

Not: "bankofchina", "agriculturalbankofchina" ve "industrialandcommercialbankofchina" birbirinin alt dizesi olabilir. Eşleştirici en uzun eşleşmeyi seçmelidir; aksi halde "bankofchina" uzun adlara da takılır.

## Sonuç

- Yanlış yazılmış ad: 0.
- Resmi listede olmayan satır: 1 (citibank).
- Eksik (kurala uyan): 3 (bankofcommunications, mitsubishiufj, industrialandcommercialbankofchina).
- Kural ihlali: 0.
- Başlık satırı: kaynak "Kasım 2024" yerine "2025 (27 Kasım 2025)" yazılmalı, "doğrulanmadı" notu doğrulama sonrası güncellenmeli.
- Dosya donmuş olduğu için bu düzeltmeler v2 olarak açılır.

## Önerilen son liste (v2)

```
jpmorgan
citigroup
bankofamerica
bankofchina
barclays
bnpparibas
deutschebank
goldmansachs
morganstanley
santander
societegenerale
standardchartered
statestreet
wellsfargo
creditagricole
mizuho
sumitomomitsui
bnymellon
royalbankofcanada
torontodominion
agriculturalbankofchina
chinaconstructionbank
groupebpce
bankofcommunications
mitsubishiufj
industrialandcommercialbankofchina
```

citibank isteğe bağlı olarak kalabilir (alt marka olarak işaretlenirse). Bu liste yalnızca marka benzerliği içindir; bir alan adının bu markaya benzemesi onu dolandırıcı yapmaz.
