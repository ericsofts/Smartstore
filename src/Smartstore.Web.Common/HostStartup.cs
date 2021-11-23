using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Filters;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Core;
using Smartstore.Core.Logging.Serilog;
using Smartstore.Bootstrapping;
using Smartstore.Engine;
using MsLogger = Microsoft.Extensions.Logging.ILogger;

namespace Smartstore.Web
{
    public class HostStartup
    {
        private IEngineStarter _engineStarter;
        private SmartApplicationContext _appContext;
        private static readonly Regex _rgSystemSource = new Regex("^File|^System|^Microsoft|^Serilog|^Autofac|^Castle|^MiniProfiler|^Newtonsoft|^Pipelines|^StackExchange|^Superpower", RegexOptions.Compiled);

        public HostStartup(WebHostBuilderContext wctx)
        {
            Configuration = wctx.Configuration;
            Environment = wctx.HostingEnvironment;

            StartupLogger = new SerilogLoggerFactory(SetupSerilog(wctx.Configuration)).CreateLogger("File");
        }

        public MsLogger StartupLogger { get; }
        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            _appContext = new SmartApplicationContext(Environment, Configuration, StartupLogger);

            _engineStarter = EngineFactory
                .Create(_appContext.AppConfiguration)
                .Start(_appContext);

            _engineStarter.ConfigureServices(services);
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            _engineStarter.ConfigureContainer(builder);
        }

        public void Configure(IApplicationBuilder app, IHostApplicationLifetime appLifetime)
        {
            // Must come very early.
            app.UseContextState();

            // Write streamlined request completion events, instead of the more verbose ones from the framework.
            // To use the default framework request logging instead, remove this line and set the "Microsoft"
            // level in appsettings.json to "Information".
            app.UseSerilogRequestLogging();

            // Executes IApplicationInitializer implementations during the very first request.
            if (_appContext.IsInstalled)
            {
                app.UseApplicationInitializer();
            }

            appLifetime.ApplicationStarted.Register(OnStarted, app);
            _engineStarter.ConfigureApplication(app);

        }

        private void OnStarted(object app = null)
        {
            _appContext.Freeze();

            _engineStarter.Dispose();
            _engineStarter = null;
        }


        private Logger SetupSerilog(IConfiguration configuration)
        {
            var builder = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext();

            // Build DEBUG logger
            if (Environment.IsDevelopment())
            {
                builder.WriteTo.Debug();
            }

            builder
                // Build INSTALL logger
                .WriteTo.Conditional(Matching.FromSource("Install"), a => a.Async(logger =>
                {
                    logger.File("App_Data/Logs/install-.log",
                        //restrictedToMinimumLevel: LogEventLevel.Debug,
                        outputTemplate: "{Timestamp:G} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        fileSizeLimitBytes: 100000000,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        rollingInterval: RollingInterval.Day,
                        flushToDiskInterval: TimeSpan.FromSeconds(5));
                }))
                // Build FILE logger (also replaces the Custoon classic "TraceLogger")
                .WriteTo.Logger(logger =>
                {
                    logger
                        .Enrich.FromLogContext()
                        // Allow only "File[/{path}]" sources
                        .Filter.ByIncludingOnly(IsFileSource)
                        // Extracts path from source and adds it as log event property name.
                        .Enrich.With<LogFilePathEnricher>()
                        .WriteTo.Map(LogFilePathEnricher.LogFilePathPropertyName, (logFilePath, wt) =>
                        {
                            wt.Async(c => c.File($"{logFilePath}",
                                //restrictedToMinimumLevel: LogEventLevel.Debug,
                                outputTemplate: "{Timestamp:G} [{Level:u3}] {Message:lj} {RequestPath} (UserId: {CustomerId}, Username: {UserName}){NewLine}{Exception}",
                                fileSizeLimitBytes: 100000000,
                                rollOnFileSizeLimit: true,
                                shared: true,
                                rollingInterval: RollingInterval.Day,
                                flushToDiskInterval: TimeSpan.FromSeconds(5)));
                        }, sinkMapCountLimit: 10);
                })
                // Build "SmartDbContext" logger
                .WriteTo.Logger(logger =>
                {
                    logger
                        .Enrich.FromLogContext()
                        // Do not allow system/3rdParty noise less than WRN level
                        .Filter.ByIncludingOnly(IsDbSource)
                        .WriteTo.DbContext(period: TimeSpan.FromSeconds(5), batchSize: 50, eagerlyEmitFirstEvent: false, queueLimit: 1000);
                }, restrictedToMinimumLevel: LogEventLevel.Information, levelSwitch: null);

            return builder.CreateLogger();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDbSource(LogEvent e)
        {
            // Allow only app logs >= INFO or system logs >= WARNING
            return e.Level >= LogEventLevel.Warning || !_rgSystemSource.IsMatch(e.GetSourceContext());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFileSource(LogEvent e)
        {
            var source = e.GetSourceContext();
            return source != null && (source.Equals("File", StringComparison.OrdinalIgnoreCase) || source.StartsWith("File/", StringComparison.OrdinalIgnoreCase));
        }
    }
}
