using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Events;
using VisionMaven.Infrastructure.Configuration;
using VisionMaven.Infrastructure.Data;
using VisionMaven.Infrastructure.Events;
using VisionMaven.Infrastructure.Logging;
using VisionMaven.Infrastructure.Security;

namespace VisionMaven.Infrastructure.DependencyInjection;

/// <summary>Infrastructure 层服务注册。</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddVisionMavenInfrastructure(
        this IServiceCollection services,
        string? applicationRoot = null)
    {
        var paths = new ApplicationPathProvider(applicationRoot);
        services.AddSingleton<ApplicationPathProvider>(paths);
        services.AddSingleton<ILogPathProvider>(paths);

        services.AddSingleton<IVisionLogSink, VisionLogSink>();
        services.AddSingleton<IVisionEventBus, VisionEventBus>();

        services.AddSingleton<IDbContextFactory<VisionMavenDbContext>, VisionMavenDbContextFactory>();
        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<IInspectionRepository, InspectionRepository>();
        services.AddSingleton<IAlarmRepository, AlarmRepository>();
        services.AddSingleton<IStatisticsRepository, StatisticsRepository>();
        services.AddSingleton<IAuditRepository, AuditRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IRoleRepository, RoleRepository>();
        services.AddSingleton<ISystemSettingRepository, SystemSettingRepository>();

        services.AddSingleton<IProjectConfigurationService, ProjectConfigurationService>();
        services.AddSingleton<IProjectFileStorage, ProjectFileStorage>();

        services.AddSingleton<UserContext>();
        services.AddSingleton<IUserContext>(provider => provider.GetRequiredService<UserContext>());
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IUserManagementService, UserManagementService>();

        return services;
    }

    /// <summary>在宿主启动时初始化数据库。</summary>
    public static async Task InitializeVisionMavenStorageAsync(
        this IServiceProvider provider,
        CancellationToken ct)
    {
        var initializer = provider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync(ct).ConfigureAwait(false);
    }
}
