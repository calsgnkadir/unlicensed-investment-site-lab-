# Kararlar

Her kayıt: tarih, karar, neden, alternatif.

## 2026-11-12: Etiket verisi (Aşama 0)

**Karar:** Pozitif etiketler, 2026 SPK bültenlerindeki erişim engeli listelerinden şu adımlarla çıkarıldı:

1. `scripts/phase0/extract_candidates.py`, 78 bültenden aday adresleri çıkardı. Sayfa altlığındaki adres ifadesi ve SPK'nın kendi adresi elendi, böylece 24 bültenden 201 alan adı ve bülten çifti kaldı.
2. Her çift, iki bağımsız ve yazılım destekli incelemeyle bültenin kendi metninden kontrol edildi. İki inceleme birbirinin sonucunu görmedi. Karar, tarih ve kategori 201 satırın tamamında aynı çıktı.
3. Üçüncü kontrol, basit bir bölüm başlığı kuralıyla yapıldı. Kuralın uyuşmadığı 27 satırın hepsi elle açıklandı: Satır sonunda bölünmüş başlık, uygulama mağazası bağlantısı ve kısaltma servisi.
4. Sonuç olarak 21 bültenden 175 kayıt ve 171 farklı alan adı elde edildi. Bu kayıtlar `data/private/phase0/engellenenler.csv` dosyasında tutulur ve git dışındadır.

**Neden:** Etiket, ölçümün temeli. Tek bir geçişte yapılan hata bütün sonuçlara taşınır.

**Alternatif:** Tamamen elle doğrulama. Daha yavaş olurdu, ama kontrol bir kişiye bağlı olurdu. Rastgele 10 satırlık elle örnek kontrolü sonradan eklenebilir.

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
