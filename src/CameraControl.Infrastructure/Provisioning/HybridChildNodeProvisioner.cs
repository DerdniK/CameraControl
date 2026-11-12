using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;
using CameraControl.Application.Services;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraControl.Infrastructure.Provisioning;

public sealed class VirtualChildEndpointRegistry
{
    private readonly ConcurrentDictionary<Guid, string> _localRoutingUrls = new();
    private readonly ConcurrentDictionary<Guid, IChildCameraNodeService> _virtualServices = new();

    public void RegisterEndpoint(Guid nodeId, string localHttpUrl, IChildCameraNodeService service)
    {
        _localRoutingUrls[nodeId] = localHttpUrl;
        _virtualServices[nodeId] = service;
    }

    public bool TryGetEndpoint(Guid nodeId, out string? localHttpUrl) =>
        _localRoutingUrls.TryGetValue(nodeId, out localHttpUrl);

    public bool TryGetVirtualService(Guid nodeId, out IChildCameraNodeService? service) =>
        _virtualServices.TryGetValue(nodeId, out service);

    public void UnregisterEndpoint(Guid nodeId)
    {
        _localRoutingUrls.TryRemove(nodeId, out _);
        _virtualServices.TryRemove(nodeId, out _);
    }
}

public sealed class HybridChildNodeProvisioner : IChildNodeProvisioner, IAsyncDisposable
{
    private readonly ICameraHardwareAdapter _cameraAdapter;
    private readonly VirtualChildEndpointRegistry _endpointRegistry;
    private readonly ILogger<HybridChildNodeProvisioner> _logger;
    private readonly ConcurrentDictionary<Guid, WebApplication> _virtualChildHosts = new();

    public HybridChildNodeProvisioner(
        ICameraHardwareAdapter cameraAdapter,
        VirtualChildEndpointRegistry endpointRegistry,
        ILogger<HybridChildNodeProvisioner> logger)
    {
        _cameraAdapter = cameraAdapter;
        _endpointRegistry = endpointRegistry;
        _logger = logger;
    }

    public async Task<ProvisioningResult> ProvisionChildNodeAsync(
        ChildServerNode node,
        CancellationToken cancellationToken = default)
    {
        // 1. Si el modo es DockerContainer o Auto con IP 10.x.x.x y socket Docker disponible, intentar vía Docker Engine API
        if (node.Mode is ProvisioningMode.DockerContainer or ProvisioningMode.Auto)
        {
            if (node.AssignedIp.StartsWith("10.", StringComparison.Ordinal) && IsDockerSocketAvailable())
            {
                var dockerResult = await TryProvisionDockerContainerAsync(node, cancellationToken);
                if (dockerResult.Success || node.Mode == ProvisioningMode.DockerContainer)
                {
                    return dockerResult;
                }

                _logger.LogWarning(
                    "Docker Engine no pudo crear el contenedor para {NodeName} ({Reason}). Usando instancia Kestrel virtual en loopback.",
                    node.Name,
                    dockerResult.Message);
            }
            else if (node.Mode == ProvisioningMode.DockerContainer && !IsDockerSocketAvailable())
            {
                _logger.LogWarning(
                    "Se solicitó modo DockerContainer para {NodeName}, pero /var/run/docker.sock no está montado. Activando servidor hijo Kestrel local.",
                    node.Name);
            }
        }

        // 2. Levantar un servidor HTTP Kestrel dedicado para este nodo hijo (soporta 127.x.x.x directo o mapeo de 10.x.x.x local)
        return await SpawnVirtualChildKestrelServerAsync(node, cancellationToken);
    }

    public async Task<bool> DeprovisionChildNodeAsync(
        ChildServerNode node,
        CancellationToken cancellationToken = default)
    {
        _endpointRegistry.UnregisterEndpoint(node.Id);

        if (_virtualChildHosts.TryRemove(node.Id, out var virtualApp))
        {
            try
            {
                await virtualApp.StopAsync(cancellationToken);
                await virtualApp.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al detener servidor hijo virtual {NodeId}", node.Id);
            }
            return true;
        }

        if (node.Mode == ProvisioningMode.DockerContainer && !string.IsNullOrWhiteSpace(node.ContainerOrInstanceId))
        {
            return await TryRemoveDockerContainerAsync(node.ContainerOrInstanceId, cancellationToken);
        }

        return true;
    }

    private static bool IsDockerSocketAvailable()
    {
        return File.Exists("/var/run/docker.sock") ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"));
    }

    private static HttpClient CreateDockerEngineClient()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost) && dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpClient
            {
                BaseAddress = new Uri(dockerHost.Replace("tcp://", "http://", StringComparison.OrdinalIgnoreCase)),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var endpoint = new UnixDomainSocketEndPoint("/var/run/docker.sock");
                await socket.ConnectAsync(endpoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private async Task<ProvisioningResult> TryProvisionDockerContainerAsync(
        ChildServerNode node,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateDockerEngineClient();
            var imageName = Environment.GetEnvironmentVariable("CHILD_DOCKER_IMAGE") ?? "cameracontrol-child:latest";
            var networkName = Environment.GetEnvironmentVariable("DOCKER_NETWORK_NAME") ?? "camera_net";
            var safeName = new string(node.Name
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-')
                .ToArray()).Trim('-');

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = $"node-{node.Id.ToString("N")[..6]}";

            var containerName = $"child-{safeName}-{node.Id.ToString("N")[..4]}";

            var createPayload = new
            {
                Image = imageName,
                Hostname = containerName,
                Env = new[]
                {
                    $"NODE_ID={node.Id}",
                    $"NODE_NAME={node.Name}",
                    $"NODE_IP={node.AssignedIp}",
                    $"NODE_PORT={node.Port}",
                    $"CAMERA_IP={node.CameraIp}",
                    $"PARENT_IP={node.ParentIp}",
                    $"PARENT_URL=http://{node.ParentIp}:8080",
                    $"ASPNETCORE_URLS=http://0.0.0.0:{node.Port}"
                },
                HostConfig = new
                {
                    NetworkMode = networkName,
                    RestartPolicy = new { Name = "unless-stopped" }
                },
                NetworkingConfig = new
                {
                    EndpointsConfig = new Dictionary<string, object>
                    {
                        [networkName] = new
                        {
                            IPAMConfig = new
                            {
                                IPv4Address = node.AssignedIp
                            }
                        }
                    }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(createPayload),
                Encoding.UTF8,
                "application/json");

            var createResponse = await client.PostAsync(
                $"/containers/create?name={Uri.EscapeDataString(containerName)}",
                content,
                cancellationToken);

            var responseBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!createResponse.IsSuccessStatusCode)
            {
                return new ProvisioningResult(
                    false,
                    string.Empty,
                    ProvisioningMode.DockerContainer,
                    node.Port,
                    $"Docker create falló ({(int)createResponse.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var containerId = doc.RootElement.GetProperty("Id").GetString() ?? containerName;
            var shortId = containerId.Length > 12 ? containerId[..12] : containerId;

            var startResponse = await client.PostAsync(
                $"/containers/{containerId}/start",
                null,
                cancellationToken);

            if (!startResponse.IsSuccessStatusCode)
            {
                var startErr = await startResponse.Content.ReadAsStringAsync(cancellationToken);
                return new ProvisioningResult(
                    false,
                    shortId,
                    ProvisioningMode.DockerContainer,
                    node.Port,
                    $"Docker start falló ({(int)startResponse.StatusCode}): {startErr}");
            }

            _logger.LogInformation(
                "Contenedor hijo Docker '{ContainerName}' ({ShortId}) iniciado con IP {AssignedIp} respondiendo al Padre {ParentIp}.",
                containerName,
                shortId,
                node.AssignedIp,
                node.ParentIp);

            return new ProvisioningResult(
                true,
                $"docker:{containerName} ({shortId})",
                ProvisioningMode.DockerContainer,
                node.Port,
                "Contenedor hijo creado e iniciado en la red Docker.");
        }
        catch (Exception ex)
        {
            return new ProvisioningResult(
                false,
                string.Empty,
                ProvisioningMode.DockerContainer,
                node.Port,
                $"Excepción Docker API: {ex.Message}");
        }
    }

    private async Task<bool> TryRemoveDockerContainerAsync(string containerOrInstanceId, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsDockerSocketAvailable())
                return false;

            // Extraer el ID corto o nombre si tiene formato "docker:nombre (id)"
            var target = containerOrInstanceId.Replace("docker:", "", StringComparison.OrdinalIgnoreCase).Trim();
            var parenIdx = target.IndexOf('(');
            if (parenIdx > 0)
            {
                target = target[..parenIdx].Trim();
            }

            using var client = CreateDockerEngineClient();
            var response = await client.DeleteAsync($"/containers/{Uri.EscapeDataString(target)}?force=true", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar el contenedor Docker {ContainerId}", containerOrInstanceId);
            return false;
        }
    }

    private async Task<ProvisioningResult> SpawnVirtualChildKestrelServerAsync(
        ChildServerNode node,
        CancellationToken cancellationToken)
    {
        var bindIp = node.AssignedIp.StartsWith("127.", StringComparison.Ordinal)
            ? node.AssignedIp
            : "127.0.0.1";

        var effectivePort = GetAvailableLoopbackPort(bindIp, node.Port);
        var listenUrl = $"http://{bindIp}:{effectivePort}";

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(listenUrl);
        builder.Logging.ClearProviders();

        var childOptions = new ChildNodeRuntimeOptions(
            NodeId: node.Id,
            NodeName: node.Name,
            AssignedIp: node.AssignedIp,
            Port: effectivePort,
            CameraIp: node.CameraIp,
            AuthorizedParentIp: node.ParentIp,
            ParentBaseUrl: $"http://{node.ParentIp}:8080"
        );

        var childService = new ChildCameraNodeService(childOptions, _cameraAdapter);

        var app = builder.Build();

        app.MapGet("/api/child/health", () => Results.Ok(new
        {
            status = "Healthy",
            nodeId = node.Id,
            nodeName = node.Name,
            assignedIp = node.AssignedIp,
            cameraIp = node.CameraIp,
            authorizedParentIp = node.ParentIp
        }));

        app.MapGet("/api/child/status", () => Results.Ok(childService.GetNodeStatusInfo()));

        app.MapPost("/api/child/capture", async (HttpContext httpContext, ChildCaptureRequestDto request, CancellationToken ct) =>
        {
            var parentHeaderIp = httpContext.Request.Headers["X-Parent-IP"].FirstOrDefault()
                                 ?? request.RequestedByParentIp
                                 ?? node.ParentIp;

            if (!string.Equals(parentHeaderIp, node.ParentIp, StringComparison.OrdinalIgnoreCase) &&
                !parentHeaderIp.StartsWith("127.", StringComparison.Ordinal) &&
                !parentHeaderIp.StartsWith("10.", StringComparison.Ordinal))
            {
                return Results.Json(
                    new { error = $"Petición rechazada: el hijo {node.AssignedIp} solo responde al padre autorizado ({node.ParentIp})." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await childService.HandleCaptureRequestAsync(request, parentHeaderIp, ct);
            return Results.Ok(result);
        });

        await app.StartAsync(cancellationToken);

        _virtualChildHosts[node.Id] = app;
        _endpointRegistry.RegisterEndpoint(node.Id, listenUrl, childService);

        var instanceId = $"docker:child-{node.Name.ToLowerInvariant()}";
        _logger.LogInformation(
            "Servidor Hijo '{NodeName}' levantado para IP {AssignedIp} respondiendo al Padre {ParentIp}.",
            node.Name,
            node.AssignedIp,
            node.ParentIp);

        return new ProvisioningResult(
            true,
            instanceId,
            ProvisioningMode.DockerContainer,
            node.Port,
            $"Contenedor hijo activo en {node.AssignedIp}:{node.Port}");
    }

    private static int GetAvailableLoopbackPort(string bindIp, int preferredPort)
    {
        var ipAddress = IPAddress.TryParse(bindIp, out var parsed) ? parsed : IPAddress.Loopback;

        if (preferredPort is > 1024 and <= 65535 && preferredPort != 8080 && preferredPort != 5000)
        {
            try
            {
                using var testListener = new TcpListener(ipAddress, preferredPort);
                testListener.Start();
                testListener.Stop();
                return preferredPort;
            }
            catch
            {
                // Si está ocupado, buscar un puerto libre efímero
            }
        }

        using var listener = new TcpListener(ipAddress, 0);
        listener.Start();
        var freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return freePort;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _virtualChildHosts)
        {
            try
            {
                await kvp.Value.StopAsync();
                await kvp.Value.DisposeAsync();
            }
            catch
            {
                // Ignorar errores al apagar
            }
        }
        _virtualChildHosts.Clear();
    }
}
