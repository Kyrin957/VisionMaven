using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Drivers;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Devices.Mes;

/// <summary>MES HTTP / WebAPI 客户端：HttpClient + Polly 重试，契约 JSON 由模板配置。</summary>
[VisionDriver("mes.http", DeviceKind.Mes, DisplayName = "MES HTTP 客户端", SdkHint = "无需额外 SDK")]
public sealed class MesHttpClient : DeviceDriverBase, IMesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<MesHttpClient> _logger;
    private readonly ISecretProtector _protector;
    private readonly ResiliencePipeline _pipeline;
    private HttpClient? _client;

    public MesHttpClient(IDeviceContext context, ISecretProtector protector, ILogger<MesHttpClient> logger)
        : base(context, DeviceKind.Mes)
    {
        _protector = protector;
        _logger = logger;

        var retryCount = Math.Clamp(context.GetInt("retryCount", 3), 0, 10);
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retryCount,
                Delay = TimeSpan.FromMilliseconds(context.GetInt("retryDelayMs", 300)),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>().Handle<TimeoutException>()
            })
            .Build();
    }

    public override string DriverKey => "mes.http";

    private string BaseUrl => Context.GetString("baseUrl", "http://mes.local/api").TrimEnd('/');

    private string UploadPath => Context.GetString("uploadPath", "/inspection/upload");

    private string HeartbeatPath => Context.GetString("heartbeatPath", "/heartbeat");

    private int TimeoutMs => Context.GetInt("timeoutMs", 3000);

    private string? TokenHeader => Context.GetString("tokenHeader", "X-Token");

    private string? Token
    {
        get
        {
            var raw = Context.GetString("token", string.Empty);
            return string.IsNullOrWhiteSpace(raw) ? null : _protector.Unprotect(raw);
        }
    }

    protected override Task OnConnectAsync(CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromMilliseconds(Math.Max(100, TimeoutMs))
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromMilliseconds(Math.Max(200, TimeoutMs))
        };

        if (Token is { Length: > 0 } token && TokenHeader is { Length: > 0 } header)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header, token);
        }

        _client = client;
        _logger.LogInformation("MES 客户端已就绪：{BaseUrl}", BaseUrl);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    public async Task<MesResult> UploadInspectionAsync(InspectionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        var client = _client
                     ?? throw new CommunicationException(ErrorCodes.DeviceDisconnected, "MES 客户端未初始化");

        var payload = new MesInspectionPayload
        {
            ProjectId = record.ProjectId,
            StationId = record.StationId,
            CameraId = record.CameraId,
            SequenceNo = record.SequenceNo,
            Result = record.Result.ToString(),
            Score = record.Score,
            ElapsedMs = record.ElapsedMs,
            InspectedAt = record.InspectedAt,
            Defects = record.Defects
                .Select(defect => new MesDefect
                {
                    ClassName = defect.ClassName,
                    Confidence = defect.Confidence,
                    X = defect.X,
                    Y = defect.Y,
                    Width = defect.Width,
                    Height = defect.Height
                })
                .ToArray()
        };

        try
        {
            var response = await _pipeline
                .ExecuteAsync(
                    async token => await client
                        .PostAsJsonAsync(UploadPath, payload, JsonOptions, token)
                        .ConfigureAwait(false),
                    ct)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MES 上报失败：状态码={Status}", (int)response.StatusCode);
                return new MesResult(false, ((int)response.StatusCode).ToString(), body);
            }

            var envelope = TryParseEnvelope(body);
            _logger.LogInformation(
                "MES 上报完成：工位={StationId} 业务码={Code}",
                record.StationId,
                envelope?.Code);

            return new MesResult(true, envelope?.Code, envelope?.Message ?? body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new MesResult(false, ErrorCodes.CommTimeout, "MES 上报超时");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ReportAlarm(ex.Message);
            return new MesResult(false, ErrorCodes.CommWriteFailed, ex.Message);
        }
    }

    public async Task<bool> HeartbeatAsync(CancellationToken ct)
    {
        var client = _client;
        if (client is null)
        {
            return false;
        }

        try
        {
            var response = await _pipeline
                .ExecuteAsync(
                    async token => await client.GetAsync(HeartbeatPath, token).ConfigureAwait(false),
                    ct)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "MES 心跳失败");
            return false;
        }
    }

    private static MesEnvelope? TryParseEnvelope(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MesEnvelope>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class MesInspectionPayload
    {
        public string ProjectId { get; set; } = string.Empty;

        public string StationId { get; set; } = string.Empty;

        public string CameraId { get; set; } = string.Empty;

        public long SequenceNo { get; set; }

        public string Result { get; set; } = string.Empty;

        public double Score { get; set; }

        public double ElapsedMs { get; set; }

        public DateTimeOffset InspectedAt { get; set; }

        public MesDefect[] Defects { get; set; } = Array.Empty<MesDefect>();
    }

    private sealed class MesDefect
    {
        public string ClassName { get; set; } = string.Empty;

        public double Confidence { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private sealed class MesEnvelope
    {
        public string? Code { get; set; }

        public string? Message { get; set; }
    }
}

/// <summary>MES HTTP 客户端驱动注册。</summary>
public sealed class MesHttpClientProvider : IDeviceDriverProvider
{
    private readonly ISecretProtector _protector;
    private readonly ILoggerFactory _loggerFactory;

    public MesHttpClientProvider(ISecretProtector protector, ILoggerFactory loggerFactory)
    {
        _protector = protector;
        _loggerFactory = loggerFactory;
    }

    public string DriverKey => "mes.http";

    public DeviceKind Kind => DeviceKind.Mes;

    public string DisplayName => "MES HTTP 客户端";

    public string? SdkHint => "无需额外 SDK";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public ParameterSchema Schema { get; } = new()
    {
        new TextParameter("baseUrl", "服务地址", "http://mes.local/api") { Required = true },
        new TextParameter("uploadPath", "上报路径", "/inspection/upload"),
        new TextParameter("heartbeatPath", "心跳路径", "/heartbeat"),
        new IntegerParameter("timeoutMs", "超时", 100, 120000, 3000) { Unit = "ms" },
        new IntegerParameter("retryCount", "重试次数", 0, 10, 3),
        new IntegerParameter("retryDelayMs", "重试间隔", 50, 60000, 300) { Unit = "ms" },
        new TextParameter("tokenHeader", "令牌请求头", "X-Token"),
        new PasswordParameter("token", "令牌", string.Empty)
    };

    public IDeviceDriver Create(IDeviceContext context)
        => new MesHttpClient(context, _protector, _loggerFactory.CreateLogger<MesHttpClient>());
}
