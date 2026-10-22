using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace RequestManagement.Api.Validation;

/// <summary>
/// Format rule for the development-only <c>X-Actor-Id</c> header: 3–64 characters of letters, digits, '.', '_' or '-',
/// starting with a letter or digit. '@' is deliberately not allowed so e-mail addresses (personal data) do not end up
/// in the audit table or logs.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class ActorIdAttribute : ValidationAttribute
{
    public const int MaxLength = 64;

    public ActorIdAttribute()
        : base("X-Actor-Id must be 3-64 characters: letters, digits, '.', '_' or '-', starting with a letter or digit (e.g. 'demo-user-1').")
    {
    }

    public override bool IsValid(object? value) => value is null || (value is string s && ActorIdPattern().IsMatch(s));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$")]
    private static partial Regex ActorIdPattern();
}
