namespace Fraud.Evaluation.Model;

/// <summary>Bir ölçüm biriminin (kayıtlı alan adı) ait olduğu grup. Kaynak: KONTROL_GRUBU_YONTEMI.md Bölüm 2–5.</summary>
public enum SubjectGroup
{
    /// <summary>SPK erişim engeli listesinde. "Dolandırıcı" demek değildir (Kural 3).</summary>
    Positive = 1,

    /// <summary>G1: Resmi kaynakta web adresi yazılı lisanslı kurumlar.</summary>
    ControlLicensed = 2,

    /// <summary>SPK "faaliyette bulunanlar" kripto listesi. Yetki listesi DEĞİLDİR; G1'e girmez (karar 5).</summary>
    ControlCryptoActive = 3,

    /// <summary>G2: Finans haber ve eğitim siteleri.</summary>
    ControlFinanceNews = 4,

    /// <summary>G3: Aynı dönemde ilk sertifikasını almış, finans kelimesi içeren normal alan adları (zor negatifler).</summary>
    ControlHardNegative = 5,

    /// <summary>G4: Rastgele normal siteler (G4a Tranco, G4b CT tarafsız örnek).</summary>
    ControlRandom = 6,
}

/// <summary>Alt grup. Pozitifte yaş sınıfı, kontrolde örnekleme çerçevesi.</summary>
public enum Subgroup
{
    /// <summary>Alt grup yok (G1, G2, kripto listesi, G4a Tranco). Bu kontroller zamanla eşleşmez.</summary>
    None = 0,

    /// <summary>Pozitif, ilk sertifikası karardan en çok 365 gün önce ("yeni ve kısa ömürlü").</summary>
    PositiveNew = 1,

    /// <summary>Pozitif, ilk sertifikası karardan 365 günden daha önce ("eski platform").</summary>
    PositiveEstablished = 2,

    /// <summary>Çerçeve K: Kelime filtreli akıştan (ct_candidates). Baseline burada "tanım gereği" alarm verir.</summary>
    FrameTermConditioned = 3,

    /// <summary>Çerçeve N: Kelimeye bakmadan tuzlu özetle seçilmiş akıştan (ct_neutral_sample).</summary>
    FrameNeutral = 4,

    /// <summary>
    /// Pozitif, yaşı bilinmiyor: Bilinen ilk sertifika karardan SONRA (önceki sertifikaları görmüyoruz). "Yeni"
    /// sayılmaz (mentör maddesi 7), çünkü fark negatif çıkar ve yanlışlıkla "çok yeni" görünür.
    /// </summary>
    PositiveUnknownAge = 5,
}

/// <summary>
/// Ölçümdeki bir birim. Kimlik opaktır: Alan adı bu nesneye hiç girmez, böylece ölçüm kodu adı yanlışlıkla basamaz.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReferenceTime"/> birimin puanlandığı andır. Pozitifte SPK karar tarihi. Kontrolde başta boş olabilir;
/// <see cref="Splitting.ReferenceTimeAssigner"/> onu "ilk sertifika + eşleşmiş gecikme" olarak doldurur.
/// </para>
/// <para>Kurucu, grup ile alt grubun uyumunu denetler: Yanlış etiketli bir birim ölçüme hiç giremez.</para>
/// </remarks>
public sealed record Subject
{
    private readonly bool? _turkeyLinked;
    private readonly bool _buildBelowMinimum;

    public Subject(
        string id,
        SubjectGroup group,
        Subgroup subgroup,
        DateTimeOffset? referenceTime,
        BulletinNumber? bulletin,
        string clusterId,
        bool linkedToPositive = false,
        DateTimeOffset? firstCertificateAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterId);

        if (group == SubjectGroup.Positive)
        {
            if (subgroup is not (Subgroup.PositiveNew or Subgroup.PositiveEstablished or Subgroup.PositiveUnknownAge))
            {
                throw new ArgumentException("Pozitifin alt grubu yeni ya da eski olmalı.", nameof(subgroup));
            }

            // Pozitifin bülteni ve karar tarihi zorunlu: Bölme bültene, puanlama karar tarihine göre yapılır.
            if (bulletin is null || referenceTime is null)
            {
                throw new ArgumentException("Pozitifin bülteni ve karar tarihi olmalı.", nameof(bulletin));
            }

            if (linkedToPositive)
            {
                throw new ArgumentException("'Pozitife bağlı' yalnızca kontrol için anlamlı.", nameof(linkedToPositive));
            }
        }
        else
        {
            if (subgroup is Subgroup.PositiveNew or Subgroup.PositiveEstablished or Subgroup.PositiveUnknownAge)
            {
                throw new ArgumentException("Kontrol pozitif alt grubu taşıyamaz.", nameof(subgroup));
            }

            if (bulletin is not null)
            {
                throw new ArgumentException("Kontrolün bülteni olmaz.", nameof(bulletin));
            }
        }

        Id = id;
        Group = group;
        Subgroup = subgroup;
        ReferenceTime = referenceTime;
        Bulletin = bulletin;
        ClusterId = clusterId;
        LinkedToPositive = linkedToPositive;
        FirstCertificateAt = firstCertificateAt;
    }

    /// <summary>Opak kimlik (ör. anahtarlı özetin ilk 16 karakteri). Alan adı değildir.</summary>
    public string Id { get; }

    public SubjectGroup Group { get; }

    public Subgroup Subgroup { get; }

    /// <summary>Puanlama anı. Sinyal yalnızca bundan ÖNCE bilinen veriyi görür.</summary>
    public DateTimeOffset? ReferenceTime { get; init; }

    /// <summary>Pozitifin en erken karar tarihli bülteni. Kontrolde <c>null</c>.</summary>
    public BulletinNumber? Bulletin { get; }

    /// <summary>Dondurulmuş küme kimliği (KONTROL_GRUBU_YONTEMI.md 7.5). Kümesi yoksa kendi kimliği verilir.</summary>
    public string ClusterId { get; }

    /// <summary>Kontrol bir pozitifle aynı kümede (kod E05). Ana yanlış alarm oranına girmez, ayrı tabakada yazılır.</summary>
    public bool LinkedToPositive { get; }

    /// <summary>Bilinen ilk sertifikanın başlangıcı. Zaman eşleşmesi ve gecikme hesabı buna dayanır.</summary>
    public DateTimeOffset? FirstCertificateAt { get; }

    public bool IsPositive => Group == SubjectGroup.Positive;

    /// <summary>Kontrol üyesinin kurulduğu ISO hafta (G3, G4b). Kilitli testte "F'de dondurulmuş üyeler" bununla seçilir.</summary>
    public string? Week { get; init; }

    /// <summary>Üyeyi kayda geçiren taahhüt.</summary>
    public string? CommitmentId { get; init; }

    /// <summary>
    /// G2 üyesinin Türkiye bağı (control_members.turkey_linked, DECISIONS 2026-11-24). Süzgeç değil etikettir: Rapor G2'yi
    /// hem tamamı hem bu alt küme için ayrı yazar. G2 dışındaki birimde <c>null</c> kalmak zorundadır.
    /// </summary>
    public bool? TurkeyLinked
    {
        get => _turkeyLinked;
        init
        {
            // Object initializer kurucudan SONRA çalışır, Group burada artık dolu.
            if (value is not null && Group != SubjectGroup.ControlFinanceNews)
            {
                throw new ArgumentException("Türkiye etiketi yalnızca G2 (finans haber) için anlamlı.", nameof(value));
            }

            _turkeyLinked = value;
        }
    }

    /// <summary>
    /// Birimin geldiği kontrol kurulumu "yeterli örnek yok" kuralına takıldı (G2: kurulumda 100'den az üye, DECISIONS
    /// 2026-11-24). Böyle bir tabakada oran hiç hesaplanmaz, raporda yalnızca "yeterli örnek yok" yazar. Kural önceden
    /// kayıtlıdır ve kurulum boyutuna bakar, sinyal sonucuna bakmaz.
    /// </summary>
    public bool BuildBelowMinimum
    {
        get => _buildBelowMinimum;
        init
        {
            if (value && IsPositive)
            {
                throw new ArgumentException("'Yeterli örnek yok' yalnızca kontrol kurulumları için anlamlı.", nameof(value));
            }

            _buildBelowMinimum = value;
        }
    }

    /// <summary>
    /// Pozitifin alt grubu: ilk sertifika karardan en çok 365 gün önceyse "yeni", daha önceyse "eski", karardan sonraysa
    /// "yaşı bilinmiyor". İlk sertifikası hiç bilinmeyen pozitif bu fonksiyona gelmez: ölçümden çıkar (mentör maddesi 8).
    /// </summary>
    public static Subgroup PositiveAge(DateTimeOffset decision, DateTimeOffset firstCertificate)
    {
        var age = decision - firstCertificate;
        if (age < TimeSpan.Zero)
        {
            return Subgroup.PositiveUnknownAge;
        }

        return age <= TimeSpan.FromDays(365) ? Subgroup.PositiveNew : Subgroup.PositiveEstablished;
    }

    /// <summary>Çerçeveli kontrol (K ya da N): ilk sertifika ayına göre zamanla eşleşir ve kilitli teste girebilir.</summary>
    public bool IsTimeMatchedControl => Subgroup is Subgroup.FrameTermConditioned or Subgroup.FrameNeutral;
}
