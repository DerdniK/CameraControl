using System.Net.Http.Json;
using System.Net.Sockets;
using CameraControl.Application.DTOs;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;
using CameraControl.Infrastructure.Provisioning;
using Microsoft.Extensions.Logging;

namespace CameraControl.Infrastructure.Communication;

public sealed class HttpChildNodeCommunicator : IChildNodeCommunicator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VirtualChildEndpointRegistry _endpointRegistry;
    private readonly ILogger<HttpChildNodeCommunicator> _logger;

    public HttpChildNodeCommunicator(
        IHttpClientFactory httpClientFactory,
        VirtualChildEndpointRegistry endpointRegistry,
        ILogger<HttpChildNodeCommunicator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _endpointRegistry = endpointRegistry;
        _logger = logger;
    }

    public async Task<CameraCaptureResult> RequestCaptureFromChildAsync(
        ChildServerNode node,
        string triggerSource,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveTargetBaseUrl(node);
        var endpoint = $"{baseUrl}/api/child/capture";

        var payload = new ChildCaptureRequestDto(
            ChildNodeId: node.Id,
            ChildNodeName: node.Name,
            ChildAssignedIp: node.AssignedIp,
            CameraIp: node.CameraIp,
            RequestedByParentIp: node.ParentIp,
            TriggerSource: triggerSource
        );

        using var client = _httpClientFactory.CreateClient("ChildNodeClient");

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = JsonContent.Create(payload)
                };
                requestMessage.Headers.Add("X-Parent-IP", node.ParentIp);
                requestMessage.Headers.Add("X-Child-Target-IP", node.AssignedIp);

                using var response = await client.SendAsync(requestMessage, cancellationToken);
                response.EnsureSuccessStatusCode();

                var captureResult = await response.Content.ReadFromJsonAsync<CameraCaptureResult>(cancellationToken: cancellationToken);
                if (captureResult is null)
                {
                    throw new InvalidOperationException($"El servidor hijo '{node.Name}' ({node.AssignedIp}) devolvió un JSON vacío.");
                }

                return captureResult;
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.AccessDenied })
            {
                lastException = ex;
                break;
            }
            catch (Exception ex) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogDebug(
                    "Intento {Attempt}/3 conectando con el servidor hijo {NodeName} ({Endpoint}): {Message}",
                    attempt,
                    node.Name,
                    endpoint,
                    ex.Message);
                await Task.Delay(450, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        // Si el nodo es virtual en el mismo host y el firewall restringe sockets loopback salientes, despachar vía canal directo en memoria
        if (_endpointRegistry.TryGetVirtualService(node.Id, out var virtualService) && virtualService is not null)
        {
            return await virtualService.HandleCaptureRequestAsync(payload, node.ParentIp, cancellationToken);
        }

        throw new HttpRequestException(
            $"No se pudo obtener respuesta del servidor hijo '{node.Name}' en {node.AssignedIp}:{node.Port} ({endpoint}): {lastException?.Message}",
            lastException);
    }

    public async Task<bool> CheckChildHealthAsync(
        ChildServerNode node,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveTargetBaseUrl(node);
        using var client = _httpClientFactory.CreateClient("ChildNodeClient");

        try
        {
            using var response = await client.GetAsync($"{baseUrl}/api/child/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string ResolveTargetBaseUrl(ChildServerNode node)
    {
        if (_endpointRegistry.TryGetEndpoint(node.Id, out var localUrl) && !string.IsNullOrWhiteSpace(localUrl))
        {
            return localUrl.TrimEnd('/');
        }

        return node.BaseUrl.TrimEnd('/');
    }
}
