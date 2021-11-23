using System;
using System.IO;
using System.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Smartstore;
using Smartstore.Engine;
using Smartstore.Core.Data.Migrations;
using Smartstore.Web;

var builder = WebApplication.CreateBuilder(args);
var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

builder.Configuration.SetBasePath(Directory.GetCurrentDirectory());
builder.Configuration.AddJsonFile("appsettings.json", true, true);
builder.Configuration.AddJsonFile($"appsettings.{environmentName}.json", optional: true);
builder.Configuration.AddJsonFile("Config/connections.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"Config/connections.{environmentName}.json", optional: true);
builder.Configuration.AddJsonFile("Config/usersettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"Config/usersettings.{environmentName}.json", optional: true);
builder.Configuration.AddEnvironmentVariables();

var hostapp = Host.CreateDefaultBuilder(args)
            .UseServiceProviderFactory(new AutofacServiceProviderFactory())
            .ConfigureLogging(cl => {
                cl.ClearProviders();
                cl.AddSerilog();
            })
            .UseSerilog(dispose: true)
            .ConfigureWebHostDefaults(wb => wb
                .UseStartup<HostStartup>(hostingContext =>
                {
                    hostingContext.Configuration = builder.Configuration;
                    return new HostStartup(hostingContext);
                })).Build();

// At this stage - after ConfigureServices & ConfigureContainer have been called - we can access IServiceProvider.
var appContext = hostapp.Services.GetRequiredService<IApplicationContext>();
var providerContainer = (appContext as IServiceProviderContainer)
    ?? throw new ApplicationException($"The implementation of '${nameof(IApplicationContext)}' must also implement '${nameof(IServiceProviderContainer)}'.");
providerContainer.ApplicationServices = hostapp.Services;

// At this stage we can set the scoped service container.
var engine = hostapp.Services.GetRequiredService<IEngine>();
engine.Scope = new ScopedServiceContainer(
    hostapp.Services.GetRequiredService<ILifetimeScopeAccessor>(),
    hostapp.Services.GetRequiredService<IHttpContextAccessor>(),
    hostapp.Services.AsLifetimeScope());

if (appContext.IsInstalled)
{
    var scopeAccessor = hostapp.Services.GetRequiredService<ILifetimeScopeAccessor>();
    using (scopeAccessor.BeginContextAwareScope(out var scope))
    {
        var initializer = scope.ResolveOptional<IDatabaseInitializer>();
        if (initializer != null)
        {
            var appLifetime = scope.ResolveOptional<IHostApplicationLifetime>();
            await initializer.InitializeDatabasesAsync(appLifetime?.ApplicationStopping ?? CancellationToken.None);
        }
    }
}

hostapp.Run();