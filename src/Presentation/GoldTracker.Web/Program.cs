using GoldTracker.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using GoldTracker.Web.Services;
using GoldTracker.Shared.UI.Services.Abstractions;
using Archery.Shared.Models;
using Archery.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Add MudBlazor services
builder.Services.AddMudServices();

// Register platform-specific services
// builder.Services.AddScoped<IMediaPickerService, BrowserMediaPickerService>();
builder.Services.AddScoped<IPlatformImageService, BrowserImageService>();
// builder.Services.AddScoped<IFileStorageService, BrowserFileStorageService>();
builder.Services.AddScoped<ICameraService, WebCameraService>();

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

// Register Object Detection Service (browser version)
builder.Services.AddScoped<IObjectDetectionService>(sp =>
{
    var config = sp.GetRequiredService<ObjectDetectionConfig>();
    var preprocessor = new DefaultImagePreprocessor(); // Browser uses default preprocessor
    
    // For browser, we'll use the ONNX model from wwwroot
    string modelPath = "wwwroot/ObjectModels/object_detection_model.onnx";
    
    return new ObjectDetectionService(modelPath, config, preprocessor);
});

// Register Target Scoring Service
builder.Services.AddScoped<ITargetScoringService, TargetScoringService>();

// Register Session Services (browser version uses localStorage)
builder.Services.AddScoped<ISessionService, BrowserSessionService>();
builder.Services.AddScoped<ISessionState, BrowserSessionState>();

await builder.Build().RunAsync();
