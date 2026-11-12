using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;

namespace CameraControl.Application.Services;

public sealed class ParentOrchestratorService : IParentOrchestratorService
{
    private readonly IChildNodeRepository _nodeRepository;
    private readonly ICaptureHistoryRepository _captureRepository;
    private readonly IChildNodeProvisioner _provisioner;
    private readonly IChildNodeCommunicator _communicator;
    private readonly IAutoPollingStateService _pollingState;

    public string ParentServerIp { get; }

    public ParentOrchestratorService(
        IChildNodeRepository nodeRepository,
        ICaptureHistoryRepository captureRepository,
        IChildNodeProvisioner provisioner,
        IChildNodeCommunicator communicator,
        IAutoPollingStateService pollingState,
        string? parentServerIp = null)
    {
        _nodeRepository = nodeRepository;
        _captureRepository = captureRepository;
        _provisioner = provisioner;
        _communicator = communicator;
        _pollingState = pollingState;
        ParentServerIp = string.IsNullOrWhiteSpace(parentServerIp)
            ? (Environment.GetEnvironmentVariable("PARENT_IP") ?? "10.10.0.2")
            : parentServerIp;
    }

    public async Task<ParentSystemSummaryDto> GetSystemSummaryAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await _nodeRepository.GetAllAsync(cancellationToken);
        var recentCaptures = await _captureRepository.GetRecentAsync(1000, null, cancellationToken);
        var dtos = nodes.Select(ChildNodeDto.FromDomain).ToList();

        return new ParentSystemSummaryDto(
            ParentServerIp: ParentServerIp,
            AllowedNetworkRanges: "10.x.x.x (Red Local / Docker Bridge), 127.x.x.x (Loopback Local)",
            TotalChildNodes: dtos.Count,
            OnlineChildNodes: nodes.Count(n => n.Status is ChildNodeStatus.Online or ChildNodeStatus.Capturing),
            TotalCapturesStored: recentCaptures.Count,
            AutoPolling: _pollingState.GetStatus(),
            ChildNodes: dtos
        );
    }

    public async Task<IReadOnlyList<ChildNodeDto>> GetAllNodesAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await _nodeRepository.GetAllAsync(cancellationToken);
        return nodes.Select(ChildNodeDto.FromDomain).ToList();
    }

    public async Task<ChildNodeDto?> GetNodeByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var node = await _nodeRepository.GetByIdAsync(id, cancellationToken);
        return node is null ? null : ChildNodeDto.FromDomain(node);
    }

    public async Task<ChildNodeDto> CreateAndProvisionChildNodeAsync(
        CreateChildNodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var existingByIp = await _nodeRepository.GetByIpAsync(request.AssignedIp.Trim(), cancellationToken);
        if (existingByIp is not null)
        {
            throw new InvalidOperationException(
                $"Ya existe un servidor hijo registrado con la IP '{request.AssignedIp}' ({existingByIp.Name}).");
        }

        var cameraIp = string.IsNullOrWhiteSpace(request.CameraIp)
            ? request.AssignedIp.Trim()
            : request.CameraIp.Trim();

        var parsedMode = Enum.TryParse<ProvisioningMode>(request.ProvisioningMode, ignoreCase: true, out var mode)
            ? mode
            : ProvisioningMode.Auto;

        var node = new ChildServerNode(
            name: request.Name,
            assignedIp: request.AssignedIp,
            port: request.Port <= 0 ? 8080 : request.Port,
            cameraIp: cameraIp,
            parentIp: ParentServerIp,
            mode: parsedMode
        );

        await _nodeRepository.AddAsync(node, cancellationToken);

        var provisionResult = await _provisioner.ProvisionChildNodeAsync(node, cancellationToken);
        if (provisionResult.Success)
        {
            node.AssignProvisionedInstance(
                provisionResult.InstanceId,
                provisionResult.EffectiveMode,
                provisionResult.EffectivePort);
        }
        else
        {
            node.MarkError(provisionResult.Message);
        }

        await _nodeRepository.UpdateAsync(node, cancellationToken);
        return ChildNodeDto.FromDomain(node);
    }

    public async Task<ChildNodeDto> RegisterOrHeartbeatChildNodeAsync(
        RegisterChildCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = request.NodeId.HasValue
            ? await _nodeRepository.GetByIdAsync(request.NodeId.Value, cancellationToken)
            : null;

        existing ??= await _nodeRepository.GetByIpAsync(request.AssignedIp.Trim(), cancellationToken);

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.ContainerId))
            {
                existing.AssignProvisionedInstance(
                    request.ContainerId,
                    ProvisioningMode.DockerContainer,
                    request.Port > 0 ? request.Port : existing.Port);
            }
            else
            {
                existing.MarkOnline();
            }

            await _nodeRepository.UpdateAsync(existing, cancellationToken);
            return ChildNodeDto.FromDomain(existing);
        }

        var newNode = new ChildServerNode(
            name: request.Name,
            assignedIp: request.AssignedIp,
            port: request.Port <= 0 ? 8080 : request.Port,
            cameraIp: string.IsNullOrWhiteSpace(request.CameraIp) ? request.AssignedIp : request.CameraIp,
            parentIp: string.IsNullOrWhiteSpace(request.ParentIp) ? ParentServerIp : request.ParentIp,
            mode: ProvisioningMode.DockerContainer,
            id: request.NodeId
        );

        newNode.AssignProvisionedInstance(
            request.ContainerId ?? $"docker-{newNode.Name.ToLowerInvariant()}",
            ProvisioningMode.DockerContainer,
            newNode.Port);

        await _nodeRepository.AddAsync(newNode, cancellationToken);
        return ChildNodeDto.FromDomain(newNode);
    }

    public async Task<bool> RemoveChildNodeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var node = await _nodeRepository.GetByIdAsync(id, cancellationToken);
        if (node is null)
            return false;

        await _provisioner.DeprovisionChildNodeAsync(node, cancellationToken);
        return await _nodeRepository.RemoveAsync(id, cancellationToken);
    }

    public async Task<CameraCaptureResult> TriggerCaptureOnChildAsync(
        Guid childId,
        string triggerSource = "ManualRequest",
        CancellationToken cancellationToken = default)
    {
        var node = await _nodeRepository.GetByIdAsync(childId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró el servidor hijo con ID '{childId}'.");

        node.MarkCapturing();
        await _nodeRepository.UpdateAsync(node, cancellationToken);

        try
        {
            var capture = await _communicator.RequestCaptureFromChildAsync(node, triggerSource, cancellationToken);
            node.RecordSuccessfulCapture(capture.CapturedAtUtc);
            await _nodeRepository.UpdateAsync(node, cancellationToken);
            await _captureRepository.AddAsync(capture, cancellationToken);
            return capture;
        }
        catch (Exception ex)
        {
            node.MarkError($"Fallo al solicitar captura: {ex.Message}");
            await _nodeRepository.UpdateAsync(node, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CameraCaptureResult>> TriggerCaptureOnAllChildrenAsync(
        string triggerSource = "BroadcastRequest",
        CancellationToken cancellationToken = default)
    {
        var nodes = await _nodeRepository.GetAllAsync(cancellationToken);
        var activeNodes = nodes
            .Where(n => n.Status != ChildNodeStatus.Offline)
            .ToList();

        var tasks = activeNodes.Select(async node =>
        {
            try
            {
                return await TriggerCaptureOnChildAsync(node.Id, triggerSource, cancellationToken);
            }
            catch
            {
                return null;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r is not null).Cast<CameraCaptureResult>().ToList();
    }

    public async Task ReceiveChildCaptureCallbackAsync(
        CameraCaptureResult capture,
        CancellationToken cancellationToken = default)
    {
        var node = await _nodeRepository.GetByIdAsync(capture.ChildNodeId, cancellationToken)
            ?? await _nodeRepository.GetByIpAsync(capture.ChildAssignedIp, cancellationToken);

        if (node is not null)
        {
            node.RecordSuccessfulCapture(capture.CapturedAtUtc);
            await _nodeRepository.UpdateAsync(node, cancellationToken);
        }

        await _captureRepository.AddAsync(capture, cancellationToken);
    }

    public Task<IReadOnlyList<CameraCaptureResult>> GetRecentCapturesAsync(
        int limit = 50,
        Guid? childNodeId = null,
        CancellationToken cancellationToken = default)
    {
        return _captureRepository.GetRecentAsync(limit, childNodeId, cancellationToken);
    }
}
