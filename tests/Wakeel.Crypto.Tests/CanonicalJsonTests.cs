using System.Text;

namespace Wakeel.Crypto.Tests;

public class CanonicalJsonTests
{
    [Fact]
    public void Member_order_of_the_source_object_does_not_change_the_bytes()
    {
        var first = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("""{"b":1,"a":2,"c":{"z":1,"y":2}}"""));
        var second = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("""{"c":{"y":2,"z":1},"a":2,"b":1}"""));

        Assert.Equal(first, second);
        Assert.Equal("""{"a":2,"b":1,"c":{"y":2,"z":1}}""", Encoding.UTF8.GetString(first));
    }

    [Fact]
    public void Whitespace_is_removed()
    {
        var canonical = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("{\n  \"a\" : [ 1, 2 ]\n}"));

        Assert.Equal("""{"a":[1,2]}""", Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public void Array_order_is_preserved()
    {
        var canonical = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("""["b","a","c"]"""));

        Assert.Equal("""["b","a","c"]""", Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public void Numbers_are_written_invariantly()
    {
        var canonical = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("""{"a":1.5,"b":-3,"c":12345678901234}"""));

        Assert.Equal("""{"a":1.5,"b":-3,"c":12345678901234}""", Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public void Arabic_text_survives_as_utf8()
    {
        var body = new RevocationEntry("جهاز-1", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        var json = CanonicalJson.Serialize(body);

        Assert.Contains("جهاز-1", json, StringComparison.Ordinal);
        Assert.Equal(body, CanonicalJson.Deserialize<RevocationEntry>(json));
    }

    [Fact]
    public void Instants_are_normalised_to_utc_with_millisecond_precision()
    {
        var local = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(3));
        var entry = new RevocationEntry("D1", local);

        var json = CanonicalJson.Serialize(entry);

        Assert.Equal("""{"deviceId":"D1","revokedAt":"2026-03-04T07:00:00.000Z"}""", json);
        Assert.Equal(local.ToUniversalTime(), CanonicalJson.Deserialize<RevocationEntry>(json).RevokedAt);
    }

    [Fact]
    public void The_same_object_always_serialises_to_the_same_bytes()
    {
        var body = new DeviceCertificateBody(
            "ORG",
            "OFFICE",
            "PC-1",
            1,
            2,
            "secretary",
            DeviceKind.Pc,
            Base64Url.Encode(new byte[32]),
            Base64Url.Encode(new byte[32]),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            "ORG");

        var first = CanonicalJson.SerializeToUtf8Bytes(body);
        var second = CanonicalJson.SerializeToUtf8Bytes(body);

        Assert.Equal(first, second);
        Assert.Equal(first, CanonicalJson.Canonicalize(first));
    }

    [Fact]
    public void Enumerations_are_written_as_their_lower_case_names()
    {
        var json = CanonicalJson.Serialize(new ContainerEntry("a.bin", 1, "ff"));

        Assert.Equal("""{"name":"a.bin","sha256":"ff","size":1}""", json);
        Assert.Contains("\"pc\"", CanonicalJson.Serialize(DeviceKind.Pc), StringComparison.Ordinal);
        Assert.Contains("msg", CanonicalJson.Serialize(ContainerKind.Msg), StringComparison.Ordinal);
    }

    [Fact]
    public void Broken_json_is_reported_as_corrupt()
    {
        var error = Assert.Throws<CryptoException>(() => CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes("{oops")));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Theory]
    [InlineData("""{"deviceId":"d","revokedAt":"not-a-date"}""")]
    [InlineData("""{"deviceId":"d","revokedAt":""}""")]
    [InlineData("""{"deviceId":"d","revokedAt":5}""")]
    [InlineData("""{"deviceId":"d","revokedAt":null}""")]
    public void An_unreadable_instant_is_reported_as_corrupt_and_nothing_else(string json)
    {
        // Every failure from a hostile document has to arrive as one of this project's own
        // errors; anything else would reach the interface as an unexplained fault.
        var error = Assert.Throws<CryptoException>(() => CanonicalJson.Deserialize<RevocationEntry>(json));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void An_instant_is_read_back_as_the_same_moment_in_universal_time()
    {
        var entry = CanonicalJson.Deserialize<RevocationEntry>(
            """{"deviceId":"d","revokedAt":"2026-09-15T12:00:00.000Z"}""");

        Assert.Equal(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero), entry.RevokedAt);
        Assert.Equal(TimeSpan.Zero, entry.RevokedAt.Offset);
    }
}
