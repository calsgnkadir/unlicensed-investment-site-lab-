using System.Net;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Integration.Tests.Fetching;

public sealed class IpAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // bulut metadata
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("100.100.100.200")] // CGNAT içindeki metadata adresi
    [InlineData("192.0.0.170")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::ffff:127.0.0.1")]          // IPv4 eşlemeli loopback
    [InlineData("::ffff:169.254.169.254")]    // IPv4 eşlemeli metadata
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::127.0.0.1")]               // eski IPv4 uyumlu biçim
    [InlineData("fe80::1")]                   // link-local
    [InlineData("fc00::1")]                   // ULA
    [InlineData("fd00:ec2::254")]             // AWS IPv6 metadata
    [InlineData("ff02::1")]                   // multicast
    [InlineData("64:ff9b::7f00:1")]           // NAT64 ile 127.0.0.1
    [InlineData("2002:7f00:1::1")]            // 6to4 ile 127.0.0.1
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("2001:db8::1")]               // belgeleme
    public void Private_reserved_and_embedded_addresses_are_blocked(string address)
    {
        Assert.False(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("207.241.224.2")]
    [InlineData("172.32.0.1")]   // 172.16/12 dışında
    [InlineData("100.128.0.1")]  // 100.64/10 dışında
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.True(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }
}
