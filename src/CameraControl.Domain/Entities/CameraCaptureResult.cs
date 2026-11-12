namespace CameraControl.Domain.Entities;

public sealed record CameraTelemetryMetadata(
    double ExposureMs,
    int Iso,
    double SensorTempCelsius,
    double Fps,
    string FirmwareVersion,
    IReadOnlyList<string> DetectedLabels,
    Dictionary<string, object> RawCameraJsonData
);

public sealed class CameraCaptureResult
{
    public Guid CaptureId { get; init; } = Guid.NewGuid();
    public Guid ChildNodeId { get; init; }
    public string ChildNodeName { get; init; } = string.Empty;
    public string ChildAssignedIp { get; init; } = string.Empty;
    public string CameraIp { get; init; } = string.Empty;
    public string RespondedToParentIp { get; init; } = string.Empty;
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
    public string TriggerSource { get; init; } = "SolicitudManual";
    public string ImageMimeType { get; init; } = "image/svg+xml";
    public string ImageBase64 { get; init; } = string.Empty;
    public string Resolution { get; init; } = "1920x1080";
    public long LatencyMs { get; init; }
    public CameraTelemetryMetadata Telemetry { get; init; } = default!;
}
