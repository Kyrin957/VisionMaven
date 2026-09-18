using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Core.Drivers;

/// <summary>
/// 设备驱动基类：统一状态机、事件与释放语义，屏蔽各驱动的重复样板。
/// 仅使用 BCL，可与契约层同处一个程序集。
/// </summary>
public abstract class DeviceDriverBase : IDeviceDriver
{
    private readonly object _sync = new();
    private DeviceState _state = DeviceState.Disconnected;
    private bool _disposed;

    protected DeviceDriverBase(IDeviceContext context, DeviceKind kind)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Kind = kind;
    }

    public abstract string DriverKey { get; }

    public string DeviceId => Context.DeviceId;

    public DeviceKind Kind { get; }

    public DeviceState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    protected IDeviceContext Context { get; }

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public async Task ConnectAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State == DeviceState.Ready)
        {
            return;
        }

        SetState(DeviceState.Connecting);
        try
        {
            await OnConnectAsync(ct).ConfigureAwait(false);
            SetState(DeviceState.Ready);
        }
        catch (OperationCanceledException)
        {
            SetState(DeviceState.Disconnected);
            throw;
        }
        catch (Exception ex)
        {
            SetState(DeviceState.Alarm, ex.Message);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await OnDisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            SetState(DeviceState.Disconnected);
        }
    }

    /// <summary>建立连接并完成初始化。</summary>
    protected abstract Task OnConnectAsync(CancellationToken ct);

    /// <summary>释放连接相关资源。</summary>
    protected virtual Task OnDisconnectAsync() => Task.CompletedTask;

    /// <summary>释放驱动自身持有的非托管资源。</summary>
    protected virtual void OnDispose()
    {
    }

    /// <summary>由派生的后台线程报告运行期异常。</summary>
    protected void ReportAlarm(string message) => SetState(DeviceState.Alarm, message);

    /// <summary>把状态恢复为就绪（例如重连成功后）。</summary>
    protected void ReportReady() => SetState(DeviceState.Ready);

    protected void SetState(DeviceState next, string? message = null)
    {
        DeviceState previous;
        lock (_sync)
        {
            if (_state == next)
            {
                return;
            }

            previous = _state;
            _state = next;
        }

        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(DeviceId, previous, next, message));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            OnDisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 释放路径上忽略断开异常。
        }

        OnDispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
