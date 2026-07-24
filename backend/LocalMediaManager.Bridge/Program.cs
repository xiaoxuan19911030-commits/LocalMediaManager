using System.Diagnostics;
using System.Text.Json;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;

string bridgeUrl = Environment.GetEnvironmentVariable("LMM_BRIDGE_URL")
    ?? "http://127.0.0.1:47831";
string nextDataRoot = Environment.GetEnvironmentVariable("LMM_DATA_ROOT")
    ?? @"D:\Local Media Manager Next Data";
string installedRoot = Environment.GetEnvironmentVariable("LMM_LEGACY_ROOT")
    ?? nextDataRoot;
string databasePath = Environment.GetEnvironmentVariable("LMM_DATABASE_PATH")
    ?? Path.Combine(nextDataRoot, "data", "LocalMediaManager.db");
string installRoot = ResolveInstallRoot(AppContext.BaseDirectory);
string configDatabasePath = Environment.GetEnvironmentVariable("LMM_CONFIG_DATABASE_PATH")
    ?? Path.Combine(nextDataRoot, "config", "app_configs.sqlite");
string imageRoot = Environment.GetEnvironmentVariable("LMM_IMAGE_ROOT")
    ?? Path.Combine(nextDataRoot, "MediaStorage");
string? sessionToken = Environment.GetEnvironmentVariable("LMM_BRIDGE_TOKEN");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(bridgeUrl);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(new RatingHistoryService(databasePath));
builder.Services.AddSingleton<IMovieNumberExtractor>(_ => new MovieNumberExtractor(
    Path.Combine(AppContext.BaseDirectory, "movie-number-rules.json")));
builder.Services.AddSingleton(serviceProvider => new MovieNumberManagementService(
    databasePath, serviceProvider.GetRequiredService<IMovieNumberExtractor>()));
builder.Services.AddSingleton(serviceProvider => new ProductWriter(databasePath, serviceProvider.GetRequiredService<RatingHistoryService>()));
builder.Services.AddSingleton(new PlaybackSettingsService(databasePath, configDatabasePath));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(serviceProvider => new LibraryWorkflowService(
    databasePath, serviceProvider.GetRequiredService<IMovieNumberExtractor>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<LibraryWorkflowService>());
builder.Services.AddSingleton(new MediaStoragePathResolver(databasePath, installRoot));
builder.Services.AddSingleton(new MetadataProviderSettingsService(databasePath));
builder.Services.AddSingleton(new MetadataWriteService(databasePath));
builder.Services.AddSingleton(serviceProvider => new MetadataHealthAnalysisService(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>()));
builder.Services.AddSingleton(sp => new MovieMetadataImporter(databasePath, sp.GetRequiredService<MetadataWriteService>()));
builder.Services.AddSingleton(sp => new MovieImageImporter(
    databasePath,
    imageRoot,
    sp.GetRequiredService<MediaStoragePathResolver>(),
    sp.GetRequiredService<ImageDownloadService>(),
    sp.GetRequiredService<IHttpClientFactory>()));
builder.Services.AddSingleton(new TaskLogService(databasePath));
builder.Services.AddSingleton(new FfmpegLocator(databasePath, AppContext.BaseDirectory));
builder.Services.AddSingleton(new FfmpegPluginSettingsService(databasePath));
builder.Services.AddSingleton(new RenameSettingsService(databasePath));
builder.Services.AddSingleton<IPersonDetectionService>(_ => new OnnxPersonDetectionService(
    Path.Combine(AppContext.BaseDirectory, "models", "ssd_mobilenet_v1_12-int8.onnx")));
builder.Services.AddSingleton(new ImageAssetService(databasePath, imageRoot));
builder.Services.AddSingleton(serviceProvider => new ImageWorkflowService(
    databasePath,
    imageRoot,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>()));
builder.Services.AddSingleton(new DataSafetyService(databasePath, configDatabasePath, imageRoot));
builder.Services.AddSingleton(new LogMaintenanceService());
builder.Services.AddSingleton(serviceProvider => new UpdateCheckService(serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(), databasePath));
builder.Services.AddSingleton<PlatformCommandService>();
builder.Services.AddSingleton<ImageDownloadService>();
builder.Services.AddSingleton(serviceProvider => new NfoService(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>()));
builder.Services.AddSingleton<MovieNfoExporter>();
builder.Services.AddSingleton(serviceProvider => new SettingsSaveCoordinator(
    databasePath,
    installRoot,
    configDatabasePath,
    serviceProvider.GetRequiredService<MetadataProviderSettingsService>(),
    serviceProvider.GetRequiredService<NfoService>(),
    serviceProvider.GetRequiredService<PlaybackSettingsService>(),
    serviceProvider.GetRequiredService<RatingHistoryService>()));
builder.Services.AddSingleton<MetaTubeProvider>();
builder.Services.AddSingleton<MdcNgProvider>();
builder.Services.AddSingleton<MetadataSyncService>();
builder.Services.AddSingleton<JavBusProvider>();
builder.Services.AddSingleton<DmmProvider>();
builder.Services.AddSingleton<JavDbProvider>();
builder.Services.AddSingleton<MinnanoActorProfileProvider>();
builder.Services.AddSingleton<WikipediaJpActorProfileProvider>();
builder.Services.AddSingleton(new ActorProfileService(databasePath));
builder.Services.AddSingleton(serviceProvider => new ActorProfileProviderService(
    databasePath,
    serviceProvider.GetRequiredService<MetadataProviderSettingsService>(),
    serviceProvider.GetRequiredService<MinnanoActorProfileProvider>(),
    serviceProvider.GetRequiredService<WikipediaJpActorProfileProvider>(),
    serviceProvider.GetRequiredService<ActorProfileService>(),
    serviceProvider.GetRequiredService<MovieImageImporter>(),
    serviceProvider.GetRequiredService<JavBusProvider>()));
builder.Services.AddSingleton(serviceProvider => new ActorProfileCompleteTaskService(
    databasePath,
    serviceProvider.GetRequiredService<ActorProfileProviderService>(),
    serviceProvider.GetRequiredService<TaskLogService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ActorProfileCompleteTaskService>());
builder.Services.AddSingleton<ProviderDiagnosticsService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ProviderDiagnosticsService>());
builder.Services.AddSingleton<IMetadataProvider, CompositeMetadataProvider>();
builder.Services.AddSingleton<IMetadataCompletionProviderClient, MetadataCompletionProviderClient>();
builder.Services.AddSingleton(serviceProvider => new MetadataSyncExecutor(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>(),
    serviceProvider.GetRequiredService<MetadataProviderSettingsService>(),
    serviceProvider.GetRequiredService<ProviderDiagnosticsService>(),
    serviceProvider.GetRequiredService<IMetadataProvider>(),
    serviceProvider.GetRequiredService<MetadataWriteService>(),
    serviceProvider.GetRequiredService<ImageDownloadService>(),
    serviceProvider.GetRequiredService<NfoService>(),
    serviceProvider.GetRequiredService<TaskLogService>(),
    serviceProvider.GetRequiredService<MovieImageImporter>(),
    serviceProvider.GetRequiredService<MetadataHealthAnalysisService>(),
    serviceProvider.GetRequiredService<IMovieNumberExtractor>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MetadataSyncExecutor>());
builder.Services.AddSingleton(serviceProvider => new ImageCacheTaskService(
    databasePath,
    serviceProvider.GetRequiredService<ImageAssetService>(),
    serviceProvider.GetRequiredService<TaskLogService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ImageCacheTaskService>());
builder.Services.AddSingleton(serviceProvider => new ImageGenerationTaskService(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>(),
    serviceProvider.GetRequiredService<ImageWorkflowService>(),
    serviceProvider.GetRequiredService<TaskLogService>(),
    serviceProvider.GetRequiredService<FfmpegLocator>(),
    serviceProvider.GetRequiredService<FfmpegPluginSettingsService>(),
    serviceProvider.GetRequiredService<IPersonDetectionService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ImageGenerationTaskService>());
builder.Services.AddSingleton(serviceProvider => new FileOrganizerService(
    databasePath, serviceProvider.GetRequiredService<TaskLogService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<FileOrganizerService>());
builder.Services.AddSingleton(serviceProvider => new MetadataRepairWorkflow(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>(),
    serviceProvider.GetRequiredService<MetadataHealthAnalysisService>(),
    serviceProvider.GetRequiredService<IMovieNumberExtractor>(),
    serviceProvider.GetRequiredService<TaskLogService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MetadataRepairWorkflow>());
builder.Services.AddSingleton(serviceProvider => new MetadataCompletionWorkflow(
    databasePath,
    serviceProvider.GetRequiredService<MediaStoragePathResolver>(),
    serviceProvider.GetRequiredService<MetadataProviderSettingsService>(),
    serviceProvider.GetRequiredService<IMetadataCompletionProviderClient>(),
    serviceProvider.GetRequiredService<MetadataWriteService>(),
    serviceProvider.GetRequiredService<ImageDownloadService>(),
    serviceProvider.GetRequiredService<NfoService>(),
    serviceProvider.GetRequiredService<MetadataHealthAnalysisService>(),
    serviceProvider.GetRequiredService<IMovieNumberExtractor>(),
    serviceProvider.GetRequiredService<TaskLogService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<MetadataCompletionWorkflow>());
builder.Services.AddSingleton(serviceProvider => new SafeDeleteWorkflowService(
    databasePath,
    serviceProvider.GetRequiredService<ProductWriter>(),
    serviceProvider.GetRequiredService<TaskLogService>(),
    serviceProvider.GetRequiredService<RatingHistoryService>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SafeDeleteWorkflowService>());
builder.Services.AddSingleton(serviceProvider => new DuplicateOrganizerWorkflowService(
    databasePath,
    serviceProvider.GetRequiredService<SafeDeleteWorkflowService>()));
builder.Services.AddSingleton(serviceProvider => new TaskCommandService(databasePath,
    serviceProvider.GetRequiredService<LibraryWorkflowService>(),
    serviceProvider.GetRequiredService<MetadataSyncExecutor>(),
    serviceProvider.GetRequiredService<ImageCacheTaskService>(),
    serviceProvider.GetRequiredService<FileOrganizerService>(),
    serviceProvider.GetRequiredService<ImageGenerationTaskService>(),
    serviceProvider.GetRequiredService<SafeDeleteWorkflowService>(),
    serviceProvider.GetRequiredService<ActorProfileCompleteTaskService>()));

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) => {
    try {
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS")) {
            if (string.IsNullOrWhiteSpace(sessionToken)) {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { code = "WRITE_SESSION_UNAVAILABLE", message = "Bridge 未由受信任的桌面会话启动，写入已禁用。" });
                return;
            }
            if (!context.Request.Headers.TryGetValue("X-LMM-Session", out var supplied) || supplied != sessionToken) {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code = "INVALID_SESSION", message = "Bridge 会话凭据无效。" });
                return;
            }
        }
        await next();
    } catch (KeyNotFoundException error) {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { code = "NOT_FOUND", message = error.Message });
    } catch (UnauthorizedAccessException error) {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "CONFIRMATION_REQUIRED", message = error.Message });
    } catch (ArgumentException error) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code = "INVALID_INPUT", message = error.Message });
    } catch (InvalidOperationException error) {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "CONFLICT", message = error.Message });
    } catch (Exception error) {
        Console.Error.WriteLine(error);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "操作失败，数据库未提交更改。" });
    }
});

app.MapGet("/health", () => Results.Ok(new {
    product = "Local Media Manager",
    abbreviation = "LMM",
    version = "0.7.2",
    status = "ok",
    databaseAvailable = File.Exists(databasePath),
    databasePath,
    configDatabaseAvailable = File.Exists(configDatabasePath),
    configDatabasePath,
    readOnly = false,
    writeEnabled = !string.IsNullOrWhiteSpace(sessionToken),
    sessionAuthentication = "X-LMM-Session",
    dataSeparated = true,
    legacyDatabaseUsedForRuntime = false,
}));

app.MapGet("/api/settings", async (MetadataProviderSettingsService settings) =>
    Results.Ok(await SettingsReader.ReadAsync(configDatabasePath, await settings.ReadMetaTubeAsync())));
app.MapGet("/api/settings/all", async (SettingsSaveCoordinator coordinator, CancellationToken token) =>
    Results.Ok(await coordinator.ReadAsync(token)));
app.MapGet("/api/settings/defaults", (SettingsSaveCoordinator coordinator) =>
    Results.Ok(coordinator.DefaultSettings()));
app.MapPut("/api/settings/all", async (UnifiedSettingsDto input, bool? createMissingMediaStorageRoot, SettingsSaveCoordinator coordinator, CancellationToken token) =>
    Results.Ok(await coordinator.SaveAsync(input, createMissingMediaStorageRoot == true, token)));
app.MapPut("/api/settings/providers/metatube", async (MetaTubeSettingsDto input, MetadataProviderSettingsService settings) =>
    Results.Ok(await settings.SaveMetaTubeAsync(input)));
app.MapPost("/api/settings/providers/mdc-ng/test", async (MdcNgSettingsDto input, MdcNgProvider provider, MetadataProviderSettingsService settingsService) =>
    Results.Ok(await provider.TestConnectionAsync(new(await settingsService.ReadMetaTubeAsync(), await settingsService.ReadJavBusAsync(), Network: await settingsService.ReadNetworkAsync()) { MdcNg = MetadataProviderSettingsService.NormalizeMdcNg(input) }, CancellationToken.None)));
app.MapPost("/api/settings/providers/mdc-ng/scrape-preview", async (MdcNgScrapeCommand input, MetadataSyncService sync, CancellationToken token) =>
    Results.Ok(await sync.SyncAsync(new(input.Code ?? "", input.MoviePath, MetadataSyncService.MdcNgProviderId), token)));
app.MapPost("/api/settings/providers/metatube/test", async (MetaTubeSettingsDto input, MetaTubeProvider provider, MetadataProviderSettingsService settingsService) =>
    Results.Ok(await provider.TestConnectionAsync(new(input with { BaseUrl = input.BaseUrl.Trim().TrimEnd('/') + "/" }, await settingsService.ReadJavBusAsync(), Network: await settingsService.ReadNetworkAsync()) { MdcNg = await settingsService.ReadMdcNgAsync() }, CancellationToken.None)));
app.MapPost("/api/settings/providers/javbus/test", async (JavBusSettingsDto input, JavBusProvider provider, MetadataProviderSettingsService settingsService) =>
    Results.Ok(await provider.TestConnectionAsync(new(await settingsService.ReadMetaTubeAsync(), MetadataProviderSettingsService.NormalizeJavBus(input), Network: await settingsService.ReadNetworkAsync()) { MdcNg = await settingsService.ReadMdcNgAsync() }, CancellationToken.None)));
app.MapGet("/api/search/remote", async (string q, string? kind, JavDbProvider provider, MetadataProviderSettingsService settingsService, CancellationToken token) =>
    Results.Ok(await provider.SearchKeywordAsync(q, kind ?? "code",
        new(await settingsService.ReadMetaTubeAsync(), await settingsService.ReadJavBusAsync(), null, await settingsService.ReadDmmAsync(), await settingsService.ReadJavDbAsync(), await settingsService.ReadNetworkAsync()), token)));
app.MapPost("/api/settings/providers/minnano/test", async (WebMetadataSettingsDto input, MinnanoActorProfileProvider provider) =>
    Results.Ok(await provider.TestConnectionAsync(MetadataProviderSettingsService.NormalizeWeb(input, "Minnano"), CancellationToken.None)));
app.MapPost("/api/settings/providers/wikipedia-jp/test", async (WebMetadataSettingsDto input, WikipediaJpActorProfileProvider provider) =>
    Results.Ok(await provider.TestConnectionAsync(MetadataProviderSettingsService.NormalizeWeb(input, "Wikipedia JP"), CancellationToken.None)));
app.MapPost("/api/settings/providers/diagnostics", async (ProviderDiagnosticsService service, CancellationToken token) =>
    Results.Ok(await service.ProbeAsync(token)));
app.MapGet("/api/actors/{actorId:long}/profile-preview", async (long actorId, string? source, ActorProfileProviderService service, CancellationToken token) =>
    Results.Ok(await service.PreviewAsync(actorId, source, token)));
app.MapPost("/api/actors/{actorId:long}/profile-apply", async (long actorId, ActorProfileCandidate candidate, ActorProfileProviderService service, CancellationToken token) =>
    Results.Ok(await service.ApplyAsync(actorId, candidate, token)));
app.MapPost("/api/actors/profile-complete", async (ActorProfileCompleteCommand command, ActorProfileCompleteTaskService service, CancellationToken token) =>
    Results.Ok(await service.EnqueueAsync(command, token)));
app.MapGet("/api/plugins/ffmpeg/status", (FfmpegLocator ffmpeg) =>
    Results.Ok(ffmpeg.Status()));
app.MapGet("/api/plugins/ffmpeg/settings", async (FfmpegPluginSettingsService settings, CancellationToken token) =>
    Results.Ok(await settings.ReadAsync(token)));
app.MapPut("/api/plugins/ffmpeg/settings", async (FfmpegPluginSettingsDto command, FfmpegPluginSettingsService settings, CancellationToken token) =>
    Results.Ok(await settings.SaveAsync(command, token)));
app.MapGet("/api/plugins/ffmpeg/person-detection", (IPersonDetectionService detector) =>
    Results.Ok(new { available = detector.IsAvailable, unavailableReason = detector.UnavailableReason }));
app.MapGet("/api/settings/rename", async (RenameSettingsService settings, CancellationToken token) =>
    Results.Ok(await settings.ReadAsync(token)));
app.MapPut("/api/settings/rename", async (RenameSettingsDto command, RenameSettingsService settings, CancellationToken token) =>
    Results.Ok(await settings.SaveAsync(command, token)));
app.MapGet("/api/plugins/mdc-ng/status", async (MdcNgProvider provider, MetadataProviderSettingsService settings, CancellationToken token) =>
    Results.Ok(await provider.StatusAsync(await settings.ReadMdcNgAsync(), token)));
app.MapGet("/api/settings/data-safety/overview", async (DataSafetyService safety) =>
    Results.Ok(await safety.OverviewAsync()));
app.MapPost("/api/settings/data-safety/backup", async (BackupCreateCommand command, DataSafetyService safety, CancellationToken token) =>
    Results.Ok(await safety.CreateBackupAsync(command, token)));
app.MapGet("/api/settings/data-safety/backup/validate", async (string path, DataSafetyService safety, CancellationToken token) =>
    Results.Ok(await safety.ValidateBackupAsync(path, token)));
app.MapPost("/api/settings/data-safety/restore-plan", async (RestorePlanCommand command, DataSafetyService safety, CancellationToken token) =>
    Results.Ok(await safety.CreateRestorePlanAsync(command, token)));
app.MapGet("/api/settings/export", async (MetadataProviderSettingsService settings, DataSafetyService safety) =>
    Results.Ok(await safety.ExportSettingsAsync(await settings.ReadMetaTubeAsync())));
app.MapPost("/api/settings/import-preview", async (JsonElement payload, DataSafetyService safety) =>
    Results.Ok(await safety.PreviewSettingsImportAsync(payload)));
app.MapGet("/api/settings/diagnostics", async (DataSafetyService safety, CancellationToken token) =>
    Results.Ok(await safety.DiagnosticsAsync(token)));
app.MapGet("/api/system/logs/cleanup-preview", (int? retentionDays, bool? includeAllHistory, LogMaintenanceService logs) =>
    Results.Ok(logs.Preview(retentionDays ?? 30, includeAllHistory == true)));
app.MapPost("/api/system/logs/cleanup", (LogCleanupCommand command, LogMaintenanceService logs) =>
    Results.Ok(logs.Cleanup(command)));
app.MapPost("/api/system/update/check", async (UpdateCheckService updates, CancellationToken token) =>
    Results.Ok(await updates.CheckAsync(token)));

app.MapGet("/api/dashboard", async (bool? refresh, MetadataHealthAnalysisService healthService, CancellationToken token) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (refresh == true) healthService.Invalidate();
    MetadataHealthSummary health = await healthService.GetAsync(token);
    return Results.Ok(await ProductReader.ReadDashboardAsync(databasePath, bridgeUrl, health));
});

app.MapGet("/api/search", async (string? q, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.SearchAsync(databasePath, bridgeUrl, q ?? string.Empty, Math.Clamp(limit ?? 12, 1, 48)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/libraries", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadLibrariesAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapPost("/api/libraries", async (LibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.CreateLibraryAsync(command)));
app.MapPut("/api/libraries/{libraryId:long}", async (long libraryId, LibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.UpdateLibraryAsync(libraryId, command)));
app.MapGet("/api/libraries/{libraryId:long}/delete-preview", async (long libraryId, LibraryWorkflowService service) =>
    Results.Ok(await service.PreviewDeleteLibraryAsync(libraryId)));
app.MapPost("/api/libraries/{libraryId:long}/delete", async (long libraryId, ConfirmCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.DeleteLibraryAsync(libraryId, command)));
app.MapGet("/api/libraries/{libraryId:long}/missing-cleanup-preview", async (long libraryId, LibraryWorkflowService service) =>
    Results.Ok(await service.PreviewMissingCleanupAsync(libraryId)));
app.MapPost("/api/libraries/{libraryId:long}/missing-cleanup", async (long libraryId, ConfirmCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.CleanupMissingAsync(libraryId, command)));
app.MapPost("/api/libraries/{libraryId:long}/scan", async (long libraryId, ScanLibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.StartScanAsync(libraryId, command)));

app.MapGet("/api/tasks", async (int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadTasksAsync(databasePath, limit is > 0 ? limit : null))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));
app.MapGet("/api/tasks/{taskId:long}/logs", async (long taskId, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadTaskLogsAsync(databasePath, taskId, Math.Clamp(limit ?? 200, 1, 1000)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));
app.MapPost("/api/tasks/{taskId:long}/pause", async (long taskId, TaskCommandService service) => Results.Ok(await service.PauseAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/resume", async (long taskId, TaskCommandService service) => Results.Ok(await service.ResumeAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/cancel", async (long taskId, TaskCommandService service) => Results.Ok(await service.CancelAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/retry", async (long taskId, TaskCommandService service) => Results.Ok(await service.RetryAsync(taskId)));
app.MapDelete("/api/tasks/{taskId:long}", async (long taskId, TaskCommandService service) => Results.Ok(await service.DeleteAsync(taskId)));
app.MapPost("/api/tasks/cleanup", async (TaskCleanupCommand command, TaskCommandService service) => Results.Ok(await service.CleanupAsync(command.Status)));
app.MapPost("/api/tasks/batch/cancel", async (IReadOnlyList<long> taskIds, TaskCommandService service) => Results.Ok(await service.CancelBatchAsync(taskIds)));
app.MapPost("/api/tasks/batch/cancel-sync", async (IReadOnlyList<long> taskIds, TaskCommandService service) => Results.Ok(await service.CancelSyncBatchAsync(taskIds)));
app.MapPost("/api/videos/{movieId:long}/sync", async (long movieId, string? source, MetadataSyncExecutor service) =>
    Results.Ok(await service.EnqueueAsync(movieId, "Manual", overwrite: false, source)));
app.MapPost("/api/videos/{movieId:long}/rescrape", async (long movieId, MetadataSyncExecutor service) => Results.Ok(await service.EnqueueAsync(movieId, "Rescrape", overwrite: true)));
app.MapPost("/api/videos/batch/sync", async (IReadOnlyList<long> movieIds, MetadataSyncExecutor service) => Results.Ok(await service.EnqueueBatchAsync(movieIds)));
app.MapPost("/api/videos/library/sync", async (SyncLibraryCommand command, MetadataSyncExecutor service) => Results.Ok(await service.EnqueueLibraryAsync(command.LibraryId)));
app.MapPost("/api/videos/filter-sync/preview", async (FilteredMovieSyncCommand command, MetadataSyncExecutor service, CancellationToken token) =>
    Results.Ok(await service.PreviewFilteredAsync(command, token)));
app.MapPost("/api/videos/filter-sync", async (FilteredMovieSyncCommand command, MetadataSyncExecutor service, CancellationToken token) =>
    Results.Ok(await service.EnqueueFilteredAsync(command, token)));
app.MapPost("/api/delete/preview", async (SafeDeletePreviewCommand command, SafeDeleteWorkflowService service, CancellationToken token) =>
    Results.Ok(await service.PreviewAsync(command, token)));
app.MapPost("/api/delete/execute", async (SafeDeleteExecuteRequest command, SafeDeleteWorkflowService service, CancellationToken token) =>
    Results.Ok(await service.ExecuteAsync(command, token)));
app.MapPost("/api/videos/{movieId:long}/images/{imageType}/replace", async (long movieId, string imageType, ImageReplaceCommand command, ImageWorkflowService service, CancellationToken token) =>
    Results.Ok(await service.ReplaceAsync(movieId, imageType, command.Path, token)));
app.MapPost("/api/videos/{movieId:long}/images/{imageType}/generate", async (long movieId, string imageType, ImageGenerationTaskService service, CancellationToken token) =>
    Results.Ok(await service.EnqueueAsync(movieId, imageType, token)));
app.MapGet("/api/image-assets/{imageId:long}/delete-preview", async (long imageId, ImageWorkflowService service, CancellationToken token) =>
    Results.Ok(await service.PreviewDeleteAsync(imageId, token)));
app.MapPost("/api/image-assets/{imageId:long}/delete", async (long imageId, ImageDeleteCommand command, ImageWorkflowService service, CancellationToken token) =>
    Results.Ok(await service.DeleteAsync(imageId, command.ConfirmationToken, token)));
app.MapPost("/api/image-assets/{imageId:long}/reveal", async (long imageId, ImageWorkflowService service, PlatformCommandService platform, CancellationToken token) =>
    Results.Ok(await service.RevealAsync(imageId, platform, token)));
app.MapPost("/api/image-assets/{imageId:long}/open-directory", async (long imageId, ImageWorkflowService service, PlatformCommandService platform, CancellationToken token) =>
    Results.Ok(await service.OpenDirectoryAsync(imageId, platform, token)));

app.MapPost("/api/platform/open-directory", (PlatformPathCommand command, PlatformCommandService platform) =>
    Results.Ok(platform.OpenDirectory(command.Path)));
app.MapPost("/api/platform/reveal-file", (PlatformPathCommand command, PlatformCommandService platform) =>
    Results.Ok(platform.RevealFile(command.Path)));
app.MapPost("/api/platform/open-url", (PlatformUrlCommand command, PlatformCommandService platform) =>
    Results.Ok(platform.OpenUrl(command.Url)));

app.MapGet("/api/entities/{entityType}", async (string entityType, string? search, string? sort, long? libraryId, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (entityType is not ("actors" or "tags" or "custom-tags" or "movie-tags" or "genres" or "directors" or "series" or "studios")) return Results.BadRequest("仅支持 actors、directors、series、studios、genres、tags 或 movie-tags。");
    return Results.Ok(await ProductReader.ReadEntitiesPageAsync(databasePath, bridgeUrl, entityType, search ?? "", sort ?? "count", Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0), libraryId));
});

app.MapGet("/api/entities/{entityType}/{entityId:long}/movies", async (string entityType, long entityId, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (entityType is not ("actors" or "tags" or "custom-tags" or "movie-tags" or "genres" or "directors" or "series" or "studios")) return Results.BadRequest("仅支持 actors、directors、series、studios、genres、tags 或 movie-tags。");
    return Results.Ok(await ProductReader.ReadEntityMoviesAsync(databasePath, bridgeUrl, entityType, entityId, Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)));
});

app.MapGet("/api/collections/{kind}", async (string kind, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (kind is not ("favorites" or "history")) return Results.BadRequest("仅支持 favorites 或 history。");
    return Results.Ok(await ProductReader.ReadCollectionAsync(databasePath, bridgeUrl, kind, Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)));
});

app.MapGet("/api/search/advanced", async (string? q, long? actorId, long? tagId, long? directorId, long? movieTagId, long? customTagId, long? genreId, long? seriesId, long? studioId, bool? favorite, bool? watched, double? ratingMin,
    string? metadata, string? fileStatus, string? metadataStatus, string? ratingFilter, long? libraryId, string? sort, int? limit, int? offset) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.AdvancedSearchAsync(databasePath, bridgeUrl, q ?? "", actorId, tagId, directorId, movieTagId, customTagId, seriesId, favorite,
        watched, Math.Clamp(ratingMin ?? 0, 0, 5), ratingFilter ?? "all", metadata ?? "all", fileStatus ?? "all", metadataStatus ?? "all", libraryId, sort ?? "newest",
        Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0), genreId, studioId))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/search/random", async (string? q, long? actorId, long? tagId, long? directorId, long? movieTagId, long? customTagId, long? genreId, long? seriesId, long? studioId, bool? favorite, bool? watched, double? ratingMin,
    string? metadata, string? fileStatus, string? metadataStatus, string? ratingFilter, long? libraryId, string? sort, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadRandomMovieAsync(databasePath, bridgeUrl, q ?? "", actorId, tagId, directorId, movieTagId, customTagId, seriesId, favorite,
        watched, Math.Clamp(ratingMin ?? 0, 0, 5), ratingFilter ?? "all", metadata ?? "all", fileStatus ?? "all", metadataStatus ?? "all", libraryId, sort ?? "newest",
        Math.Clamp(limit ?? 24, 1, 96), genreId, studioId))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/metadata/overview", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadMetadataOverviewAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/diagnostics", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadDiagnosticsAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/duplicates", async (string? rule, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadDuplicateResultsAsync(databasePath, rule ?? "all", Math.Clamp(limit ?? 100, 1, 200)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/maintenance/report", async (int? limit, int? offset) => File.Exists(databasePath)
    ? Results.Ok(await MaintenanceReader.ReadAsync(databasePath, imageRoot, bridgeUrl, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/metadata/health", async (MetadataHealthAnalysisService service, CancellationToken token) => File.Exists(databasePath)
    ? Results.Ok(await service.GetAsync(token))
    : Results.NotFound());
app.MapGet("/api/metadata/health/analysis", (MetadataHealthAnalysisService service) => Results.Ok(service.GetState()));
app.MapPost("/api/metadata/health/analysis", (MetadataHealthAnalysisService service) => Results.Ok(service.Start()));
app.MapPost("/api/metadata/health/analysis/cancel", (MetadataHealthAnalysisService service) => Results.Ok(service.Cancel()));
app.MapGet("/api/metadata/health/storage", async (MediaStoragePathResolver resolver, CancellationToken token) =>
    Results.Ok(await resolver.AvailabilityAsync(token)));
app.MapGet("/api/platform/path-exists", (string path) => Results.Ok(new { path, exists = File.Exists(path) }));

app.MapGet("/api/library/summary", async () => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM Movies";
    long count = (long)(await command.ExecuteScalarAsync() ?? 0L);
    return Results.Ok(new { videoCount = count, databasePath, readOnly = true });
});

app.MapGet("/api/videos", async (int? limit, int? offset, string? search, string? sort) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    int take = Math.Clamp(limit ?? 24, 1, 96);
    int skip = Math.Max(0, offset ?? 0);
    return Results.Ok(await ProductReader.ReadVideosPageAsync(databasePath, bridgeUrl, search ?? "", sort ?? "newest", take, skip));
});

app.MapGet("/api/videos/{movieId:long}", async (long movieId, IMovieNumberExtractor extractor) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    MovieDetailDto? detail = await ProductReader.ReadMovieAsync(databasePath, bridgeUrl, movieId, extractor);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapGet("/api/videos/{movieId:long}/neighbors", async (long movieId, string? search, string? sort) =>
    File.Exists(databasePath)
        ? Results.Ok(await ProductReader.ReadNeighborsAsync(databasePath, movieId, search ?? "", sort ?? "newest"))
        : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapPost("/api/videos/{movieId:long}/number/reidentify", async (long movieId, MovieNumberManagementService service) =>
    Results.Ok(await service.ReidentifyAsync(movieId)));
app.MapPut("/api/videos/{movieId:long}/number", async (long movieId, MovieNumberUpdateCommand command, MovieNumberManagementService service) =>
    Results.Ok(await service.ReidentifyAsync(movieId, command.Number)));

app.MapPatch("/api/videos/{movieId:long}/state", async (long movieId, UserStateCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetUserStateAsync(movieId, command)));

app.MapPost("/api/videos/batch/favorite", async (BatchFavoriteCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetFavoritesAsync(command)));
app.MapPost("/api/videos/batch/rating", async (BatchRatingCommand command, ProductWriter writer) => Results.Ok(await writer.SetRatingsAsync(command)));

app.MapPost("/api/tags", async (TagCommand command, ProductWriter writer) => {
    var created = await writer.CreateTagAsync(command);
    return Results.Ok(new { id = created.Id, created.Result.Changed, created.Result.AuditId, created.Result.Message });
});
app.MapPut("/api/tags/{tagId:long}", async (long tagId, TagCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateTagAsync(tagId, command)));
app.MapGet("/api/tags/{tagId:long}/delete-preview", async (long tagId, ProductWriter writer) =>
    Results.Ok(await writer.PreviewDeleteTagAsync(tagId)));
app.MapPost("/api/tags/{tagId:long}/delete", async (long tagId, ConfirmCommand command, ProductWriter writer) =>
    Results.Ok(await writer.DeleteTagAsync(tagId, command)));
app.MapPost("/api/operations/{auditId:long}/rollback", async (long auditId, ProductWriter writer) =>
    Results.Ok(await writer.RollbackAsync(auditId)));
app.MapPatch("/api/videos/{movieId:long}/tags", async (long movieId, MovieTagsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateMovieTagsAsync(movieId, command)));
app.MapPost("/api/videos/batch/tags", async (BatchTagsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateBatchTagsAsync(command)));

app.MapPut("/api/actors/{actorId:long}", async (long actorId, ActorCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateActorAsync(actorId, command)));
app.MapPut("/api/videos/{movieId:long}/actors", async (long movieId, MovieActorsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetMovieActorsAsync(movieId, command)));
app.MapGet("/api/actors/repair-preview", async (ProductWriter writer) => Results.Ok(await writer.PreviewActorRepairAsync()));
app.MapPost("/api/actors/repair", async (ConfirmCommand command, ProductWriter writer) => Results.Ok(await writer.ApplyActorRepairAsync(command)));

app.MapPost("/api/videos/{movieId:long}/remember-rating", async (long movieId, ProductWriter writer) =>
    Results.Ok(new { remembered = await writer.RememberDeletedRatingAsync(movieId) }));
app.MapPost("/api/videos/{movieId:long}/restore-rating", async (long movieId, ProductWriter writer) =>
    Results.Ok(new { restored = await writer.RestoreDeletedRatingAsync(movieId) }));
app.MapGet("/api/videos/{movieId:long}/delete-preview", async (long movieId, ProductWriter writer) =>
    Results.Ok(await writer.PreviewDeleteMovieAsync(movieId)));
app.MapPost("/api/videos/{movieId:long}/delete", async (long movieId, ConfirmCommand command, ProductWriter writer) =>
    Results.Ok(await writer.DeleteMovieAsync(movieId, command)));

app.MapGet("/api/covers/{code}", (string code) => {
    string? path = FindCover(imageRoot, code);
    return path is null
        ? Results.NotFound()
        : Results.File(path, ContentType(path), enableRangeProcessing: true);
});

app.MapGet("/api/images/{movieId:long}/primary", async (long movieId, string? variant, string? source, ImageAssetService images, CancellationToken token) => {
    ImageAssetContent? content = await images.ResolveMovieAsync(movieId, variant ?? "original", source, token);
    return content is null ? Results.NotFound() : Results.File(content.Path, content.ContentType, enableRangeProcessing: true);
});

app.MapGet("/api/videos/{movieId:long}/images", async (long movieId, ImageAssetService images, CancellationToken token) =>
    Results.Ok(await images.ReadMovieAssetsAsync(movieId, bridgeUrl, token)));
app.MapGet("/api/videos/{movieId:long}/images/status", async (long movieId, ImageAssetService images, CancellationToken token) =>
    Results.Ok(await images.ReadMovieStatusAsync(movieId, bridgeUrl, token)));

app.MapGet("/api/image-assets/{imageId:long}/content", async (long imageId, ImageAssetService images, CancellationToken token) => {
    ImageAssetContent? content = await images.ResolveAssetAsync(imageId, token);
    return content is null ? Results.NotFound() : Results.File(content.Path, content.ContentType, enableRangeProcessing: true);
});

app.MapPut("/api/image-assets/{imageId:long}/lock", async (long imageId, ImageLockCommand command, ImageAssetService images, CancellationToken token) =>
    Results.Ok(await images.SetLockAsync(imageId, command.Locked, token)));

app.MapPost("/api/videos/{movieId:long}/images/crop-card", async (long movieId, ImageCropCommand command, ImageWorkflowService images, CancellationToken token) =>
    Results.Ok(await images.CropCardAsync(movieId, command, token)));

app.MapGet("/api/images/cache/cleanup-preview", async (ImageAssetService images, CancellationToken token) =>
    Results.Ok(await images.PreviewCacheCleanupAsync(token)));

app.MapPost("/api/images/cache/cleanup", async (ImageCacheCleanupCommand command, ImageAssetService images, CancellationToken token) =>
    Results.Ok(await images.CleanupCacheAsync(command.ConfirmationToken, token)));

app.MapPost("/api/images/cache/rebuild", async (ImageCacheTaskService tasks) =>
    Results.Ok(await tasks.EnqueueAsync()));

app.MapGet("/api/videos/{movieId:long}/nfo/export-preview", async (long movieId, MovieNfoExporter exporter, CancellationToken token) =>
    Results.Ok(await exporter.PreviewAsync(movieId, token)));
app.MapPost("/api/videos/{movieId:long}/nfo/export", async (long movieId, NfoConfirmCommand command, bool? separateWhenLocked, MovieNfoExporter exporter, CancellationToken token) =>
    Results.Ok(await exporter.ExportAsync(movieId, command.ConfirmationToken, separateWhenLocked ?? false, token)));
app.MapGet("/api/videos/{movieId:long}/nfo/import-preview", async (long movieId, NfoService nfo, CancellationToken token) =>
    Results.Ok(await nfo.PreviewImportAsync(movieId, token)));
app.MapPost("/api/videos/{movieId:long}/nfo/import", async (long movieId, NfoConfirmCommand command, NfoService nfo, CancellationToken token) =>
    Results.Ok(await nfo.ImportAsync(movieId, command.ConfirmationToken, token)));
app.MapGet("/api/settings/nfo", async (NfoService nfo, CancellationToken token) =>
    Results.Ok(await nfo.ReadSettingsAsync(token)));
app.MapPut("/api/settings/nfo", async (NfoSettingsDto command, NfoService nfo, CancellationToken token) =>
    Results.Ok(await nfo.SaveSettingsAsync(command, token)));
app.MapGet("/api/settings/playback", async (PlaybackSettingsService playback, CancellationToken token) =>
    Results.Ok(await playback.ReadAsync(token)));
app.MapPut("/api/settings/playback", async (PlaybackSettingsDto command, PlaybackSettingsService playback, CancellationToken token) =>
    Results.Ok(await playback.SaveAsync(command, token)));
app.MapGet("/api/settings/rating-history", async (RatingHistoryService ratings, CancellationToken token) =>
    Results.Ok(await ratings.ReadSettingsAsync(token)));
app.MapPut("/api/settings/rating-history", async (RatingRetentionSettingsDto command, RatingHistoryService ratings, CancellationToken token) =>
    Results.Ok(await ratings.SaveSettingsAsync(command, token)));

app.MapPost("/api/organizer/dry-run", async (OrganizerPlanCommand command, FileOrganizerService organizer, CancellationToken token) =>
    Results.Ok(await organizer.DryRunAsync(command, token)));
app.MapGet("/api/organizer/{taskId:long}/preview", async (long taskId, FileOrganizerService organizer, CancellationToken token) =>
    Results.Ok(await organizer.PreviewAsync(taskId, token)));
app.MapPost("/api/organizer/{taskId:long}/execute", async (long taskId, OrganizerExecuteCommand command, FileOrganizerService organizer, CancellationToken token) =>
    Results.Ok(await organizer.ExecuteConfirmedAsync(taskId, command.ConfirmationToken, token)));
app.MapPost("/api/metadata/repair/dry-run", async (MetadataRepairScanCommand command, MetadataRepairWorkflow repair, CancellationToken token) =>
    Results.Ok(await repair.StartDryRunAsync(command, token)));
app.MapGet("/api/metadata/repair/{taskId:long}", async (long taskId, MetadataRepairWorkflow repair, CancellationToken token) =>
    Results.Ok(await repair.GetAsync(taskId, token)));
app.MapPost("/api/metadata/repair/{taskId:long}/execute", async (long taskId, MetadataRepairExecuteCommand command, MetadataRepairWorkflow repair, CancellationToken token) =>
    Results.Ok(await repair.ExecuteConfirmedAsync(taskId, command.ConfirmationToken, token)));
app.MapPost("/api/metadata/repair/{taskId:long}/cancel", async (long taskId, MetadataRepairWorkflow repair) =>
    Results.Ok(await repair.CancelAsync(taskId)));
app.MapPost("/api/metadata/repair/{taskId:long}/rollback", async (long taskId, MetadataRepairWorkflow repair, CancellationToken token) =>
    Results.Ok(await repair.RollbackAsync(taskId, token)));
app.MapPost("/api/metadata/repair/{taskId:long}/export", async (long taskId, MetadataRepairWorkflow repair, CancellationToken token) =>
    Results.Ok(await repair.ExportAsync(taskId, token)));
app.MapPost("/api/metadata/completion/dry-run", async (MetadataCompletionScanCommand command, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.StartDryRunAsync(command, token)));
app.MapGet("/api/metadata/completion/{taskId:long}", async (long taskId, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.GetAsync(taskId, token)));
app.MapPost("/api/metadata/completion/{taskId:long}/execute", async (long taskId, MetadataCompletionExecuteCommand command, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.ExecuteConfirmedAsync(taskId, command.ConfirmationToken, token)));
app.MapPost("/api/metadata/completion/{taskId:long}/pause", async (long taskId, MetadataCompletionWorkflow completion) =>
    Results.Ok(await completion.PauseAsync(taskId)));
app.MapPost("/api/metadata/completion/{taskId:long}/resume", async (long taskId, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.ResumeAsync(taskId, token)));
app.MapPost("/api/metadata/completion/{taskId:long}/rollback", async (long taskId, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.RollbackAsync(taskId, token)));
app.MapPost("/api/metadata/completion/{taskId:long}/export", async (long taskId, MetadataCompletionWorkflow completion, CancellationToken token) =>
    Results.Ok(await completion.ExportAsync(taskId, token)));
app.MapPost("/api/organizer/duplicates/preview-delete", async (DuplicateDeletePlanCommand command, DuplicateOrganizerWorkflowService organizer, CancellationToken token) =>
    Results.Ok(await organizer.PreviewAsync(command, token)));
app.MapPost("/api/organizer/duplicates/execute-delete", async (DuplicateDeleteExecuteRequest command, DuplicateOrganizerWorkflowService organizer, CancellationToken token) =>
    Results.Ok(await organizer.ExecuteAsync(command, token)));

app.MapGet("/api/actors/{actorId:long}/image", async (long actorId, ImageAssetService images, CancellationToken token) => {
    ImageAssetContent? content = await images.ResolveActorAsync(actorId, token);
    return content is null ? Results.NotFound() : Results.File(content.Path, content.ContentType, enableRangeProcessing: true);
});

app.MapGet("/api/actors/{actorId:long}", async (long actorId) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    ActorDetailDto? actor = await ProductReader.ReadActorAsync(databasePath, actorId);
    return actor is null ? Results.NotFound() : Results.Ok(actor);
});

app.MapPost("/api/videos/{dataId:long}/play", async (long dataId, ProductWriter writer, PlaybackSettingsService playback) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,COALESCE(FilePath, '') FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 AND MediaType='Video' LIMIT 1";
    command.Parameters.AddWithValue("$id", dataId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound("影片没有可播放的主文件。");
    long mediaFileId = reader.GetInt64(0);
    string path = reader.GetString(1);
    if (!File.Exists(path))
        return Results.NotFound($"影片文件不存在：{path}");
    if (!IsVideoFile(path))
        return Results.BadRequest($"该记录不是可播放的影片文件：{path}");

    string? configuredPlayer = (await playback.ReadAsync()).PlayerPath;
    var startInfo = new ProcessStartInfo { UseShellExecute = true };
    if (!string.IsNullOrWhiteSpace(configuredPlayer) && File.Exists(configuredPlayer)) {
        startInfo.FileName = configuredPlayer;
        startInfo.ArgumentList.Add(path);
    } else {
        startInfo.FileName = path;
    }
    DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    Process? player = Process.Start(startInfo);
    if (player is null) return Results.Problem("播放器未能启动。", statusCode: 502);
    _ = Task.Run(async () => {
        try {
            await player.WaitForExitAsync();
            if (player.ExitCode == 0)
                await writer.RecordPlaybackAsync(dataId, mediaFileId, Path.GetFileName(startInfo.FileName), startedAt, DateTimeOffset.UtcNow);
        } catch (Exception error) { Console.Error.WriteLine($"Playback tracking failed: {error}"); }
        finally { player.Dispose(); }
    });
    return Results.Ok(new { started = true, path, trackingWritten = false, trackingMode = "on-normal-exit" });
});

Console.WriteLine($"Local Media Manager Bridge: {bridgeUrl}");
Console.WriteLine($"Database (read/write via authenticated commands): {databasePath}");
await app.RunAsync();

static string ResolveInstallRoot(string baseDirectory)
{
    string current = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    DirectoryInfo? directory = new(current);
    if (directory.Name.Equals("bridge", StringComparison.OrdinalIgnoreCase)
        && directory.Parent?.Name.Equals("resources", StringComparison.OrdinalIgnoreCase) == true
        && directory.Parent.Parent is not null)
    {
        return directory.Parent.Parent.FullName;
    }
    if (directory.Name.Equals("resources", StringComparison.OrdinalIgnoreCase)
        && directory.Parent is not null)
    {
        return directory.Parent.FullName;
    }
    return current;
}

static async Task<SqliteConnection> OpenReadOnlyAsync(string databasePath)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Shared,
    }.ToString());
    await connection.OpenAsync();
    return connection;
}

static string? FindCover(string imageRoot, string code)
{
    if (string.IsNullOrWhiteSpace(code) || Path.GetFileName(code) != code)
        return null;
    foreach (string folder in new[] { "Covers", "Posters", "Thumbnails", "WallCrops" })
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".webp" }) {
            string candidate = Path.Combine(imageRoot, folder, code, code + extension);
            if (File.Exists(candidate))
                return candidate;
        }
    return null;
}

static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
    ".png" => "image/png",
    ".webp" => "image/webp",
    _ => "image/jpeg",
};

static bool IsVideoFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
    ".mp4" or ".mkv" or ".avi" or ".wmv" or ".mov" or ".ts" or ".m2ts" or ".flv" or ".webm"
    or ".vob" or ".mpg" or ".mpeg";
