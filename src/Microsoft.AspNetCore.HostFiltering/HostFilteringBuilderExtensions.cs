// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Extension methods for the HostFiltering middleware.
    /// </summary>
    public static class HostFilteringBuilderExtensions
    {
        /// <summary>
        /// Adds middleware for filtering requests by allowed host headers. Invalid requests will be rejected with a
        /// 400 status code.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/> instance this method extends.</param>
        /// <returns>The original <see cref="IApplicationBuilder"/>.</returns>
        public static IApplicationBuilder UseHostFiltering(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            // TODO: Check for the new IReverseProxyFeature in ServerFeatures to see if the middleware should be added at all.

            app.UseMiddleware<HostFilteringMiddleware>(app.ServerFeatures.Get<IServerAddressesFeature>()
                ?? new EmptyServerAddresses());

            return app;
        }

        private class EmptyServerAddresses : IServerAddressesFeature
        {
            public ICollection<string> Addresses => Array.Empty<string>();

            public bool PreferHostingUrls { get; set; }
        }
    }
}
