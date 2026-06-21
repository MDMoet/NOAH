using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Api.Interfaces;
using Api.Interfaces.Providers;
using Api.Options;
using Api.Services;
using Api.Services.Providers;
using Application.Configuration;
using Application.Interfaces;
using Application.Services;
using Infrastructure.AI;
using Infrastructure.Repository;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NOAH.Api.Authentication;
using NOAH.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
NoahAuthenticationOptions authenticationOptions =
    builder.Configuration.GetSection(NoahAuthenticationOptions.SectionName).Get<NoahAuthenticationOptions>() ??
    new NoahAuthenticationOptions();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IRequestConnectionStringResolver, RequestConnectionStringResolver>();

builder.Services.AddDbContext<NoahDbContext>((serviceProvider, options) =>
{
    IRequestConnectionStringResolver connectionStringResolver =
        serviceProvider.GetRequiredService<IRequestConnectionStringResolver>();

    options.UseSqlServer(connectionStringResolver.ResolveNoahConnectionString());
});

builder.Services.AddDbContext<DemoAuthenticationDbContext>((serviceProvider, options) =>
{
    IRequestConnectionStringResolver connectionStringResolver =
        serviceProvider.GetRequiredService<IRequestConnectionStringResolver>();

    options.UseSqlServer(connectionStringResolver.ResolveDemoAuthenticationConnectionString());
});

builder.Services.Configure<OpenStreetMapModel>(
    builder.Configuration.GetSection(OpenStreetMapModel.SectionName));
builder.Services.Configure<PlanningModel>(
    builder.Configuration.GetSection(PlanningModel.SectionName));
builder.Services.Configure<LlmOptions>(
    builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<NoahAuthenticationOptions>(
    builder.Configuration.GetSection(NoahAuthenticationOptions.SectionName));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "NoahHybrid";
        options.DefaultChallengeScheme = "NoahHybrid";
    })
    .AddPolicyScheme("NoahHybrid", "NOAH API authentication", options =>
    {
        options.ForwardDefaultSelector = httpContext =>
        {
            string authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

            if (!string.IsNullOrWhiteSpace(authorizationHeader) &&
                authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return "ApiKey";
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authenticationOptions.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = authenticationOptions.Jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authenticationOptions.Jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("NoahHybrid")
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddHttpClient<IPlacesProvider, OverpassPlacesProvider>();
builder.Services.AddHttpClient<IGeocodingProvider, NominatimGeocodingProvider>();
builder.Services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>();

// Assistant-specific infrastructure lives beside the existing feature services so the API can
// route, plan, execute, and observe assistant requests end to end.
builder.Services.AddScoped<INotesService, NotesService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<ILocationsService, LocationsService>();
builder.Services.AddScoped<IRemindersService, RemindersService>();
builder.Services.AddScoped<IPlanningService, PlanningService>();
builder.Services.AddScoped<IMileageService, MileageService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IAssistantService, AssistantService>();
builder.Services.AddScoped<IAssistantSettingsService, AssistantSettingsService>();
builder.Services.AddScoped<IAssistantChatService, AssistantChatService>();
builder.Services.AddScoped<IAssistantMemoryService, AssistantMemoryService>();
builder.Services.AddScoped<Application.Interfaces.IAuthenticationService, DemoUserAuthenticationService>();
builder.Services.AddScoped<IAssistantPromptBuilder, AssistantPromptBuilder>();
builder.Services.AddScoped<IAssistantToolService, AssistantToolService>();
builder.Services.AddScoped<IAssistantInteractionRepository, AssistantInteractionRepository>();
builder.Services.AddSingleton<IAssistantModelProcessManager, AssistantModelProcessManager>();
builder.Services.AddSingleton<IAssistantModelRouter, AssistantModelRouter>();
builder.Services.AddHostedService<AssistantModelLifecycleService>();
builder.Services.AddHealthChecks()
    .AddCheck<LlmHealthCheck>("assistant_llm");

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
// Expose a dedicated assistant health surface so model/process state can be checked without
// sending a normal assistant message through the full pipeline.
app.MapHealthChecks("/api/assistant/health", new HealthCheckOptions
{
    ResponseWriter = async (httpContext, healthReport) =>
    {
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(new
        {
            status = healthReport.Status.ToString(),
            totalDuration = healthReport.TotalDuration,
            checks = healthReport.Entries.ToDictionary(
                healthCheckEntry => healthCheckEntry.Key,
                healthCheckEntry => new
                {
                    status = healthCheckEntry.Value.Status.ToString(),
                    healthCheckEntry.Value.Description,
                    healthCheckEntry.Value.Data
                })
        });
    }
});

app.Run();
