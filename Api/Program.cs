using System.Text.Json.Serialization;
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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NOAH.Api.Middleware;
using NOAH.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<NoahDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<OpenStreetMapModel>(
    builder.Configuration.GetSection(OpenStreetMapModel.SectionName));
builder.Services.Configure<PlanningModel>(
    builder.Configuration.GetSection(PlanningModel.SectionName));
builder.Services.Configure<LlmOptions>(
    builder.Configuration.GetSection(LlmOptions.SectionName));

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

app.UseMiddleware<ApiKeyMiddleware>();

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
