# Fraud.Evaluation (Hafta 6 ölçüm altyapısı)

Önce sınav, sonra model. Bu proje gerçek sinyal içermez (yalnızca sahte `random`); Hafta 7 sinyalleri `Fraud.Signals` içindedir. Burada sinyallerin nasıl ölçüleceği, toplam sinyali (`SumSignal`) ve ablasyon (`Running/Ablation.cs`) durur. Yalnızca `Fraud.Domain`'e bağlıdır. Veritabanından yükleme ve komut satırı `Fraud.Collectors` içindedir (`Evaluation/`).

Bu bir yatırım tavsiyesi değildir. "Pozitif", "SPK erişim engeli listesinde" demektir, "dolandırıcı" demek değildir.

## Hat

1. **Referans zamanı** (`Splitting/ReferenceTimeAssigner.cs`)
   - Pozitif: karar tarihi, Türkiye saatiyle (TR) gün başı.
   - Kontrol: Önce ilk sertifikaya eşleşmiş gecikme eklenir. Referans, bu andan sonraki ilk TR gün başıdır. Gecikme yalnızca ayar pozitiflerinin "ilk sertifika → karar" gecikmelerinden tohumlu olarak çekilir.
   - Referansı "şimdi"den sonraya düşen kontrol ölçümden çıkar ve sayılır.
2. **Bölme** (`Splitting/Splitter.cs`): KONTROL_GRUBU_YONTEMI.md Bölüm 7 kuralları uygulanır.
   - Aynı bülten ya da küme iki tarafa düşerse bileşenin tamamı ayara taşınır.
   - Ayardaki bir birimle aynı bileşende olan hiçbir birim, dışarıda kalmış olsa bile, ileriye dönük teste girmez.
   - İlk sertifikası bilinmeyen birim iki tarafta da dışarıda kalır.
   - F'den sonraki kontrol yalnızca kilitli pencereye girebilir.
3. **Eşik** (`Running/Predictor.cs`, `ThresholdSelector`)
   - Yalnızca ayar bölmesindeki G3 ve G4 kontrollerinden seçilir.
   - Hedef yanlış alarm oranı %1'dir (onaylandı, DECISIONS 2026-11-16).
4. **Test tahmini**
   - Sinyal yalnızca `AsOfView` görür: `KnownAt <= AsOf < referans`.
   - `AsOf` her zaman bir TR gününün son anıdır (`Model/DayGrid.cs`), yani saat etiket taşımaz.
   - Sinyal, ilk gözlemden referanstan önceki son güne kadar her gün değerlendirilir.
   - Puan, referansa kadarki en yüksek puandır. İlk alarm, eşiğin ilk geçildiği gündür.
5. **Metrikler** (`Metrics/`)
   - Recall, kontrol tabakası başına yanlış alarm oranı ve precision.
   - Taban orana göre düzeltilmiş precision.
   - Günlük alarm.
   - Erken uyarı.
   - Küme ve bülten düzeyinde bootstrap güven aralığı.
   - Veri kapsaması uyarısı (`CoverageCheck`): Bir grupta "kaydı var mı" oranı pozitiflerden %20'den fazla farklıysa rapor uyarı verir.

## Formüller

- Recall = yakalanan pozitif / test pozitifi.
- Yanlış alarm oranı f = alarm veren kontrol / tabakadaki kontrol. "Yanlış", yalnızca "SPK listesinde değil" demektir.
- Düzeltilmiş precision: `PPV = r·π / (r·π + f·(1 − π))`.
  - π varsayılan olarak 0,001'dir. Bu değer ölçülmedi, bir varsayımdır. Kural dondurulmadan önce bir kez gerçek ölçümle güncellenecek.
  - f = 0 ise PPV yazılmaz.
- Günlük yanlış alarm (yalnızca Çerçeve N) = f × günlük yeni alan adı sayısı. `DailyNewDomains` ölçülmeden bu değer yazılmaz.
- Erken uyarı = karar tarihi − ilk alarm (gün). Ortanca, en az ve en çok değer yalnızca 10 ve daha fazla yakalanan pozitifte verilir.
- 10'dan az birimde oran, aralık ve precision verilmez, yalnızca ham sayı yazılır. 5'ten küçük her sayı `<5` olarak basılır.

## "Ne zaman bilinir" kuralı (`Model/Observation.cs`, `KnownAtPolicy`)

| Kayıt | Geçmiş ve kilitli test | İleriye dönük test | Not |
|---|---|---|---|
| crt.sh / CT sertifikası | `not_before` | `observed_at` | Varsayım: CT logları sertifikayı neredeyse anında yayımlar |
| Wayback | `captured_at + 7 gün` | `observed_at` | Dizine geç girme payı, yapılandırılabilir |
| DNS, RIPEstat, InternetDB | `queried_at` | `queried_at` | Geçmişi yok. Geçmiş veri testinde hiç görünmez |

## Yapılandırma

Örnek dosya: `evaluation.example.json`.

- Bilinmeyen alan reddedilir.
- Sayı olarak yazılmış enum reddedilir.
- Dosya en fazla 64 KB olabilir.
- Bootstrap tekrar sayısı en fazla 20.000, pencere en fazla 365 gün olabilir.
- Kilit dosyasının yolu yapılandırmada **yoktur**: Kodda sabittir ve git'in ortak klasöründedir (`<git-common-dir>/fraud-lab/locked-run.json`). Eski sürümün `data/private/evaluation/locked-run.json` kaydı varsa önce yeni konuma taşınır. Böylece `--allow-rerun` ile bile ikinci çalıştırma "ilk" görünmez.
- İleriye dönük test için `timestampAuthorityRootFile` alanı zorunludur. Bu alan, TSA kök sertifikasının yoludur (DER ya da PEM). Kök ayrıca sabitlenmiş listede olmalıdır (aşağıda).
- git, PATH'teki tam yollardan bulunur. Göreli girdiler ve çalışma klasörü atlanır. İstenirse `Git:Path` ayarı ya da `Git__Path` ortam değişkeniyle tam yol olarak verilebilir.

## Kilitli test

```
git status                      # temiz olmalı
git rev-parse HEAD              # dondurulmuş commit
dotnet run --project src/Fraud.Collectors -- eval-run --mode Locked --config <depoda izlenen yapılandırma> --commit <40 karakter>
```

Komut ana çalışma ağacından çağrılır ve kendisi şunları yapar (E11):

1. Ana ağaçta aşağıdaki 1–4 numaralı denetimleri yapar.
2. `git -c core.fsmonitor=false worktree add --detach <geçici klasör> HEAD` ile temiz, geçici bir worktree açar.
3. Orada `dotnet build src/Fraud.Collectors -c Release` çalıştırır.
4. Derlenen programla aynı komutu geçici klasörde "iç kip" olarak çalıştırır. Kilidi yalnızca iç kip alır.
5. İş bitince geçici worktree'yi siler.

Neden: Ana ağaçta "temiz ağaç + ikilinin commit'i" çalışan kodu kanıtlamıyordu:

- git'in yok saydığı bir klasördeki (ör. `src/.../out/`) `.cs` dosyası derlenir.
- İkilideki `+sha` değeri derleme sırasında elle verilebilir.

Temiz worktree'de yalnızca HEAD'deki izlenen dosyalar vardır. Bu yüzden ikilideki `+sha` değeri yalnızca orada güvenilir sayılır. Geçici worktree'nin içinden derlenmemiş bir program kilit almaz.

Ayrı bir komut yerine `eval-run --mode Locked` seçildi. Böylece hatırlanacak tek komut var ve kirli ağaçtan kilitli test yolu hiç yok. İleriye dönük kip de aynı yoldan geçer (E13).

Derleme `-nodeReuse:false --disable-build-servers` ile yapılır. Böylece derleme sunucusu süreçleri geçici klasörü açık tutmaz. Klasör yine de silinemezse komut hata vermez; yalnızca uyarı yazar. Bu durumda klasör elle silinir ve `git worktree prune` çalıştırılır.

Her iki aşamada da depo kökü `git rev-parse --show-toplevel` çıktısıyla aynı olmalıdır. Bütün git çağrıları `-c core.fsmonitor=false` ile yapılır. Denetimler:

1. Yapılandırmanın tamamı doğrulanmış, `freezeDate` dahil. Böylece bir yapılandırma hatası tek çalıştırma hakkını harcamaz.
2. `--commit` 40 karakterlik bir git kimliği ve `git rev-parse HEAD` ile aynı.
3. Çalışma ağacı temiz:
   - `git status --porcelain` boş.
   - `git ls-files -v` içinde `h` (değişmedi say) ya da `S` (atla) işaretli dosya yok.
   - `git status --ignored --porcelain` çıktısında `src/` ya da `tests/` altında yok sayılan dosya yok (`bin/` ve `obj/` hariç).
4. Yapılandırma dosyası depoda izleniyor. Bu yüzden özeti, dondurulmuş commit'teki dosyanın özetidir.
5. (Yalnızca iç kip) Program geçici worktree'nin içinden derlenmiş ve derlendiği commit (`AssemblyInformationalVersion` içindeki `+sha`) HEAD ile aynı.

Kilit dosyası git ortak klasöründe, `CreateNew` ile ve veri yüklenmeden önce yazılır. Yarıda kesilen çalıştırma da "kullanıldı" sayılır. İkinci çalıştırma yalnızca `--allow-rerun true --reason "..."` ile olur; bu durumda rapor "YENİDEN ÇALIŞTIRMA" diye başlar.

**Zorunlu dış çapa:** Kilit dosyası yereldir. Depo başka bir klasöre klonlanırsa kilit görünmez. Bu yüzden komut, kilidi alır almaz şunları yapar:

- Kilit kaydının SHA-256 özetini ekrana basar.
- Özeti `docs/LOCKED_RUNS.txt` dosyasına ekleyip commit etmek için gereken komutu yazar.
- Kilidin yanına bir `.tsq` (RFC 3161 isteği) dosyası yazar ve onu damgalatmak için gereken `curl` komutunu yazar.

Bu iki komut elle ve hemen çalıştırılır. Yapılmazsa kilitli testin bir kez çalıştığı dışarıdan kanıtlanamaz.

## İleriye dönük test

```
dotnet run --project src/Fraud.Collectors -- eval-run --mode Prospective --config <depoda izlenen dosya> --commit <40 karakter> --commitment <açık taahhüt> --opening <açıklama dosyası>
```

- Alarm kümesi yeniden **hesaplanmaz**. Taahhüt edilmiş listeden okunur.
- Açıklama dosyasındaki her kaydın taahhüdü tuzundan yeniden hesaplanır ve kök, açık dosyadaki kökle karşılaştırılır. Tutmazsa test çalışmaz.
- Test kümesi: Referansı taahhüt anından (C) sonra olan, ayarda olmayan ve ayardaki hiçbir birime bağlı olmayan birimler.
- Bir birim yalnızca taahhüt edilmiş listede varsa "alarm verdi" sayılır.
- C, açık dosyadaki `CreatedAt` alanı **değildir**; o alan bir beyandır. C, RFC 3161 cevabındaki `genTime` değeridir. Cevap varsayılan olarak `<açık dosya>.tsr`, istek `<açık dosya>.tsq` dosyasındadır; `--tsr` ve `--tsq` ile başka yol verilebilir. Çevrimdışı yapılan denetimler:
  - İstekteki özet, açık dosyanın SHA-256'sı olmalı.
  - Cevap bu isteğe ait olmalı (nonce).
  - TSA imzası geçerli olmalı.
  - TSA sertifikası, yapılandırmadaki köke zincirlenmeli.
  - Kökün SHA-256 parmak izi, git'te izlenen `src/Fraud.Collectors/Evaluation/tsa_roots.txt` dosyasında olmalı (E12). Dosya derlemeye gömülüdür.

  Bunlardan biri tutmazsa sonuç basılmaz. Kullanılan kökün parmak izi çıktıya yazılır.
- **Sabitlenmiş kök listesi şu an boş.** Bu çalışmada ağa erişim yoktu, gerçek bir TSA kökü indirilmedi. Liste boşken ileriye dönük test çalışmaz. Kök ekleme adımı dosyanın başında yazar. Kök, taahhütten **önce** commit edilmelidir.
- **İleriye dönük kip de temiz derlemeyle çalışır (E13):** Kilitli testteki gibi geçici worktree, HEAD eşleşmesi, temiz ağaç ve ikilinin `+sha` kontrolü uygulanır. Neden: Kabul edilen kökler, çalışan ikiliye gömülü listedir. Commit edilmemiş bir `tsa_roots.txt` ile derlenmiş program kendi kökünü kabul ederdi.
- **Kökün sabitlendiği tarih:** Kökün parmak izi, damga zamanından **önce** commit edilmiş olmalı. Tarih şu komutla bulunur: `git log --format="%H %cI" -S<parmak_izi> -- src/Fraud.Collectors/Evaluation/tsa_roots.txt`. En eski commit, kökün eklendiği commit sayılır. Commit bulunamazsa ya da tarihi damgadan sonraysa sonuç basılmaz. Commit ve tarihi çıktıya yazılır.
- **Sınır:** Commit tarihi, commit'i yapanın beyanıdır (`GIT_COMMITTER_DATE` ile değiştirilebilir). Bu denetim "kök sonuç görüldükten sonra eklendi" hatasını yakalar. Kasıtlı geriye tarihlemeye karşı ise commit'in zamanında uzak depoya itilmesi ya da damgalanması gerekir.
- **Bilinen sınır:** İptal (CRL / OCSP) denetimi yapılmaz, çünkü çevrimdışı çalışıyoruz. İptal edilmiş bir TSA sertifikasının damgası bu yüzden kabul edilebilir. Damga, sertifikanın `genTime` anındaki geçerliliğine göre değerlendirilir.
- İlk sertifikası bilinmeyen birim, pozitif de kontrol de olsa, bu teste girmez.

## Taahhüt ve zaman damgası

```
dotnet run --project src/Fraud.Collectors -- commit-create --records data/private/control/liste.txt --name kontrol-2026-12 --public-out docs/commitments/kontrol-2026-12.json [--previous <önceki açık dosya>]
dotnet run --project src/Fraud.Collectors -- commit-prove --opening data/private/commitments/kontrol-2026-12.opening.json --index 3
dotnet run --project src/Fraud.Collectors -- commit-verify --public docs/commitments/kontrol-2026-12.json --proof data/private/commitments/kontrol-2026-12.proof-3.json [--record <ad>] [--previous <önceki açık dosya>]
```

- **Kayıt biçimi:** Kayıtlar taahhütten önce kayıtlı alan adına çevrilir (küçük harf, punycode, `www.` ve alt alan adı atılmış). `commit-verify --record` aynı çevirmeyi uygular.
- **Açık dosya:** Kök, sayı, zaman, önceki dosyanın özeti ve sürüm yazar. Alan adı içermez. `--previous` ile verilen dosya geçerli bir açık taahhüt olmalıdır. `commit-verify --previous` zincir bağını denetler.
- **Gizli açıklama dosyası:** Her kaydın tuzunu içerir ve depo kökündeki `data/private/commitments/` altında kalır.
  - Aynı adla ikinci taahhüt reddedilir.
  - Dosya önce geçici bir dosyaya `CreateNew` ile yazılır, sonra yeniden adlandırılır.
- **`.tsq` dosyası:** `commit-create`, açık dosyanın yanına bir `.tsq` dosyası da yazar. Bu dosya, açık dosyanın SHA-256'sı için RFC 3161 isteğidir.

Program hiçbir dış servise bağlanmaz. Zaman damgası **elle** alınır:

1. RFC 3161 (ör. FreeTSA):
   ```
   curl -sS -H "Content-Type: application/timestamp-query" --data-binary @kontrol-2026-12.json.tsq https://freetsa.org/tsr -o kontrol-2026-12.json.tsr
   openssl ts -verify -in kontrol-2026-12.json.tsr -queryfile kontrol-2026-12.json.tsq -CAfile cacert.pem -untrusted tsa.crt
   ```
   `cacert.pem` ve `tsa.crt` servisin kendi sayfasından indirilir.
2. OpenTimestamps (isteğe bağlı): `ots stamp kontrol-2026-12.json`. Birkaç saat sonra `ots upgrade kontrol-2026-12.json.ots` çalıştırılır. `.ots` dosyasını OpenTimestamps istemcisi üretir. Bu proje `.ots` üretmez, çünkü takvim sunucularına bağlanmak gerekir.

`.tsr` ve `.ots` dosyaları açık dosyanın yanında saklanır. Bu dosyalar alan adı içermez.
