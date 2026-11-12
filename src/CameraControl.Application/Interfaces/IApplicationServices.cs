using CameraControl.Application.DTOs;
using CameraControl.Domain.Entities;

namespace CameraControl.Application.Interfaces;

public interface IParentOrchestratorService
{
    string ParentServerIp { get; }
    Task<ParentSystemSummaryDto> GetSystemSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChildNodeDto>> GetAllNodesAsync(CancellationToken cancellationToken = default);
    Task<ChildNodeDto?> GetNodeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChildNodeDto> CreateAndProvisionChildNodeAsync(CreateChildNodeRequest request, CancellationToken cancellationToken = default);
    Task<ChildNodeDto> RegisterOrHeartbeatChildNodeAsync(RegisterChildCallbackRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveChildNodeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CameraCaptureResult> TriggerCaptureOnChildAsync(Guid childId, string triggerSource = "ManualRequest", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraCaptureResult>> TriggerCaptureOnAllChildrenAsync(string triggerSource = "BroadcastRequest", CancellationToken cancellationToken = default);
    Task ReceiveChildCaptureCallbackAsync(CameraCaptureResult capture, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraCaptureResult>> GetRecentCapturesAsync(int limit = 50, Guid? childNodeId = null, CancellationToken cancellationToken = default);
}

public interface IChildCameraNodeService
{
    Task<CameraCaptureResult> HandleCaptureRequestAsync(
        ChildCaptureRequestDto request,
        string callerParentIp,
        CancellationToken cancellationToken = default);

    object GetNodeStatusInfo();
}

public interface IAutoPollingStateService
{
    bool Enabled { get; }
    int IntervalSeconds { get; }
    DateTime? LastCycleAtUtc { get; }
    int TotalAutomatedCycles { get; }
    PollingConfigurationDto GetStatus();
    PollingConfigurationDto UpdateConfiguration(bool enabled, int intervalSeconds);
    void RecordCycleExecution();
}
