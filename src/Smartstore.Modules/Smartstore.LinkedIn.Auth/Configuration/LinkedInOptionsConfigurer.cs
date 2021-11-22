using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using Smartstore.Engine;
using Smartstore.LinkedIn.Auth;

namespace Smartstore.LinkedIn.Auth.Configuration
{
    internal sealed class LinkedInOptionsConfigurer : IConfigureOptions<AuthenticationOptions>, IConfigureNamedOptions<LinkedInOptions>
    {
        private readonly IApplicationContext _appContext;

        public LinkedInOptionsConfigurer(IApplicationContext appContext)
        {
            _appContext = appContext;
        }

        public void Configure(AuthenticationOptions options)
        {
            // Register the OpenID Connect client handler in the authentication handlers collection.
            options.AddScheme(LinkedInDefaults.AuthenticationScheme, builder =>
            {
                builder.DisplayName = "LinkedIn";
                builder.HandlerType = typeof(LinkedInHandler);
            });
        }

        public void Configure(string name, LinkedInOptions options)
        {
            //Ignore OpenID Connect client handler instances that don't correspond to the instance managed by the OpenID module.
            if (name.HasValue() && !string.Equals(name, LinkedInDefaults.AuthenticationScheme))
            {
                return;
            }

            var settings = _appContext.Services.Resolve<LinkedInExternalAuthSettings>();
            options.ClientId = settings.ClientKeyIdentifier;
            options.ClientSecret = settings.ClientSecret;

            options.Events = new OAuthEvents
            {
                OnRemoteFailure = context =>
                {
                    var errorUrl = $"/identity/externalerrorcallback?provider=linkedin&errorMessage={context.Failure.Message.UrlEncode()}";
                    context.Response.Redirect(errorUrl);
                    context.HandleResponse();

                    return Task.CompletedTask;
                }
            };
        }

        public void Configure(LinkedInOptions options)
            => Debug.Fail("This infrastructure method shouldn't be called.");
    }
}
