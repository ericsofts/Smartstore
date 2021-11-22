using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Smartstore.ComponentModel;
using Smartstore.Core.Security;
using Smartstore.Engine.Modularity;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling.Settings;
using Smartstore.LinkedIn.Auth.Models;
using Smartstore.LinkedIn.Auth.Configuration;

namespace Smartstore.LinkedIn.Auth.Controllers
{
    [Route("[area]/linkedin/auth/{action=index}/{id?}")]
    public class LinkedInAuthController : AdminController
    {
        private readonly IOptionsMonitorCache<LinkedInOptions> _optionsCache;
        private readonly IProviderManager _providerManager;

        public LinkedInAuthController(IOptionsMonitorCache<LinkedInOptions> optionsCache, IProviderManager providerManager)
        {
            _optionsCache = optionsCache;
            _providerManager = providerManager;
        }

        [HttpGet, LoadSetting]
        [Permission(Permissions.Configuration.Authentication.Read)]
        public IActionResult Configure(LinkedInExternalAuthSettings settings)
        {
            var model = MiniMapper.Map<LinkedInExternalAuthSettings, ConfigurationModel>(settings);

            var host = Services.StoreContext.CurrentStore.GetHost(true).EnsureEndsWith("/");
            model.RedirectUrl = $"{host}signin-linkedin";

            ViewBag.Provider = _providerManager.GetProvider("Smartstore.LinkedIn.Auth").Metadata;

            return View(model);
        }

        [HttpPost, SaveSetting]
        [Permission(Permissions.Configuration.Authentication.Update)]
        public IActionResult Configure(ConfigurationModel model, LinkedInExternalAuthSettings settings, int storeScope)
        {
            if (!ModelState.IsValid)
            {
                return Configure(settings);
            }

            ModelState.Clear();

            MiniMapper.Map(model, settings);

            // TODO: (mh) (core) This must also be called when settings change via all settings grid.
            _optionsCache.TryRemove(LinkedInDefaults.AuthenticationScheme);

            NotifySuccess(T("Admin.Common.DataSuccessfullySaved"));

            return RedirectToAction(nameof(Configure));
        }
    }
}
