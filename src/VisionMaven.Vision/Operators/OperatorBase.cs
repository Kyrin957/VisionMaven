using System.Diagnostics;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Exceptions;
using VisionMaven.Vision.Imaging;

namespace VisionMaven.Vision.Operators;

/// <summary>算子基类：统一计时、异常包装与输出图像封装。</summary>
public abstract class OperatorBase : IImageOperator
{
    public abstract string TypeKey { get; }

    public abstract ParameterSchema Schema { get; }

    public abstract string DisplayName { get; }

    public abstract Core.Domain.OperatorCategory Category { get; }

    public virtual int Order => 100;

    public Task<OperatorResult> ExecuteAsync(
        IImageFrame input,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var mat = FrameAccess.RequireMat(input);
            var result = Execute(mat, input, parameters, ct);
            stopwatch.Stop();

            return Task.FromResult(result with { ElapsedMs = stopwatch.ElapsedMilliseconds });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VisionMavenException ex)
        {
            return Task.FromResult(OperatorResult.Fail(ex.ErrorCode, ex.Message, stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorResult.Fail(
                ErrorCodes.DeviceAlarm,
                $"{DisplayName} 执行失败：{ex.Message}",
                stopwatch.ElapsedMilliseconds));
        }
    }

    /// <summary>算子实现入口。返回结果不含耗时，由基类填充。</summary>
    protected abstract OperatorResult Execute(
        Mat input,
        IImageFrame source,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken ct);

    /// <summary>把输出图像封装为帧，所有权交给调用方。</summary>
    protected static IImageFrame Wrap(Mat mat, IImageFrame source)
        => new MatImageFrame(source.CameraId, mat, 0, source.Timestamp);

    /// <summary>构造成功结果（含图像输出）。</summary>
    protected static OperatorResult OkWithImage(
        Mat output,
        IImageFrame source,
        IReadOnlyDictionary<string, object?>? outputs = null)
    {
        var frame = Wrap(output, source);
        var map = outputs is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(outputs);
        map["image"] = frame;

        return new OperatorResult
        {
            Success = true,
            Image = frame,
            Outputs = map
        };
    }
}
