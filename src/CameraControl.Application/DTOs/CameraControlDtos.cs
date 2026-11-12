using CameraControl.Domain.Entities;

namespace CameraControl.Application.DTOs;

public sealed record CreateChildNodeRequest(
    string Name,
    string AssignedIp,
    string? CameraIp = null,
    int Port = 8080,
    string ProvisioningMode = "DockerContainer"
);

public sealed record RegisterChildCallbackRequest(
    Guid? NodeId,
    string Name,
    string AssignedIp,
    int Port,
    string CameraIp,
    string ParentIp,
    string? ContainerId = null
);

public sealed record ChildNodeDto(
    Guid Id,
    string Name,
    string AssignedIp,
    int Port,
    string BaseUrl,
    string CameraIp,
    string ParentIp,
    string Status,
    string ProvisioningMode,
    string? ContainerOrInstanceId,
    DateTime CreatedAtUtc,
    DateTime? LastSeenAtUtc,
    DateTime? LastCaptureAtUtc,
    int TotalCaptures,
    string? LastErrorMessage
)
{
    public static ChildNodeDto FromDomain(ChildServerNode node) => new(
        node.Id,
        node.Name,
        node.AssignedIp,
        node.Port,
        node.BaseUrl,
        node.CameraIp,
        node.ParentIp,
        node.Status.ToString(),
        node.Mode.ToString(),
        node.ContainerOrInstanceId,
        node.CreatedAtUtc,
        node.LastSeenAtUtc,
        node.LastCaptureAtUtc,
        node.TotalCaptures,
        node.LastErrorMessage
    );
}

public sealed record ChildCaptureRequestDto(
    Guid ChildNodeId,
    string ChildNodeName,
    string ChildAssignedIp,
    string CameraIp,
    string RequestedByParentIp,
    string TriggerSource = "SolicitudManual"
);

public sealed record PollingConfigurationDto(
    bool Enabled,
    int IntervalSeconds,
    DateTime? LastCycleAtUtc,
    int TotalAutomatedCycles
);

public sealed record UpdatePollingRequest(
    bool Enabled,
    int IntervalSeconds
);

public sealed record ParentSystemSummaryDto(
    string ParentServerIp,
    string AllowedNetworkRanges,
    int TotalChildNodes,
    int OnlineChildNodes,
    int TotalCapturesStored,
    PollingConfigurationDto AutoPolling,
    IReadOnlyList<ChildNodeDto> ChildNodes
);
