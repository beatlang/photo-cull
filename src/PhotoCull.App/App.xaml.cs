using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using PhotoCull.Infrastructure;
using PhotoCull.App.ViewModels;

namespace PhotoCull.App
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; }
        private readonly string _modelPath;
        private string? _startupError;

        public App()
        {
            this.InitializeComponent();

            // configure logging
            var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCull","logs");
            System.IO.Directory.CreateDirectory(logDir);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(System.IO.Path.Combine(logDir, "photocull-.log"), rollingInterval: Serilog.RollingInterval.Day)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddPhotoCull();

            var configuredModelPath = Environment.GetEnvironmentVariable("PHOTOCULL_MODEL");
            _modelPath = string.IsNullOrWhiteSpace(configuredModelPath)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCull", "models", "siglip-vision.onnx")
                : configuredModelPath;

            if (!System.IO.File.Exists(_modelPath))
            {
                _startupError = $"The required AI model was not found at:{Environment.NewLine}{_modelPath}{Environment.NewLine}{Environment.NewLine}Download it with scripts\\download-models.ps1, then restart PhotoCull, or set PHOTOCULL_MODEL to a valid ONNX model file.";
                Log.Logger.Error("Required ONNX model not found at {Model}", _modelPath);
            }
            else
            {
                try
                {
                    Log.Logger.Information("Loading required ONNX model from {Model}", _modelPath);
                    var engine = new PhotoCull.Ai.Inference.OnnxInferenceEngine(_modelPath);
                    engine.Initialize();
                    services.AddSingleton<PhotoCull.Core.Abstractions.IInferenceEngine>(engine);
                    services.AddSingleton<PhotoCull.Core.Abstractions.IEmbeddingService>(sp => new PhotoCull.Ai.Embeddings.ImageEmbeddingService(engine, sp.GetRequiredService<PhotoCull.Core.Abstractions.IImageRepository>(), PhotoCull.Ai.Embeddings.PreprocessingOptions.SiglipPreset, null, sp.GetRequiredService<PhotoCull.Core.Abstractions.IImageDecoder>()));
                }
                catch (Exception ex)
                {
                    _startupError = $"The required AI model could not be loaded from:{Environment.NewLine}{_modelPath}{Environment.NewLine}{Environment.NewLine}{ex.Message}";
                    Log.Logger.Error(ex, "Failed to load required ONNX model from {Model}", _modelPath);
                }
            }

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton(Log.Logger);

            Services = services.BuildServiceProvider();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var wnd = new MainWindow(_startupError);
            wnd.Activate();
        }
    }
}