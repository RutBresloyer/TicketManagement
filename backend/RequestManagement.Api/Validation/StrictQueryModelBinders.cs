using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RequestManagement.Api.Validation;

/// <summary>
/// Replaces MVC's default binders for enum and <see cref="DateTimeOffset"/> values coming from the query string, route or headers.
/// </summary>
/// <remarks>
/// The defaults are too lenient for a public contract: the enum binder accepts numbers (<c>status=2</c>) and comma lists
/// (<c>status=New,InProgress</c> silently becomes the numeric OR of the two, i.e. <c>Waiting</c>), and the
/// <see cref="DateTimeOffset"/> binder interprets a value without an offset in the server's local time zone.
/// JSON bodies are not affected; they are handled by System.Text.Json with integer enum values disabled.
/// </remarks>
public sealed class StrictQueryModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        if (type.IsEnum)
        {
            return new StrictEnumModelBinder(type);
        }

        return type == typeof(DateTimeOffset) ? new UtcTimestampModelBinder() : null;
    }
}

/// <summary>Accepts only declared enum names (or their <see cref="JsonStringEnumMemberNameAttribute"/> names), case-insensitively.</summary>
public sealed class StrictEnumModelBinder : IModelBinder
{
    private readonly Dictionary<string, object> _valuesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _allowed;

    public StrictEnumModelBinder(Type enumType)
    {
        var publicNames = new List<string>();
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = field.GetValue(null)!;
            var publicName = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name;
            _valuesByName[field.Name] = value;
            _valuesByName[publicName] = value;
            publicNames.Add(publicName);
        }

        _allowed = string.Join(", ", publicNames);
    }

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return Task.CompletedTask;
        }

        if (_valuesByName.TryGetValue(raw, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"'{Truncate(raw)}' is not a valid value for {bindingContext.ModelName}. Allowed values: {_allowed}.");
        }

        return Task.CompletedTask;
    }

    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "…";
}

/// <summary>Parses ISO 8601 timestamps with the invariant culture; a value without an offset is treated as UTC.</summary>
public sealed class UtcTimestampModelBinder : IModelBinder
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd",
    ];

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return Task.CompletedTask;
        }

        if (DateTimeOffset.TryParseExact(
                raw,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            bindingContext.Result = ModelBindingResult.Success(timestamp);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"{bindingContext.ModelName} must be an ISO 8601 timestamp, e.g. 2026-01-31T00:00:00Z.");
        }

        return Task.CompletedTask;
    }
}
