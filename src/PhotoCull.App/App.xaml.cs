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