﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Smartstore.Caching;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Checkout.Payment;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Common.Settings;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Messaging;
using Smartstore.Core.Security;
using Smartstore.Core.Seo;
using Smartstore.Engine.Modularity;
using Smartstore.Utilities;
using Smartstore.Web.Infrastructure.Hooks;
using Smartstore.Web.Modelling;
using Smartstore.Web.Models.Common;
using Smartstore.Web.Models.Customers;

namespace Smartstore.Web.Controllers
{
    public class CustomerController : PublicController
    {
        private readonly SmartDbContext _db;
        private readonly INewsletterSubscriptionService _newsletterSubscriptionService;
        private readonly ITaxService _taxService;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderCalculationService _orderCalculationService;
        private readonly IOrderService _orderService;
        private readonly IPaymentService _paymentService;
        private readonly IMediaService _mediaService;
        private readonly IDownloadService _downloadService;
        private readonly ICurrencyService _currencyService;
        private readonly IMessageFactory _messageFactory;
        private readonly UserManager<Customer> _userManager;
        private readonly SignInManager<Customer> _signInManager;
        private readonly IProviderManager _providerManager;
        private readonly ICacheManager _cache;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ProductUrlHelper _productUrlHelper;
        private readonly DateTimeSettings _dateTimeSettings;
        private readonly CustomerSettings _customerSettings;
        private readonly TaxSettings _taxSettings;
        private readonly LocalizationSettings _localizationSettings;
        private readonly OrderSettings _orderSettings;
        private readonly RewardPointsSettings _rewardPointsSettings;
        private readonly MediaSettings _mediaSettings;

        public CustomerController(
            SmartDbContext db,
            INewsletterSubscriptionService newsletterSubscriptionService,
            ITaxService taxService,
            ILocalizationService localizationService,
            IOrderProcessingService orderProcessingService,
            IOrderCalculationService orderCalculationService,
            IOrderService orderService,
            IPaymentService paymentService,
            IMediaService mediaService,
            IDownloadService downloadService,
            ICurrencyService currencyService,
            IMessageFactory messageFactory,
            UserManager<Customer> userManager,
            SignInManager<Customer> signInManager,
            IProviderManager providerManager,
            ICacheManager cache,
            IDateTimeHelper dateTimeHelper,
            ProductUrlHelper productUrlHelper,
            DateTimeSettings dateTimeSettings,
            CustomerSettings customerSettings,
            TaxSettings taxSettings,
            LocalizationSettings localizationSettings,
            OrderSettings orderSettings,
            RewardPointsSettings rewardPointsSettings,
            MediaSettings mediaSettings)
        {
            _db = db;
            _newsletterSubscriptionService = newsletterSubscriptionService;
            _taxService = taxService;
            _localizationService = localizationService;
            _orderProcessingService = orderProcessingService;
            _orderCalculationService = orderCalculationService;
            _orderService = orderService;
            _paymentService = paymentService;
            _mediaService = mediaService;
            _downloadService = downloadService;
            _currencyService = currencyService;
            _messageFactory = messageFactory;
            _userManager = userManager;
            _signInManager = signInManager;
            _providerManager = providerManager;
            _cache = cache;
            _dateTimeHelper = dateTimeHelper;
            _productUrlHelper = productUrlHelper;
            _dateTimeSettings = dateTimeSettings;
            _customerSettings = customerSettings;
            _taxSettings = taxSettings;
            _localizationSettings = localizationSettings;
            _orderSettings = orderSettings;
            _rewardPointsSettings = rewardPointsSettings;
            _mediaSettings = mediaSettings;
        }

        [RequireSsl]
        public async Task<IActionResult> Info()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }
            
            var model = new CustomerInfoModel();
            await PrepareCustomerInfoModelAsync(model, customer, false);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Info(CustomerInfoModel model)
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            if (model.Email.IsEmpty())
            {
                ModelState.AddModelError(string.Empty, "Email is not provided.");
            }
            if (_customerSettings.CustomerLoginType != CustomerLoginType.Email && _customerSettings.AllowUsersToChangeUsernames && model.Username.IsEmpty())
            {
                ModelState.AddModelError(string.Empty, "Username is not provided.");
            }

            try
            {
                if (ModelState.IsValid)
                {
                    customer.FirstName = model.FirstName;
                    customer.LastName = model.LastName;

                    // Username.
                    if (_customerSettings.CustomerLoginType != CustomerLoginType.Email && _customerSettings.AllowUsersToChangeUsernames)
                    {
                        if (!customer.Username.EqualsNoCase(model.Username.Trim()))
                        {
                            // Change username.
                            await _userManager.SetUserNameAsync(customer, model.Username.Trim());
                            // Re-authenticate.
                            await _signInManager.SignInAsync(customer, true);
                        }
                    }

                    // Email.
                    if (!customer.Email.Equals(model.Email.Trim(), StringComparison.InvariantCultureIgnoreCase))
                    {
                        // Change email.
                        await _userManager.SetEmailAsync(customer, model.Email.Trim());
                        // Re-authenticate (if usernames are disabled).
                        if (_customerSettings.CustomerLoginType == CustomerLoginType.Email)
                        {
                            await _signInManager.SignInAsync(customer, true);
                        }
                    }

                    // VAT number.
                    if (_taxSettings.EuVatEnabled)
                    {
                        var prevVatNumber = customer.GenericAttributes.VatNumber;
                        customer.GenericAttributes.VatNumber = model.VatNumber;

                        if (prevVatNumber != model.VatNumber)
                        {
                            var vatCheckResult = await _taxService.GetVatNumberStatusAsync(model.VatNumber);
                            customer.VatNumberStatusId = (int)vatCheckResult.Status;

                            // Send VAT number admin notification.
                            if (model.VatNumber.HasValue() && _taxSettings.EuVatEmailAdminWhenNewVatSubmitted)
                            {
                                await _messageFactory.SendNewVatSubmittedStoreOwnerNotificationAsync(customer, model.VatNumber, vatCheckResult.Address, _localizationSettings.DefaultAdminLanguageId);
                            }
                        }
                    }

                    // Customer number.
                    if (_customerSettings.CustomerNumberMethod != CustomerNumberMethod.Disabled)
                    {
                        var numberExists = await _db.Customers.Where(x => x.CustomerNumber == model.CustomerNumber).AnyAsync();

                        if (model.CustomerNumber != customer.CustomerNumber && numberExists)
                        {
                            NotifyError("Common.CustomerNumberAlreadyExists");
                        }
                        else
                        {
                            customer.CustomerNumber = model.CustomerNumber;
                        }
                    }

                    if (_customerSettings.DateOfBirthEnabled)
                    {
                        try
                        {
                            if (model.DateOfBirthYear.HasValue && model.DateOfBirthMonth.HasValue && model.DateOfBirthDay.HasValue)
                            {
                                customer.BirthDate = new DateTime(model.DateOfBirthYear.Value, model.DateOfBirthMonth.Value, model.DateOfBirthDay.Value);
                            }
                            else
                            {
                                customer.BirthDate = null;
                            }
                        }
                        catch { }
                    }

                    if (_customerSettings.CompanyEnabled)
                    {
                        customer.Company = model.Company;
                    }
                    if (_customerSettings.TitleEnabled)
                    {
                        customer.Title = model.Title;
                    }
                    if (_customerSettings.GenderEnabled)
                    {
                        customer.Gender = model.Gender;
                    }
                    if (_customerSettings.StreetAddressEnabled)
                    {
                        customer.GenericAttributes.StreetAddress = model.StreetAddress;
                    }
                    if (_customerSettings.StreetAddress2Enabled)
                    {
                        customer.GenericAttributes.StreetAddress2 = model.StreetAddress2;
                    }
                    if (_customerSettings.ZipPostalCodeEnabled)
                    {
                        customer.GenericAttributes.ZipPostalCode = model.ZipPostalCode;
                    }
                    if (_customerSettings.CityEnabled)
                    {
                        customer.GenericAttributes.City = model.City;
                    }
                    if (_customerSettings.CountryEnabled)
                    {
                        customer.GenericAttributes.CountryId = model.CountryId;
                    }
                    if (_customerSettings.CountryEnabled && _customerSettings.StateProvinceEnabled)
                    {
                        customer.GenericAttributes.StateProvinceId = model.StateProvinceId;
                    }
                    if (_customerSettings.PhoneEnabled)
                    {
                        customer.GenericAttributes.Phone = model.Phone;
                    }
                    if (_customerSettings.FaxEnabled)
                    {
                        customer.GenericAttributes.Fax = model.Fax;
                    }
                    if (_customerSettings.NewsletterEnabled)
                    {
                        await _newsletterSubscriptionService.ApplySubscriptionAsync(model.Newsletter, customer.Email, Services.StoreContext.CurrentStore.Id);
                    }
                    if (_dateTimeSettings.AllowCustomersToSetTimeZone)
                    {
                        customer.TimeZoneId = model.TimeZoneId;
                    }

                    await _db.SaveChangesAsync();

                    await Services.EventPublisher.PublishAsync(new ModelBoundEvent(model, customer, Request.Form));

                    return RedirectToAction("Info");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await PrepareCustomerInfoModelAsync(model, customer, false);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CheckUsernameAvailability(string username)
        {
            var usernameAvailable = false;
            var statusText = T("Account.CheckUsernameAvailability.NotAvailable");

            if (_customerSettings.CustomerLoginType != CustomerLoginType.Email && username != null)
            {
                username = username.Trim();

                if (username.HasValue())
                {
                    var customer = Services.WorkContext.CurrentCustomer;
                    if (customer != null && customer.Username != null && customer.Username.EqualsNoCase(username))
                    {
                        statusText = T("Account.CheckUsernameAvailability.CurrentUsername");
                    }
                    else
                    {
                        var userExists = await _db.Customers
                            .AsNoTracking()
                            .ApplyIdentFilter(userName: username)
                            .AnyAsync();

                        if (!userExists)
                        {
                            statusText = T("Account.CheckUsernameAvailability.Available");
                            usernameAvailable = true;
                        }
                    }
                }
            }

            return Json(new { Available = usernameAvailable, Text = statusText.Value });
        }

        #region Addresses

        [RequireSsl]
        public async Task<IActionResult> Addresses()
        {
            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            var model = new List<AddressModel>();
            var countries = await _db.Countries
                .AsNoTracking()
                .ApplyStandardFilter(false, Services.StoreContext.CurrentStore.Id)
                .ToListAsync();

            foreach (var address in customer.Addresses)
            {
                var addressModel = new AddressModel();
                await address.MapAsync(addressModel, null, countries);
                model.Add(addressModel);
            }

            return View(model);
        }

        [RequireSsl]
        public async Task<IActionResult> AddressDelete(int id)
        {
            if (id < 1)
                return NotFound();

            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            // Find address and ensure that it belongs to the current customer.
            var address = customer.Addresses.Where(a => a.Id == id).FirstOrDefault();
            if (address != null)
            {
                customer.RemoveAddress(address);
                // Now delete the address record.
                _db.Addresses.Remove(address);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Addresses");
        }

        [RequireSsl]
        public async Task<IActionResult> AddressAdd()
        {
            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            var model = new AddressModel();
            await new Address().MapAsync(model);
            model.Email = customer?.Email;

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddressAdd(AddressModel model)
        {
            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            if (ModelState.IsValid)
            {
                var address = new Address();
                MiniMapper.Map(model, address);
                customer.Addresses.Add(address);

                await _db.SaveChangesAsync();

                return RedirectToAction("Addresses");
            }

            // If we got this far something failed. Redisplay form.
            await new Address().MapAsync(model);

            return View(model);
        }

        [RequireSsl]
        public async Task<IActionResult> AddressEdit(int id)
        {
            if (id < 1)
            {
                return NotFound();
            }
            
            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            // Find address and ensure that it belongs to the current customer.
            var address = customer.Addresses.Where(a => a.Id == id).FirstOrDefault();
            if (address == null)
            {
                return RedirectToAction("Addresses");
            }
            
            var model = new AddressModel();
            await address.MapAsync(model);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddressEdit(AddressModel model, int id)
        {
            var customer = Services.WorkContext.CurrentCustomer;
            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            // Find address and ensure that it belongs to the current customer.
            var address = customer.Addresses.Where(a => a.Id == id).FirstOrDefault();
            if (address == null)
            {
                return RedirectToAction("Addresses");
            }

            if (ModelState.IsValid)
            {
                MiniMapper.Map(model, address);
                _db.Addresses.Update(address);
                await _db.SaveChangesAsync();

                return RedirectToAction("Addresses");
            }

            // If we got this far something failed. Redisplay form.
            await new Address().MapAsync(model);

            return View(model);
        }

        #endregion

        #region Orders

        [RequireSsl]
        public async Task<IActionResult> Orders(int? page, int? recurringPaymentsPage)
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            var ordersPageIndex = Math.Max((page ?? 0) - 1, 0);
            var rpPageIndex = Math.Max((recurringPaymentsPage ?? 0) - 1, 0);

            var model = await PrepareCustomerOrderListModelAsync(customer, ordersPageIndex, rpPageIndex);
            model.OrdersPage = page;
            model.RecurringPaymentsPage = recurringPaymentsPage;

            return View(model);
        }

        [HttpPost, ActionName("Orders")]
        [FormValueRequired(FormValueRequirementOperator.StartsWith, "cancelRecurringPayment")]
        public async Task<IActionResult> CancelRecurringPayment()
        {
            var customer = Services.WorkContext.CurrentCustomer;
            var form = HttpContext.Request.Form;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            // Get recurring payment identifier.
            var recurringPaymentId = 0;
            foreach (var formValue in form.Keys)
            {
                if (formValue.StartsWith("cancelRecurringPayment", StringComparison.InvariantCultureIgnoreCase))
                {
                    recurringPaymentId = Convert.ToInt32(formValue["cancelRecurringPayment".Length..]);
                }
            }

            var recurringPayment = await _db.RecurringPayments
                .Include(x => x.InitialOrder)
                .ThenInclude(x => x.Customer)
                .FindByIdAsync(recurringPaymentId, false);

            if (recurringPayment == null)
            {
                return RedirectToAction("Orders");
            }

            if (await _orderProcessingService.CanCancelRecurringPaymentAsync(recurringPayment, customer))
            {
                var errors = await _orderProcessingService.CancelRecurringPaymentAsync(recurringPayment);
                var model = await PrepareCustomerOrderListModelAsync(customer, 0, 0);
                model.CancelRecurringPaymentErrors = errors.ToList();

                return View(model);
            }

            return RedirectToAction("Orders");
        }

        #endregion

        #region Return request

        [RequireSsl]
        public async Task<IActionResult> ReturnRequests()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            var model = new CustomerReturnRequestsModel();
            var returnRequests = await _db.ReturnRequests
                .AsNoTracking()
                .ApplyStandardFilter(storeId: Services.StoreContext.CurrentStore.Id, customerId: customer.Id)
                .ToListAsync();
            
            foreach (var returnRequest in returnRequests)
            {
                var orderItem = await _db.OrderItems
                    .Include(x => x.Product)
                    .FindByIdAsync(returnRequest.OrderItemId, false);

                if (orderItem != null)
                {
                    var itemModel = new CustomerReturnRequestsModel.ReturnRequestModel
                    {
                        Id = returnRequest.Id,
                        ReturnRequestStatus = await returnRequest.ReturnRequestStatus.GetLocalizedEnumAsync(Services.WorkContext.WorkingLanguage.Id),
                        ProductId = orderItem.Product.Id,
                        ProductName = orderItem.Product.GetLocalized(x => x.Name),
                        ProductSeName = await orderItem.Product.GetActiveSlugAsync(),
                        Quantity = returnRequest.Quantity,
                        ReturnAction = returnRequest.RequestedAction,
                        ReturnReason = returnRequest.ReasonForReturn,
                        Comments = returnRequest.CustomerComments,
                        CreatedOn = _dateTimeHelper.ConvertToUserTime(returnRequest.CreatedOnUtc, DateTimeKind.Utc)
                    };

                    itemModel.ProductUrl = await _productUrlHelper.GetProductUrlAsync(itemModel.ProductSeName, orderItem);

                    model.Items.Add(itemModel);
                }
            }

            return View(model);
        }

        #endregion

        #region Downloadable products
        
        [RequireSsl]
        public async Task<IActionResult> DownloadableProducts()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            var model = new CustomerDownloadableProductsModel();

            var items = await _db.OrderItems
                .AsNoTracking()
                .ApplyStandardFilter(customerId: customer.Id)
                .Include(x => x.Product)
                .Include(x => x.Order)
                .Where(x => x.Product.IsDownload)
                .ToListAsync();
            
            foreach (var item in items)
            {
                var itemModel = new CustomerDownloadableProductsModel.DownloadableProductsModel
                {
                    OrderItemGuid = item.OrderItemGuid,
                    OrderId = item.OrderId,
                    CreatedOn = _dateTimeHelper.ConvertToUserTime(item.Order.CreatedOnUtc, DateTimeKind.Utc),
                    ProductName = item.Product.GetLocalized(x => x.Name),
                    ProductSeName = await item.Product.GetActiveSlugAsync(),
                    ProductAttributes = item.AttributeDescription,
                    ProductId = item.ProductId
                };

                itemModel.ProductUrl = await _productUrlHelper.GetProductUrlAsync(item.ProductId, itemModel.ProductSeName, item.AttributeSelection);

                model.Items.Add(itemModel);

                itemModel.IsDownloadAllowed = _downloadService.IsDownloadAllowed(item);

                if (itemModel.IsDownloadAllowed)
                {
                    var downloads = (await _db.Downloads
                        .AsNoTracking()
                        .ApplyEntityFilter(item.Product)
                        .ApplyVersionFilter()
                        .Include(x => x.MediaFile)
                        .ToListAsync())
                        .OrderByVersion();

                    itemModel.DownloadVersions = downloads
                        .Select(x => new DownloadVersion
                        {
                            DownloadId = x.Id,
                            FileVersion = x.FileVersion,
                            FileName = x.MediaFile.Name,
                            DownloadGuid = x.DownloadGuid,
                            Changelog = x.Changelog
                        })
                        .ToList();
                }

                if (_downloadService.IsLicenseDownloadAllowed(item))
                {
                    itemModel.LicenseId = item.LicenseDownloadId ?? 0;
                }
            }

            return View(model);
        }

        /// <summary>
        /// Gets the user agreement for purchased download.
        /// </summary>
        /// <param name="id">OrderItemId <see cref="OrderItem.OrderItemGuid"/></param>
        /// <param name="fileVersion">Requested version of purchased download.</param>
        public async Task<IActionResult> UserAgreement(Guid id, string fileVersion = "")
        {
            if (id == Guid.Empty)
            {
                return NotFound();
            }

            var orderItem = await _db.OrderItems
                .AsNoTracking()
                .Include(x => x.Product)
                .Where(x => x.OrderItemGuid == id)
                .FirstOrDefaultAsync();
                
            if (orderItem == null)
            {
                NotifyError(T("Customer.UserAgreement.OrderItemNotFound"));
                return RedirectToRoute("Homepage");
            }

            var product = orderItem.Product;
            if (product == null || !product.HasUserAgreement)
            {
                NotifyError(T("Customer.UserAgreement.ProductNotFound"));
                return RedirectToRoute("Homepage");
            }

            var model = new UserAgreementModel
            {
                UserAgreementText = product.UserAgreementText,
                OrderItemGuid = id,
                FileVersion = fileVersion
            };

            return View(model);
        }

        #endregion

        #region Avatar

        [RequireSsl]
        public IActionResult Avatar()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            if (!_customerSettings.AllowCustomersToUploadAvatars)
            {
                return RedirectToAction("Info");
            }

            var model = new CustomerAvatarEditModel
            {
                Avatar = customer.ToAvatarModel(null, true),
                MaxFileSize = Prettifier.HumanizeBytes(_customerSettings.AvatarMaximumSizeBytes)
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UploadAvatar()
        {
            var customer = Services.WorkContext.CurrentCustomer;
            var success = false;
            string avatarUrl = null;

            try
            {
                if (customer.IsRegistered() && _customerSettings.AllowCustomersToUploadAvatars)
                {
                    var uploadedFile = Request.Form.Files[0];
                    if (uploadedFile != null && uploadedFile.FileName.HasValue())
                    {
                        if (uploadedFile.Length > _customerSettings.AvatarMaximumSizeBytes)
                        {
                            throw new SmartException(T("Account.Avatar.MaximumUploadedFileSize", Prettifier.HumanizeBytes(_customerSettings.AvatarMaximumSizeBytes)));
                        }

                        var oldAvatar = await _db.MediaFiles.FindByIdAsync(customer.GenericAttributes.AvatarPictureId ?? 0);
                        if (oldAvatar != null)
                        {
                            await _mediaService.DeleteFileAsync(oldAvatar, true);
                        }                        

                        var path = _mediaService.CombinePaths(SystemAlbumProvider.Customers, uploadedFile.FileName.ToValidFileName());

                        using var stream = uploadedFile.OpenReadStream();
                        var newAvatar = await _mediaService.SaveFileAsync(path, stream, false, DuplicateFileHandling.Rename);

                        if (newAvatar != null)
                        {
                            customer.GenericAttributes.AvatarPictureId = newAvatar.Id;
                            await _db.SaveChangesAsync();

                            avatarUrl = _mediaService.GetUrl(newAvatar, _mediaSettings.AvatarPictureSize, null, false);
                            success = avatarUrl.HasValue();
                        }
                    }
                }
            }
            catch
            {
                throw;
            }

            return Json(new { success, avatarUrl });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveAvatar()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (customer.IsRegistered() && _customerSettings.AllowCustomersToUploadAvatars)
            {
                var avatar = await _db.MediaFiles.FindByIdAsync((int)customer.GenericAttributes.AvatarPictureId);
                if (avatar != null)
                {
                    await _mediaService.DeleteFileAsync(avatar, true);
                }

                customer.GenericAttributes.AvatarPictureId = 0;
                customer.GenericAttributes.AvatarColor = null;

                await _db.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        #endregion

        #region Reward points

        [RequireSsl]
        public IActionResult RewardPoints()
        {
            var customer = Services.WorkContext.CurrentCustomer;

            if (!customer.IsRegistered())
            {
                return new UnauthorizedResult();
            }

            if (!_rewardPointsSettings.Enabled)
            {
                return RedirectToAction("Info");
            }
            
            var model = new CustomerRewardPointsModel();
            foreach (var rph in customer.RewardPointsHistory.OrderByDescending(rph => rph.CreatedOnUtc).ThenByDescending(rph => rph.Id))
            {
                model.RewardPoints.Add(new CustomerRewardPointsModel.RewardPointsHistoryModel()
                {
                    Points = rph.Points,
                    PointsBalance = rph.PointsBalance,
                    Message = rph.Message,
                    CreatedOn = _dateTimeHelper.ConvertToUserTime(rph.CreatedOnUtc, DateTimeKind.Utc)
                });
            }

            int rewardPointsBalance = customer.GetRewardPointsBalance();
            var rewardPointsAmountBase = _orderCalculationService.ConvertRewardPointsToAmount(rewardPointsBalance);
            var rewardPointsAmount = _currencyService.ConvertFromPrimaryCurrency(rewardPointsAmountBase.Amount, Services.WorkContext.WorkingCurrency);
            model.RewardPointsBalanceFormatted = T("RewardPoints.CurrentBalance", rewardPointsBalance, rewardPointsAmount.ToString());

            return View(model);
        }

        #endregion

        #region Stock subscriptions

        public async Task<IActionResult> StockSubscriptions(int? page)
        {
            if (_customerSettings.HideBackInStockSubscriptionsTab)
            {
                return RedirectToAction("Info");
            }

            int pageIndex = 0;
            if (page > 0)
            {
                pageIndex = page.Value - 1;
            }

            var customer = Services.WorkContext.CurrentCustomer;
            var list = await _db.BackInStockSubscriptions
                .AsNoTracking()
                .Include(x => x.Product)
                .ApplyStandardFilter(customerId: customer.Id, storeId: Services.StoreContext.CurrentStore.Id)
                .ToPagedList(pageIndex, 10)
                .LoadAsync();

            var model = new CustomerStockSubscriptionsModel(list);

            foreach (var subscription in list)
            {
                var product = subscription.Product;

                if (product != null)
                {
                    var subscriptionModel = new StockSubscriptionModel
                    {
                        Id = subscription.Id,
                        ProductId = product.Id,
                        ProductName = product.GetLocalized(x => x.Name),
                        SeName = await product.GetActiveSlugAsync()
                    };
                    model.Subscriptions.Add(subscriptionModel);
                }
            }

            return View(model);
        }

        [HttpPost, ActionName("StockSubscriptions")]
        public async Task<IActionResult> StockSubscriptionsPOST()
        {
            var form = HttpContext.Request.Form;
            var customerId = Services.WorkContext.CurrentCustomer.Id;
            
            foreach (var key in form.Keys)
            {
                var value = form[key];

                if (value.Equals("on") && key.StartsWith("biss", StringComparison.InvariantCultureIgnoreCase))
                {
                    var id = key.Replace("biss", "").Trim();

                    if (int.TryParse(id, out var subscriptionId))
                    {
                        var subscription = await _db.BackInStockSubscriptions.FindByIdAsync(subscriptionId);
                        if (subscription != null && subscription.CustomerId == customerId)
                        {
                            _db.BackInStockSubscriptions.Remove(subscription);
                        }
                    }
                }
            }

            await _db.SaveChangesAsync();

            return RedirectToAction("StockSubscriptions");
        }

        #endregion

        #region Utilities

        protected async Task PrepareCustomerInfoModelAsync(CustomerInfoModel model, Customer customer, bool excludeProperties)
        {
            Guard.NotNull(model, nameof(model));
            Guard.NotNull(customer, nameof(customer));

            model.Id = customer.Id;
            model.AllowCustomersToSetTimeZone = _dateTimeSettings.AllowCustomersToSetTimeZone;

            var availableTimeZones = new List<SelectListItem>();
            foreach (var tzi in _dateTimeHelper.GetSystemTimeZones())
            {
                availableTimeZones.Add(new SelectListItem
                {
                    Text = tzi.DisplayName,
                    Value = tzi.Id,
                    Selected = (excludeProperties ? tzi.Id == model.TimeZoneId : tzi.Id == _dateTimeHelper.CurrentTimeZone.Id)
                });
            }
            ViewBag.AvailableTimeZones = availableTimeZones;

            if (!excludeProperties)
            {
                var dateOfBirth = customer.BirthDate;

                var newsletterSubscription = await _db.NewsletterSubscriptions
                    .AsNoTracking()
                    .ApplyMailAddressFilter(customer.Email, Services.StoreContext.CurrentStore.Id)
                    .FirstOrDefaultAsync();

                model.Company = customer.Company;
                model.Title = customer.Title;
                model.FirstName = customer.FirstName;
                model.LastName = customer.LastName;
                model.Gender = customer.Gender;
                model.CustomerNumber = customer.CustomerNumber;
                model.Email = customer.Email;
                model.Username = customer.Username;

                if (dateOfBirth.HasValue)
                {
                    model.DateOfBirthDay = dateOfBirth.Value.Day;
                    model.DateOfBirthMonth = dateOfBirth.Value.Month;
                    model.DateOfBirthYear = dateOfBirth.Value.Year;
                }

                model.VatNumber = customer.GenericAttributes.VatNumber;
                model.StreetAddress = customer.GenericAttributes.StreetAddress;
                model.StreetAddress2 = customer.GenericAttributes.StreetAddress2;
                model.City = customer.GenericAttributes.City;
                model.ZipPostalCode = customer.GenericAttributes.ZipPostalCode;
                model.CountryId = Convert.ToInt32(customer.GenericAttributes.CountryId);
                model.StateProvinceId = Convert.ToInt32(customer.GenericAttributes.StateProvinceId);
                model.Phone = customer.GenericAttributes.Phone;
                model.Fax = customer.GenericAttributes.Fax;
                model.Newsletter = newsletterSubscription != null && newsletterSubscription.Active;
            }
            else
            {
                if (_customerSettings.CustomerLoginType != CustomerLoginType.Email && !_customerSettings.AllowUsersToChangeUsernames)
                {
                    model.Username = customer.Username;
                }
            }

            // TODO: (mh) (core) Create some generic solution for this always repeating code.
            // Countries and states.
            if (_customerSettings.CountryEnabled)
            {
                var availableCountries = new List<SelectListItem>
                {
                    new SelectListItem { Text = T("Address.SelectCountry"), Value = "0" }
                };

                var countries = await _db.Countries
                    .AsNoTracking()
                    .ApplyStandardFilter()
                    .ToListAsync();

                foreach (var c in countries)
                {
                    availableCountries.Add(new SelectListItem
                    {
                        Text = c.GetLocalized(x => x.Name),
                        Value = c.Id.ToString(),
                        Selected = c.Id == model.CountryId
                    });
                }

                ViewBag.AvailableCountries = availableCountries;

                if (_customerSettings.StateProvinceEnabled)
                {
                    var availableStates = new List<SelectListItem>();

                    var states = await _db.StateProvinces
                        .AsNoTracking()
                        .ApplyCountryFilter(model.CountryId)
                        .ToListAsync();

                    if (states.Any())
                    {
                        foreach (var s in states)
                        {
                            availableStates.Add(new SelectListItem
                            {
                                Text = s.GetLocalized(x => x.Name),
                                Value = s.Id.ToString(),
                                Selected = (s.Id == model.StateProvinceId)
                            });
                        }
                    }
                    else
                    {
                        availableStates.Add(new SelectListItem { Text = T("Address.OtherNonUS"), Value = "0" });
                    }

                    ViewBag.AvailableStates = availableStates;
                }
            }

            model.DisplayVatNumber = _taxSettings.EuVatEnabled;
            model.VatNumberStatusNote = await ((VatNumberStatus)customer.VatNumberStatusId).GetLocalizedEnumAsync(Services.WorkContext.WorkingLanguage.Id);
            model.GenderEnabled = _customerSettings.GenderEnabled;
            model.TitleEnabled = _customerSettings.TitleEnabled;
            model.DateOfBirthEnabled = _customerSettings.DateOfBirthEnabled;
            model.CompanyEnabled = _customerSettings.CompanyEnabled;
            model.CompanyRequired = _customerSettings.CompanyRequired;
            model.StreetAddressEnabled = _customerSettings.StreetAddressEnabled;
            model.StreetAddressRequired = _customerSettings.StreetAddressRequired;
            model.StreetAddress2Enabled = _customerSettings.StreetAddress2Enabled;
            model.StreetAddress2Required = _customerSettings.StreetAddress2Required;
            model.ZipPostalCodeEnabled = _customerSettings.ZipPostalCodeEnabled;
            model.ZipPostalCodeRequired = _customerSettings.ZipPostalCodeRequired;
            model.CityEnabled = _customerSettings.CityEnabled;
            model.CityRequired = _customerSettings.CityRequired;
            model.CountryEnabled = _customerSettings.CountryEnabled;
            model.StateProvinceEnabled = _customerSettings.StateProvinceEnabled;
            model.PhoneEnabled = _customerSettings.PhoneEnabled;
            model.PhoneRequired = _customerSettings.PhoneRequired;
            model.FaxEnabled = _customerSettings.FaxEnabled;
            model.FaxRequired = _customerSettings.FaxRequired;
            model.NewsletterEnabled = _customerSettings.NewsletterEnabled;
            model.UsernamesEnabled = _customerSettings.CustomerLoginType != CustomerLoginType.Email;
            model.AllowUsersToChangeUsernames = _customerSettings.AllowUsersToChangeUsernames;
            model.CheckUsernameAvailabilityEnabled = _customerSettings.CheckUsernameAvailabilityEnabled;
            model.DisplayCustomerNumber = _customerSettings.CustomerNumberMethod != CustomerNumberMethod.Disabled
                && _customerSettings.CustomerNumberVisibility != CustomerNumberVisibility.None;

            if (_customerSettings.CustomerNumberMethod != CustomerNumberMethod.Disabled
                && (_customerSettings.CustomerNumberVisibility == CustomerNumberVisibility.Editable
                || (_customerSettings.CustomerNumberVisibility == CustomerNumberVisibility.EditableIfEmpty && model.CustomerNumber.IsEmpty())))
            {
                model.CustomerNumberEnabled = true;
            }
            else
            {
                model.CustomerNumberEnabled = false;
            }

            // External authentication.
            var authProviders = await _signInManager.GetExternalAuthenticationSchemesAsync();
            
            foreach (var ear in customer.ExternalAuthenticationRecords)
            {
                var provider = authProviders.Where(x => ear.ProviderSystemName.Contains(x.Name)).FirstOrDefault();
                
                if (provider == null)
                  continue;

                model.AssociatedExternalAuthRecords.Add(new CustomerInfoModel.AssociatedExternalAuthModel
                {
                    Id = ear.Id,
                    Email = ear.Email,
                    ExternalIdentifier = ear.ExternalIdentifier,
                    AuthMethodName = provider.Name
                });
            }
        }

        protected async Task<CustomerOrderListModel> PrepareCustomerOrderListModelAsync(Customer customer, int orderPageIndex, int recurringPaymentPageIndex)
        {
            Guard.NotNull(customer, nameof(customer));

            var store = Services.StoreContext.CurrentStore;
            var model = new CustomerOrderListModel();

            var orders = await _db.Orders
                .AsNoTracking()
                .ApplyStandardFilter(customer.Id, _orderSettings.DisplayOrdersOfAllStores ? 0 : store.Id)
                .ToPagedList(orderPageIndex, _orderSettings.OrderListPageSize)
                .LoadAsync();

            var customerCurrencyCodes = orders.Select(x => x.CustomerCurrencyCode).Distinct().ToArray();
            var customerCurrencies = (await _db.Currencies
                .AsNoTracking()
                .Where(x => customerCurrencyCodes.Contains(x.CurrencyCode))
                .ToListAsync())
                .ToDictionarySafe(x => x.CurrencyCode, x => x, StringComparer.OrdinalIgnoreCase);

            var orderModels = await orders
                .SelectAsync(async x =>
                {
                    var orderModel = new CustomerOrderListModel.OrderDetailsModel
                    {
                        Id = x.Id,
                        OrderNumber = x.GetOrderNumber(),
                        CreatedOn = _dateTimeHelper.ConvertToUserTime(x.CreatedOnUtc, DateTimeKind.Utc),
                        OrderStatus = await _localizationService.GetLocalizedEnumAsync(x.OrderStatus),
                        IsReturnRequestAllowed = _orderProcessingService.IsReturnRequestAllowed(x)
                    };

                    // TODO: (mh) (core) Check format!
                    customerCurrencies.TryGetValue(x.CustomerCurrencyCode, out var customerCurrency);

                    (var orderTotal, var roundingAmount) = await _orderService.GetOrderTotalInCustomerCurrencyAsync(x, customerCurrency);
                    orderModel.OrderTotal = orderTotal;

                    return orderModel;
                })
                .AsyncToList();

            model.Orders = orderModels.ToPagedList(orders.PageIndex, orders.PageSize, orders.TotalCount);

            // Recurring payments.
            var recurringPayments = await _db.RecurringPayments
                .AsNoTracking()
                .Include(x => x.InitialOrder)
                .ThenInclude(x => x.Customer)
                .Include(x => x.RecurringPaymentHistory)
                .ApplyStandardFilter(customerId: customer.Id, storeId: store.Id)
                .ToPagedList(recurringPaymentPageIndex, _orderSettings.RecurringPaymentListPageSize)
                .LoadAsync();

            var rpModels = await recurringPayments
                .SelectAsync(async x =>
                {
                    var nextPaymentDate = await _paymentService.GetNextRecurringPaymentDateAsync(x);

                    return new CustomerOrderListModel.RecurringPaymentModel
                    {
                        Id = x.Id,
                        StartDate = _dateTimeHelper.ConvertToUserTime(x.StartDateUtc, DateTimeKind.Utc).ToString(),
                        CycleInfo = $"{x.CycleLength} {await _localizationService.GetLocalizedEnumAsync(x.CyclePeriod)}",
                        NextPayment = nextPaymentDate.HasValue ? _dateTimeHelper.ConvertToUserTime(nextPaymentDate.Value, DateTimeKind.Utc).ToString() : string.Empty,
                        TotalCycles = x.TotalCycles,
                        CyclesRemaining = await _paymentService.GetRecurringPaymentRemainingCyclesAsync(x),
                        InitialOrderId = x.InitialOrder.Id,
                        CanCancel = await _orderProcessingService.CanCancelRecurringPaymentAsync(x, customer)
                    };
                })
                .AsyncToList();

            model.RecurringPayments = rpModels.ToPagedList(recurringPayments.PageIndex, recurringPayments.PageSize, recurringPayments.TotalCount);

            return model;
        }

        #endregion

        // INFO: (mh) (core) Current CountryController just has this one method. Details TBD.
        // RE: But it does NOT belong here. Find another - perhaps more generic - controller please.
        /// <summary>
        /// This action method gets called via an ajax request.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> StatesByCountryId(string countryId, bool addEmptyStateIfRequired)
        {
            // TODO: (mh) (core) Don't throw in frontend.
            if (!countryId.HasValue())
                throw new ArgumentNullException(nameof(countryId));

            string cacheKey = string.Format(ModelCacheInvalidator.STATEPROVINCES_BY_COUNTRY_MODEL_KEY, countryId, addEmptyStateIfRequired, Services.WorkContext.WorkingLanguage.Id);
            var cacheModel = await _cache.GetAsync(cacheKey, async () =>
            {
                var country = await _db.Countries
                    .AsNoTracking()
                    .Where(x => x.Id == Convert.ToInt32(countryId))
                    .FirstOrDefaultAsync();

                var states = await _db.StateProvinces
                    .AsNoTracking()
                    .ApplyCountryFilter(country != null ? country.Id : 0)
                    .ToListAsync();

                var result = (from s in states
                              select new { id = s.Id, name = s.GetLocalized(x => x.Name).Value })
                              .ToList();

                if (addEmptyStateIfRequired && result.Count == 0)
                {
                    result.Insert(0, new { id = 0, name = T("Address.OtherNonUS").Value });
                }
                
                return result;
            });

            return Json(cacheModel);
        }
    }
}
