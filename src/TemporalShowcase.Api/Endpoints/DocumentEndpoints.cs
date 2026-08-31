using Temporalio.Client;
using Temporalio.Exceptions;
using TemporalShowcase.Application.Configuration;
using TemporalShowcase.Application.Workflows;
using TemporalShowcase.Contracts;

namespace TemporalShowcase.Api.Endpoints;

/// <summary>Maps the HTTP endpoints for starting, querying, and cancelling document-processing workflows.</summary>
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents", StartAsync)
            .WithName("StartDocumentProcessing")
            .WithSummary("Starts a new document-processing workflow.")
            .WithTags("Documents")
            .Produces<StartDocumentProcessingResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        app.MapGet("/api/documents/{workflowId}", GetStateAsync)
            .WithName("GetDocumentProcessingState")
            .WithSummary("Gets the current state of a document-processing workflow.")
            .WithTags("Documents")
            .Produces<DocumentProcessingState>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/documents/{workflowId}/cancel", CancelAsync)
            .WithName("CancelDocumentProcessing")
            .WithSummary("Requests cancellation of a document-processing workflow.")
            .WithTags("Documents")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> StartAsync(StartDocumentProcessingRequest request, ITemporalClient client)
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
    }

    private static async Task<IResult> GetStateAsync(string workflowId, ITemporalClient client)
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
    }

    private static async Task<IResult> CancelAsync(string workflowId, ITemporalClient client)
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
    }
}
