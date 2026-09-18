using System.Collections.Concurrent;
using OpenCvSharp;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Vision.Imaging;

/// <summary>
/// Mat 缓冲池：按 (宽, 高, 类型) 复用底层缓冲，避免进入 GC 压力区。
/// 借出的帧被 Dispose 时归还缓冲；池自身 Dispose 时统一释放。
/// </summary>
public sealed class MatBufferPool : IBufferPool
{
    private const int MaxIdlePerBucket = 8;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<Mat>> _buckets = new();
    private bool _disposed;

    public int IdleCount => _buckets.Values.Sum(bucket => bucket.Count);

    public long TotalRented { get; private set; }

    public IImageFrame Rent(string cameraId, int width, int height, int channels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "缓冲尺寸必须为正");
        }

        var type = channels switch
        {
            4 => MatType.CV_8UC4,
            3 => MatType.CV_8UC3,
            _ => MatType.CV_8UC1
        };

        var key = $"{width}x{height}x{type.Value}";
        var bucket = _buckets.GetOrAdd(key, _ => new ConcurrentQueue<Mat>());

        Mat mat;
        if (!bucket.TryDequeue(out mat!) || mat.IsDisposed)
        {
            mat = new Mat(height, width, type);
        }

        Interlocked.Increment(ref _totalRented);
        return new PooledMatImageFrame(this, bucket, cameraId, mat);
    }

    private long _totalRented;

    private void Return(ConcurrentQueue<Mat> bucket, Mat mat)
    {
        if (_disposed || mat.IsDisposed)
        {
            mat.Dispose();
            return;
        }

        if (bucket.Count >= MaxIdlePerBucket)
        {
            mat.Dispose();
            return;
        }

        bucket.Enqueue(mat);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var bucket in _buckets.Values)
        {
            while (bucket.TryDequeue(out var mat))
            {
                mat.Dispose();
            }
        }

        _buckets.Clear();
    }

    /// <summary>池化帧：Dispose 时归还缓冲而非销毁。</summary>
    private sealed class PooledMatImageFrame : MatImageFrame
    {
        private readonly MatBufferPool _owner;
        private readonly ConcurrentQueue<Mat> _bucket;
        private Mat? _returned;

        public PooledMatImageFrame(MatBufferPool owner, ConcurrentQueue<Mat> bucket, string cameraId, Mat mat)
            : base(cameraId, mat)
        {
            _owner = owner;
            _bucket = bucket;
            _returned = mat;
        }

        public override void Dispose()
        {
            var mat = Interlocked.Exchange(ref _returned, null);
            if (mat is null)
            {
                return;
            }

            _owner.Return(_bucket, mat);
        }
    }
}
