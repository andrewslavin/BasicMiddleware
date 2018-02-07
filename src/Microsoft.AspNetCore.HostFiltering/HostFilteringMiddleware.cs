// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.HostFiltering
{
    /// <summary>
    /// 
    /// </summary>
    public class HostFilteringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HostFilteringMiddleware> _logger;
        private readonly HostFilteringOptions _options;
        private readonly IServerAddressesFeature _serverAddresses;
        private IList<string> _allowedHosts;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <param name="serverAddresses"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public HostFilteringMiddleware(RequestDelegate next, ILogger<HostFilteringMiddleware> logger, 
            IOptions<HostFilteringOptions> options, IServerAddressesFeature serverAddresses)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _serverAddresses = serverAddresses ?? throw new ArgumentNullException(nameof(serverAddresses));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task Invoke(HttpContext context)
        {
            EnsureConfigured();

            if (!ValidateHost(context))
            {
                context.Response.StatusCode = 400;
                _logger.LogDebug("Request rejected due to incorrect host header.");
                return Task.CompletedTask;
            }

            return _next(context);
        }

        private void EnsureConfigured()
        {
            if (_allowedHosts?.Count > 0)
            {
                return;
            }

            if (_options.AllowedHosts?.Count > 0)
            {
                // TODO: Should we try to normalize the values? E.g. Punycode?
                _allowedHosts = _options.AllowedHosts;
                return;
            }

            var allowedHosts = new List<string>();
            foreach (var address in _serverAddresses.Addresses)
            {
                // TODO: What about Punycode? Http.Sys requires you to register Unicode hosts.
                var bindingAddress = BindingAddress.Parse(address);
                if (!allowedHosts.Contains(bindingAddress.Host, StringComparer.OrdinalIgnoreCase))
                {
                    allowedHosts.Add(bindingAddress.Host);
                }
            }

            if (allowedHosts.Count == 0)
            {
                throw new InvalidOperationException("No allowed hosts were configured and none could be discovered from the server.");
            }

            _allowedHosts = allowedHosts;
        }

        // This does not duplicate format validations that are expected to be performed by the host.
        private bool ValidateHost(HttpContext context)
        {
            StringSegment host = context.Request.Headers[HeaderNames.Host].ToString().Trim();

            if (StringSegment.IsNullOrEmpty(host))
            {
                // Http/1.0 does not require the host header.
                // Http/1.1 requires the header but the value may be empty.
                return _options.AllowEmptyHosts;
            }

            // Drop the port

            var colonIndex = host.LastIndexOf(':');

            // IPv6 special case
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracketIndex = host.IndexOf(']');
                if (endBracketIndex < 0)
                {
                    // Invalid format
                    return false;
                }
                if (colonIndex < endBracketIndex)
                {
                    // No port, just the IPv6 Host
                    colonIndex = -1;
                }
            }

            if (colonIndex > 0)
            {
                host = host.Subsegment(0, colonIndex);
            }

            foreach (var allowedHost in _allowedHosts)
            {
                if (StringSegment.Equals(allowedHost, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Sub-domain wildcards: *.example.com
                if (allowedHost.StartsWith("*.", StringComparison.Ordinal) && host.Length >= allowedHost.Length)
                {
                    // .example.com
                    var allowedRoot = new StringSegment(allowedHost, 1, allowedHost.Length - 1);

                    var hostRoot = host.Subsegment(host.Length - allowedRoot.Length, allowedRoot.Length);
                    if (hostRoot.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
