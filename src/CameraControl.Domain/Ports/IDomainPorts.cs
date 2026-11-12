using CameraControl.Domain.Entities;

namespace CameraControl.Domain.Ports;

public interface IChildNodeRepository
{
    Task<IReadOnlyList<ChildServerNode>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ChildServerNode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChildServerNode?> GetByIpAsync(string assignedIp, CancellationToken cancellationToken = default);
    Task AddAsync(ChildServerNode node, CancellationToken cancellationToken = default);
    Task UpdateAsync(ChildServerNode node, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICaptureHistoryRepository
{
    Task AddAsync(CameraCaptureResult capture, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CameraCaptureResult>> GetRecentAsync(int limit = 50, Guid? childNodeId = null, CancellationToken cancellationToken = default);
    Task<CameraCaptureResult?> GetByIdAsync(Guid captureId, CancellationToken cancellationToken = default);
}

public sealed record ProvisioningResult(
    bool Success,
    string InstanceId,
    ProvisioningMode EffectiveMode,
    int EffectivePort,
    string Message
);

public interface IChildNodeProvisioner
{
    Task<ProvisioningResult> ProvisionChildNodeAsync(ChildServerNode node, CancellationToken cancellationToken = default);
    Task<bool> DeprovisionChildNodeAsync(ChildServerNode node, CancellationToken cancellationToken = default);
}

public interface IChildNodeCommunicator
{
    Task<CameraCaptureResult> RequestCaptureFromChildAsync(
        ChildServerNode node,
        string triggerSource,
        CancellationToken cancellationToken = default);

    Task<bool> CheckChildHealthAsync(
        ChildServerNode node,
        CancellationToken cancellationToken = default);
}

public interface ICameraHardwareAdapter
{
    Task<CameraCaptureResult> CaptureSnapshotAsync(
        Guid childNodeId,
        string childNodeName,
        string childAssignedIp,
        string cameraIp,
        string requestedByParentIp,
        string triggerSource,
        CancellationToken cancellationToken = default);
}
