using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using PCull.Infrastructure;
using PCull.App.ViewModels;
using PCull.App.Settings;

namespace PCull.App
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; }
        private readonly string _modelPath;
        private readonly List<string> _startupErrors = new();
        private readonly CullingSettingsStore _settingsStore;
        public CullingSettings CullingSettings { get; set; } = new();

        public App()
        {
            this.InitializeComponent();

            // configure logging
            var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pCull","logs");
            System.IO.Directory.CreateDirectory(logDir);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(System.IO.Path.Combine(logDir, "pcull-.log"), rollingInterval: Serilog.RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddPCull();

            var settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pCull", "settings.json");
            _settingsStore = new CullingSettingsStore(settingsPath);
            try
            {
                CullingSettings = _settingsStore.Load();
            }
            catch (Exception ex)
            {
                _startupErrors.Add($"Culling settings could not be loaded:{Environment.NewLine}{settingsPath}{Environment.NewLine}{Environment.NewLine}{ex.Message}");
                Log.Logger.Error(ex, "Failed to load culling settings from {SettingsPath}", settingsPath);
            }

            var configuredModelPath = Environment.GetEnvironmentVariable("PCULL_MODEL");
            _modelPath = string.IsNullOrWhiteSpace(configuredModelPath)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pCull", "models", "siglip-vision.onnx")
                : configuredModelPath;

            if (!System.IO.File.Exists(_modelPath))
            {
                _startupErrors.Add($"The required AI model was not found at:{Environment.NewLine}{_modelPath}{Environment.NewLine}{Environment.NewLine}Download it with scripts\\download-models.ps1, then restart pCull, or set PCULL_MODEL to a valid ONNX model file.");
                Log.Logger.Error("Required ONNX model not found at {Model}", _modelPath);
            }
            else
            {
                try
                {
                    Log.Logger.Information("Loading required ONNX model from {Model}", _modelPath);
                    var engine = new PCull.Ai.Inference.OnnxInferenceEngine(_modelPath);
                    engine.Initialize();
                    services.AddSingleton<PCull.Core.Abstractions.IInferenceEngine>(engine);
                    services.AddSingleton<PCull.Core.Abstractions.IEmbeddingService>(sp => new PCull.Ai.Embeddings.ImageEmbeddingService(engine, sp.GetRequiredService<PCull.Core.Abstractions.IImageRepository>(), PCull.Ai.Embeddings.PreprocessingOptions.SiglipPreset, null, sp.GetRequiredService<PCull.Core.Abstractions.IImageDecoder>()));
                }
                catch (Exception ex)
                {
                    _startupErrors.Add($"The required AI model could not be loaded from:{Environment.NewLine}{_modelPath}{Environment.NewLine}{Environment.NewLine}{ex.Message}");
                    Log.Logger.Error(ex, "Failed to load required ONNX model from {Model}", _modelPath);
                }
            }

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton(Log.Logger);

            Services = services.BuildServiceProvider();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var wnd = new MainWindow(
                _startupErrors.Count == 0 ? null : string.Join($"{Environment.NewLine}{Environment.NewLine}", _startupErrors),
                _settingsStore,
                CullingSettings);
            wnd.Activate();
        }
    }
}