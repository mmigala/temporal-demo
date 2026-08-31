using Temporalio.Client;
using Temporalio.Exceptions;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Application.Workflows;
using TemporalShowcase.Contracts;

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

var temporalClient = await ConnectWithRetryAsync(
    () => TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost) { Namespace = temporalNamespace }),
    startupLogger,
    "Temporal",
    CancellationToken.None);
builder.Services.AddSingleton<ITemporalClient>(temporalClient);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");

app.MapPost("/api/documents", async (StartDocumentProcessingRequest request, ITemporalClient client) =>
{
    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.FileName)] = ["File name is required."],
        });
    }

    var documentId = Guid.NewGuid().ToString("n");
    var workflowId = TemporalConstants.BuildWorkflowId(documentId);
    var input = new DocumentProcessingInput(
        documentId,
        request.FileName,
        new SimulationOptions
        {
            SimulateCapacityFailures = request.SimulateCapacityFailures,
            SimulateProcessingFailure = request.SimulateProcessingFailure,
            SimulateLostCompletionSignal = request.SimulateLostCompletionSignal,
        });

    try
    {
        await client.StartWorkflowAsync(
            (DocumentProcessingWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(id: workflowId, taskQueue: TemporalConstants.TaskQueue));
    }
    catch (WorkflowAlreadyStartedException)
    {
        return Results.Conflict(new { message = $"A workflow with ID '{workflowId}' already exists." });
    }

    var statusUrl = $"/api/documents/{workflowId}";
    return Results.Accepted(statusUrl, new StartDocumentProcessingResponse(workflowId, documentId, statusUrl));
})
.WithName("StartDocumentProcessing")
.WithSummary("Starts a new document-processing workflow.")
.WithTags("Documents")
.Produces<StartDocumentProcessingResponse>(StatusCodes.Status202Accepted)
.ProducesValidationProblem()
.Produces(StatusCodes.Status409Conflict);

app.MapGet("/api/documents/{workflowId}", async (string workflowId, ITemporalClient client) =>
{
    try
    {
        var handle = client.GetWorkflowHandle<DocumentProcessingWorkflow>(workflowId);
        var state = await handle.QueryAsync(wf => wf.GetState());
        return Results.Ok(state);
    }
    catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
    {
        return Results.NotFound();
    }
})
.WithName("GetDocumentProcessingState")
.WithSummary("Gets the current state of a document-processing workflow.")
.WithTags("Documents")
.Produces<DocumentProcessingState>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/documents/{workflowId}/cancel", async (string workflowId, ITemporalClient client) =>
{
    try
    {
        var handle = client.GetWorkflowHandle(workflowId);
        await handle.CancelAsync();
        return Results.Accepted();
    }
    catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
    {
        return Results.NotFound();
    }
})
.WithName("CancelDocumentProcessing")
.WithSummary("Requests cancellation of a document-processing workflow.")
.WithTags("Documents")
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status404NotFound);

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
