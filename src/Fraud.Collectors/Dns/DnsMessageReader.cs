using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Paketi baştan sona okuyan imleç. Her okuma, önce "bu kadar bayt kaldı mı?" diye bakar; kalmadıysa
/// <see cref="DnsFormatException"/> fırlatır. Böylece paket ne kadar bozuk olursa olsun dizinin dışına çıkılmaz.
/// </summary>
/// <remarks>
/// Paketin içindeki hiçbir sayıya güvenilmez: Sayaçlar, uzunluk baytları ve işaretçiler saldırganın
/// yazdığı değerler olabilir. Bu sınıf yalnızca "okunabilir mi" sorusunu cevaplar; anlamı ayrıştırıcı denetler.
/// </remarks>
internal sealed class DnsMessageReader(byte[] message)
{
    /// <summary>Sıradaki okunacak baytın konumu (0'dan başlar).</summary>
    public int Position { get; private set; }

    public int Remaining => message.Length - Position;

    public byte ReadByte()
    {
        Require(1);
        return message[Position++];
    }

    /// <summary>2 bayt, ağ bayt sırasıyla (büyük uç önce, RFC 1035 Bölüm 2.3.2).</summary>
    public ushort ReadUInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(Position, 2));
        Position += 2;
        return value;
    }

    /// <summary>4 bayt, ağ bayt sırasıyla.</summary>
    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        Require(count);
        var bytes = message.AsSpan(Position, count).ToArray();
        Position += count;
        return bytes;
    }

    public void Skip(int count)
    {
        Require(count);
        Position += count;
    }

    /// <summary>
    /// Bir alan adı okur (RFC 1035 Bölüm 3.1 ve 4.1.4). Sıkıştırma işaretçilerini izler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uzunluk baytının üst iki biti etiketin türünü söyler:
    /// <list type="bullet">
    /// <item><c>00</c>: Normal etiket. Alt 6 bit uzunluktur (0-63). Uzunluk 0 ise ad bitti (kök).</item>
    /// <item><c>11</c>: Sıkıştırma işaretçisi. Bu bayt ve bir sonraki bayt birlikte 14 bitlik bir konum verir:
    /// "Adın geri kalanı paketin şu konumunda zaten yazılı." Aynı son ek her seferinde yeniden yazılmasın diye var.</item>
    /// <item><c>01</c> ve <c>10</c>: Ayrılmış (RFC 6891 Bölüm 5 eski genişletilmiş etiketleri kaldırdı). Reddedilir.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Saldırgan paket işaretçiyle döngü kurabilir (A, B'yi; B, A'yı gösterir) ve okuyucuyu sonsuza kadar döndürebilir.
    /// Üç kilit var:
    /// <list type="number">
    /// <item>İşaretçi, okunmakta olan parçanın başlangıcından KESİNLİKLE önceye göstermeli. Her atlamada hedef
    /// küçüldüğü için döngü kurulamaz. Meşru sıkıştırma hep daha önce yazılmış bir adı gösterir, bu kural onu bozmaz.</item>
    /// <item>En fazla <see cref="DnsWire.MaxCompressionJumps"/> atlama.</item>
    /// <item>Açılmış adın toplam uzunluğu 255 baytı geçemez.</item>
    /// </list>
    /// Ayrıca işaretçi başlığın içini (ilk 12 bayt) gösteremez: Orada ad yoktur.
    /// </para>
    /// <para>
    /// İmleç: İşaretçiye ilk rastlandığında, ana imleç işaretçinin hemen arkasında kalır (ad orada biter);
    /// atlamalar yalnızca adı okumak için kullanılan yerel bir imleçle yapılır.
    /// </para>
    /// </remarks>
    /// <returns>Küçük harfli ad, sonda nokta yok. Kök ad için <c>"."</c>.</returns>
    public string ReadName()
    {
        var name = new StringBuilder();
        var cursor = Position;          // adı okurken gezdiğimiz konum
        var segmentStart = Position;    // şu an okunan etiket dizisinin başladığı konum
        int? continueAt = null;         // ilk işaretçiden sonra ana imlecin devam edeceği konum
        var wireLength = 0;             // açılmış adın paketteki uzunluğu (uzunluk baytları dahil)
        var jumps = 0;

        while (true)
        {
            if (cursor >= message.Length)
            {
                throw new DnsFormatException("Ad paketin sonunda yarım kaldı.");
            }

            var lengthByte = message[cursor];
            var kind = lengthByte & 0xC0; // üst iki bit

            if (kind == 0xC0)
            {
                // İşaretçi 2 bayttır; ikinci baytın da pakette olması gerekir.
                if (cursor + 1 >= message.Length)
                {
                    throw new DnsFormatException("Sıkıştırma işaretçisi yarım.");
                }

                // Konum = ilk baytın alt 6 biti (üstte) + ikinci bayt (altta). En fazla 16383.
                var target = ((lengthByte & 0x3F) << 8) | message[cursor + 1];

                if (target < DnsWire.HeaderLength)
                {
                    throw new DnsFormatException("İşaretçi başlığın içini gösteriyor.");
                }

                if (target >= segmentStart)
                {
                    throw new DnsFormatException("İşaretçi geriye değil ileriye ya da kendine gösteriyor.");
                }

                jumps++;
                if (jumps > DnsWire.MaxCompressionJumps)
                {
                    throw new DnsFormatException("Çok fazla sıkıştırma işaretçisi.");
                }

                continueAt ??= cursor + 2;
                cursor = target;
                segmentStart = target;
                continue;
            }

            if (kind != 0)
            {
                throw new DnsFormatException("Ayrılmış etiket türü (01 veya 10).");
            }

            // Normal etiket: lengthByte 0-63 arasıdır (üst iki bit 0).
            if (lengthByte == 0)
            {
                wireLength += 1; // kökün 00 baytı
                cursor += 1;
                break;
            }

            // Etiketin baytları pakette olmalı.
            if (cursor + 1 + lengthByte > message.Length)
            {
                throw new DnsFormatException("Etiket uzunluğu paketin sonunu aşıyor.");
            }

            // Bu etiket + en az kökün 00 baytı 255'e sığmalı.
            wireLength += 1 + lengthByte;
            if (wireLength + 1 > DnsWire.MaxNameWireLength)
            {
                throw new DnsFormatException("Ad 255 baytı aşıyor.");
            }

            AppendLabel(name, message.AsSpan(cursor + 1, lengthByte));
            name.Append('.');
            cursor += 1 + lengthByte;
        }

        Position = continueAt ?? cursor;
        return name.Length == 0 ? "." : name.ToString(0, name.Length - 1);
    }

    /// <summary>
    /// Etiketi metne çevirir. Harf, rakam, tire ve alt çizgi olduğu gibi (harfler küçültülerek) yazılır;
    /// başka her bayt RFC 1035 Bölüm 5.1'deki gibi <c>\DDD</c> (üç haneli ondalık) olarak yazılır.
    /// </summary>
    /// <remarks>
    /// Etiket herhangi bir bayt içerebilir (nokta, boşluk, kontrol karakteri, HTML). Kaçış sayesinde saklanan
    /// metin her zaman düz ASCII olur ve "a.b" adlı tek etiket iki etiketle karışmaz.
    /// </remarks>
    private static void AppendLabel(StringBuilder name, ReadOnlySpan<byte> label)
    {
        foreach (var b in label)
        {
            var c = (char)b;
            if (c is >= 'A' and <= 'Z')
            {
                name.Append((char)(c + ('a' - 'A')));
            }
            else if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            {
                name.Append(c);
            }
            else
            {
                name.Append('\\').Append(b.ToString("D3", CultureInfo.InvariantCulture));
            }
        }
    }

    private void Require(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new DnsFormatException("Paket beklenenden kısa.");
        }
    }
}
