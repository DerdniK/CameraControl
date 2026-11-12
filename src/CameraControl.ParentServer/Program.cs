using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;
using CameraControl.Domain.Entities;
using CameraControl.Infrastructure.DependencyInjection;
using CameraControl.ParentServer.Workers;

var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(webRoot))
{
    var fallbackWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    if (Directory.Exists(fallbackWebRoot))
    {
        webRoot = fallbackWebRoot;
    }
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRoot
});

builder.Services.AddCameraControlParentInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AutoCapturePollingWorker>();

var app = builder.Build();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        ctx.Context.Response.Headers.Pragma = "no-cache";
        ctx.Context.Response.Headers.Expires = "0";
    }
});

app.MapGet("/", () => Results.Redirect("/dashboard/index.html"));
app.MapGet("/index.html", () => Results.Redirect("/dashboard/index.html"));
app.MapGet("/dashboard", () => Results.Redirect("/dashboard/index.html"));
app.MapGet("/history", () => Results.Redirect("/history/index.html"));
app.MapGet("/history.html", () => Results.Redirect("/history/index.html"));

app.MapGet("/openapi/v1.json", () => Results.Ok(new
{
    openapi = "3.0.1",
    info = new
    {
        title = "CameraControl Parent Orchestrator API",
        version = "1.0.0",
        description = "API REST Cliente-Servidor (Onion Architecture) para crear servidores hijos (10.x.x.x / 127.x.x.x) y solicitar capturas JSON de cámaras."
    },
    paths = new Dictionary<string, object>
    {
        ["/api/parent/summary"] = new { get = new { summary = "Resumen general del Servidor Padre, IPs e Hijos activos" } },
        ["/api/parent/nodes"] = new
        {
            get = new { summary = "Listar todos los servidores hijos" },
            post = new { summary = "Crear y aprovisionar dinámicamente un servidor hijo con IP 10.x.x.x o 127.x.x.x" }
        },
        ["/api/parent/nodes/{id}/capture"] = new { post = new { summary = "Solicitar a un servidor hijo que tome una foto y regrese el JSON" } },
        ["/api/parent/nodes/capture-all"] = new { post = new { summary = "Solicitar foto y JSON a todos los servidores hijos simultáneamente" } },
        ["/api/parent/captures"] = new { get = new { summary = "Consultar historial de capturas JSON recibidas por el Padre" } },
        ["/api/parent/polling"] = new
        {
            get = new { summary = "Consultar estado del worker de poleo automático" },
            put = new { summary = "Activar/desactivar o cambiar intervalo del poleo automático" }
        },
        ["/api/parent/callbacks/register"] = new { post = new { summary = "Callback usado por los servidores hijos para registrarse ante la IP del Padre" } }
    }
}));

var parentApi = app.MapGroup("/api/parent");

parentApi.MapGet("/summary", async (IParentOrchestratorService orchestrator, CancellationToken ct) =>
{
    var summary = await orchestrator.GetSystemSummaryAsync(ct);
    return Results.Ok(summary);
})
.WithName("GetParentSystemSummary");

parentApi.MapGet("/nodes", async (IParentOrchestratorService orchestrator, CancellationToken ct) =>
{
    var nodes = await orchestrator.GetAllNodesAsync(ct);
    return Results.Ok(nodes);
})
.WithName("GetAllChildNodes");

parentApi.MapGet("/nodes/{id:guid}", async (Guid id, IParentOrchestratorService orchestrator, CancellationToken ct) =>
{
    var node = await orchestrator.GetNodeByIdAsync(id, ct);
    return node is null ? Results.NotFound(new { error = "Servidor hijo no encontrado." }) : Results.Ok(node);
})
.WithName("GetChildNodeById");

parentApi.MapPost("/nodes", async (
    CreateChildNodeRequest request,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    try
    {
        var created = await orchestrator.CreateAndProvisionChildNodeAsync(request, ct);
        return Results.Created($"/api/parent/nodes/{created.Id}", created);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
})
.WithName("CreateAndProvisionChildNode");

parentApi.MapDelete("/nodes/{id:guid}", async (
    Guid id,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    var removed = await orchestrator.RemoveChildNodeAsync(id, ct);
    return removed
        ? Results.Ok(new { message = "Servidor hijo detenido y eliminado correctamente.", id })
        : Results.NotFound(new { error = "Servidor hijo no encontrado." });
})
.WithName("RemoveChildNode");

parentApi.MapPost("/nodes/{id:guid}/capture", async (
    Guid id,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    try
    {
        var capture = await orchestrator.TriggerCaptureOnChildAsync(id, "SolicitudManual", ct);
        return Results.Ok(capture);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Error al solicitar captura al servidor hijo",
            statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("TriggerCaptureOnChild");

parentApi.MapPost("/nodes/capture-all", async (
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    var captures = await orchestrator.TriggerCaptureOnAllChildrenAsync("SolicitudManual", ct);
    return Results.Ok(new
    {
        totalCaptured = captures.Count,
        timestampUtc = DateTime.UtcNow,
        captures
    });
})
.WithName("TriggerCaptureOnAllChildren");

parentApi.MapGet("/captures", async (
    int? limit,
    Guid? childNodeId,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    var captures = await orchestrator.GetRecentCapturesAsync(limit ?? 30, childNodeId, ct);
    return Results.Ok(captures);
})
.WithName("GetRecentCaptures");

parentApi.MapGet("/polling", (IAutoPollingStateService pollingState) =>
    Results.Ok(pollingState.GetStatus()))
.WithName("GetPollingStatus");

parentApi.MapPut("/polling", (UpdatePollingRequest request, IAutoPollingStateService pollingState) =>
{
    var updated = pollingState.UpdateConfiguration(request.Enabled, request.IntervalSeconds);
    return Results.Ok(updated);
})
.WithName("UpdatePollingConfiguration");

parentApi.MapPost("/callbacks/register", async (
    RegisterChildCallbackRequest request,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    try
    {
        var dto = await orchestrator.RegisterOrHeartbeatChildNodeAsync(request, ct);
        return Results.Ok(dto);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("RegisterChildCallback");

parentApi.MapPost("/callbacks/capture", async (
    CameraCaptureResult capture,
    IParentOrchestratorService orchestrator,
    CancellationToken ct) =>
{
    await orchestrator.ReceiveChildCaptureCallbackAsync(capture, ct);
    return Results.Accepted();
})
.WithName("ReceiveChildCaptureCallback");

app.Run();
