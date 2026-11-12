using System.Net.Http.Json;
using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;
using CameraControl.Application.Services;
using CameraControl.Domain.Ports;
using CameraControl.Infrastructure.Camera;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("ParentCallbackClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});

var nodeIdStr = Environment.GetEnvironmentVariable("NODE_ID") ?? builder.Configuration["ChildSettings:NodeId"];
var nodeId = Guid.TryParse(nodeIdStr, out var parsedId) ? parsedId : Guid.NewGuid();
var nodeName = Environment.GetEnvironmentVariable("NODE_NAME")
               ?? builder.Configuration["ChildSettings:NodeName"]
               ?? $"Camara-Hijo-{Environment.MachineName}";
var assignedIp = Environment.GetEnvironmentVariable("NODE_IP")
                 ?? builder.Configuration["ChildSettings:AssignedIp"]
                 ?? "10.10.0.11";
var portStr = Environment.GetEnvironmentVariable("NODE_PORT")
              ?? builder.Configuration["ChildSettings:Port"]
              ?? "8080";
var port = int.TryParse(portStr, out var parsedPort) ? parsedPort : 8080;
var cameraIp = Environment.GetEnvironmentVariable("CAMERA_IP")
               ?? builder.Configuration["ChildSettings:CameraIp"]
               ?? assignedIp;
var parentIp = Environment.GetEnvironmentVariable("PARENT_IP")
               ?? builder.Configuration["ChildSettings:ParentIp"]
               ?? "10.10.0.2";
var parentUrl = Environment.GetEnvironmentVariable("PARENT_URL")
                ?? builder.Configuration["ChildSettings:ParentUrl"]
                ?? $"http://{parentIp}:8080";

var runtimeOptions = new ChildNodeRuntimeOptions(
    NodeId: nodeId,
    NodeName: nodeName,
    AssignedIp: assignedIp,
    Port: port,
    CameraIp: cameraIp,
    AuthorizedParentIp: parentIp,
    ParentBaseUrl: parentUrl.TrimEnd('/')
);

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton<ICameraHardwareAdapter, SimulatedCameraHardwareAdapter>();
builder.Services.AddSingleton<IChildCameraNodeService, ChildCameraNodeService>();
builder.Services.AddHostedService<ParentRegistrationBackgroundService>();

var app = builder.Build();

app.MapGet("/", (IChildCameraNodeService service) => Results.Ok(service.GetNodeStatusInfo()));

app.MapGet("/api/child/health", (ChildNodeRuntimeOptions opts) => Results.Ok(new
{
    status = "Healthy",
    nodeId = opts.NodeId,
    nodeName = opts.NodeName,
    assignedIp = opts.AssignedIp,
    cameraIp = opts.CameraIp,
    authorizedParentIp = opts.AuthorizedParentIp
}));

app.MapGet("/api/child/status", (IChildCameraNodeService service) =>
    Results.Ok(service.GetNodeStatusInfo()));

app.MapPost("/api/child/capture", async (
    HttpContext httpContext,
    ChildCaptureRequestDto request,
    ChildNodeRuntimeOptions opts,
    IChildCameraNodeService service,
    CancellationToken ct) =>
{
    var callerParentIp = httpContext.Request.Headers["X-Parent-IP"].FirstOrDefault()
                         ?? request.RequestedByParentIp
                         ?? httpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
                         ?? opts.AuthorizedParentIp;

    // Validar que el hijo responda al servidor padre autorizado (o red local 10.x.x.x / 127.x.x.x)
    if (!string.Equals(callerParentIp, opts.AuthorizedParentIp, StringComparison.OrdinalIgnoreCase) &&
        !callerParentIp.StartsWith("10.", StringComparison.Ordinal) &&
        !callerParentIp.StartsWith("127.", StringComparison.Ordinal))
    {
        return Results.Json(
            new { error = $"Acceso denegado: este servidor hijo ({opts.AssignedIp}) solo responde al servidor padre ({opts.AuthorizedParentIp})." },
            statusCode: StatusCodes.Status403Forbidden);
    }

    var capture = await service.HandleCaptureRequestAsync(request, callerParentIp, ct);
    return Results.Ok(capture);
});

app.Run();

public sealed class ParentRegistrationBackgroundService : BackgroundService
{
    private readonly ChildNodeRuntimeOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ParentRegistrationBackgroundService> _logger;

    public ParentRegistrationBackgroundService(
        ChildNodeRuntimeOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<ParentRegistrationBackgroundService> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeña espera inicial para que Kestrel esté listo antes de registrarse con el Padre
        await Task.Delay(1500, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient("ParentCallbackClient");
                var payload = new RegisterChildCallbackRequest(
                    NodeId: _options.NodeId,
                    Name: _options.NodeName,
                    AssignedIp: _options.AssignedIp,
                    Port: _options.Port,
                    CameraIp: _options.CameraIp,
                    ParentIp: _options.AuthorizedParentIp,
                    ContainerId: $"docker:{Environment.MachineName}"
                );

                var response = await client.PostAsJsonAsync(
                    $"{_options.ParentBaseUrl}/api/parent/callbacks/register",
                    payload,
                    stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Servidor Hijo '{NodeName}' ({AssignedIp}) registrado/sincronizado exitosamente con el Padre en {ParentBaseUrl}.",
                        _options.NodeName,
                        _options.AssignedIp,
                        _options.ParentBaseUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    "Aún esperando disponibilidad del Servidor Padre en {ParentBaseUrl}: {Message}",
                    _options.ParentBaseUrl,
                    ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
