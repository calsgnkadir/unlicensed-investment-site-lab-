# Canlı Static CT akışı: Linux kurulumu

Bu klasör, `ct-follow` komutunu (Static CT logunu canlı izleyen süreç) bir Linux makinede 7/24 çalıştırmak içindir.
Makinede yalnızca iki servis sürekli çalışır: PostgreSQL (`db`) ve izleyici (`ct-follow`). API bu makinede çalışmaz.

Bu bir araştırma aracıdır, yatırım tavsiyesi değildir. Toplanan aday listesi SPK veya USOM listesinde olmayan alan adları
içerir; bu yüzden veritabanı ve yedekler gizlidir (bkz. "Güvenlik notları").

## Ne yapar, ne yapmaz

- Sycamore Static CT logunun checkpoint ve tile dosyalarını okur, imzayı ve Merkle kökünü doğrular.
- Ham tile diske yazılmaz. Yalnızca finans kelimesi geçen aday satırları ve sayaçlar saklanır.
- Şüpheli sitelere hiçbir istek atmaz. Tek dış bağlantı CT logunun izleme adresidir.
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
| `CT_MIN_REQUEST_INTERVAL` | İsteğe bağlı. Log başına iki isteğin başlangıcı arasındaki en kısa süre. Varsayılan `00:00:00.500`. |

Bu ayarlar yalnızca bu Linux kurulumunu etkiler; `src/Fraud.Collectors/appsettings.json` içindeki geliştirme
varsayılanı değişmez.

Ayar değişince: `docker compose -f docker-compose.ct.yml up -d` (yalnızca değişen servis yeniden başlar).

## Durum kontrolü

```bash
./status.sh
```

Yalnızca sayı basar, hiçbir alan adı basmaz:

- Konteyner durumu ve sağlık sonucu (`ct-follow` her log en az bir turu 15 dakika içinde hatasız bitirmediyse `unhealthy`).
- Log başına sıradaki indeks, son checkpoint boyutu, geride kalan kayıt, işlenen ve atlanan kayıt, kapsama oranı,
  aday sayısı, son checkpoint yaşı.
- Son 24 saatte tur, hata, işlenen tile ve tahmini indirme (`tahmini_indirme_gb` = işlenen tile x `AVG_TILE_KB`,
  varsayılan 175 KB). Konteyner açıldığından beri gerçek ağ trafiği de `docker stats` ile yazılır.
- Son 1 saatte işlenen kayıt. Sıfırsa log durmuş, dilim kapanmış veya izleyici hata alıyor olabilir.
- Veritabanı boyutu, disk ve yedek sayısı.

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
başlar. Güncellemeden önce bir yedek almak iyi olur: `./backup.sh`.

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
  Bu kurulum hiçbir port yayımlamaz.
- **PostgreSQL dışa kapalı:** `db` servisi port yayımlamaz ve dışarı çıkışı olmayan iç ağdadır. Yedek ve durum
  betikleri konteynerin içinden bağlanır. Port açmanız gerekirse yalnızca `127.0.0.1:` ile açın ve SSH tüneli kullanın.
- **Gizli veri:** `ct_candidates`, SPK veya USOM listesinde olmayan alan adları içerir. Veritabanı, yedekler ve
  Windows'a taşınan dosya `data/private/` ile aynı gizlilikte tutulur: depoya girmez, paylaşılmaz, ekran görüntüsüne
  ve rapora adlar girmez. Yalnızca toplu sayılar yayımlanır.
- **`.env`:** Veritabanı parolasını içerir, izni `600`. Depoya girmez (`.gitignore`).
- **docker grubu:** `docker` grubundaki kullanıcı fiilen root yetkisine sahiptir. Bu grubu yalnızca kendi
  kullanıcınıza verin.
- **Konteyner sertleştirmesi:** İki servis de root olmayan kullanıcıyla, salt okunur dosya sistemiyle, tüm Linux
  yetkileri kaldırılmış (`cap_drop: ALL`) ve `no-new-privileges` ile çalışır. CPU, bellek ve süreç sayısı sınırlıdır.
  Konteyner logları 10 MB x 5 dosya ile sınırlıdır.
