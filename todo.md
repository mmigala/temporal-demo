A small document-processing workflow is a good showcase because it demonstrates Temporal without fake financial examples. The API starts an orchestration, HTTP activities simulate distributed services, RabbitMQ handles asynchronous work, failures trigger Temporal retries, and reconciliation handles missing messages. Temporal’s .NET SDK supports workflows, activities, workers, signals, timers, retries, testing, and dependency injection, while RabbitMQ acknowledgements and publisher confirms illustrate messaging reliability.

Below is a complete implementation plan you can copy into todo.md.

# Temporal .NET Showcase

## Objective

Build a small, educational .NET application running entirely through Docker Compose.

The application must demonstrate the fundamental Temporal concepts to a developer who has never used Temporal:

- Workflows
- Activities
- Workers and task queues
- Durable execution
- Activity retries and backoff
- Activity timeouts
- Workflow signals
- Durable timers
- Waiting for external events
- RabbitMQ messaging
- HTTP communication between services
- Idempotency
- Reconciliation
- Compensation
- Worker restart recovery
- Temporal Web UI and workflow event history
- Automated workflow and activity testing

Keep the implementation intentionally small and readable. This is a learning project, not a production-ready framework.

---

# Business scenario

Implement a document-processing workflow.

A client submits a document-processing request through an HTTP API.

The workflow performs these steps:

1. Validate the request.
2. Reserve processing capacity through an HTTP service.
3. Publish a `ProcessDocument` command to RabbitMQ.
4. Wait for the processor to complete the document.
5. Receive completion through a Temporal signal.
6. If the completion signal does not arrive in time, query the processor status through HTTP.
7. Complete the workflow when processing succeeds.
8. Release the reserved capacity if processing permanently fails.

The workflow must survive API, worker, RabbitMQ, and processor restarts without losing its state.

No real document file needs to be uploaded. A document can be represented by an ID and filename.

---

# Solution structure

Create the following solution:

```text
TemporalShowcase.sln

src/
  TemporalShowcase.Api/
  TemporalShowcase.Worker/
  TemporalShowcase.CapacityService/
  TemporalShowcase.DocumentProcessor/
  TemporalShowcase.Contracts/
  TemporalShowcase.Application/

tests/
  TemporalShowcase.Application.Tests/
  TemporalShowcase.IntegrationTests/

docker-compose.yml
Directory.Build.props
Directory.Packages.props
README.md

Project responsibilities
TemporalShowcase.Api

ASP.NET Core API responsible only for interacting with Temporal.

Endpoints:

POST /api/documents
GET /api/documents/{workflowId}
POST /api/documents/{workflowId}/cancel

The API must not execute orchestration logic itself.

TemporalShowcase.Worker

.NET Worker Service hosting the Temporal worker.

Responsibilities:

Register workflows.
Register activities.
Connect to the Temporal server.
Listen on the document-processing task queue.
TemporalShowcase.CapacityService

Small ASP.NET Core service simulating an external dependency.

Responsibilities:

Reserve processing capacity.
Release processing capacity.
Simulate transient HTTP failures.
Make reserve and release operations idempotent.
TemporalShowcase.DocumentProcessor

.NET Worker Service consuming RabbitMQ commands.

Responsibilities:

Consume ProcessDocument commands.
Simulate document processing.
Store processing status in memory.
Signal the corresponding Temporal workflow when processing finishes.
Expose an HTTP endpoint used for reconciliation.
Support configurable failure and message-loss simulations.
TemporalShowcase.Contracts

Shared transport contracts only:

API request and response DTOs
RabbitMQ messages
HTTP service contracts
Workflow input and result records

Do not place business logic in this project.

TemporalShowcase.Application

Temporal implementation:

Workflow interfaces
Workflow implementations
Activity interfaces
Activity implementations
Retry and timeout configuration
Task queue constants
Domain-oriented status and result types
Technical constraints
Use the latest stable .NET LTS target available in the environment.
Enable nullable reference types.
Enable implicit usings.
Treat warnings as errors.
Use primary constructors where appropriate.
Use central package management.
Use async APIs throughout.
Pass CancellationToken where supported outside workflow code.
Use System.Text.Json.
Use the official Temporal .NET SDK.
Use the official RabbitMQ .NET client unless a small abstraction is necessary.
Use NUnit for all tests.
Use Assert.That assertions.
Use the test naming convention:
NameOfTheTestedMethod_ConditionThatIsBeingTested_ExpectedResult


Avoid introducing:

MediatR
EF Core
A database
Generic repositories
AutoMapper
Unnecessary abstractions
A frontend
Authentication or authorization

The purpose is to demonstrate Temporal, not supporting infrastructure.

Domain contracts

Create immutable records similar to:

StartDocumentProcessingRequest
- FileName
- SimulateCapacityFailures
- SimulateProcessingFailure
- SimulateLostCompletionSignal

DocumentProcessingInput
- DocumentId
- FileName
- SimulationOptions

DocumentProcessingResult
- DocumentId
- Status
- ProcessingAttemptCount
- ReconciliationUsed
- StartedAt
- CompletedAt

ProcessDocumentCommand
- MessageId
- WorkflowId
- DocumentId
- FileName
- SimulateProcessingFailure
- SimulateLostCompletionSignal

DocumentProcessedSignal
- DocumentId
- Succeeded
- Error

ProcessingStatusResponse
- DocumentId
- Status
- Error


Use strings or enums for statuses, but keep serialization compatibility in mind.

Expected workflow statuses:

Pending
ReservingCapacity
ProcessingRequested
WaitingForCompletion
Reconciling
Compensating
Completed
Failed
Cancelled
Temporal workflow

Create a workflow named DocumentProcessingWorkflow.

Use a stable workflow type name and the task queue:

document-processing

Workflow ID

The API must generate:

document-processing-{documentId}


Starting the same document twice must not create two independent workflows.

Return an appropriate HTTP conflict response if a workflow with the same ID already exists.

Workflow state

The workflow must expose a query returning its current state:

DocumentProcessingState
- DocumentId
- FileName
- Status
- CurrentStep
- ProcessingAttemptCount
- ReconciliationUsed
- LastError

Workflow algorithm

Implement the following orchestration:

Set state to ReservingCapacity.
Execute ReserveCapacityActivity.
Set state to ProcessingRequested.
Execute PublishProcessDocumentCommandActivity.
Set state to WaitingForCompletion.
Wait for a DocumentProcessedSignal.
Use a durable Temporal timeout while waiting.
If the signal arrives:
Validate that it belongs to the expected document.
Complete when successful.
Fail when the processor reports a permanent failure.
If the signal does not arrive before the timeout:
Set state to Reconciling.
Execute GetDocumentProcessingStatusActivity.
Complete if HTTP status reports successful processing.
Fail if HTTP status reports permanent failure.
If processing is still pending, wait using a durable timer and reconcile again.
Limit reconciliation to a clearly defined number of attempts so the demo completes predictably.
If processing fails permanently after capacity was reserved:
Set state to Compensating.
Execute ReleaseCapacityActivity.
Return a final result or throw a meaningful workflow failure.
Handle cancellation and release capacity when necessary.

Do not catch every exception and convert it into a boolean. Preserve useful Temporal failure information.

Workflow determinism

Workflow code must remain deterministic.

Do not use the following directly inside workflow code:

DateTime.UtcNow
Guid.NewGuid()
Task.Delay
Random values
HTTP clients
RabbitMQ clients
File system access
Environment variables
Database access
Mutable global state

Use Temporal workflow APIs for:

Current time
Durable timers
Waiting for conditions
Activities
Signals
Queries

All external side effects must happen inside activities.

Add comments explaining why workflow determinism matters and how Temporal replays workflow history.

Activities
ReserveCapacityActivity

Call the capacity service through HTTP.

Requirements:

Use an idempotency key based on workflowId.
Configure an activity retry policy.
Configure start-to-close timeout.
Retry transient HTTP failures.
Do not retry validation failures or HTTP 4xx responses representing permanent errors.
Log the Temporal activity attempt number.

Suggested demo retry policy:

Initial interval: 1 second
Backoff coefficient: 2
Maximum interval: 5 seconds
Maximum attempts: 5

PublishProcessDocumentCommandActivity

Publish a durable RabbitMQ message.

Requirements:

Use a durable exchange and queue.
Mark messages as persistent.
Use workflowId as correlation ID.
Use a stable message ID.
Enable publisher confirmations.
Treat an unconfirmed publication as an activity failure.
Make repeated publication safe.

Because an activity may execute more than once, duplicate RabbitMQ messages must be expected.

GetDocumentProcessingStatusActivity

Call:

GET /api/internal/documents/{documentId}/status


Requirements:

Return Pending, Completed, or Failed.
Retry transient HTTP failures.
Treat Not Found as Pending during the demo.
Use a short timeout.
ReleaseCapacityActivity

Call the capacity service through HTTP.

Requirements:

Make release idempotent.
Retrying release must be safe.
Use a separate retry policy.
Log whether capacity was released or was already absent.
RabbitMQ processing

Create:

Exchange: document-processing
Queue: document-processing.commands
Routing key: document.process


The processor must:

Consume using manual acknowledgements.
Deserialize and validate the message.
Check whether MessageId was already processed.
Ignore duplicate commands safely.
Simulate processing with a configurable delay.
Store the result in an in-memory concurrent collection.
Signal the Temporal workflow through SignalAsync.
Acknowledge the RabbitMQ message only after local processing state is stored.
Reject invalid messages without infinite requeueing.
Requeue transient failures only in a controlled way.

For this educational application, in-memory deduplication is acceptable, but the README must explain that production systems require durable idempotency storage.

Signal handling

Add a workflow signal named similarly to:

DocumentProcessedAsync


Signal requirements:

Accept DocumentProcessedSignal.
Ignore signals for another document ID.
Make duplicate signals harmless.
Do not overwrite a completed result.
Store enough state for the workflow waiting condition to continue.

The processor calls Temporal using the workflow ID from the RabbitMQ message.

Reconciliation demonstration

Reconciliation is a required feature.

Support this scenario:

The processor successfully processes the document.
The processor stores status as Completed.
The processor intentionally does not send the Temporal signal.
The workflow timeout expires.
The workflow queries the processor through HTTP.
The workflow discovers that processing completed.
The workflow completes successfully.

Expose this behavior using:

{
  "fileName": "example.pdf",
  "simulateLostCompletionSignal": true
}


Keep the default timeout short, for example 10 seconds, so the scenario is easy to demonstrate.

Explain in the README that reconciliation protects against missed callbacks, messages, or external state changes, but Temporal already provides durable workflow state.

Failure simulation

Support deterministic demo scenarios through the initial API request.

Normal execution
{
  "fileName": "example.pdf"
}


Expected result:

Capacity is reserved.
RabbitMQ command is published.
Processor completes.
Signal reaches the workflow.
Workflow completes.
Activity retries
{
  "fileName": "retry-example.pdf",
  "simulateCapacityFailures": 3
}


Expected result:

Capacity service fails three times with HTTP 503.
Temporal retries the activity.
The fourth attempt succeeds.
Workflow completes.
Reconciliation
{
  "fileName": "reconciliation-example.pdf",
  "simulateLostCompletionSignal": true
}


Expected result:

Processor completes.
Completion signal is intentionally skipped.
Workflow timeout expires.
Workflow reconciles through HTTP.
Workflow completes.
Permanent processing failure
{
  "fileName": "failed-example.pdf",
  "simulateProcessingFailure": true
}


Expected result:

Processor reports a permanent failure.
Workflow releases previously reserved capacity.
Workflow finishes as failed.
Event history shows compensation.
HTTP API
Start workflow
POST /api/documents


Return 202 Accepted.

Example response:

{
  "workflowId": "document-processing-...",
  "documentId": "...",
  "statusUrl": "/api/documents/document-processing-..."
}


Follow REST best practices:

Use 202 Accepted because processing is asynchronous.
Include a Location header pointing to the status endpoint.
Validate inputs.
Use Problem Details for errors.
Return 409 Conflict when the workflow ID already exists.
Query workflow
GET /api/documents/{workflowId}


Query the workflow state through Temporal.

Return a representation containing:

Workflow ID
Document ID
Current status
Current step
Retry or processing attempt information
Whether reconciliation was used
Last error
Final result when available

Return 404 Not Found if the workflow does not exist.

Cancel workflow
POST /api/documents/{workflowId}/cancel


Request Temporal workflow cancellation.

Return:

202 Accepted when cancellation is requested.
404 Not Found when the workflow does not exist.
An appropriate response when the workflow is already closed.
Capacity service

Implement:

POST /api/internal/capacity/reservations
DELETE /api/internal/capacity/reservations/{workflowId}


Maintain reservations in a thread-safe in-memory collection.

Reservation creation must be idempotent:

First request creates the reservation.
Repeated requests with the same workflow ID return the existing reservation.
Repeated delete requests succeed.

For failure simulation, track the number of requests for each workflow and return HTTP 503 for the configured number of initial attempts.

Document processor status API

Implement:

GET /api/internal/documents/{documentId}/status


Return the current in-memory processing status.

The RabbitMQ consumer and HTTP endpoint must share the same singleton status store.

Docker Compose

Create a root docker-compose.yml containing:

Temporal server
Temporal Web UI
Temporal persistence database
RabbitMQ with management UI
TemporalShowcase.Api
TemporalShowcase.Worker
TemporalShowcase.CapacityService
TemporalShowcase.DocumentProcessor

Use current supported container images and pin explicit versions. Do not use latest.

Expose:

API:                  http://localhost:8080
Temporal UI:          http://localhost:8088
RabbitMQ Management:  http://localhost:15672


Add health checks for:

Temporal
RabbitMQ
API
Capacity service
Document processor

Use depends_on with health conditions where supported, but also implement connection retries because startup ordering does not guarantee service readiness.

Place all services on one Docker network.

Use environment variables for:

Temporal address
Temporal namespace
Temporal task queue
RabbitMQ host
RabbitMQ username
RabbitMQ password
Capacity service base URL
Processor service base URL
Reconciliation timeout
Processing delay

Do not place real secrets in source control. Demo credentials may be clearly marked as local-development-only.

Logging and observability

Use structured logging.

Every relevant log entry should include where available:

WorkflowId
RunId
DocumentId
Activity name
Activity attempt
RabbitMQ MessageId
CorrelationId

Add logs for:

Workflow start
Activity start and completion
Activity retry attempts
Command publication
Command consumption
Duplicate command detection
Signal delivery
Reconciliation start and result
Compensation
Workflow completion or failure

Avoid logging entire serialized message bodies unnecessarily.

The README must explain how the same execution can be inspected through the Temporal UI event history.

Automated tests

Use NUnit and Assert.That.

Test names must follow:

NameOfTheTestedMethod_ConditionThatIsBeingTested_ExpectedResult


Use Temporal's workflow testing facilities where appropriate.

Workflow tests

Cover at least:

RunAsync_AllOperationsSucceed_ReturnsCompletedResult
RunAsync_CapacityActivityFailsTransiently_RetriesAndCompletes
RunAsync_CompletionSignalIsReceived_CompletesWithoutReconciliation
RunAsync_CompletionSignalIsDuplicated_CompletesOnlyOnce
RunAsync_SignalContainsDifferentDocumentId_IgnoresSignal
RunAsync_SignalIsNotReceived_UsesReconciliation
RunAsync_ReconciliationReportsCompleted_ReturnsCompletedResult
RunAsync_ReconciliationReportsPending_ReconcilesAgain
RunAsync_ReconciliationNeverCompletes_ReleasesCapacityAndFails
RunAsync_ProcessingReportsPermanentFailure_ReleasesCapacityAndFails
RunAsync_CancelledAfterCapacityReservation_ReleasesCapacity
RunAsync_CancelledBeforeCapacityReservation_DoesNotReleaseCapacity
RunAsync_ReleaseCapacityFailsTransiently_RetriesCompensation

Tests must use shortened or skipped workflow time where supported. Do not make tests wait for real ten-second timers.

Activity tests

Cover at least:

Successful HTTP requests
Transient HTTP failures
Permanent HTTP failures
Idempotency headers
RabbitMQ message serialization
Stable message and correlation IDs
Publisher confirmation failure
Cancellation propagation
Processor tests

Cover at least:

Valid message is processed and acknowledged
Invalid message is rejected
Duplicate message is not processed twice
Successful processing sends a Temporal signal
Lost-signal simulation stores completion without sending a signal
Transient processing error does not incorrectly acknowledge the message
Permanent processing failure is stored and signalled
API tests

Cover at least:

Valid request returns 202 Accepted
Location header is returned
Invalid request returns validation Problem Details
Duplicate workflow returns 409 Conflict
Existing workflow status is returned
Missing workflow returns 404 Not Found
Cancellation requests workflow cancellation

Aim for full coverage of meaningful branches. Do not add tests that only verify constructors or framework behavior.

README

Create a beginner-friendly README.md.

Use simple language and diagrams written in Mermaid.

Required sections
1. What this project demonstrates

Briefly explain:

Temporal is the orchestrator.
RabbitMQ transports asynchronous commands.
HTTP represents synchronous service communication.
The worker executes workflow and activity code.
Temporal stores durable workflow history.
2. Architecture

Include a Mermaid diagram similar to:

flowchart LR
    Client --> API
    API --> Temporal
    Temporal --> Worker
    Worker -->|HTTP| CapacityService
    Worker -->|Publish command| RabbitMQ
    RabbitMQ --> Processor
    Processor -->|Signal| Temporal
    Worker -->|Reconciliation HTTP| Processor

3. Temporal concepts

Explain each concept with references to this application:

Workflow
Activity
Worker
Task queue
Workflow ID
Event history
Replay
Determinism
Retry policy
Timeout
Durable timer
Signal
Query
Cancellation
Compensation
Reconciliation

Clearly explain:

Workflows orchestrate.
Activities perform side effects.
Temporal retries activities, not arbitrary workflow code.
Activity execution is at-least-once, so activities must be idempotent.
Workflow state is rebuilt through deterministic replay.
Temporal does not make external systems transactional.
4. Temporal versus RabbitMQ

Explain that they solve different problems:

Temporal coordinates and durably tracks a long-running process.
RabbitMQ transports messages between components.
RabbitMQ does not provide the complete orchestration state.
Temporal does not replace every messaging use case.
They can be used together.
5. Running the application

Provide exact commands:

docker compose up --build


Explain how to wait for health checks and list all URLs.

6. Running each scenario

Provide ready-to-copy curl or PowerShell examples for:

Successful execution
Activity retries
Missing signal and reconciliation
Permanent processing failure
Cancelling a workflow
7. Inspecting Temporal UI

Explain how to:

Find the workflow by workflow ID.
Inspect event history.
Find activity failures and retry attempts.
Find timers.
Find signals.
Find compensation activities.
Determine whether reconciliation was used.
8. Crash recovery demonstration

Include steps:

Start a workflow with a processing delay.
Stop the Temporal worker container.
Wait a few seconds.
Start the worker again.
Show that execution resumes from its durable history.

Also demonstrate restarting the API does not lose the workflow because the API is not the workflow state owner.

9. Important reliability notes

Explain:

HTTP timeouts can produce ambiguous outcomes.
RabbitMQ delivery can be duplicated.
Publisher confirms and consumer acknowledgements solve different reliability boundaries.
Activities must be idempotent.
The demo uses in-memory stores and is therefore not production-safe.
Production deduplication and external service state require durable storage.
Temporal persistence should use an appropriate production database and deployment model.
Reconciliation should be designed around authoritative external state.
10. Running tests

Provide:

dotnet test

11. Cleanup

Provide:

docker compose down -v

Implementation order

Implement in this order:

Create the solution and projects.
Add central package management and common build settings.
Define contracts and workflow state types.
Implement the basic workflow with mocked activities.
Add workflow tests.
Implement the capacity HTTP service.
Implement capacity activities.
Implement RabbitMQ topology and command publisher.
Implement the RabbitMQ processor.
Implement Temporal signalling from the processor.
Implement processor status HTTP endpoint.
Implement reconciliation in the workflow.
Implement compensation and cancellation.
Implement the API.
Add Dockerfiles.
Add Docker Compose infrastructure.
Add health checks and startup retries.
Add integration tests.
Write the README.
Run formatting, build, tests, and Docker Compose verification.
Definition of done

The implementation is complete when:

dotnet build succeeds without warnings.
dotnet test succeeds.
docker compose up --build starts the entire environment.
A successful workflow can be started through HTTP.
Activity retries are visible in Temporal UI.
RabbitMQ command processing works.
A Temporal signal completes the normal workflow.
Missing-signal simulation completes through reconciliation.
Permanent failure executes compensation.
Cancelling a workflow executes required cleanup.
Restarting the Temporal worker does not lose workflow progress.
Duplicate RabbitMQ delivery does not duplicate document processing.
Repeated activities do not duplicate capacity reservations.
README instructions work from a clean checkout.
Copilot implementation instructions

Work through this plan incrementally.

For every implementation step:

Inspect the existing solution before making changes.
Keep changes focused on the current step.
Build after meaningful changes.
Add or update tests with the implementation.
Use NUnit and Assert.That.
Use full branch-focused test coverage.
Use primary constructors where appropriate.
Follow DDD and REST principles without overengineering.
Keep workflow code deterministic.
Keep all external side effects inside activities.
Make activity operations idempotent.
Do not silently swallow exceptions.
Do not introduce infrastructure that is not required by this plan.
Update the README when commands or behavior change.
Before finishing, run:
dotnet format --verify-no-changes
dotnet build
dotnet test
docker compose config
docker compose up --build -d
docker compose ps

Execute at least one successful workflow and one reconciliation workflow.
Inspect container logs for unhandled exceptions.
Shut down the environment after verification:
docker compose down -v


One important design choice here is that **RabbitMQ is used for asynchronous command delivery, not as a second orchestration engine**. Temporal owns the overall process state, retries and timers, while RabbitMQ demonstrates reliable message communication through confirms and acknowledgements. 【4-a9320c】【3-708160】