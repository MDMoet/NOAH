using Api.Interfaces;
using Api.Interfaces.Providers;
using Api.Options;
using Api.Services;
using Api.Services.Providers;
using Application.Interfaces;
using Application.Services;
using Infrastructure.AI;
using Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;
using NOAH.Api.Middleware;
using NOAH.Infrastructure.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<NoahDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<OpenStreetMapModel>(
    builder.Configuration.GetSection(OpenStreetMapModel.SectionName));
builder.Services.Configure<PlanningModel>(
    builder.Configuration.GetSection(PlanningModel.SectionName));

builder.Services.AddHttpClient<IPlacesProvider, OverpassPlacesProvider>();
builder.Services.AddHttpClient<IGeocodingProvider, NominatimGeocodingProvider>();

builder.Services.AddScoped<INotesService, NotesService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<ILocationsService, LocationsService>();
builder.Services.AddScoped<IRemindersService, RemindersService>();
builder.Services.AddScoped<IPlanningService, PlanningService>();
builder.Services.AddScoped<IMileageService, MileageService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IAssistantService, AssistantService>();
builder.Services.AddScoped<ILlmClient, TestLlmClient>();
builder.Services.AddScoped<IAssistantInteractionRepository, AssistantInteractionRepository>();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

app.Run();
