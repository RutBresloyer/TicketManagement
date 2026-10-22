using RequestManagement.Api.Validation;

namespace RequestManagement.Tests.Unit;

public sealed class RowVersionTokenTests
{
    [Fact]
    public void Round_trips_an_8_byte_rowversion()
    {
        byte[] rowVersion = [0, 0, 0, 0, 0, 1, 0x8F, 0xFD];

        var token = RowVersionToken.Encode(rowVersion);

        Assert.Equal("AAAAAAABj/0=", token);
        Assert.True(RowVersionToken.TryDecode(token, out var decoded));
        Assert.Equal(rowVersion, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("AAECAw==")] // valid base64, but only 4 bytes
    [InlineData("AAECAwQFBgcICQ==")] // valid base64, but 10 bytes
    [InlineData("AAECAwQFBg?=")] // 12 characters, invalid alphabet
    public void Rejects_anything_that_is_not_base64_of_exactly_8_bytes(string? token)
    {
        Assert.False(RowVersionToken.TryDecode(token, out _));
        Assert.False(new RowVersionTokenAttribute().IsValid(token ?? "x"));
    }
}
