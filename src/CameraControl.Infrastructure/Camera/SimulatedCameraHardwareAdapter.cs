using System.Diagnostics;
using System.Text;
using CameraControl.Domain.Entities;
using CameraControl.Domain.Ports;

namespace CameraControl.Infrastructure.Camera;

public sealed class SimulatedCameraHardwareAdapter : ICameraHardwareAdapter
{
    private static readonly string[][] SampleDetectionSets =
    [
        ["Vehiculo-Sedan (98.4%)", "Placa-Detectada (94.1%)"],
        ["Operador-Con-Casco (99.1%)", "Montacargas (92.7%)"],
        ["Acceso-Despejado (100%)"],
        ["Camion-Carga (97.8%)", "Contenedor-ISO (95.3%)"],
        ["Persona-Autorizada (96.5%)"]
    ];

    public async Task<CameraCaptureResult> CaptureSnapshotAsync(
        Guid childNodeId,
        string childNodeName,
        string childAssignedIp,
        string cameraIp,
        string requestedByParentIp,
        string triggerSource,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // Simula tiempo real de obturación y lectura del buffer de la cámara (25-85 ms)
        var simulatedShutterDelay = Random.Shared.Next(25, 85);
        await Task.Delay(simulatedShutterDelay, cancellationToken);

        var now = DateTime.UtcNow;
        var captureId = Guid.NewGuid();
        var exposureMs = Math.Round(4.5 + Random.Shared.NextDouble() * 12.0, 2);
        var iso = new[] { 100, 200, 400, 800 }[Random.Shared.Next(4)];
        var tempC = Math.Round(36.0 + Random.Shared.NextDouble() * 8.5, 1);
        var fps = Math.Round(29.5 + Random.Shared.NextDouble() * 0.5, 1);
        var detections = SampleDetectionSets[Random.Shared.Next(SampleDetectionSets.Length)];

        var svgBase64 = GenerateCameraFrameSvgBase64(
            captureId,
            childNodeName,
            childAssignedIp,
            cameraIp,
            requestedByParentIp,
            now,
            exposureMs,
            iso,
            tempC,
            detections);

        sw.Stop();

        var rawLegacyPayload = new Dictionary<string, object>
        {
            ["camera_ip"] = cameraIp,
            ["child_server_ip"] = childAssignedIp,
            ["parent_server_ip"] = requestedByParentIp,
            ["camera_name"] = childNodeName,
            ["frame_id"] = captureId.ToString("N")[..12].ToUpperInvariant(),
            ["timestamp_unix_ms"] = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            ["status_code"] = "OK_FRAME_CAPTURED",
            ["detections_count"] = detections.Length,
            ["detections"] = detections
        };

        return new CameraCaptureResult
        {
            CaptureId = captureId,
            ChildNodeId = childNodeId,
            ChildNodeName = childNodeName,
            ChildAssignedIp = childAssignedIp,
            CameraIp = cameraIp,
            RespondedToParentIp = requestedByParentIp,
            CapturedAtUtc = now,
            TriggerSource = triggerSource,
            ImageMimeType = "image/svg+xml",
            ImageBase64 = svgBase64,
            Resolution = "1920x1080",
            LatencyMs = sw.ElapsedMilliseconds,
            Telemetry = new CameraTelemetryMetadata(
                ExposureMs: exposureMs,
                Iso: iso,
                SensorTempCelsius: tempC,
                Fps: fps,
                FirmwareVersion: "CAM-FW-v4.2.1",
                DetectedLabels: detections,
                RawCameraJsonData: rawLegacyPayload
            )
        };
    }

    private static string GenerateCameraFrameSvgBase64(
        Guid captureId,
        string nodeName,
        string childIp,
        string cameraIp,
        string parentIp,
        DateTime timestampUtc,
        double exposureMs,
        int iso,
        double tempC,
        string[] detections)
    {
        var hue = Math.Abs(HashCode.Combine(nodeName, childIp)) % 360;
        var boxX = 180 + (Math.Abs(captureId.GetHashCode()) % 220);
        var boxY = 110 + (Math.Abs(timestampUtc.Millisecond) % 80);
        var primaryLabel = detections.FirstOrDefault() ?? "Frame-OK";

        var svg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 450" width="100%" height="100%">
          <defs>
            <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="hsl({hue}, 35%, 12%)" />
              <stop offset="50%" stop-color="hsl({(hue + 40) % 360}, 30%, 18%)" />
              <stop offset="100%" stop-color="#0a0f18" />
            </linearGradient>
            <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse">
              <path d="M 40 0 L 0 0 0 40" fill="none" stroke="rgba(255,255,255,0.05)" stroke-width="1"/>
            </pattern>
          </defs>
          <rect width="800" height="450" fill="url(#bg)" />
          <rect width="800" height="450" fill="url(#grid)" />

          <!-- Escena simulada y retícula óptica -->
          <circle cx="400" cy="225" r="140" fill="none" stroke="rgba(56, 189, 248, 0.2)" stroke-width="1.5" stroke-dasharray="6 4"/>
          <line x1="360" y1="225" x2="440" y2="225" stroke="rgba(56, 189, 248, 0.45)" stroke-width="1.5"/>
          <line x1="400" y1="185" x2="400" y2="265" stroke="rgba(56, 189, 248, 0.45)" stroke-width="1.5"/>

          <!-- Bounding Box de detección simulada -->
          <rect x="{boxX}" y="{boxY}" width="240" height="145" rx="4" fill="rgba(16, 185, 129, 0.12)" stroke="#10b981" stroke-width="2"/>
          <rect x="{boxX}" y="{boxY - 24}" width="215" height="22" rx="3" fill="#10b981"/>
          <text x="{boxX + 8}" y="{boxY - 9}" fill="#052e16" font-family="monospace" font-size="12" font-weight="bold">{primaryLabel}</text>

          <!-- Barra superior HUD -->
          <rect x="0" y="0" width="800" height="44" fill="rgba(0,0,0,0.68)"/>
          <circle cx="24" cy="22" r="7" fill="#ef4444"/>
          <text x="40" y="27" fill="#f8fafc" font-family="monospace" font-size="15" font-weight="bold">REC • {nodeName}</text>
          <text x="490" y="27" fill="#38bdf8" font-family="monospace" font-size="14">{timestampUtc:yyyy-MM-dd HH:mm:ss.fff} UTC</text>

          <!-- Barra inferior HUD con IPs Padre / Hijo / Cámara -->
          <rect x="0" y="382" width="800" height="68" fill="rgba(0,0,0,0.76)"/>
          <text x="18" y="406" fill="#22d3ee" font-family="monospace" font-size="13" font-weight="bold">HIJO IP: {childIp}  |  CAMARA IP: {cameraIp}  |  RESPONDIENDO A PADRE IP: {parentIp}</text>
          <text x="18" y="430" fill="#94a3b8" font-family="monospace" font-size="12">FRAME: {captureId.ToString("N")[..10].ToUpperInvariant()}  •  EXP: {exposureMs}ms  •  ISO: {iso}  •  TEMP: {tempC}°C  •  1920x1080</text>
        </svg>
        """;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
}
