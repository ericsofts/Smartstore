﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Smartstore.Caching;
using Smartstore.ComponentModel;
using Smartstore.Core;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Localization.Routing;
using Smartstore.Core.Logging;
using Smartstore.Core.Messaging;
using Smartstore.Core.OutputCache;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Core.Stores;
using Smartstore.Core.Web;
using Smartstore.Core.Widgets;
using Smartstore.Http;
using Smartstore.Net;
using Smartstore.News.Domain;
using Smartstore.News.Hooks;
using Smartstore.News.Messaging;
using Smartstore.News.Models.Mappers;
using Smartstore.News.Models.Public;
using Smartstore.Utilities.Html;
using Smartstore.Web.Controllers;
using Smartstore.Web.Filters;

namespace Smartstore.News.Controllers
{
    public class NewsController : PublicController
    {
        private readonly SmartDbContext _db;
        private readonly ICommonServices _services;
        private readonly IMediaService _mediaService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IPageAssetBuilder _pageAssetBuilder;
        private readonly ICacheManager _cache;
        private readonly NewsHelper _helper;
        private readonly Lazy<IWebHelper> _webHelper;
        private readonly Lazy<IActivityLogger> _activityLogger;
        private readonly Lazy<IMessageFactory> _messageFactory;
        private readonly Lazy<IUrlHelper> _urlHelper;

        private readonly NewsSettings _newsSettings;
        private readonly LocalizationSettings _localizationSettings;
        private readonly CustomerSettings _customerSettings;
        private readonly CaptchaSettings _captchaSettings;
        private readonly SeoSettings _seoSettings;

        public NewsController(
            SmartDbContext db,
            ICommonServices services,
            IMediaService mediaService,
            IDateTimeHelper dateTimeHelper,
            IStoreMappingService storeMappingService,
            IPageAssetBuilder pageAssetBuilder,
            ICacheManager cache,
            NewsHelper helper,
            Lazy<IWebHelper> webHelper,
            Lazy<IActivityLogger> activityLogger,
            Lazy<IMessageFactory> messageFactory,
            Lazy<IUrlHelper> urlHelper,
            NewsSettings newsSettings,
            LocalizationSettings localizationSettings,
            CustomerSettings customerSettings,
            CaptchaSettings captchaSettings,
            SeoSettings seoSettings)
        {
            _db = db;
            _services = services;
            _mediaService = mediaService;
            _dateTimeHelper = dateTimeHelper;
            _storeMappingService = storeMappingService;
            _pageAssetBuilder = pageAssetBuilder;
            _cache = cache;
            _helper = helper;
            _webHelper = webHelper;
            _activityLogger = activityLogger;
            _messageFactory = messageFactory;
            _urlHelper = urlHelper;

            _newsSettings = newsSettings;
            _localizationSettings = localizationSettings;
            _customerSettings = customerSettings;
            _captchaSettings = captchaSettings;
            _seoSettings = seoSettings;
        }

        [LocalizedRoute("news", Name = "NewsArchive")]
        public async Task<IActionResult> List(NewsPagingFilteringModel command)
        {
            if (!_newsSettings.Enabled)
            {
                return NotFound();
            }

            var model = await _helper.PrepareNewsItemListModelAsync(command);
            var storeId = _services.StoreContext.CurrentStore.Id;

            model.StoreName = _services.StoreContext.CurrentStore.Name;
            model.MetaTitle = _newsSettings.GetLocalizedSetting(x => x.MetaTitle, storeId);
            model.MetaDescription = _newsSettings.GetLocalizedSetting(x => x.MetaDescription, storeId);
            model.MetaKeywords = _newsSettings.GetLocalizedSetting(x => x.MetaKeywords, storeId);

            if (!model.MetaTitle.HasValue())
            {
                model.MetaTitle = T("PageTitle.NewsArchive").Value;
            }

            ViewBag.CanonicalUrlsEnabled = _seoSettings.CanonicalUrlsEnabled;
            ViewBag.StoreName = _services.StoreContext.CurrentStore.Name;

            return View(model);
        }

        [ActionName("rss")]
        [LocalizedRoute("/news/rss", Name = "NewsRSS")]
        public async Task<IActionResult> ListRss()
        {
            DateTime? maxAge = null;
            var language = _services.WorkContext.WorkingLanguage;
            var store = _services.StoreContext.CurrentStore;
            var protocol = _webHelper.Value.IsCurrentConnectionSecured() ? "https" : "http";
            var selfLink = Url.Action("rss", "News", null, protocol);
            var newsLink = Url.RouteUrl("NewsArchive", null, protocol);
            var title = $"{store.Name} - News";

            if (_newsSettings.MaxAgeInDays > 0)
            {
                maxAge = DateTime.UtcNow.Subtract(new TimeSpan(_newsSettings.MaxAgeInDays, 0, 0, 0));
            }

            var feed = new SmartSyndicationFeed(new Uri(newsLink), title);

            feed.AddNamespaces(true);
            feed.Init(selfLink, language.LanguageCulture.EmptyNull().ToLower());

            if (!_newsSettings.Enabled)
            {
                return new RssActionResult(feed);
            }

            var items = new List<SyndicationItem>();
            var query = _db.NewsItems().AsNoTracking().ApplyStandardFilter(store.Id, language.Id);
            
            if (maxAge != null)
            {
                query = (IOrderedQueryable<NewsItem>)query.Where(n => n.CreatedOnUtc >= maxAge.Value);
            }

            var newsItems = await query.ToPagedList(0, int.MaxValue).LoadAsync();
            
            foreach (var news in newsItems)
            {
                var newsUrl = Url.RouteUrl("NewsItem", new { SeName = await news.GetActiveSlugAsync(ensureTwoPublishedLanguages: false) }, protocol);
                var content = news.GetLocalized(x => x.Full, true).Value;

                if (content.HasValue())
                {
                    content = WebHelper.MakeAllUrlsAbsolute(content, Request);
                }

                var item = feed.CreateItem(
                    news.GetLocalized(x => x.Title),
                    news.GetLocalized(x => x.Short),
                    newsUrl,
                    news.CreatedOnUtc,
                    content);

                items.Add(item);
            }

            feed.Items = items;

            Services.DisplayControl.AnnounceRange(newsItems);

            return new RssActionResult(feed);
        }

        [GdprConsent]
        public async Task<IActionResult> NewsItem(int newsItemId)
        {
            if (!_newsSettings.Enabled)
            {
                return NotFound();
            }

            var newsItem = await _db.NewsItems()
                .Include(x => x.NewsComments)
                .ThenInclude(x => x.Customer)
                .FindByIdAsync(newsItemId, false);

            if (newsItem == null)
            {
                return NotFound();
            }

            if (!newsItem.Published ||
                (newsItem.LanguageId.HasValue && newsItem.LanguageId != _services.WorkContext.WorkingLanguage.Id) ||
                (newsItem.StartDateUtc.HasValue && newsItem.StartDateUtc.Value >= DateTime.UtcNow) ||
                (newsItem.EndDateUtc.HasValue && newsItem.EndDateUtc.Value <= DateTime.UtcNow) ||
                !await _storeMappingService.AuthorizeAsync(newsItem))
            {
                if (!_services.WorkContext.CurrentCustomer.IsAdmin())
                {
                    return NotFound();
                }
            }

            var model = await newsItem.MapAsync(new { PrepareComments = true });

            ViewBag.CanonicalUrlsEnabled = _seoSettings.CanonicalUrlsEnabled;
            ViewBag.StoreName = _services.StoreContext.CurrentStore.Name;

            return View(model);
        }

        [HttpPost]
        [ValidateCaptcha]
        [GdprConsent]
        public async Task<IActionResult> NewsCommentAdd(PublicNewsItemModel model, string captchaError)
        {
            if (!_newsSettings.Enabled)
            {
                return NotFound();
            }

            var newsItem = await _db.NewsItems().FindByIdAsync(model.Id, false);
            if (newsItem == null || !newsItem.Published || !newsItem.AllowComments)
            {
                return NotFound();
            }

            if (_captchaSettings.ShowOnNewsCommentPage && captchaError.HasValue())
            {
                ModelState.AddModelError(string.Empty, captchaError);
            }

            if (_services.WorkContext.CurrentCustomer.IsGuest() && !_newsSettings.AllowNotRegisteredUsersToLeaveComments)
            {
                ModelState.AddModelError(string.Empty, T("News.Comments.OnlyRegisteredUsersLeaveComments"));
            }

            if (ModelState.IsValid)
            {
                var comment = new NewsComment
                {
                    NewsItemId = newsItem.Id,
                    CustomerId = _services.WorkContext.CurrentCustomer.Id,
                    IpAddress = _webHelper.Value.GetClientIpAddress().ToString(),
                    CommentTitle = model.AddNewComment.CommentTitle?.RemoveHtml(),
                    CommentText = HtmlUtility.SanitizeHtml(model.AddNewComment.CommentText, HtmlSanitizerOptions.UserCommentSuitable),
                    IsApproved = true
                };

                _db.CustomerContent.Add(comment);
                await _db.SaveChangesAsync();

                // Notify the store owner.
                if (_newsSettings.NotifyAboutNewNewsComments)
                {
                    await _messageFactory.Value.SendNewsCommentNotificationMessage(comment, _localizationSettings.DefaultAdminLanguageId);
                }

                _activityLogger.Value.LogActivity(KnownActivityLogTypes.PublicStoreAddNewsComment, T("ActivityLog.PublicStore.AddNewsComment"));

                NotifySuccess(T("News.Comments.SuccessfullyAdded"));

                var seName = await newsItem.GetActiveSlugAsync(ensureTwoPublishedLanguages: false);
                var url = _urlHelper.Value.RouteUrl(new UrlRouteContext
                {
                    RouteName = "NewsItem",
                    Values = new { SeName = seName },
                    Fragment = "customer-comment-list"
                });

                return Redirect(url);
            }

            // If we got this far something failed. Redisplay form.
            model = await newsItem.MapAsync(new { PrepareComments = true });
            
            ViewBag.CanonicalUrlsEnabled = _seoSettings.CanonicalUrlsEnabled;
            ViewBag.StoreName = _services.StoreContext.CurrentStore.Name;

            return View("NewsItem", model);
        }
    }
}
