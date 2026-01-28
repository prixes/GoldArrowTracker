using Archery.Shared.Models;

namespace GoldTracker.Mobile.Services;

public class ModelInitializationService
{
    public ObjectDetectionConfig? Config { get; private set; }
    public string? ModelPath { get; private set; }
    public bool IsInitialized { get; private set; }

    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        try 
        {
            // Run in parallel
            var configTask = ObjectDetectionConfig.LoadFromJsonAsync("Archery.Shared.Configurations.object_detection_config.json");
            var deployTask = Task.Run(() => ObjectDetectionModelDeploymentService.EnsureModelDeployed());

            await Task.WhenAll(configTask, deployTask);

            Config = await configTask;
            ModelPath = await deployTask;
            
            IsInitialized = true;
            System.Diagnostics.Debug.WriteLine("[ModelInitializationService] Initialization Complete.");
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[ModelInitializationService] Failed: {ex.Message}");
             throw;
        }
    }
}
