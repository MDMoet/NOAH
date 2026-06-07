using Api.Interfaces;
using Api.Interfaces.Providers;
using Api.Options;
using Api.Services;
using Api.Services.Providers;
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

builder.Services.AddHttpClient<IPlacesProvider, OverpassPlacesProvider>();
builder.Services.AddHttpClient<IGeocodingProvider, NominatimGeocodingProvider>();

builder.Services.AddScoped<INotesService, NotesService>();
builder.Services.AddScoped<ITasksService, TasksService>();
builder.Services.AddScoped<ILocationsService, LocationsService>();
builder.Services.AddScoped<IRemindersService, RemindersService>();

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
