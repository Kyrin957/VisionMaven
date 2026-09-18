using Microsoft.Extensions.DependencyInjection;
using VisionMaven.Core.Abstractions;
using VisionMaven.Vision.Calibration;
using VisionMaven.Vision.Imaging;
using VisionMaven.Vision.Inference;
using VisionMaven.Vision.Operators;
using VisionMaven.Vision.Rendering;

namespace VisionMaven.Vision.DependencyInjection;

/// <summary>Vision 层服务注册：算子、标定、推理与渲染。</summary>
public static class VisionServiceCollectionExtensions
{
    public static IServiceCollection AddVisionMavenVision(this IServiceCollection services)
    {
        services.AddSingleton<IImageFrameFactory, OpenCvImageFrameFactory>();
        services.AddSingleton<IBufferPool, MatBufferPool>();
        services.AddSingleton<ICalibrationService, CalibrationService>();
        services.AddSingleton<IOverlayRenderer, OpenCvOverlayRenderer>();

        var calibrationProvider = new ProjectCalibrationProvider();
        services.AddSingleton<ICalibrationProvider>(calibrationProvider);
        services.AddSingleton(calibrationProvider);

        RegisterOperators(services);

        services.AddSingleton<IOperatorRegistry>(provider => new OperatorRegistry(
            new IImageOperator[]
            {
                provider.GetRequiredService<GrayOperator>(),
                provider.GetRequiredService<FilterOperator>(),
                provider.GetRequiredService<MorphologyOperator>(),
                provider.GetRequiredService<RoiOperator>(),
                provider.GetRequiredService<ThresholdOperator>(),
                provider.GetRequiredService<BlobFindOperator>(),
                provider.GetRequiredService<TemplateMatchOperator>(),
                provider.GetRequiredService<FindLineOperator>(),
                provider.GetRequiredService<FindCircleOperator>(),
                provider.GetRequiredService<MeasureDistanceOperator>(),
                provider.GetRequiredService<CalibrationTransformOperator>()
            }));

        services.AddSingleton<IInferenceEngineRegistry>(provider => new InferenceEngineRegistry(
            type => ActivatorUtilities.CreateInstance(provider, type)));

        return services;
    }

    private static void RegisterOperators(IServiceCollection services)
    {
        services.AddSingleton<GrayOperator>();
        services.AddSingleton<FilterOperator>();
        services.AddSingleton<MorphologyOperator>();
        services.AddSingleton<RoiOperator>();
        services.AddSingleton<ThresholdOperator>();
        services.AddSingleton<BlobFindOperator>();
        services.AddSingleton<TemplateMatchOperator>();
        services.AddSingleton<FindLineOperator>();
        services.AddSingleton<FindCircleOperator>();
        services.AddSingleton<MeasureDistanceOperator>();
        services.AddSingleton<CalibrationTransformOperator>();
    }
}
