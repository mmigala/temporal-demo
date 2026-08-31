using RabbitMQ.Client;
using Temporalio.Client;
using TemporalShowcase.Contracts;
using TemporalShowcase.DocumentProcessor.Configuration;
using TemporalShowcase.DocumentProcessor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ProcessingOptions>(builder.Configuration.GetSection(ProcessingOptions.SectionName));
builder.Services.AddSingleton<ProcessingStatusStore>();
builder.Services.AddHealthChecks();

var rabbitMqOptions = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
var temporalHost = builder.Configuration["Temporal:Host"] ?? "temporal:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

var startupLogger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Startup");

var rabbitMqConnection = await ConnectWithRetryAsync(
    async () =>
    {
        var factory = new ConnectionFactory
        {
            HostName = rabbitMqOptions.HostName,
            Port = rabbitMqOptions.Port,
            UserName = rabbitMqOptions.UserName,
            Password = rabbitMqOptions.Password,
        };
        return await factory.CreateConnectionAsync();
    },
    startupLogger,
    "RabbitMQ",
    CancellationToken.None);
builder.Services.AddSingleton(rabbitMqConnection);

var temporalClient = await ConnectWithRetryAsync(
    () => TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost) { Namespace = temporalNamespace }),
    startupLogger,
    "Temporal",
    CancellationToken.None);
builder.Services.AddSingleton<ITemporalClient>(temporalClient);

builder.Services.AddHostedService<RabbitMqConsumerService>();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/api/internal/documents/{documentId}/status", (string documentId, ProcessingStatusStore store) =>
    store.TryGetStatus(documentId, out var status)
        ? Results.Ok(status)
        : Results.NotFound());

app.Run();

static async Task<T> ConnectWithRetryAsync<T>(Func<Task<T>> connect, ILogger logger, string dependencyName, CancellationToken cancellationToken)
{
    var delay = TimeSpan.FromSeconds(2);
    while (true)
    {
        try
        {
            var result = await connect();
            logger.LogInformation("Connected to {Dependency}", dependencyName);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to {Dependency}, retrying in {Delay}", dependencyName, delay);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
}

/// <summary>Entry point marker so integration tests can bootstrap this API via WebApplicationFactory.</summary>
public partial class Program
{
}
