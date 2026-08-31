using Temporalio.Client;
using TemporalShowcase.Api.Endpoints;
using TemporalShowcase.Api.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Temporal Showcase API", Version = "v1" });
});

var temporalHost = builder.Configuration["Temporal:Host"] ?? "temporal:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

var startupLogger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Startup");

var temporalClient = await RetryingConnection.ConnectWithRetryAsync(
    () => TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost) { Namespace = temporalNamespace }),
    startupLogger,
    "Temporal",
    CancellationToken.None);
builder.Services.AddSingleton<ITemporalClient>(temporalClient);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapDocumentEndpoints();

app.Run();

/// <summary>Entry point marker so integration tests can bootstrap this API via WebApplicationFactory.</summary>
public partial class Program
{
}
