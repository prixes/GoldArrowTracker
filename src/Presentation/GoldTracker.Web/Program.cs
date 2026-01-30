using GoldTracker.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using GoldTracker.Web.Services;
using GoldTracker.Shared.UI.Services.Abstractions;
using GoldTracker.Shared.UI.Services;
using Archery.Shared.Models;
using Archery.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = config["Settings:ServerUrl"];
    
    // If not configured, use the current host address
    if (string.IsNullOrEmpty(serverUrl) || serverUrl.Contains("localhost"))
    {
        serverUrl = builder.HostEnvironment.BaseAddress;
    }
    
    return new HttpClient { BaseAddress = new Uri(serverUrl) };
});

// Add MudBlazor services
builder.Services.AddMudServices();

// Register platform-specific services
builder.Services.AddScoped<IPlatformImageService, BrowserImageService>();
builder.Services.AddScoped<ICameraService, WebCameraService>();
builder.Services.AddScoped<IServerAuthService, BrowserAuthService>();

// Register Object Detection Configuration
builder.Services.AddSingleton(sp =>
{
    try
    {
        return ObjectDetectionConfig.LoadFromJsonAsync("Archery.Shared.Configurations.object_detection_config.json").Result;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Program] Error loading ObjectDetectionConfig: {ex.Message}");
        throw;
    }
});

// Register Object Detection Service (browser version - no-op for now)
builder.Services.AddScoped<IObjectDetectionService, NoOpObjectDetectionService>();

// Register Target Scoring Service
builder.Services.AddScoped<ITargetScoringService, TargetScoringService>();

// Register Session Services (browser version uses localStorage)
builder.Services.AddScoped<BrowserSessionService>();
builder.Services.AddScoped<ISessionService>(sp => sp.GetRequiredService<BrowserSessionService>());
builder.Services.AddScoped<ISessionSyncService>(sp => sp.GetRequiredService<BrowserSessionService>());
builder.Services.AddScoped<ISessionState, BrowserSessionState>();
builder.Services.AddScoped<IPreferenceService, BrowserPreferenceService>();
builder.Services.AddScoped<IPlatformProvider, BrowserPlatformProvider>();
builder.Services.AddScoped<IDatasetSyncService, BrowserDatasetSyncService>();
builder.Services.AddScoped<IDatasetExportService, DatasetExportService>();

await builder.Build().RunAsync();
