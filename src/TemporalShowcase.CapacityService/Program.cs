using TemporalShowcase.CapacityService.Endpoints;
using TemporalShowcase.CapacityService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ReservationStore>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapCapacityEndpoints();

app.Run();

/// <summary>Entry point marker so integration tests can bootstrap this API via WebApplicationFactory.</summary>
public partial class Program
{
}
