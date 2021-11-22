using System.Threading.Tasks;
using Smartstore.Core.Widgets;
using Smartstore.Engine.Modularity;
using Smartstore.LinkedIn.Auth.Components;
using Smartstore.Core.Identity;
using Smartstore.Http;

namespace Smartstore.LinkedIn.Auth
{
    internal class Module : ModuleBase, IExternalAuthenticationMethod, IConfigurable
    {
        public RouteInfo GetConfigurationRoute()
            => new("Configure", "LinkedInAuth", new { area = "Admin" });

        public WidgetInvoker GetDisplayWidget(int storeId)
            => new ComponentWidgetInvoker(typeof(LinkedInAuthViewComponent), null);

        public override async Task InstallAsync(ModuleInstallationContext context)
        {
            await ImportLanguageResourcesAsync();
            await base.InstallAsync(context);
        }

        public override async Task UninstallAsync()
        {
            await DeleteLanguageResourcesAsync();
            await base.UninstallAsync();
        }
    }
}