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
