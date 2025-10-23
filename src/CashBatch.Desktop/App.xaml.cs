using System.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using CashBatch.Application;
using CashBatch.Infrastructure;
using CashBatch.Infrastructure.Services;
using CashBatch.Integration;
using Microsoft.EntityFrameworkCore;
using CashBatch.Desktop.Services;

namespace CashBatch.Desktop;

public partial class App : System.Windows.Application
{
    public static IHost HostApp { get; private set; } = null!;
    private IServiceScope? _uiScope;

    protected override void OnStartup(StartupEventArgs e)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .UseSerilog((ctx, lc) =>
            {
                var path = ctx.Configuration["Logging:Path"] ?? "logs\\cashbatch-.log";
                lc.WriteTo.Console()
                  .WriteTo.File(path, rollingInterval: RollingInterval.Day);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.AddDbContextPool<AppDbContext>(o =>
                    o.UseSqlServer(ctx.Configuration.GetConnectionString("WriteDb")));
                services.AddPooledDbContextFactory<AppDbContext>(o =>
                    o.UseSqlServer(ctx.Configuration.GetConnectionString("WriteDb")));

                // Application services (interfaces in Application, impls in Infrastructure/Integration)
                services.AddScoped<IImportService, ImportService>();
                services.AddScoped<IMatchingService, MatchingService>();
                services.AddScoped<IBatchService, BatchService>();
                services.AddScoped<ILookupService, LookupService>();
                services.AddScoped<IERPExportService, ERPExportService>();
                services.AddScoped<ITemplateService, TemplateService>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();

                // ViewModels / Views (scoped to UI scope)
                services.AddScoped<MainViewModel>();
                services.AddScoped<MainWindow>();
            });

        HostApp = builder.Build();
        HostApp.Start();

        // Removed runtime schema alterations per request

        // Create a UI scope so scoped services (DbContext, etc.) can be injected
        _uiScope = HostApp.Services.CreateScope();
        var window = _uiScope.ServiceProvider.GetRequiredService<MainWindow>();
        window.Show();

        // Global UI exception handler
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        this.DispatcherUnhandledException -= App_DispatcherUnhandledException;
        _uiScope?.Dispose();
        HostApp.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Serilog.Log.Error(e.Exception, "Unhandled UI exception");
        var msg = e.Exception.Message;
        if (e.Exception.InnerException != null)
        {
            msg += System.Environment.NewLine + System.Environment.NewLine + "Details: " + e.Exception.InnerException.Message;
        }
        System.Windows.MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    
}
