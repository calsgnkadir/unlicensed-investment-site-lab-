# Public Suffix List kopyası

`public_suffix_list.dat`, https://publicsuffix.org/list/public_suffix_list.dat adresinden indirilmiş sabit bir kopyadır ve derlemeye gömülür (`Fraud.Infrastructure.csproj`, `EmbeddedResource`).

| Alan | Değer |
|---|---|
| Liste sürümü | 2026-11-08_13-21-08_UTC |
| Liste commit'i | 7a21dc3491d43db036124fdd6a3a77d1cc44ad8a |
| SHA-256 | dc00ceb7bb9256c41506b12a863db6a4b1c6021771eeccf5e073827750ac4b74 |
| Lisans | MPL 2.0 (dosyanın başında yazar) |

Neden sabit kopya: Liste çalışma anında indirilirse aynı alan adı farklı günlerde farklı "kayıtlı alan adına" ayrılabilir ve sonuçlar tekrarlanamaz. Sürüm dosyada ve bu tabloda yazılı olduğu için hangi listeyle çalışıldığı her zaman bilinir.

Güncelleme: Dosyayı aynı adresten yeniden indir, tablodaki sürüm, commit ve SHA-256 değerlerini değiştir, testleri çalıştır. Güncelleme ayrı bir commit olur; ölçüm dönemi içinde liste değiştirilmez.
