﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Humanizer;
using Smartstore.ComponentModel;
using Smartstore.Core;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Blog.Domain;
using Smartstore.Blog.Models.Public;
using Smartstore.Web.Models.Common;
using Smartstore.Web.Models.Customers;
using Smartstore.Web.Models.Media;
using Smartstore.Utilities.Html;

namespace Smartstore.Blog.Models.Mappers
{
    public static partial class BlogMappingExtensions
    {
        public static async Task<PublicBlogPostModel> MapAsync(this BlogPost entity, dynamic parameters = null)
        {
            var to = new PublicBlogPostModel();
            await MapAsync(entity, to, parameters);

            return to;
        }

        public static async Task MapAsync(this BlogPost entity, PublicBlogPostModel to, dynamic parameters = null)
        {
            await MapperFactory.MapAsync(entity, to, parameters);
        }
    }

    public class BlogPostMapper : Mapper<BlogPost, PublicBlogPostModel>
    {
        private readonly ICommonServices _services;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly CustomerSettings _customerSettings;
        private readonly CaptchaSettings _captchaSettings;

        public BlogPostMapper(ICommonServices services, IDateTimeHelper dateTimeHelper, CustomerSettings customerSettings, CaptchaSettings captchaSettings)
        {
            _services = services;
            _dateTimeHelper = dateTimeHelper;
            _customerSettings = customerSettings;
            _captchaSettings = captchaSettings;
        }

        public Localizer T { get; set; } = NullLocalizer.Instance;

        protected override void Map(BlogPost from, PublicBlogPostModel to, dynamic parameters = null)
            => throw new NotImplementedException();

        public override async Task MapAsync(BlogPost from, PublicBlogPostModel to, dynamic parameters = null)
        {
            Guard.NotNull(from, nameof(from));
            Guard.NotNull(to, nameof(to));
            
            var prepareComments = parameters?.PrepareComments == true;

            MiniMapper.Map(from, to);

            to.Title = from.GetLocalized(x => x.Title);
            to.Intro = from.GetLocalized(x => x.Intro);
            to.Body = from.GetLocalized(x => x.Body, true);
            to.MetaTitle = from.GetLocalized(x => x.MetaTitle);
            to.MetaDescription = from.GetLocalized(x => x.MetaDescription);
            to.MetaKeywords = from.GetLocalized(x => x.MetaKeywords);
            to.SeName = await from.GetActiveSlugAsync(ensureTwoPublishedLanguages: false);
            to.CreatedOn = _dateTimeHelper.ConvertToUserTime(from.CreatedOnUtc, DateTimeKind.Utc);
            to.CreatedOnUTC = from.CreatedOnUtc;
            to.AddNewComment.DisplayCaptcha = _captchaSettings.CanDisplayCaptcha && _captchaSettings.ShowOnBlogCommentPage;
            to.Comments.AllowComments = from.AllowComments;
            to.Comments.NumberOfComments = from.ApprovedCommentCount;
            to.Comments.AllowCustomersToUploadAvatars = _customerSettings.AllowCustomersToUploadAvatars;
            to.DisplayAdminLink = _services.Permissions.Authorize(Permissions.System.AccessBackend, _services.WorkContext.CurrentCustomer);

            to.HasBgImage = from.PreviewDisplayType == PreviewDisplayType.DefaultSectionBg || from.PreviewDisplayType == PreviewDisplayType.PreviewSectionBg;

            var mapper = MapperFactory.GetMapper<BlogPost, ImageModel>();
            to.Image = await mapper.MapAsync(from, new { FileId = from.MediaFileId });

            if (from.PreviewDisplayType == PreviewDisplayType.Default || from.PreviewDisplayType == PreviewDisplayType.DefaultSectionBg)
            {
                to.Preview = to.Image;
            }
            else if (from.PreviewDisplayType == PreviewDisplayType.Preview || from.PreviewDisplayType == PreviewDisplayType.PreviewSectionBg)
            {
                to.Preview = await mapper.MapAsync(from, new { FileId = from.PreviewMediaFileId });
            }

            if (from.PreviewDisplayType == PreviewDisplayType.Preview ||
                from.PreviewDisplayType == PreviewDisplayType.Default ||
                from.PreviewDisplayType == PreviewDisplayType.Bare)
            {
                to.SectionBg = string.Empty;
            }

            to.Tags = from.ParseTags().Select(x => new BlogPostTagModel
            {
                Name = x,
                SeName = SeoHelper.BuildSlug(x)
            }).ToList();

            to.MetaProperties = await to.MapMetaPropertiesAsync();

            if (prepareComments)
            {
                var blogComments = from.BlogComments
                    .Where(pr => pr.IsApproved)
                    .OrderBy(pr => pr.CreatedOnUtc);

                foreach (var bc in blogComments)
                {
                    var isGuest = bc.Customer.IsGuest();

                    var commentModel = new CommentModel(to.Comments)
                    {
                        Id = bc.Id,
                        CustomerId = bc.CustomerId,
                        CustomerName = bc.Customer.FormatUserName(_customerSettings, T, false),
                        CommentText = HtmlUtility.SanitizeHtml(bc.CommentText, HtmlSanitizerOptions.UserCommentSuitable),
                        CreatedOn = _dateTimeHelper.ConvertToUserTime(bc.CreatedOnUtc, DateTimeKind.Utc),
                        CreatedOnPretty = _services.DateTimeHelper.ConvertToUserTime(bc.CreatedOnUtc, DateTimeKind.Utc).Humanize(false),
                        AllowViewingProfiles = _customerSettings.AllowViewingProfiles && !isGuest
                    };

                    commentModel.Avatar = bc.Customer.ToAvatarModel(null, false);

                    to.Comments.Comments.Add(commentModel);
                }
            }

            _services.DisplayControl.Announce(from);
        }
    }
}
