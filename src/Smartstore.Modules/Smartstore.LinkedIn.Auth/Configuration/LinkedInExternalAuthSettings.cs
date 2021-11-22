using Smartstore.Core.Configuration;

namespace Smartstore.LinkedIn.Auth.Configuration
{
    public class LinkedInExternalAuthSettings : ISettings
    {
        public string ClientKeyIdentifier { get; set; }
        public string ClientSecret { get; set; }
    }
}
