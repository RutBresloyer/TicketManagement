using System.ComponentModel.DataAnnotations;

namespace RequestManagement.Api.Validation;

/// <summary>Encoding of SQL Server <c>rowversion</c> values as the API's opaque base64 token.</summary>
public static class RowVersionToken
{
    public const int ByteLength = 8;

    // 8 bytes always encode to exactly 12 base64 characters (with one '=' of padding).
    private const int EncodedLength = 12;

    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    public static bool TryDecode(string? token, out byte[] rowVersion)
    {
        rowVersion = [];
        if (token is null || token.Length != EncodedLength)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[ByteLength + 1];
        if (!Convert.TryFromBase64String(token, buffer, out var written) || written != ByteLength)
        {
            return false;
        }

        rowVersion = buffer[..ByteLength].ToArray();
        return true;
    }

    /// <summary>Decodes a token that has already passed <see cref="RowVersionTokenAttribute"/> validation.</summary>
    public static byte[] DecodeValidated(string token) =>
        TryDecode(token, out var rowVersion)
            ? rowVersion
            : throw new ArgumentException("Token was not validated before decoding.", nameof(token));
}

/// <summary>Validates that a string is a base64-encoded 8-byte rowversion token. <c>null</c> is left to <c>[Required]</c>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RowVersionTokenAttribute : ValidationAttribute
{
    public RowVersionTokenAttribute()
        : base("Must be a base64-encoded 8-byte row version, exactly as returned in 'rowVersion' (e.g. 'AAAAAAAAB9E=').")
    {
    }

    public override bool IsValid(object? value) => value is null || RowVersionToken.TryDecode(value as string, out _);
}
