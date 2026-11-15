# Canlı Static CT akışı: Linux kurulumu

Bu klasör, `ct-follow` komutunu (Static CT logunu canlı izleyen süreç) ve günlük ağ kaydını (`network`) bir Linux
makinede 7/24 çalıştırmak içindir. Makinede üç veri servisi sürekli çalışır: PostgreSQL (`db`), izleyici (`ct-follow`)
ve ağ kaydı (`network`). Yanında üç izleme servisi çalışır: Prometheus, Grafana ve kendi makinemizin port denetimi
(`port-audit`); bkz. "İzleme ve uyarılar". API bu makinede çalışmaz.

Bu bir araştırma aracıdır, yatırım tavsiyesi değildir. Toplanan aday listesi SPK veya USOM listesinde olmayan alan adları
içerir; bu yüzden veritabanı ve yedekler gizlidir (bkz. "Güvenlik notları").

## Ne yapar, ne yapmaz

- Sycamore Static CT logunun checkpoint ve tile dosyalarını okur, imzayı ve Merkle kökünü doğrular.
- Ham tile diske yazılmaz. Yalnızca finans kelimesi geçen aday satırları ve sayaçlar saklanır.
- Şüpheli sitelere hiçbir istek atmaz. Dış bağlantılar: CT logunun izleme adresi, iki herkese açık DNS çözücüsü
  (Cloudflare 1.1.1.1 ve `cloudflare-dns.com`, Google 8.8.8.8 ve `dns.google`), `stat.ripe.net` ve
  `internetdb.shodan.io`. Adresler koda gömülü izin listesindedir; ayarla değiştirilemez.
- PostgreSQL portu dışarı açılmaz; veritabanı yalnızca Docker'ın iç ağında durur.

## Gereksinimler

| Konu | Öneri |
|---|---|
| İşletim sistemi | Docker Engine çalışan güncel bir Linux (Debian, Ubuntu, Fedora vb.) |
| Docker | Docker Engine ve `docker compose` eklentisi. `install.sh` bunları kurmaz, yoksa söyler. |
| Disk | En az 20 GB boş (`install.sh` altında uyarır). Ham tile diske yazılmaz; adaylar günde onlarca MB. Uzun süre çalışacaksa ve 7 yedek tutulacaksa 50 GB ve üstü rahat eder. |
| Bant genişliği | **Günde ~15-16 GB, ayda ~450-500 GB indirme. Kotalı bağlantıda uygun değil.** (Varsayılan: yalnızca 2027h1 dilimi.) Ortalama hız ~1,5 Mbit/s; kesinti sonrası yetişirken log başına en fazla ~2 tile/sn, yani ~3 Mbit/s. |
| İşlemci | 2027h1 saniyede ~265 kayıt üretiyor; her kaydın sertifikası okunur. 2 çekirdek önerilir. |
| Bellek | 2 GB RAM yeterli. Servis sınırları: `db` 1 GB / 1 CPU, `ct-follow` 768 MB / 1 CPU. |
| Sürekli açık kalma | Makine uykuya geçmemeli. Kapalı kalınan süre kaybolmaz ama geride kalan kayıt birikir. Log, eski tile'ları sınırsız tutmayabilir; uzun kesinti atlanan kayıt olarak kapsama oranına yansır. |
| Saat | Saat senkronu açık olmalı: `sudo timedatectl set-ntp true`. Kontrol: `timedatectl` çıktısında `System clock synchronized: yes`. |

### Hacim ölçümü ve sınırı

Rakamlar tek bir 15 dakikalık ölçümden çıkarıldı; gün içinde ve haftalar içinde değişebilir:

| Dilim | Kayıt / gün | Tile / gün | İndirme / gün |
|---|---|---|---|
| Sycamore 2027h1 (varsayılan açık) | ~22,9 milyon | ~89 bin | ~15-16 GB (tile başına ~175 KB, gzip) |
| Sycamore 2026h2 (varsayılan kapalı) | ~1,5 milyon | ~6 bin | ~1 GB |

Yeni sitelerin 90 günlük sertifikaları 2027h1'e düştüğü için varsayılan olarak yalnızca o izlenir. 2026h2 dilimi
17 Aralık 2026'da kapanıyor. Gerçek indirme miktarını ilk günden sonra `./status.sh` ile kontrol edin
(`tahmini_indirme_gb` ve konteynerin ağ sayacı).

Hız payı dar: Varsayılan ayar log başına en fazla ~2 tile/sn indirir, 2027h1 ~1 tile/sn üretir. Bir günlük kesintiyi
kapatmak yaklaşık bir gün sürer. `status.sh` içinde `geride_kalan` sürekli artıyorsa `.env` içine
`CT_MIN_REQUEST_INTERVAL=00:00:00.300` gibi daha kısa bir değer yazılabilir (en az 100 ms); log sunucusunu zorlamamak
için gereğinden kısa yapmayın.

## Kurulum

```bash
# 1) Depoyu al (SSH anahtarınla). Klasör adı serbest.
git clone <depo-adresi> fraud-lab
cd fraud-lab/deploy/linux

# 2) Betikleri çalıştırılabilir yap (git izinleri korumadıysa)
chmod +x install.sh backup.sh status.sh

# 3) Kur ve başlat
./install.sh
```

`install.sh` şunları yapar:

1. `docker` ve `docker compose` var mı, Docker servisine erişiliyor mu bakar. Yoksa ne yapacağını yazar ve durur.
2. Saat senkronunu ve boş diski kontrol eder (yalnızca uyarır).
3. `.env` yoksa rastgele bir veritabanı parolasıyla oluşturur ve iznini `600` yapar. Varsa dokunmaz.
4. İmajı derler, servisleri başlatır. Önce `db` sağlıklı olur, sonra `migrate` şemayı bir kez günceller ve çıkar,
   en son `ct-follow` başlar.

Tekrar çalıştırmak güvenlidir. Değişiklik yoksa hiçbir servisi yeniden başlatmaz.

İlk `migrate` logunda `Failed executing DbCommand ... __EFMigrationsHistory` satırı görülebilir. Bu, boş veritabanında
geçmiş tablosu henüz yokken EF Core'un yazdığı bilinen bir satırdır; ardından `Migration tamam.` gelmeli ve servis 0 ile
çıkmalıdır.

### Ayarlar (`.env`)

| Değişken | Anlamı |
|---|---|
| `POSTGRES_PASSWORD` | Veritabanı parolası. Rastgele üretilir; değiştirmeyin (değiştirirseniz mevcut veritabanı eski parolayla kalır). |
| `MEASUREMENT_LOCATION` | `collection_log` içindeki ölçüm konumu etiketi. Kişisel bilgi yazmayın; ör. `lab-linux`. |
| `CT_ENABLE_2027H1` | `true` (varsayılan): 2027h1 dilimi izlenir. Günde ~15-16 GB. |
| `CT_ENABLE_2026H2` | `false` (varsayılan). `true` ise 2026h2 dilimi de izlenir (günde ~1 GB daha; 17 Aralık 2026'da kapanıyor). |
| `DNS_LOCATION` | İsteğe bağlı. Ağ kaydının ölçüm konumu. Varsayılan `tr-ev-ubuntu`. |
| `CT_MIN_REQUEST_INTERVAL` | İsteğe bağlı. Log başına iki isteğin başlangıcı arasındaki en kısa süre. Varsayılan `00:00:00.500`. |
| `GRAFANA_PORT` | Grafana'nın `127.0.0.1` portu. Varsayılan `53000`. Değişirse `port-audit/expected` da değişmeli. |
| `COMPOSE_FILE` | Bu klasörde `-f` yazmadan `docker compose` iki dosyayı (veri + izleme) birlikte okusun diye. |

Bu ayarlar yalnızca bu Linux kurulumunu etkiler; `src/Fraud.Collectors/appsettings.json` içindeki geliştirme
varsayılanı değişmez.

Ayar değişince: `docker compose -f docker-compose.ct.yml up -d` (yalnızca değişen servis yeniden başlar).

## Tarafsız CT örneklemi (`ct_neutral_sample`)

`ct-follow`, kelime filtresinden bağımsız ikinci bir kural uygular: Her kayıttaki kayıtlı alan adı için
`SHA-256(tuz || alan_adı)` değerinin ilk 8 baytı `oran × 2^64` değerinden küçükse alan adı saklanır (varsayılan oran
0,01). Kural kelimeye bakmaz; kontrol grubunun "Çerçeve N"i buradan seçilir
([KONTROL_GRUBU_YONTEMI.md](../../docs/research/KONTROL_GRUBU_YONTEMI.md) 4.5, karar 3).

- **Tuz:** `install.sh` ilk çalıştırmada `.env` içine `CT_NEUTRAL_SALT` (32 bayt, onaltılık) yazar. Var olan tuza
  dokunmaz ve hiçbir zaman ekrana basmaz. Tuzu değiştirmeyin: Değişirse örnek baştan başlar. Tuz yoksa compose ve
  `ct-follow` başlamaz (örnek sessizce kapalı kalmasın diye).
- **Eski kurulumu güncelleme:** `git pull` sonra `./install.sh`. Betik eksik tuzu ekler, `migrate` yeni tabloyu kurar.
- **Oran:** `.env` içinde `CT_NEUTRAL_RATE` (yoksa 0,01). Karar 3'e göre yalnızca dondurmadan önce bir kez değişir.
- **Gizlilik (Kural 4):** Tablo listede olmayan adlar içerir. `status.sh`, Grafana (`fraud_ct_neutral_sample_total`)
  ve loglar yalnızca sayı gösterir.

## Günlük ağ kaydı (`network` servisi)

`network` servisi `worker` komutunu şu sırayla çalıştırır, sonra 24 saat bekler:

1. **DNS:** `official_actions` içindeki her alan adı, iki çözücüye üç yoldan (UDP, TCP, DoH) A, AAAA ve NS
   türünde sorulur. Alan adı başına 18 sorgu, bütün yollar için ortak sınır saniyede en fazla 1 paket.
   ~175 alan adı için tur yaklaşık 1 saat sürer. Kesik (TC) UDP cevabı TCP ile yeniden sorulur.
   Cevapta bilinen engel sayfası IP'si varsa (`195.175.254.2`) kayıt "engel işareti" alır.
2. **RIPEstat:** Yalnızca DoH ile alınmış, engel işareti olmayan A/AAAA cevaplarındaki IP'ler sorulur; alan adı
   gönderilmez. Saniyede en fazla 1 istek, tek bağlantı, `sourceapp=fraudlab-research`. Bir IP 7 günde bir sorulur.
   ASN bilinen bir CDN veya bulutsa (`cloudflare`, `akamai`, `fastly`, `aws`, `google`, `microsoft`) işaretlenir.
3. **InternetDB:** Aynı IP'ler, haftada bir (IP başına 7 gün). Yalnızca port, etiket ve CPE saklanır;
   `vulns` ve `hostnames` hiç saklanmaz.

Sonuçlar `dns_snapshots`, `ip_asn_snapshots` ve `port_snapshots` tablolarına yazılır. Bu tablolar `data/private/` ile
aynı gizliliktedir: yedekler dahil depoya ve rapora girmez, yalnızca toplu sayı yayımlanır.

Ölçüm konumu `tr-ev-ubuntu` (karar: `docs/DECISIONS.md`, 2026-11-13). Başka bir makinede çalıştırırsanız `.env`
içine `DNS_LOCATION=...` yazın (küçük harf, rakam, tire; kişisel bilgi yok). Konumu boş bir DNS kaydı yazılmaz:
`Dns:Location` yoksa servis hiç sorgu atmadan durur.

Karşılaştırma (yalnızca sayı basar, alan adı ve IP basmaz; sorgu atmaz, servis çalışırken de güvenlidir):

```bash
docker compose -f docker-compose.ct.yml run --rm network dns-compare            # son ölçüm günü
docker compose -f docker-compose.ct.yml run --rm network dns-compare --day 2026-11-14
```

Elle toplama turu: Her ağ kaynağı (DNS, RIPEstat, InternetDB) PostgreSQL kilidiyle korunur; aynı kaynağı aynı anda
tek süreç toplar. `network` servisi o kaynağı topluyorsa elle başlatılan komut hiç sorgu atmadan çıkar (çıkış kodu 75,
"başka bir süreç tarafından toplanıyor"). Böylece çözücülere saniyede 1 paketten fazlası gitmez. Elle tur için önce
servisi durdurun:

```bash
docker compose -f docker-compose.ct.yml stop network
docker compose -f docker-compose.ct.yml run --rm network collect-dns
docker compose -f docker-compose.ct.yml run --rm network collect-ripestat
docker compose -f docker-compose.ct.yml run --rm network collect-internetdb
docker compose -f docker-compose.ct.yml start network   # açılışta yeni tur başlar; önbellek tekrar sorguyu önler
```

`dns-compare`, her çözücü için UDP (ve TCP) cevabının DoH cevabıyla kaç alan adında farklı olduğunu, yol başına
NXDOMAIN oranını, engel işareti sayısını ve TC sonrası TCP'ye geçiş sayısını yazar. "Farklı" araya girme kanıtı
değildir; yük dağıtımı yapan siteler her sorguda farklı IP döndürebilir.

Bilinen sınırlar: Ölçüm tek konumdan ve tek servis sağlayıcıdan yapılır. Çözücüler sorgulanan alan adlarını görür.
Kayıtlar bugünden itibaren tutulur; karar tarihindeki DNS durumunu göstermez.

## İzleme ve uyarılar (Prometheus, Grafana, port denetimi)

`install.sh` iki compose dosyasını birlikte çalıştırır:

| Dosya | Servisler | Kural |
|---|---|---|
| `docker-compose.ct.yml` | `db`, `migrate`, `ct-follow`, `network` | Hiç port yayımlanmaz, host ağı yok. |
| `docker-compose.monitoring.yml` | `prometheus`, `grafana`, `port-audit`, `port-audit-exporter` | İki bilinçli istisna: Grafana `127.0.0.1:53000`, port denetimi host ağında (port dinlemez). |

`.env` içindeki `COMPOSE_FILE` satırı sayesinde bu klasörde `-f` yazmadan `docker compose ps`, `docker compose logs grafana`
gibi komutlar iki dosyayı birlikte okur. Aşağıdaki komutlar bu klasörde (`deploy/linux`) çalıştırılır.

Ne nereden okunur:

- `ct-follow` ve `network` metriklerini kendi konteynerlerinde `9464` portundan verir. Port host'a yayımlanmaz;
  yalnızca dışarı çıkışı olmayan `metrics` iç ağındaki Prometheus okur. Veritabanı bu ağda değildir.
- `port-audit` host ağındadır ama **hiçbir port dinlemez** (THREAT_MODEL M1). Sonucunu `port_audit_state` Docker
  birimindeki bir dosyaya yazar; `port-audit-exporter` (iç ağda, host ağında değil) dosyayı salt okur ve `9464`'ten sunar.
  Neden: Host ağındaki bir dinleyici, köprü adresinde bile olsa, Linux'un zayıf host modeli yüzünden başka Docker
  ağlarından ve bazı kurulumlarda LAN'dan erişilebilirdi. Güvenlik duvarı kuralı WSL yeniden başlayınca silinir ve
  `sudo` ister; dosya yolunun ikisine de ihtiyacı yok.
- Prometheus 15 saniyede bir okur, 30 gün (en fazla 2 GB) saklar. Hiçbir port yayımlamaz.
- Uyarılar dışarı gönderilmez: e-posta, webhook, Alertmanager yok. Ateşlenen uyarılar Grafana panelinde, `./status.sh`
  "Alarmlar" bölümünde ve (kurulursa) Windows bildiriminde görünür; bkz. "Bildirim".

Metrik etiketlerinde yalnızca sabit değerler vardır: kaynak adı, log kısa adı, çözücü kısa adı (cloudflare, google),
yol (Udp, Tcp, Doh), sonuç, hata türü, kendi makinemizin port numarası. Alan adı, IP, URL ve sertifika alanı hiçbir
etikete girmez; uygulama bunu izin listesiyle zorlar (`src/Fraud.Infrastructure/Observability/MetricLabels.cs`) ve bir test
`/metrics` çıktısını okuyup denetler.

### Grafana

Tarayıcıda (WSL'deki Ubuntu'da veya Windows'ta): `http://localhost:53000`. Kullanıcı `admin`.

Grafana yalnızca `127.0.0.1`'de dinler. WSL, Linux'taki localhost portlarını Windows'un localhost'una iletir; LAN'daki
başka bir bilgisayar erişemez. Anonim erişim ve kayıt kapalıdır. Grafana dışarıya istek atmaz (güncelleme denetimi,
eklenti indirme, kullanım raporu, Gravatar kapalı).

Parola `install.sh` tarafından rastgele üretilir ve `secrets/grafana_admin_password` dosyasında durur. Grafana'ya ortam
değişkeniyle değil dosyayla verilir (`GF_SECURITY_ADMIN_PASSWORD__FILE`, salt okunur bağlı); `docker inspect`'te görünmez.
`secrets/` klasörü `700` (bu makinedeki başka kullanıcılar okuyamaz), dosya `644` (konteynerdeki grafana kullanıcısı
okuyabilsin diye). Klasör depoya girmez. Hiçbir betik parolayı ekrana basmaz. Ekrana basmadan panoya kopyalamak için
(Windows'ta WSL içinden):

```bash
tr -d '\n' < secrets/grafana_admin_password | clip.exe
```

Parola yalnızca Grafana'nın ilk açılışında uygulanır. Sonradan değiştirmek için (yeni parola kabuk geçmişine ve komut
satırına girmez; dosyadan standart girdiyle verilir):

```bash
( umask 022; od -An -tx1 -N32 /dev/urandom | tr -d ' \n' > secrets/grafana_admin_password )
docker compose exec -T grafana grafana cli admin reset-admin-password --password-from-stdin < secrets/grafana_admin_password
```

Panel "fraud-lab izleme" açılışta gelir: uyarılar, CT gecikmesi ve hızı, günlük aday sayısı, DNS sonuçları (yol ve
sınıf), gecikmeler, toplama turları, tablo tazeliği, port denetimi, disk ve süreçler. Panel depodaki JSON'dan yüklenir
(`monitoring/grafana/dashboards/fraud-lab.json`); arayüzden yapılan değişiklik kaydedilmez.

### Uyarılar ve eşikler

Kurallar: `monitoring/prometheus/rules/fraud-lab.yml`. Birim testleri: `monitoring/prometheus/tests/fraud-lab.test.yml`
(CI'da `promtool test rules` ile çalışır).

| Uyarı | Koşul | Neden bu eşik |
|---|---|---|
| `CollectorDown` | Bir metrik ucu 5 dk okunamıyor (`up == 0`) | Konteyner durmuş veya çökmüş. 5 dk, yeniden başlatmayı (restart) beklemeye yeter. |
| `Watchdog` | Her zaman ateşlenir | Uyarı hattının çalıştığının kanıtı. `status.sh --check` görmezse 2 koduyla çıkar (Prometheus durmuş veya kurallar yüklenmemiş). Panelde ve listede ayrıca sayılmaz. |
| `CtFollowerStalled` | Bir log için 15 dk hatasız tur yok; ya da log 15 dk'dır izleniyor ama bu süreçte hiç başarılı tur bitirmedi; ya da 15 dk hiç CT metriği yok | `ct-follow` sağlık kontrolüyle (heartbeat 900 sn) aynı eşik. İkinci kol: iki log açıkken birinin hiç tur bitirememesi (M4c). Üçüncü kol: süreç durunca. |
| `CtLagGrowing` | Geride kalan > 2 milyon kayıt ve son 1 saatte artmış, ya da gecikme > 2 saat (15 dk sürerse) | 2027h1 saatte ~1 milyon kayıt üretir; 2 milyon ~2 saatlik iştir. Kesinti sonrası yetişirken de çıkar; bu doğrudur. |
| `NetworkCollectionStale` | 26 saattir başarılı DNS turu yok | Tur 24 saatte bir, ~1 saat sürer; 2 saat pay. "Başarılı" = en az bir alan adı cevap aldı. Hiç hedefi olmayan tur (`NoTargets`, ör. `official_actions` boşaldı) başarı sayılmaz (M4a). |
| `DataStale` | `ct_candidates` en yeni satırı > 2 saat, `dns_snapshots` > 26 saat; ya da aynı süre boyunca tazelik serisi hiç yok (tablo boş veya ölçüm durdu; Prometheus en az o kadar süredir açıksa) (10 dk sürerse) | Toplayıcı "başarılı" görünse de tabloya veri gelmiyor olabilir (M4b). 2027h1'de saatte onlarca aday gelir. Diğer tablolar için eşik yok (elle yüklenir veya haftalık); panelde görünür. |
| `DnsErrorRateHigh` | Son turda bir yolda (Udp, Tcp, Doh) hata payı > %20, en az 20 sorgu | Aşağıda SERVFAIL notu. Yol başına bakılır: Servis sağlayıcı yalnızca UDP 53'ü engellerse toplam oran bunu gizlerdi. |
| `UnexpectedOpenPort` | Beklenen listede olmayan dinleyici 2 dk açık | Kısa ömürlü UDP istemci soketleri bu sürede kapanır. |
| `PortAuditStale` | Port denetimi 5 dk sonuç üretmedi | Denetim 30 sn'de bir; 10 tur kaçmış demektir. |
| `DiskLow` | Docker veri diskinde < 10 GB boş (10 dk) | `install.sh` 20 GB altında uyarır; veritabanı ve Prometheus bu diskte. |

**SERVFAIL kararı:** SERVFAIL ve NXDOMAIN, `DnsErrorRateHigh` oranına girmez. İkisi de çözücünün verdiği gerçek bir
cevaptır: Engellenen, süresi dolan veya yetkili sunucusu kapanan alan adlarında olağandır, yani hedef kümenin
özelliğidir, bizim ağımızın arızası değil. Bunları hata saysaydık uyarı listedeki alan adlarının durumuna göre
çalardı. Oran yalnızca ölçüm altyapısının hatalarına bakar: zaman aşımı, bağlantı hatası, bozuk paket, REFUSED, DoH
HTTP hatası. SERVFAIL sayısı panelde ayrı gösterilir; ani bir artış gözle izlenir.

### Uyarıları denemek

Toplayıcıyı bilerek durdurmak (`CollectorDown` ~5 dk, `CtFollowerStalled` ~16 dk sonra):

```bash
docker compose stop ct-follow
# Grafana panelinde "Uyarılar" satırı veya:
./status.sh            # "Alarmlar" bölümü: önce pending, sonra firing
docker compose start ct-follow   # kalınan yerden devam eder; uyarılar birkaç dakikada kapanır
```

Fazladan port açmak (`UnexpectedOpenPort` ~2-3 dk sonra):

```bash
python3 -m http.server 8099 --bind 127.0.0.1     # WSL Ubuntu'da, ayrı bir terminalde
docker compose run --rm port-audit port-audit --once true   # hemen kontrol: "beklenmeyen: Tcp 8099 (Loopback)", çıkış kodu 3
# Ctrl+C ile sunucuyu kapatın; uyarı bir sonraki denetimde (30 sn) temizlenir.
```

`--once true` yalnızca sayı, protokol, port ve kapsam basar (adres ve süreç adı yok). Çıkış kodu: 0 temiz, 3 fark var,
1 okunamadı.

Yanlışlıkla yayımlanan bir Docker portunu denemek (port denetiminden bağımsız, `docker ps` ile; M2):

```bash
docker run -d --rm --name fl-yayin-deneme -p 127.0.0.1:18099:9090 \
  prom/prometheus:v3.15.1@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2   # zaten indirilmiş imaj
./status.sh --check; echo $?     # "beklenmeyen Docker portu: 18099/tcp(loopback)", çıkış kodu 1
docker stop fl-yayin-deneme
```

### Bildirim (Windows, `status.sh --check`)

Uyarılar dışarıya gönderilmediği için biri bakmazsa fark edilmezdi; Prometheus'un kendisi durursa hiçbir uyarı da
ateşlenmezdi (THREAT_MODEL M3). Bunun için iki parça var:

- `./status.sh --check`: Hiçbir şey basmaz, yalnızca tek satır özet ve çıkış kodu verir. 0 temiz; 1 ateşlenen uyarı
  (adlarıyla) veya beklenmeyen yayımlanan Docker portu; 2 Prometheus'a ulaşılamıyor ya da `Watchdog` yok; 3 bir
  konteyner çalışmıyor veya sağlıksız. Birden fazla sorun varsa öncelik 2 > 3 > 1, özet hepsini yazar. Alan adı ve IP basmaz.
- `deploy/windows/fraud-lab-check.ps1` (Windows PowerShell 5.1, harici modül yok): `wsl.exe -d Ubuntu --exec bash -lc
  '~/fraud-lab/deploy/linux/status.sh --check'` çağırır. Kod 0 değilse veya WSL/Ubuntu 120 saniyede cevap vermezse
  Windows'un yerleşik bildirimini gösterir (başlık + özet). Aynı durum saatte en fazla bir kez tekrarlanır; sorun
  düzelince bir kez "sorun giderildi" der. Durum dosyası `%LOCALAPPDATA%\fraud-lab\check-state.json`. Dış servis,
  e-posta, webhook yok.

Elle deneme (bildirim yerine konsola yazar):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\fraud-lab-check.ps1 -NoToast
```

**Zamanlanmış görevi kurmak (bir kez, kendi Windows hesabınızla, yönetici yetkisi gerekmez):**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\install-check-task.ps1
```

Görev depodaki betiği değil, **kopyasını** çalıştırır (THREAT_MODEL M5). Neden: `C:\dev\...` altındaki dosyalar
C:\ kökünden "Authenticated Users: Değiştirme" iznini devralır; makinedeki başka bir yerel hesap depodaki betiği
değiştirip görev aracılığıyla sizin hesabınızla kod çalıştırabilirdi. Ayrıca `git pull` ile gelen her değişiklik
gözden geçirilmeden 15 dakikada bir çalışırdı. Kurulum betiği:

1. `fraud-lab-check.ps1`'i `%LOCALAPPDATA%\fraud-lab\` altına kopyalar.
2. Klasörün ve kopyanın devralınan izinlerini kaldırır (`icacls ... /inheritance:r`) ve yalnızca sizin hesabınıza,
   SYSTEM'e ve Administrators'a izin verir; sonra izinleri geri okuyup doğrular.
3. Görevi bu kopyadan, sizin hesabınızla, oturum açıkken (bildirim için gerekli) ve yönetici yetkisi olmadan kurar.
4. Kopyanın SHA256 özetini yazar.

Depo güncellenince kopya **değişmez**. Yeni sürümü kullanmak için değişikliği (`git diff`) gözden geçirip kurulum
betiğini yeniden çalıştırın; kopyalama yalnızca o zaman olur.

Görevin karşılığı (bilgi için; kurulum betiği aynısını Windows'un ScheduledTasks komutlarıyla kurar, `cmd.exe`'de
`%LOCALAPPDATA%` genişler):

```
schtasks /Create /TN "fraud-lab-check" /SC MINUTE /MO 15 /F /TR "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File %LOCALAPPDATA%\fraud-lab\fraud-lab-check.ps1"
```

**Kaldırmak** (görev, kopya ve durum dosyası silinir):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\fraud-lab\deploy\windows\install-check-task.ps1 -Uninstall
```

Yalnızca görevi silmek: `schtasks /Delete /TN "fraud-lab-check" /F`.

Notlar: `-WindowStyle Hidden` her çalışmada bir an konsol penceresi gösterebilir. Betik Ubuntu'da `status.sh`'i
`timeout 100` ile çalıştırır; Windows tarafı 120 saniye bekler. Docker takılırsa iki sınırdan biri dolar ve "cevap
vermedi" bildirimi gelir.

### Port denetimi

Kural 2: Port denetimi yalnızca kendi makinemizde yapılır. `port-audit` hiçbir yere bağlanmaz ve paket göndermez;
çekirdeğin soket tablolarını (`/proc/net/tcp`, `tcp6`, `udp`, `udp6`) okur. TCP'de yalnızca LISTEN durumundakiler,
UDP'de yalnızca bağlanmamış (her yerden paket kabul eden) soketler sayılır.

Neden `network_mode: host`: `/proc/net`, okuyan sürecin ağ ad alanını gösterir. Konteyner kendi ağ ad alanında olsaydı
yalnızca kendini görürdü. Bunun bedeli küçük tutuldu: yetkisiz kullanıcı, salt okunur dosya sistemi, bütün Linux
yetkileri kaldırılmış, `pid: host` yok (süreçlere bakılmaz), veritabanı parolası verilmez.

Beklenen liste: `port-audit/expected` (açıklamalar dosyanın içinde). Varsayılan liste bu WSL Ubuntu + Docker Engine
kurulumu için düşünüldü:

| Kayıt | Ne |
|---|---|
| `tcp/53000@loopback` | Grafana (Docker'ın `docker-proxy` süreci 127.0.0.1:53000'de dinler) |
| `udp/53@local`, `tcp/53@local` | WSL DNS tüneli (10.255.255.254) veya systemd-resolved (127.0.0.53, 127.0.0.54) |

SSH, chrony veya containerd'nin loopback portu gerekiyorsa dosyadaki yorumlu satırları açın. Dosya değişince yeniden
başlatma gerekmez (30 sn içinde okunur): Konteynere dosya değil `port-audit/` klasörü salt okunur bağlanır. `git pull`
ve çoğu düzenleyici dosyayı yeni bir inode ile değiştirir; tek dosya bağlansaydı çalışan konteyner eski içeriği görmeye
devam ederdi. Bu klasöre başka dosya koymayın. Bu dosya makinenin "açık kapı" listesidir; değişikliği bilerek ve commit
mesajında nedeniyle yapın.

"Eksik beklenen" uyarı değil, bilgidir: Ör. Grafana durmuşsa `tcp/53000` eksik görünür.

Bilinen sınırlar:

- **Docker Desktop:** Docker Engine bu Ubuntu'nun içine kurulu olmalı. Docker Desktop'ın WSL entegrasyonu
  kullanılırsa "host ağı" Docker Desktop'ın kendi sanal makinesidir; denetim bu Ubuntu'yu değil onu görür
  (`install.sh` bunu algılayıp uyarır).
- **userland-proxy kapalıysa** (`/etc/docker/daemon.json` içinde `"userland-proxy": false`) Docker yayımlanan portu
  iptables ile yönlendirir, dinleyen soket olmaz. O zaman port denetimi yayımlanan portları göremez ve `tcp/53000`
  "eksik" görünür; WSL'nin localhost iletimi de çalışmayabilir. `install.sh` bu durumu bildirir; `status.sh` ve
  `--check` yayımlanan portları ayrıca `docker ps` ile denetler (beklenen tek yayın `127.0.0.1:GRAFANA_PORT`).

### İmaj sabitleme

Prometheus ve Grafana imajları sürüm ve sha256 özetiyle sabittir (`prom/prometheus:v3.15.1@sha256:...`,
`grafana/grafana:13.2.6@sha256:...`). Aynı etiket sonradan başka içerikle yayımlansa bile çekilmez. Güncelleme:

```bash
docker buildx imagetools inspect prom/prometheus:vX.Y.Z   # yalnızca kayıt defterini sorar; "Digest:" satırı
```

Yeni özeti `docker-compose.monitoring.yml`, kök `docker-compose.yml` ve `.github/workflows/ci.yml` içinde birlikte
değiştirin; ayrı bir commit'te.

## Durum kontrolü

```bash
./status.sh
```

Yalnızca sayı basar, hiçbir alan adı basmaz:

- Konteyner durumu ve sağlık sonucu (`ct-follow` her log en az bir turu 15 dakika içinde hatasız bitirmediyse `unhealthy`).
- Log başına sıradaki indeks, son checkpoint boyutu, geride kalan kayıt, işlenen ve atlanan kayıt, kapsama oranı,
  aday sayısı, son checkpoint yaşı.
- DNS: son 24 saatte çözücü ve yol başına sorgu, cevap, NXDOMAIN, hata, engel işareti, TC sonrası TCP ve ortalama
  gecikme; RIPEstat ve InternetDB için son 7 günün sayıları.
- Son 24 saatte tur, hata, işlenen tile ve tahmini indirme (`tahmini_indirme_gb` = işlenen tile x `AVG_TILE_KB`,
  varsayılan 175 KB). Konteyner açıldığından beri gerçek ağ trafiği de `docker stats` ile yazılır.
- Son 1 saatte işlenen kayıt. Sıfırsa log durmuş, dilim kapanmış veya izleyici hata alıyor olabilir.
- Alarmlar: Prometheus'ta ateşlenen ve bekleyen uyarıların adı ve sayısı (yalnızca ad; ayrıntı Grafana'da).
- Port denetiminin son değişiklikleri (protokol, port, kapsam; adres yok).
- Veritabanı boyutu, disk ve yedek sayısı.

Tek satır ve çıkış koduyla denetim (Windows bildirimi bunu kullanır): `./status.sh --check`; bkz. "Bildirim".

Daha ayrıntılı rapor (yine yalnızca sayı):

```bash
docker compose -f docker-compose.ct.yml run --rm ct-follow quality-report
```

Canlı log (alan adı yazılmaz, log `#1`, `#2` diye anılır):

```bash
docker compose -f docker-compose.ct.yml logs -f --tail 50 ct-follow
```

### Aynı log için tek süreç

Her log için PostgreSQL advisory lock alınır. İkinci bir `ct-follow` (ör. yanlışlıkla `docker compose run` ile) aynı
veritabanına bağlanırsa "başka bir süreç bu logu izliyor" der ve bekler; ilk süreç durursa devralır. Aynı aralık iki
kez işlenmez.

## Durdurma ve başlatma

```bash
docker compose -f docker-compose.ct.yml stop ct-follow   # yalnızca izleyiciyi durdur
docker compose -f docker-compose.ct.yml stop             # her şeyi durdur (veri kalır)
docker compose -f docker-compose.ct.yml start            # yeniden başlat
```

Durdurmak veri kaybettirmez: Kalınan yer veritabanındadır, yeniden başlayınca oradan devam edilir.

`docker compose down -v` veritabanı birimini SİLER. Yedeğiniz yoksa kullanmayın.

## Güncelleme

```bash
cd fraud-lab
git pull
cd deploy/linux
./install.sh
```

`install.sh` imajı yeniden derler; şema değiştiyse `migrate` yeni migration'ı uygular, sonra `ct-follow` yeni imajla
başlar. Eski bir kurulumda `.env` dosyasına eksik anahtarları (ör. `GRAFANA_PORT`, `COMPOSE_FILE`) ve `secrets/` parola dosyasını ekler;
var olan satırlara dokunmaz. `git pull` sonrası `status.sh` çalışmadan önce `install.sh` bir kez çalışmalı. Güncellemeden önce bir yedek almak iyi olur: `./backup.sh`.

## Yedek

```bash
./backup.sh
```

- `pg_dump` ile sıkıştırılmış tek dosya: `~/fraud-lab-backups/fraud_lab_<UTC zaman>.dump`
- Dosya izni `600`, klasör izni `700`. Son 7 yedek tutulur, eskiler silinir.
- Yedek bitince `pg_restore --list` ile okunabilirliği kontrol edilir; yarım dosya asıl adla kalmaz.
- Ayar: `BACKUP_DIR=/baska/klasor KEEP=14 ./backup.sh`

### Günlük çalıştırma: cron

```bash
crontab -e
```

Şu satırı ekleyin (her gün 03:15; yolu kendi klasörünüze göre düzeltin):

```
15 3 * * * /home/KULLANICI/fraud-lab/deploy/linux/backup.sh >> /home/KULLANICI/fraud-lab-backups/backup.log 2>&1
```

Kullanıcı `docker` grubunda değilse cron satırını root'un crontab'ına (`sudo crontab -e`) yazın.

### Günlük çalıştırma: systemd timer (cron yerine)

`systemd/` klasöründe örnek dosyalar var. İçlerindeki yolu kendi klasörünüze göre düzeltin, sonra:

```bash
mkdir -p ~/.config/systemd/user
cp systemd/fraud-lab-backup.service systemd/fraud-lab-backup.timer ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now fraud-lab-backup.timer
sudo loginctl enable-linger "$USER"   # oturum kapalıyken de çalışsın
systemctl --user list-timers          # sonraki çalışma zamanı
```

## Yedekten dönme

```bash
docker compose -f docker-compose.ct.yml stop ct-follow
docker compose -f docker-compose.ct.yml exec -T db \
  pg_restore -U fraud_lab -d fraud_lab --clean --if-exists --no-owner --single-transaction \
  < ~/fraud-lab-backups/fraud_lab_YYYYMMDDTHHMMSSZ.dump
docker compose -f docker-compose.ct.yml start ct-follow
```

`--single-transaction`: Geri yükleme yarıda kalırsa hiçbir şey değişmez. Yedekten sonra işlenen aralıklar, izleyici
yeniden başlayınca tekrar okunur; adaylar çoğalmaz (`log_name`, `entry_index` tekil).

## Veriyi Windows makineye taşıma

Yedek dosyası tek başına yeterlidir. Windows'ta (PowerShell; OpenSSH istemcisi Windows 10/11'de hazır gelir):

```powershell
# 1) Dosyayı çek (Linux makinenin adı veya IP'si ile)
scp kullanici@linux-makine:~/fraud-lab-backups/fraud_lab_YYYYMMDDTHHMMSSZ.dump C:\dev\fraud-lab\data\private\

# 2) Yerel veritabanında AYRI bir veritabanına yükle (çalışan veritabanının üzerine yazmamak için)
cd C:\dev\fraud-lab
docker compose cp data\private\fraud_lab_YYYYMMDDTHHMMSSZ.dump db:/tmp/linux.dump
docker compose exec db createdb -U fraud_lab fraud_lab_linux
docker compose exec db pg_restore -U fraud_lab -d fraud_lab_linux --no-owner /tmp/linux.dump
docker compose exec db rm /tmp/linux.dump
```

- Dosyayı `data\private\` altına koyun: Bu klasör `.gitignore`'da, depoya girmez.
- PowerShell `<` yönlendirmesini desteklemediği için dosya önce konteynere kopyalanır.
- Aynı veritabanını yeniden yüklemek için önce `docker compose exec db dropdb -U fraud_lab fraud_lab_linux`.

## Güvenlik notları

- **SSH:** Makineye yalnızca anahtarla girin. `/etc/ssh/sshd_config` içinde `PasswordAuthentication no` ve
  `PermitRootLogin no`, sonra `sudo systemctl reload ssh` (bazı dağıtımlarda `sshd`). Önce anahtarla girişin
  çalıştığını ayrı bir oturumda deneyin.
- **Güvenlik duvarı:** Dışarıdan yalnızca SSH açık kalsın (ör. `sudo ufw allow OpenSSH && sudo ufw enable`).
  Veri servisleri hiçbir port yayımlamaz. Tek yayımlanan port Grafana'dır ve yalnızca `127.0.0.1`'dedir.
- **Grafana:** Anonim erişim kapalı, parola rastgele ve `secrets/grafana_admin_password` içinde. LAN'a açmayın (`0.0.0.0:53000` yazmayın);
  port denetimi bunu "beklenmeyen dinleyici" olarak yakalar. Uzaktan erişim gerekirse SSH tüneli kullanın.
- **PostgreSQL dışa kapalı:** `db` servisi port yayımlamaz ve dışarı çıkışı olmayan iç ağdadır. Yedek ve durum
  betikleri konteynerin içinden bağlanır. Port açmanız gerekirse yalnızca `127.0.0.1:` ile açın ve SSH tüneli kullanın.
- **Gizli veri:** `ct_candidates`, SPK veya USOM listesinde olmayan alan adları içerir. Veritabanı, yedekler ve
  Windows'a taşınan dosya `data/private/` ile aynı gizlilikte tutulur: depoya girmez, paylaşılmaz, ekran görüntüsüne
  ve rapora adlar girmez. Yalnızca toplu sayılar yayımlanır.
- **`.env`:** Veritabanı parolasını içerir, izni `600`. Depoya girmez (`.gitignore`).
- **docker grubu:** `docker` grubundaki kullanıcı fiilen root yetkisine sahiptir. Bu grubu yalnızca kendi
  kullanıcınıza verin.
- **Port denetimi host ağında:** `port-audit` host ağ ad alanında çalışır (soket tablosunu görmek için). Yetkisiz
  kullanıcı, salt okunur, yetki yok; hiçbir yere bağlanmaz ve hiçbir port dinlemez. Ayrıntı: "Port denetimi".
- **Konteyner sertleştirmesi:** Bütün servisler root olmayan kullanıcıyla, salt okunur dosya sistemiyle, tüm Linux
  yetkileri kaldırılmış (`cap_drop: ALL`) ve `no-new-privileges` ile çalışır. CPU, bellek ve süreç sayısı sınırlıdır.
  Konteyner logları 10 MB x 5 dosya ile sınırlıdır.
