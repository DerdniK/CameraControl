using CameraControl.Application.Interfaces;
using CameraControl.Application.Services;
using CameraControl.Domain.Ports;
using CameraControl.Infrastructure.Camera;
using CameraControl.Infrastructure.Communication;
using CameraControl.Infrastructure.Persistence;
using CameraControl.Infrastructure.Provisioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CameraControl.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddCameraControlParentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient("ChildNodeClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<VirtualChildEndpointRegistry>();
        services.AddSingleton<IChildNodeRepository, InMemoryChildNodeRepository>();
        services.AddSingleton<ICaptureHistoryRepository, InMemoryCaptureHistoryRepository>();
        services.AddSingleton<ICameraHardwareAdapter, SimulatedCameraHardwareAdapter>();
        services.AddSingleton<IChildNodeProvisioner, HybridChildNodeProvisioner>();
        services.AddSingleton<IChildNodeCommunicator, HttpChildNodeCommunicator>();

        var autoPollingEnabled = configuration.GetValue("ParentSettings:AutoPollingEnabled", false);
        var autoPollingSeconds = configuration.GetValue("ParentSettings:AutoPollingIntervalSeconds", 15);
        var parentIp = Environment.GetEnvironmentVariable("PARENT_IP")
                       ?? configuration["ParentSettings:ParentIp"]
                       ?? "10.10.0.2";

        services.AddSingleton<IAutoPollingStateService>(
            new AutoPollingStateService(autoPollingEnabled, autoPollingSeconds));

        services.AddSingleton<IParentOrchestratorService>(sp => new ParentOrchestratorService(
            sp.GetRequiredService<IChildNodeRepository>(),
            sp.GetRequiredService<ICaptureHistoryRepository>(),
            sp.GetRequiredService<IChildNodeProvisioner>(),
            sp.GetRequiredService<IChildNodeCommunicator>(),
            sp.GetRequiredService<IAutoPollingStateService>(),
            parentIp
        ));

        return services;
    }
}
