using RabbitMQ.Client;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using TemporalShowcase.Application.Activities;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Application.Http;
using TemporalShowcase.Application.Messaging;
using TemporalShowcase.Application.Workflows;
using TemporalShowcase.Contracts;

var builder = Host.CreateApplicationBuilder(args);

var rabbitMqOptions = builder.Configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
var temporalHost = builder.Configuration["Temporal:Host"] ?? "temporal:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";
var capacityServiceBaseUrl = builder.Configuration["Services:CapacityServiceBaseUrl"] ?? "http://capacity-service:8080";
var documentProcessorBaseUrl = builder.Configuration["Services:DocumentProcessorBaseUrl"] ?? "http://document-processor:8080";

builder.Services.AddHttpClient(HttpClientNames.CapacityService, client => client.BaseAddress = new Uri(capacityServiceBaseUrl));
builder.Services.AddHttpClient(HttpClientNames.DocumentProcessor, client => client.BaseAddress = new Uri(documentProcessorBaseUrl));
builder.Services.AddSingleton<CapacityServiceClient>();
builder.Services.AddSingleton<DocumentProcessorStatusClient>();
builder.Services.AddSingleton<ProcessDocumentPublisher>();

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

builder.Services
    .AddHostedTemporalWorker(TemporalConstants.TaskQueue)
    .AddScopedActivities<DocumentProcessingActivities>()
    .AddWorkflow<DocumentProcessingWorkflow>();

var host = builder.Build();
await host.RunAsync();

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
