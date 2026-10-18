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

            // Try to wire optional AI model (ONNX) if available
            try
            {
                var modelPath = Environment.GetEnvironmentVariable("PHOTOCULL_MODEL")
                                ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCull", "models", "siglip-vision.onnx");
                if (System.IO.File.Exists(modelPath))
                {
                    Log.Logger.Information("Loading ONNX model from {Model}", modelPath);
                    var engine = new PhotoCull.Ai.Inference.OnnxInferenceEngine(modelPath);
                    services.AddSingleton<PhotoCull.Core.Abstractions.IInferenceEngine>(engine);
                    // default to SigLIP preset when model present
                    services.AddSingleton<PhotoCull.Core.Abstractions.IEmbeddingService>(sp => new PhotoCull.Ai.Embeddings.ImageEmbeddingService(engine, sp.GetRequiredService<PhotoCull.Core.Abstractions.IImageRepository>(), PhotoCull.Ai.Embeddings.PreprocessingOptions.SiglipPreset, null, sp.GetRequiredService<PhotoCull.Core.Abstractions.IImageDecoder>()));
                }
                else
                {
                    Log.Logger.Information("ONNX model not found at {Model}; embeddings will be disabled.", modelPath);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to initialize ONNX engine; embeddings disabled.");
            }

            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton(Log.Logger);

            Services = services.BuildServiceProvider();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var wnd = new MainWindow();
            wnd.Activate();
        }
    }
}