using CameraControl.Application.Interfaces;

namespace CameraControl.ParentServer.Workers;

public sealed class AutoCapturePollingWorker : BackgroundService
{
    private readonly IParentOrchestratorService _orchestrator;
    private readonly IAutoPollingStateService _pollingState;
    private readonly ILogger<AutoCapturePollingWorker> _logger;

    public AutoCapturePollingWorker(
        IParentOrchestratorService orchestrator,
        IAutoPollingStateService pollingState,
        ILogger<AutoCapturePollingWorker> logger)
    {
        _orchestrator = orchestrator;
        _pollingState = pollingState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = _pollingState.IntervalSeconds;

            if (_pollingState.Enabled)
            {
                try
                {
                    var nodes = await _orchestrator.GetAllNodesAsync(stoppingToken);
                    if (nodes.Count > 0)
                    {
                        _logger.LogInformation(
                            "[AutoPolling] Solicitando captura automatica a {Count} servidores hijos desde el Padre ({ParentIp})...",
                            nodes.Count,
                            _orchestrator.ParentServerIp);

                        var captures = await _orchestrator.TriggerCaptureOnAllChildrenAsync(
                            triggerSource: "AutoPolling",
                            cancellationToken: stoppingToken);

                        _pollingState.RecordCycleExecution();

                        _logger.LogInformation(
                            "[AutoPolling] Ciclo completado: {Received}/{Total} respuestas JSON recibidas.",
                            captures.Count,
                            nodes.Count);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[AutoPolling] Error durante el ciclo de consulta automatica.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, intervalSeconds)), stoppingToken);
        }
    }
}
