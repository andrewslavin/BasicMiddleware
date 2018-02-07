﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private static readonly byte[] DefaultResponse = Encoding.ASCII.GetBytes(
            "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\"\"http://www.w3.org/TR/html4/strict.dtd\">\r\n"
            + "<HTML><HEAD><TITLE>Bad Request</TITLE>\r\n"
            + "<META HTTP-EQUIV=\"Content-Type\" Content=\"text/html; charset=us-ascii\"></ HEAD >\r\n"
            + "<BODY><h2>Bad Request - Invalid Hostname</h2>\r\n"
            + "<hr><p>HTTP Error 400. The request hostname is invalid.</p>\r\n"
            + "</BODY></HTML>");

        private readonly RequestDelegate _next;
        private readonly ILogger<HostFilteringMiddleware> _logger;
        private readonly HostFilteringOptions _options;
        private readonly IServerAddressesFeature _serverAddresses;
        private IList<string> _allowedHosts;
        private bool? _allowAnyNonEmptyHost;

        /// <summary>
        /// A middleware used to filter requests by their Host header.
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
        /// Runs the filtering
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task Invoke(HttpContext context)
        {
            EnsureConfigured();

            if (!CheckHost(context))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentLength = DefaultResponse.Length;
                return context.Response.Body.WriteAsync(DefaultResponse, 0, DefaultResponse.Length);
            }

            return _next(context);
        }

        private void EnsureConfigured()
        {
            if (_allowAnyNonEmptyHost == true || _allowedHosts?.Count > 0)
            {
                return;
            }

            var allowedHosts = new List<string>();
            if (_options.AllowedHosts?.Count > 0)
            {
                if (!TryProcessHosts(_options.AllowedHosts, allowedHosts))
                {
                    _logger.LogDebug("Wildcard detected, all requests with hosts will be allowed.");
                    _allowAnyNonEmptyHost = true;
                    return;
                }
            }
            else
            {
                if (!TryProcessHosts(_serverAddresses.Addresses.Select(address => BindingAddress.Parse(address).Host), allowedHosts))
                {
                    _logger.LogDebug("Wildcard detected, all requests with hosts will be allowed.");
                    _allowAnyNonEmptyHost = true;
                    return;
                }
            }

            if (allowedHosts.Count == 0)
            {
                throw new InvalidOperationException("No allowed hosts were configured and none could be discovered from the server.");
            }

            _logger.LogDebug("Allowed hosts: " + string.Join("; ", allowedHosts));
            _allowedHosts = allowedHosts;
        }

        // returns false if any wildcards were found
        private bool TryProcessHosts(IEnumerable<string> incoming, IList<string> resutls)
        {
            foreach (var entry in incoming)
            {
                var host = entry;
                if (ContainsNonAscii(host))
                {
                    // Punycode. Http.Sys requires you to register Unicode hosts, but the headers contain punycode.
                    host = new IdnMapping().GetAscii(host);
                }

                if (!resutls.Contains(host, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals("*", host, StringComparison.Ordinal) // HttpSys wildcard
                        || string.Equals("[::]", host, StringComparison.Ordinal) // Kestrel wildcard, IPv6 Any
                        || string.Equals("0.0.0.0", host, StringComparison.Ordinal)) // IPv4 Any
                    {
                        return false;
                    }

                    resutls.Add(host);
                }
            }

            return true;
        }

        private bool ContainsNonAscii(string host)
        {
            foreach (var ch in host)
            {
                if (ch > 127)
                {
                    return true;
                }
            }
            return false;
        }

        // This does not duplicate format validations that are expected to be performed by the host.
        private bool CheckHost(HttpContext context)
        {
            var host = new StringSegment(context.Request.Headers[HeaderNames.Host].ToString()).Trim();

            if (StringSegment.IsNullOrEmpty(host))
            {
                // Http/1.0 does not require the host header.
                // Http/1.1 requires the header but the value may be empty.
                if (!_options.AllowEmptyHosts)
                {
                    _logger.LogInformation($"{context.Request.Protocol} request rejected due to missing or empty host header.");
                    return false;
                }
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug($"{context.Request.Protocol} request allowed with missing or empty host header.");
                }
                return true;
            }

            if (_allowAnyNonEmptyHost == true)
            {
                return true;
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
                    _logger.LogInformation($"The host '{host}' has an invalid format.");
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
                    var allowedRoot = new StringSegment(allowedHost).Subsegment(1);

                    var hostRoot = host.Subsegment(host.Length - allowedRoot.Length);
                    if (hostRoot.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug($"The host '{host}' matches the allowed subdomain wildcard '{allowedHost}'.");
                        }
                        return true;
                    }
                }
            }

            _logger.LogInformation($"The host '{host}' does not match an allowed host.");
            return false;
        }
    }
}
