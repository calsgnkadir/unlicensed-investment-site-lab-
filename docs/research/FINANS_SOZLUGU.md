# Geniş Finans Sözlüğü (v1)

Tarih: 15 Kasım 2026. Terim dosyası: [finans_sozlugu.txt](finans_sozlugu.txt). Karar kaynağı: [KONTROL_GRUBU_YONTEMI.md](KONTROL_GRUBU_YONTEMI.md) Bölüm 10.1, karar 4 ve Bölüm 4.3 (madde 2 ve 3).

Bu belgede alan adı yok (Kural 4). Bu bir yatırım tavsiyesi değildir.

## 1. Amaç

- G2 ve G3 kontrol gruplarını seçmek ve "adında finans kelimesi var mı" çerçevesini tanımlamak (Bölüm 4.5, madde 2).
- **Hafta 7 baseline'ından ayrıdır.** Baseline kendi 8 terimiyle ölçülür ve kendi listesi değişmez. Bu sözlük sinyal değildir, puan üretmez, yalnızca örnekleme çerçevesidir.
- Sözlük, baseline sonuçlarına, pozitif listesine, SPK listesine ve `data/private` ile aday veritabanına **bakılmadan** yazıldı. Terimler hiçbir veriden türetilmedi.

## 2. Terimler nereden geldi

- Terimler, Türkçe ve İngilizce temel finans, kripto, forex, yatırım, borsa ve banka kelimelerinden yazarın genel alan bilgisiyle seçildi. Belirli bir kaynak listesi okunarak çıkarılmadı. Bu yüzden her terim için kaynak bağlantısı yok. Durum: [doğrulanamadı] (kaynak taraması bu çalışmada yapılmadı).
- Bu bir eksiklik olarak yazılır: Sözlük bir "tam finans sözlüğü" iddiası taşımaz, makul ve geniş bir ön tanımdır.
- **Marka adı yok:** Yerli ve yabancı banka, aracı kurum, borsa, kripto platformu, işlem yazılımı adları sözlüğe girmedi. Marka benzerliği ayrı bir sinyaldir. Bazı sözcükler hem genel hem marka olabilir, bunlar da alınmadı (örnek mantık: Yalnızca marka olarak yaygın bir sözcük elendi).
- Kapsam dışı bırakılanlar: Sigorta, emlak, genel "para kazanma" ve "bonus" sözcükleri, çok genel sözcükler (money, profit, token, wallet, asset, stock, yield). Gerekçe: Finans dışı sitelerde çok geçer, G3'ü seyreltir.

## 3. Terim sayısı

- Toplam **55 terim**: Türkçe 22, İngilizce 26, kripto 7.
- Baseline'ın 8 terimi dahil. Yalnızca geniş sözlükte olan terim sayısı: **47**.
- Hariç tutma dizesi: 6.

## 4. Eşleştirme kuralları

1. **Neye bakılır:** Kayıtlı alan adının (eTLD+1, Public Suffix List ile) **soldaki etiketi**. Üst düzey alan adı (TLD / kamu soneki) ve alt alan adları aranmaz. Örnek: Yalnızca ".trade" ile biten bir ad, etiketinde terim yoksa eşleşmez.
2. **Alt dize:** Terim etiketin herhangi bir yerinde geçerse eşleşir (tam sözcük şartı yok). Çünkü alan adlarında sözcükler bitişik yazılır ve sözcük sınırı güvenilir bulunamaz. Bu, yanlış eşleşmeyi artırır, bilinen bir sınırdır.
3. **Normalleştirme (sırayla):**
   - `xn--` ile başlayan etiket Unicode'a çevrilir (IDNA). Çevrilemezse eşleşmez ve ayrı sayılır.
   - Harf katlama: ı, İ, I ve î → i. ş → s. ğ → g. ü ve û → u. ö → o. ç → c. â → a.
   - Ardından ayrıştırma (NFD) yapılır ve birleştirici işaretler atılır, küçük harfe çevrilir.
   - Terimler zaten ASCII ve küçük harf.
4. **Tire:** Etiket olduğu gibi aranır, tireler atılmaz. Böylece "kripto-para" eşleşir, ama tireyle bölünmüş bir terim ("kri-pto") eşleşmez.
5. **En kısa terim: 5 karakter.** 3–4 karakterli terimler (bank, fund, loan, btc, etf, cfd, fx, kur) alınmadı: Alt dize olarak çok sayıda ilgisiz ada takılır. Bu, kısa terimle yazılmış alan adlarını kaçırır.
6. **Hariç tutma:** `finans_sozlugu.txt` içinde `!` ile başlayan dizeler eşleştirmeden önce etiketten silinir. Sonra terimler aranır. Etiket hem hariç dize hem gerçek terim içeriyorsa gerçek terim yine eşleşir.
7. **Sayı ve görsel benzerlik:** Rakamlı yazım (leet) ve benzer görünen farklı alfabe harfleri (örneğin Kiril) işlenmez.
8. **Çıktı:** Hangi terimin eşleştiği kaydedilir (baseline alt kümesi mi, yalnızca geniş mi ayrımı için, Bölüm 5).

## 5. Baseline ile ilişki ve döngüsellik

- Baseline'ın 8 terimi (`yatirim`, `borsa`, `kripto`, `forex`, `trade`, `invest`, `capital`, `menkul`) bu sözlüğün **alt kümesidir**. Kaynak: `CandidateFilterOptions.DefaultTerms` (`KeywordCandidateFilter.cs`), [yerel].
- Fark: Baseline ve CT akışı terimi **tüm DNS adında** arar (TLD ve alt alan adı dahil). Bu sözlük yalnızca kayıtlı alan adının etiketine bakar. Bu fark bilerek konuldu, eşleşme ayrıntısı farklıdır. Raporda "baseline terimiyle eşleşme" iki tanıma göre ayrı verilirse not edilir.
- **Döngüselliğin önlenmesi (Bölüm 4.3):**
  - G3, tarafsız örneklem akışından (Çerçeve N) bu sözlükle süzülür. Baseline terimi içeren alt küme ile yalnızca geniş sözlükte olan alt küme ayrı raporlanır. Baseline için adil sınav ikinci alt kümedir.
  - Sözlük baseline sonuçlarından önce dondurulur. Böylece sözlük, baseline'ın yanlış alarmına veya kaçırdığına göre ayarlanamaz.
  - Baseline'ın 8 terimi de dondurulur. Biri değişirse kilitli test geçersiz sayılır (Bölüm 4.3, madde 3).
- Dürüst not: Yazar baseline'ın 8 terimini biliyordu. Sözlük bundan etkilenmiş olabilir, alt küme olması zaten bunu gösterir. Etkisi, baseline sonuçlarından değil yalnızca terim listesinden gelir.

## 6. Bilinen sınırlar

- Terim seçimi öznel, kaynak taraması yok (Bölüm 2).
- Alt dize eşleşmesi yanlış eşleşme üretir. Özellikle `exchange`, `capital`, `trade`, `credit`, `banka`, `funding`, `treasury`. Yanlış eşleşme oranı ölçülmedi. Hariç tutma listesi küçük ve elle yazıldı, ölçüme dayanmıyor.
- Yalnızca Türkçe ve İngilizce. Diğer diller, rakamlı yazım ve Kiril benzeşimi yok.
- Kısa terimler (5 karakterden kısa) yok.
- Sözlük pozitiflerin kullandığı sözcükleri kapsamayabilir. Pozitifler bu sözlük için kontrol edilmedi ve kapsama oranı bilinmiyor.
- Sözlükte olmak "finans sitesi" ya da "izinsiz" demek değildir. Sistem "yalan" veya "dolandırıcı" demez, "resmi kayıtla uyuşmuyor" der.

## 7. Dondurma bilgisi

| Alan | Değer |
|---|---|
| Dondurma tarihi | 2026-11-15 (dosyanın yazıldığı gün). Önceden kayıt sayılması commit tarihine bağlıdır (Bölüm 10.1, karar 10) |
| Commit | Bu belgeyi ve terim dosyasını ekleyen commit (Değişiklik kaydı ve `git log -- docs/research/finans_sozlugu.txt`) |
| Terim dosyası | `docs/research/finans_sozlugu.txt`, 55 terim, 6 hariç dize |
| SHA-256 | `65b9dcc4ac358d872e1579e6edbaef6f80a4a8c58e0e70109ade9c5876832f05` (LF satır sonlarıyla; `.gitattributes` bu dosyayı her platformda LF tutar) |

Dondurulduktan sonra terim eklenmez veya çıkarılmaz. Değişiklik gerekirse yalnızca yeni sürüm (v2) açılır, eskisi silinmez, kilitli test geçersiz sayılır.

## Değişiklik kaydı

- 2026-11-15: v1 yazıldı ve donduruldu. SHA-256 `65b9dcc4ac358d872e1579e6edbaef6f80a4a8c58e0e70109ade9c5876832f05`. Pozitiflere, kontrol verisine ve baseline sonuçlarına bakılmadan yazıldı.
