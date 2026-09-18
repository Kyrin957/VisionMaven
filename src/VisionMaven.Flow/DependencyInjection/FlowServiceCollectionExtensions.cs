using Microsoft.Extensions.DependencyInjection;
using VisionMaven.Core.Abstractions;
using VisionMaven.Flow.Devices;
using VisionMaven.Flow.Engine;
using VisionMaven.Flow.Inference;
using VisionMaven.Flow.Nodes;
using VisionMaven.Flow.Preview;
using VisionMaven.Flow.Runtime;

namespace VisionMaven.Flow.DependencyInjection;

/// <summary>Flow 层服务注册：设备会话、节点工厂、流程引擎与多工位运行时。</summary>
public static class FlowServiceCollectionExtensions
{
    public static IServiceCollection AddVisionMavenFlow(this IServiceCollection services)
    {
        services.AddSingleton<ProjectAccessor>();
        services.AddSingleton<IProjectAccessor>(provider => provider.GetRequiredService<ProjectAccessor>());

        services.AddSingleton<IDeviceSession, DeviceSession>();
        services.AddSingleton<ILivePreviewService, LivePreviewService>();
        services.AddSingleton<IInferenceSessionPool, InferenceSessionPool>();

        services.AddSingleton<FlowNodeEnvironment>();
        services.AddSingleton<IFlowNodeFactory>(provider => new FlowNodeFactory(
            type => ActivatorUtilities.CreateInstance(provider, type)));
        services.AddSingleton<IFlowEngine, FlowEngine>();

        services.AddSingleton<IStationRuntimeFactory, StationRuntimeFactory>();
        services.AddSingleton<IRuntimeCoordinator, RuntimeCoordinator>();

        return services;
    }
}
