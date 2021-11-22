using Autofac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Smartstore.Engine;
using Smartstore.Engine.Builders;
using Smartstore.LinkedIn.Auth.Configuration;

namespace Smartstore.LinkedIn.Auth
{
    internal class Startup : StarterBase
    {
        public override void ConfigureContainer(ContainerBuilder builder, IApplicationContext appContext)
        {
            builder.RegisterType<LinkedInOptionsConfigurer>()
                .As<IConfigureOptions<AuthenticationOptions>>()
                .As<IConfigureOptions<LinkedInOptions>>()
                .InstancePerDependency();

            builder.RegisterType<OAuthPostConfigureOptions<LinkedInOptions, LinkedInHandler>>()
                .As<IPostConfigureOptions<LinkedInOptions>>()
                .InstancePerDependency();
        }
    }
}