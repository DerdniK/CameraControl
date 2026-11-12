using CameraControl.Application.DTOs;
using CameraControl.Application.Interfaces;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;

namespace CameraControl.Application.Services;

public sealed record ChildNodeRuntimeOptions(
    Guid NodeId,
    string NodeName,
    string AssignedIp,
    int Port,
    string CameraIp,
    string AuthorizedParentIp,
    string ParentBaseUrl
);

public sealed class ChildCameraNodeService : IChildCameraNodeService
{
    private readonly ChildNodeRuntimeOptions _options;
    private readonly ICameraHardwareAdapter _cameraAdapter;
    private int _capturesServed;
    private DateTime? _lastCaptureAtUtc;

    public ChildCameraNodeService(
        ChildNodeRuntimeOptions options,
        ICameraHardwareAdapter cameraAdapter)
    {
        _options = options;
        _cameraAdapter = cameraAdapter;
    }

    public async Task<CameraCaptureResult> HandleCaptureRequestAsync(
        ChildCaptureRequestDto request,
        string callerParentIp,
        CancellationToken cancellationToken = default)
    {
        var effectiveParentIp = !string.IsNullOrWhiteSpace(request.RequestedByParentIp)
            ? request.RequestedByParentIp
            : (!string.IsNullOrWhiteSpace(callerParentIp) ? callerParentIp : _options.AuthorizedParentIp);

        var result = await _cameraAdapter.CaptureSnapshotAsync(
            childNodeId: request.ChildNodeId != Guid.Empty ? request.ChildNodeId : _options.NodeId,
            childNodeName: !string.IsNullOrWhiteSpace(request.ChildNodeName) ? request.ChildNodeName : _options.NodeName,
            childAssignedIp: !string.IsNullOrWhiteSpace(request.ChildAssignedIp) ? request.ChildAssignedIp : _options.AssignedIp,
            cameraIp: !string.IsNullOrWhiteSpace(request.CameraIp) ? request.CameraIp : _options.CameraIp,
            requestedByParentIp: effectiveParentIp,
            triggerSource: request.TriggerSource,
            cancellationToken: cancellationToken
        );

        Interlocked.Increment(ref _capturesServed);
        _lastCaptureAtUtc = result.CapturedAtUtc;

        return result;
    }

    public object GetNodeStatusInfo() => new
    {
        nodeId = _options.NodeId,
        nodeName = _options.NodeName,
        assignedIp = _options.AssignedIp,
        port = _options.Port,
        cameraIp = _options.CameraIp,
        authorizedParentIp = _options.AuthorizedParentIp,
        parentBaseUrl = _options.ParentBaseUrl,
        status = "Online",
        capturesServed = _capturesServed,
        lastCaptureAtUtc = _lastCaptureAtUtc,
        timestampUtc = DateTime.UtcNow
    };
}
