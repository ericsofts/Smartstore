﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Smartstore.ComponentModel;
using Smartstore.Core;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Common.Settings;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;

namespace Smartstore.Web.Models.Common
{
    public static class AddressMappingExtensions
    {
        /// <summary>
        /// Extension method to map an <see cref="Address"/> entity to <see cref="AddressModel"/>.
        /// </summary>
        /// <param name="entity">Source <see cref="Address"/> to be mapped.</param>
        /// <param name="model">Target <see cref="AddressModel"/> to which <paramref name="entity"/> is to be mapped.</param>
        /// <param name="addCountries">
        /// A value indicating whether to add countries and state provinces to the model.
        /// If <c>null</c>, it will be obtained from <see cref="AddressSettings.CountryEnabled"/> and <see cref="AddressSettings.StateProvinceEnabled"/>.
        /// </param>
        /// <param name="countries">Countries to be added to the model.</param>
        public static async Task MapAsync(this Address entity,
            AddressModel model,
            bool? addCountries = null,
            IEnumerable<Country> countries = null)
        {
            dynamic parameters = new ExpandoObject();
            parameters.AddCountries = addCountries;
            parameters.Countries = countries;

            await MapperFactory.MapAsync(entity, model, parameters);
        }
    }

    internal class AddressMapper : Mapper<Address, AddressModel>
    {
        private readonly SmartDbContext _db;
        private readonly ICommonServices _services;
        private readonly IAddressService _addressService;
        private readonly AddressSettings _addressSettings;

        public AddressMapper(
            SmartDbContext db,
            ICommonServices services,
            IAddressService addressService, 
            AddressSettings addressSettings)
        {
            _db = db;
            _services = services;
            _addressService = addressService;
            _addressSettings = addressSettings;
        }

        protected override void Map(Address from, AddressModel to, dynamic parameters = null)
            => throw new NotImplementedException();

        public override async Task MapAsync(Address from, AddressModel to, dynamic parameters = null)
        {
            Guard.NotNull(to, nameof(to));

            var addCountries = parameters?.AddCountries as bool?;
            var explicitAddCountries = addCountries.GetValueOrDefault();

            MiniMapper.Map(_addressSettings, to);

            // INFO: transient entity is not mapped to re-display entered model values when model validation failed.
            if (from != null && !from.IsTransientRecord())
            {
                await _db.LoadReferenceAsync(from, x => x.Country);
                await _db.LoadReferenceAsync(from, x => x.StateProvince);

                MiniMapper.Map(from, to);

                to.EmailMatch = from.Email;
                to.CountryName = from.Country?.GetLocalized(x => x.Name);
                to.StateProvinceName = from.StateProvince?.GetLocalized(x => x.Name);
                to.FormattedAddress = await _addressService.FormatAddressAsync(from, true);
            }

            // Countries and states.
            if (addCountries ?? _addressSettings.CountryEnabled)
            {
                var countries = parameters?.Countries as IEnumerable<Country>;
                if (countries == null)
                {
                    countries = await _db.Countries
                        .AsNoTracking()
                        .ApplyStandardFilter(explicitAddCountries, explicitAddCountries ? 0 : _services.StoreContext.CurrentStore.Id)
                        .ToListAsync();
                }

                if (countries?.Any() ?? false)
                {
                    if (!explicitAddCountries)
                    {
                        to.AvailableCountries.Add(new SelectListItem 
                        { 
                            Text = _services.Localization.GetResource("Address.SelectCountry"), 
                            Value = "0"
                        });
                    }

                    foreach (var country in countries)
                    {
                        to.AvailableCountries.Add(new SelectListItem
                        {
                            Text = country.GetLocalized(x => x.Name),
                            Value = country.Id.ToString(),
                            Selected = country.Id == to.CountryId
                        });
                    }

                    if (addCountries ?? _addressSettings.StateProvinceEnabled)
                    {
                        if (to.CountryId.HasValue)
                        {
                            var states = await _db.StateProvinces
                                .AsNoTracking()
                                .ApplyCountryFilter(to.CountryId.Value)
                                .ToListAsync();

                            foreach (var state in states)
                            {
                                to.AvailableStates.Add(new SelectListItem
                                {
                                    Text = state.GetLocalized(x => x.Name),
                                    Value = state.Id.ToString(),
                                    Selected = state.Id == to.StateProvinceId
                                });
                            }
                        }
                        else
                        {
                            to.AvailableStates.Add(new SelectListItem 
                            {
                                Text = _services.Localization.GetResource("Address.OtherNonUS"), 
                                Value = "0"
                            });
                        }
                    }
                }
            }

            string salutations = _addressSettings.GetLocalizedSetting(x => x.Salutations);

            foreach (var salutation in salutations.SplitSafe(','))
            {
                to.AvailableSalutations.Add(new SelectListItem { Text = salutation, Value = salutation });
            }
        }
    }
}
