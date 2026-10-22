using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using RequestManagement.Api.Configuration;
using RequestManagement.Api.Data;
using RequestManagement.Api.ErrorHandling;
using RequestManagement.Api.Services;
using RequestManagement.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// --- MVC, JSON contract, validation -------------------------------------------------------------
builder.Services
    .AddControllers(options =>
    {
        options.ModelBinderProviders.Insert(0, new StrictQueryModelBinderProvider());
        // Validation error keys use the JSON (camelCase) names, e.g. "items[0].expectedVersion".
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        // Required-ness is declared explicitly with [Required]; avoids a redundant "body field is required" on bad JSON.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        // Enums travel as names ("InProgress"); numeric enum values and numbers-as-strings are rejected with 400.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        // Malformed JSON yields a field-level 400 without echoing serializer/type details.
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ProblemResults.InvalidModelState);

// The OpenAPI generator reads the minimal-API JSON options; keep them identical so the documented enums match.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// --- Data access ------------------------------------------------------------------------------
// A factory (singleton) instead of a request-scoped context: every unit of work, including each bulk item,
// gets its own short-lived DbContext. The connection string is resolved lazily from final configuration.
builder.Services.AddDbContextFactory<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("RequestManagement")
        ?? throw new InvalidOperationException(
            "Connection string 'RequestManagement' is not configured. See README § Configuration.");
    options.UseSqlServer(connectionString);
});

// --- Application services ---------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RequestSummaryService>(); // owns the shared cache generation counter
builder.Services.AddScoped<RequestQueryService>();
builder.Services.AddScoped<RequestStatusService>();
builder.Services.AddOptions<DemoIdentityOptions>().BindConfiguration(DemoIdentityOptions.SectionName);

// --- Cross-cutting ----------------------------------------------------------------------------
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "PATCH")
    .WithHeaders("Content-Type", DemoIdentityOptions.HeaderName)));

// Access log: method, path, status and duration only — no headers, query strings or bodies.
builder.Services.AddHttpLogging(options => options.LoggingFields =
    HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration);
builder.Services.AddHealthChecks();

builder.Services.AddOpenApi(OpenApiConfiguration.Configure);

var app = builder.Build();

if (DemoCommands.IsCommand(args))
{
    return await DemoCommands.RunAsync(app, args);
}

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("RequestManagement")))
{
    app.Logger.LogCritical("Connection string 'RequestManagement' is not configured. See README § Configuration.");
    return 1;
}

if (app.Configuration.GetValue<bool>($"{DemoIdentityOptions.SectionName}:Enabled"))
{
    app.Logger.LogWarning(
        "Demo identity is ENABLED: {Header} is trusted as the actor without authentication. Development/demo use only.",
        DemoIdentityOptions.HeaderName);
}

// --- Pipeline ---------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    // TLS everywhere outside local development.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseHttpLogging();
app.UseCors(FrontendCorsPolicy);

app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    // API description and interactive UI are served by this process, in Development only.
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Request Management API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Request Management API";
        options.EnableTryItOutByDefault();
        options.DisplayRequestDuration();
    });
}

await app.RunAsync();
return 0;

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
