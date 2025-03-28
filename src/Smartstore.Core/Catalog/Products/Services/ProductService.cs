﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Collections;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Checkout.Orders;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Messaging;
using Smartstore.Core.Stores;

namespace Smartstore.Core.Catalog.Products
{
    public partial class ProductService : IProductService
    {
        private readonly SmartDbContext _db;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ICommonServices _services;
        private readonly IProductAttributeMaterializer _productAttributeMaterializer;
        private readonly IMessageFactory _messageFactory;
        private readonly LocalizationSettings _localizationSettings;

        public ProductService(
            SmartDbContext db,
            IWorkContext workContext,
            IStoreContext storeContext,
            ICommonServices services,
            IProductAttributeMaterializer productAttributeMaterializer,
            IMessageFactory messageFactory,
            LocalizationSettings localizationSettings)
        {
            _db = db;
            _workContext = workContext;
            _storeContext = storeContext;
            _services = services;
            _productAttributeMaterializer = productAttributeMaterializer;
            _messageFactory = messageFactory;
            _localizationSettings = localizationSettings;
        }

        public virtual async Task<(Product Product, ProductVariantAttributeCombination VariantCombination)> GetProductByIdentificationNumberAsync(
            string identificationNumber,
            bool includeHidden = false,
            bool tracked = false)
        {
            if (string.IsNullOrWhiteSpace(identificationNumber))
            {
                return (null, null);
            }

            identificationNumber = identificationNumber.Trim();

            var pq = _db.Products
                .ApplyTracking(tracked)
                .ApplyStandardFilter(includeHidden)
                .Where(x => x.Sku == identificationNumber || x.ManufacturerPartNumber == identificationNumber || x.Gtin == identificationNumber);

            if (!includeHidden)
            {
                pq = pq.Where(x => x.Visibility <= ProductVisibility.SearchResults);
            }

            var product = await pq.FirstOrDefaultAsync();
            if (product != null)
            {
                return (product, null);
            }

            var pvcq = _db.ProductVariantAttributeCombinations
                .Include(x => x.Product)
                .ApplyTracking(tracked)
                .ApplyStandardFilter(includeHidden)
                .Where(x => x.Sku == identificationNumber || x.ManufacturerPartNumber == identificationNumber || x.Gtin == identificationNumber);

            if (!includeHidden)
            {
                pvcq = pvcq.Where(x => x.Product.Visibility <= ProductVisibility.SearchResults);
            }

            var variantCombination = await pvcq.FirstOrDefaultAsync();

            return (variantCombination?.Product, variantCombination);
        }

        public virtual IQueryable<Product> GetLowStockProducts(bool tracked = false)
        {
            // TODO: (mh) (core) Create query extension method "ApplyLowStockFilter()" and remove this method (bad convention: GetXyz() should always return a resultset).
            var query = _db.Products
                .ApplyTracking(tracked)
                .ApplyStandardFilter(true);

            // Track inventory for product.
            var query1 = 
                from p in query
                orderby p.MinStockQuantity
                where p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock && p.MinStockQuantity >= p.StockQuantity
                select p;

            // Track inventory for product by product attributes.
            var query2 = 
                from p in query
                from pvac in p.ProductVariantAttributeCombinations
                where p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStockByAttributes && pvac.StockQuantity <= 0
                select p;

            // INFO: (mh) (core) This will throw. EF is not able to "Concat" rows.
            // The reason why this was not a query filter but a service method is because the list is a combination of 2 query results.
            return query1.Concat(query2);
        }

        public virtual async Task<Multimap<int, ProductTag>> GetProductTagsByProductIdsAsync(int[] productIds, bool includeHidden = false)
        {
            Guard.NotNull(productIds, nameof(productIds));

            var map = new Multimap<int, ProductTag>();
            if (!productIds.Any())
            {
                return map;
            }

            var query = _db.Products
                .AsNoTracking()
                .Include(x => x.ProductTags)
                .Where(x => productIds.Contains(x.Id))
                .ApplyStandardFilter(includeHidden);

            if (!includeHidden)
            {
                // Only tags of products that are fully visible.
                query = query.Where(x => x.Visibility == ProductVisibility.Full);
            }

            var items = await query
                .Select(x => new
                {
                    ProductId = x.Id,
                    Tags = x.ProductTags.Where(y => includeHidden || y.Published)
                })
                .ToListAsync();

            foreach (var item in items)
            {
                map.AddRange(item.ProductId, item.Tags);
            }

            return map;
        }

        public virtual async Task<Multimap<int, Discount>> GetAppliedDiscountsByProductIdsAsync(int[] productIds, bool includeHidden = false, bool tracked = false)
        {
            Guard.NotNull(productIds, nameof(productIds));

            var map = new Multimap<int, Discount>();
            if (!productIds.Any())
            {
                return map;
            }

            var query = _db.Products
                .Include(x => x.AppliedDiscounts)
                    .ThenInclude(y => y.RuleSets)
                .ApplyTracking(tracked)
                .Where(x => productIds.Contains(x.Id))
                .ApplyStandardFilter(includeHidden)
                .Select(x => new
                {
                    ProductId = x.Id,
                    Discounts = x.AppliedDiscounts
                });

            var items = await query.ToListAsync();

            foreach (var item in items)
            {
                map.AddRange(item.ProductId, item.Discounts);
            }

            return map;
        }

        public virtual async Task<IList<Product>> GetCrossSellProductsByProductIdsAsync(int[] productIds, int numberOfProducts, bool includeHidden = false)
        {
            Guard.NotNull(productIds, nameof(productIds));

            var result = new List<Product>();

            if (numberOfProducts == 0 || !productIds.Any())
            {
                return result;
            }

            var query =
                from csp in _db.CrossSellProducts.AsNoTracking()
                join p in _db.Products on csp.ProductId2 equals p.Id
                where productIds.Contains(csp.ProductId1) && (includeHidden || p.Published)
                orderby csp.Id
                select csp;

            var csItems = await query.ToListAsync();
            var productIds1 = new HashSet<int>(csItems
                .Select(x => x.ProductId2)
                .Except(productIds));

            if (productIds1.Any())
            {
                var productIds2 = productIds1.Take(numberOfProducts).ToArray();

                var products = await _db.Products
                    .AsNoTracking()
                    .Where(x => productIds2.Contains(x.Id))
                    .ToListAsync();

                result.AddRange(products.OrderBySequence(productIds2));
            }

            return result;
        }

        public virtual void ApplyProductReviewTotals(Product product)
        {
            Guard.NotNull(product, nameof(product));

            // TODO: (core) Make a faster ApplyProductReviewTotals later without the necessity to eager load reviews.

            var approvedRatingSum = 0;
            var notApprovedRatingSum = 0;
            var approvedTotalReviews = 0;
            var notApprovedTotalReviews = 0;
            var reviews = product.ProductReviews;

            foreach (var pr in reviews)
            {
                if (pr.IsApproved)
                {
                    approvedRatingSum += pr.Rating;
                    approvedTotalReviews++;
                }
                else
                {
                    notApprovedRatingSum += pr.Rating;
                    notApprovedTotalReviews++;
                }
            }

            product.ApprovedRatingSum = approvedRatingSum;
            product.NotApprovedRatingSum = notApprovedRatingSum;
            product.ApprovedTotalReviews = approvedTotalReviews;
            product.NotApprovedTotalReviews = notApprovedTotalReviews;
        }

        public virtual async Task<AdjustInventoryResult> AdjustInventoryAsync(OrderItem orderItem, bool decrease, int quantity)
        {
            Guard.NotNull(orderItem, nameof(orderItem));

            if (orderItem.Product.ProductType == ProductType.BundledProduct && orderItem.Product.BundlePerItemShoppingCart)
            {
                if (orderItem.BundleData.HasValue())
                {
                    var bundleData = orderItem.GetBundleData();
                    if (bundleData.Any())
                    {
                        var productIds = bundleData
                            .Select(x => x.ProductId)
                            .Distinct()
                            .ToArray();

                        var products = await _db.Products
                            .Where(x => productIds.Contains(x.Id))
                            .ToListAsync();

                        var productsDic = products.ToDictionary(x => x.Id);

                        foreach (var item in bundleData)
                        {
                            if (productsDic.TryGetValue(item.ProductId, out var product))
                            {
                                await AdjustInventoryAsync(product, item.AttributeSelection, decrease, quantity * item.Quantity);
                            }
                        }
                    }
                }

                return new AdjustInventoryResult();
            }
            else
            {
                return await AdjustInventoryAsync(orderItem.Product, orderItem.AttributeSelection, decrease, quantity);
            }
        }

        public virtual async Task<AdjustInventoryResult> AdjustInventoryAsync(Product product, ProductVariantAttributeSelection selection, bool decrease, int quantity)
        {
            Guard.NotNull(product, nameof(product));
            Guard.NotNull(selection, nameof(selection));

            var result = new AdjustInventoryResult();

            switch (product.ManageInventoryMethod)
            {
                case ManageInventoryMethod.ManageStock:
                    {
                        result.StockQuantityOld = product.StockQuantity;

                        result.StockQuantityNew = decrease
                            ? product.StockQuantity - quantity
                            : product.StockQuantity + quantity;

                        var newPublished = product.Published;
                        var newDisableBuyButton = product.DisableBuyButton;
                        var newDisableWishlistButton = product.DisableWishlistButton;

                        // Check if the minimum quantity is reached.
                        switch (product.LowStockActivity)
                        {
                            case LowStockActivity.DisableBuyButton:
                                newDisableBuyButton = product.MinStockQuantity >= result.StockQuantityNew;
                                newDisableWishlistButton = product.MinStockQuantity >= result.StockQuantityNew;
                                break;
                            case LowStockActivity.Unpublish:
                                newPublished = product.MinStockQuantity <= result.StockQuantityNew;
                                break;
                        }

                        product.StockQuantity = result.StockQuantityNew;
                        product.DisableBuyButton = newDisableBuyButton;
                        product.DisableWishlistButton = newDisableWishlistButton;
                        product.Published = newPublished;

                        // SaveChanges is not necessary because SendQuantityBelowStoreOwnerNotificationAsync
                        // does not reload anything that has been changed in the meantime.

                        if (decrease && product.NotifyAdminForQuantityBelow > result.StockQuantityNew)
                        {
                            await _messageFactory.SendQuantityBelowStoreOwnerNotificationAsync(product, _localizationSettings.DefaultAdminLanguageId);
                        }
                    }
                    break;
                case ManageInventoryMethod.ManageStockByAttributes:
                    {
                        var combination = await _productAttributeMaterializer.FindAttributeCombinationAsync(product.Id, selection);
                        if (combination != null)
                        {
                            result.StockQuantityOld = combination.StockQuantity;

                            result.StockQuantityNew = decrease
                                ? combination.StockQuantity - quantity
                                : combination.StockQuantity + quantity;

                            combination.StockQuantity = result.StockQuantityNew;
                        }
                    }
                    break;
                case ManageInventoryMethod.DontManageStock:
                default:
                    // Do nothing.
                    break;
            }

            var attributeValues = await _productAttributeMaterializer.MaterializeProductVariantAttributeValuesAsync(selection);

            var productLinkageValues = attributeValues
                .Where(x => x.ValueType == ProductVariantAttributeValueType.ProductLinkage)
                .ToList();

            foreach (var chunk in productLinkageValues.Chunk(100))
            {
                var linkedProductIds = chunk.Select(x => x.LinkedProductId).Distinct().ToArray();
                var linkedProducts = await _db.Products.GetManyAsync(linkedProductIds, true);
                var linkedProductsDic = linkedProducts.ToDictionarySafe(x => x.Id);

                foreach (var value in chunk)
                {
                    if (linkedProductsDic.TryGetValue(value.LinkedProductId, out var linkedProduct))
                    {
                        await AdjustInventoryAsync(linkedProduct, null, decrease, quantity * value.Quantity);
                    }
                }
            }

            return result;
        }

        public virtual async Task<int> EnsureMutuallyRelatedProductsAsync(int productId1)
        {
            var productQuery = _db.Products.ApplyStandardFilter(true);

            var relatedProductIdsQuery =
                from rp in _db.RelatedProducts
                join p in productQuery on rp.ProductId2 equals p.Id
                where rp.ProductId1 == productId1
                orderby rp.DisplayOrder
                select rp.ProductId2;

            var productIds = await relatedProductIdsQuery.ToListAsync();

            if (productIds.Count > 0 && !productIds.Any(x => x == productId1))
            {
                productIds.Add(productId1);
            }
            if (!productIds.Any())
            {
                return 0;
            }

            var query =
                from rp in _db.RelatedProducts
                join p in _db.Products on rp.ProductId2 equals p.Id
                where productIds.Contains(rp.ProductId2)
                select new { rp.ProductId1, rp.ProductId2 };

            var allAssociatedIds = await query.ToListAsync();
            var associatedIdsMap = allAssociatedIds.ToMultimap(x => x.ProductId2, x => x.ProductId1);
            var added = 0;

            foreach (var id1 in productIds)
            {
                var associatedIds = associatedIdsMap.ContainsKey(id1)
                    ? associatedIdsMap[id1]
                    : new List<int>();

                foreach (var id2 in productIds)
                {
                    if (id1 == id2)
                    {
                        continue;
                    }

                    if (!associatedIds.Any(x => x == id2))
                    {
                        var maxDisplayOrder = await _db.RelatedProducts
                            .Where(x => x.ProductId1 == id2)
                            .OrderByDescending(x => x.DisplayOrder)
                            .Select(x => x.DisplayOrder)
                            .FirstOrDefaultAsync();

                        _db.RelatedProducts.Add(new RelatedProduct
                        {
                            ProductId1 = id2,
                            ProductId2 = id1,
                            DisplayOrder = maxDisplayOrder + 1
                        });

                        ++added;
                    }
                }
            }

            return added;
        }

        public virtual async Task<int> EnsureMutuallyCrossSellProductsAsync(int productId1)
        {
            var productQuery = _db.Products.ApplyStandardFilter(true);

            var crossSellProductIdsQuery = 
                from csp in _db.CrossSellProducts
                join p in productQuery on csp.ProductId2 equals p.Id
                where csp.ProductId1 == productId1
                orderby csp.Id
                select csp.ProductId2;

            var productIds = await crossSellProductIdsQuery.ToListAsync();

            if (productIds.Count > 0 && !productIds.Any(x => x == productId1))
            {
                productIds.Add(productId1);
            }
            if (!productIds.Any())
            {
                return 0;
            }

            var query =
                from csp in _db.CrossSellProducts
                join p in _db.Products on csp.ProductId2 equals p.Id
                where productIds.Contains(csp.ProductId2)
                select new { csp.ProductId1, csp.ProductId2 };

            var allAssociatedIds = await query.ToListAsync();
            var associatedIdsMap = allAssociatedIds.ToMultimap(x => x.ProductId2, x => x.ProductId1);
            var added = 0;

            foreach (var id1 in productIds)
            {
                var associatedIds = associatedIdsMap.ContainsKey(id1)
                    ? associatedIdsMap[id1]
                    : new List<int>();

                foreach (var id2 in productIds)
                {
                    if (id1 == id2)
                    {
                        continue;
                    }

                    if (!associatedIds.Any(x => x == id2))
                    {
                        _db.CrossSellProducts.Add(new CrossSellProduct
                        {
                            ProductId1 = id2,
                            ProductId2 = id1
                        });

                        ++added;
                    }
                }
            }

            return added;
        }

        public virtual ProductBatchContext CreateProductBatchContext(
            IEnumerable<Product> products = null,
            Store store = null,
            Customer customer = null,
            bool includeHidden = true,
            bool loadMainMediaOnly = false)
        {
            return new ProductBatchContext(
                products,
                _services,
                store ?? _storeContext.CurrentStore,
                customer ?? _workContext.CurrentCustomer,
                includeHidden,
                loadMainMediaOnly);
        }
    }

    // TODO: (mg) (core) Summary of various issues to be checked and done later:
    // - Add fluent validations when inserting ProductBundleItem:
    //      if (bundleItem.BundleProductId == 0) throw new SmartException("BundleProductId of a bundle item cannot be 0.");
    //      if (bundleItem.ProductId == 0) throw new SmartException("ProductId of a bundle item cannot be 0.");
    //      if (bundleItem.ProductId == bundleItem.BundleProductId) throw new SmartException("A bundle item cannot be an element of itself.");
    // - Check callers of IProductService.GetAppliedDiscountsByProductIdsAsync. Must now be called with includeHidden (default value was previously 'true').
    // - UI. Always show Currency.RoundNumDecimals on currency edit page. It is no longer used only for order item rounding.
    // - SystemCustomerAttributeNames.WalletEnabled belongs to Wallet module (as extension method for CustomerAttributeCollection).
    // - Check callers of IPaymentMethod.IsPaymentDataValidAsync. Default return value is now null and not an empty list anymore.
    // - String resources to be deleted (not used anymore): Admin.DataExchange.Export.FolderName.Validate
}
