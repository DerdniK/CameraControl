using System.Net;

namespace CameraControl.Domain.Entities;

public enum ChildNodeStatus
{
    Provisioning,
    Online,
    Capturing,
    Offline,
    Error
}

public enum ProvisioningMode
{
    Auto,
    DockerContainer,
    VirtualLoopback
}

public sealed class ChildServerNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; private set; }
    public string AssignedIp { get; private set; }
    public int Port { get; private set; }
    public string CameraIp { get; private set; }
    public string ParentIp { get; private set; }
    public ChildNodeStatus Status { get; private set; }
    public ProvisioningMode Mode { get; private set; }
    public string? ContainerOrInstanceId { get; private set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; private set; }
    public DateTime? LastCaptureAtUtc { get; private set; }
    public int TotalCaptures { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public ChildServerNode(
        string name,
        string assignedIp,
        int port,
        string cameraIp,
        string parentIp,
        ProvisioningMode mode = ProvisioningMode.Auto,
        Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del servidor hijo es obligatorio.", nameof(name));

        ValidateAllowedNetworkIp(assignedIp, nameof(assignedIp));
        ValidateAllowedNetworkIp(cameraIp, nameof(cameraIp));

        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "El puerto debe estar entre 1 y 65535.");

        Id = id ?? Guid.NewGuid();
        Name = name.Trim();
        AssignedIp = assignedIp.Trim();
        Port = port;
        CameraIp = cameraIp.Trim();
        ParentIp = string.IsNullOrWhiteSpace(parentIp) ? "10.10.0.2" : parentIp.Trim();
        Mode = mode;
        Status = ChildNodeStatus.Provisioning;
    }

    public string BaseUrl => $"http://{AssignedIp}:{Port}";

    public static void ValidateAllowedNetworkIp(string ip, string paramName)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out var parsedIp))
        {
            throw new ArgumentException($"La dirección IP '{ip}' no tiene un formato IPv4 válido.", paramName);
        }

        var bytes = parsedIp.GetAddressBytes();
        // Permitir redes locales 10.x.x.x o loopback 127.x.x.x según requerimiento del negocio
        if (bytes.Length != 4 || (bytes[0] != 10 && bytes[0] != 127))
        {
            throw new ArgumentException(
                $"La IP '{ip}' no pertenece a los segmentos permitidos (10.x.x.x o 127.x.x.x).",
                paramName);
        }
    }

    public void AssignProvisionedInstance(string instanceId, ProvisioningMode effectiveMode, int? effectivePort = null)
    {
        ContainerOrInstanceId = instanceId;
        Mode = effectiveMode;
        if (effectivePort is > 0 and <= 65535)
        {
            Port = effectivePort.Value;
        }
        Status = ChildNodeStatus.Online;
        LastSeenAtUtc = DateTime.UtcNow;
        LastErrorMessage = null;
    }

    public void MarkOnline()
    {
        Status = ChildNodeStatus.Online;
        LastSeenAtUtc = DateTime.UtcNow;
        LastErrorMessage = null;
    }

    public void MarkCapturing()
    {
        Status = ChildNodeStatus.Capturing;
        LastSeenAtUtc = DateTime.UtcNow;
    }

    public void RecordSuccessfulCapture(DateTime capturedAtUtc)
    {
        Status = ChildNodeStatus.Online;
        LastSeenAtUtc = DateTime.UtcNow;
        LastCaptureAtUtc = capturedAtUtc;
        TotalCaptures++;
        LastErrorMessage = null;
    }

    public void MarkError(string errorMessage)
    {
        Status = ChildNodeStatus.Error;
        LastErrorMessage = errorMessage;
    }

    public void MarkOffline(string? reason = null)
    {
        Status = ChildNodeStatus.Offline;
        LastErrorMessage = reason;
    }
}
