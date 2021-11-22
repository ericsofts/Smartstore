using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Smartstore.Web.Components;

namespace Smartstore.LinkedIn.Auth.Components
{
    public class LinkedInAuthViewComponent : SmartViewComponent
    {
        private readonly LinkedInOptions _linkedinOptions;
        private readonly IUrlHelper _urlHelper;

        public LinkedInAuthViewComponent(IOptionsMonitor<LinkedInOptions> linkedinOptions, IUrlHelper urlHelper)
        {
            _linkedinOptions = linkedinOptions.CurrentValue;
            _urlHelper = urlHelper;
        }


        public IViewComponentResult Invoke()
        {
            if (!_linkedinOptions.ClientId.HasValue() && !_linkedinOptions.ClientSecret.HasValue())
            {
                return Empty();
            }

            var returnUrl = HttpContext.Request.Query["returnUrl"].ToString();
            var href = _urlHelper.Action("ExternalLogin", "Identity", new { provider = "LinkedIn", returnUrl });
            var title = T("Plugins.Smartstore.LinkedIn.Auth.Login").Value;
            var html = $"<a class='btn btn-primary btn-block btn-lg btn-extauth btn-brand-linkedin' href='{href}'>" +
                       $"<i class='fab fa-fw fa-lg fa-linkedin'></i><span>{title}</span></a>";

            return HtmlContent(html);
        }
    }
}
