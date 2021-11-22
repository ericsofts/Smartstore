using System;
using Microsoft.AspNetCore.Authentication;
using Smartstore.LinkedIn.Auth;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods to add LinkedIn authentication capabilities to an HTTP application pipeline.
    /// </summary>
    public static class LinkedInExtensions
    {
        /// <summary>
        /// Adds <see cref="LinkedInAuthenticationHandler"/> to the specified
        /// <see cref="AuthenticationBuilder"/>, which enables LinkedIn authentication capabilities.
        /// </summary>
        /// <param name="builder">The <see cref="AuthenticationBuilder"/> to add the middleware to.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static AuthenticationBuilder AddLinkedIn(this AuthenticationBuilder builder)
            => builder.AddLinkedIn(LinkedInDefaults.AuthenticationScheme, options => { });

        /// <summary>
        /// Adds <see cref="LinkedInAuthenticationHandler"/> to the specified
        /// <see cref="AuthenticationBuilder"/>, which enables LinkedIn authentication capabilities.
        /// </summary>
        /// <param name="builder">The <see cref="AuthenticationBuilder"/> to add the middleware to.</param>
        /// <param name="configuration">The delegate used to configure the OpenID 2.0 options.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static AuthenticationBuilder AddLinkedIn(
            this AuthenticationBuilder builder,
            Action<LinkedInOptions> configuration)
            => builder.AddLinkedIn(LinkedInDefaults.AuthenticationScheme, configuration);

        /// <summary>
        /// Adds <see cref="LinkedInAuthenticationHandler"/> to the specified
        /// <see cref="AuthenticationBuilder"/>, which enables LinkedIn authentication capabilities.
        /// </summary>
        /// <param name="builder">The authentication builder.</param>
        /// <param name="scheme">The authentication scheme associated with this instance.</param>
        /// <param name="configuration">The delegate used to configure the LinkedIn options.</param>
        /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
        public static AuthenticationBuilder AddLinkedIn(
            this AuthenticationBuilder builder,
            string scheme,
            Action<LinkedInOptions> configuration)
            => builder.AddLinkedIn(scheme, LinkedInDefaults.DisplayName, configuration);

        /// <summary>
        /// Adds <see cref="LinkedInAuthenticationHandler"/> to the specified
        /// <see cref="AuthenticationBuilder"/>, which enables LinkedIn authentication capabilities.
        /// </summary>
        /// <param name="builder">The authentication builder.</param>
        /// <param name="scheme">The authentication scheme associated with this instance.</param>
        /// <param name="caption">The optional display name associated with this instance.</param>
        /// <param name="configuration">The delegate used to configure the LinkedIn options.</param>
        /// <returns>The <see cref="AuthenticationBuilder"/>.</returns>
        public static AuthenticationBuilder AddLinkedIn(
            this AuthenticationBuilder builder,
            string scheme,
            string caption,
            Action<LinkedInOptions> configuration)
            => builder.AddOAuth<LinkedInOptions, LinkedInHandler>(scheme, caption, configuration);
    }
}
