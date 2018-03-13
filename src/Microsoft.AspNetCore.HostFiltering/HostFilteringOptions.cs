// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.HostFiltering
{
    /// <summary>
    /// Options for the HostFiltering middleware
    /// </summary>
    public class HostFilteringOptions
    {
        /// <summary>
        /// The hosts headers that are allowed to access this site. At least one value is required.
        /// </summary>
        /// <remarks>
        /// - Port numbers must be excluded
        /// - A top level wildcard "*" allows all non-empty hosts.
        /// - Subdomain wildcards are permitted. E.g. "*.example.com" matches subdomains like foo.example.com,
        ///    but not the parent domain example.com.
        /// - Unicode host names are allowed but will be converted to punycode for matching.
        /// - IPv6 addresses must include their bounding brackets and be in their normalized form.
        /// </remarks>
        public IList<string> AllowedHosts { get; set; } = new List<string>();

        /// <summary>
        /// Indicates if requests without hosts are allowed. The default is true.
        /// </summary>
        /// <remarks>
        /// HTTP/1.0 does not require a host header.
        /// Http/1.1 requires a host header, but says the value may be empty.
        /// </remarks>
        public bool AllowEmptyHosts { get; set; } = true;

        // Note if this were disabled then things like the status code middleware may try to re-execute
        // the request. This is a low level protocol violation, pretty error pages should not be required.
        /// <summary>
        /// Indicates if the 400 response should include a default message or be empty. This is enabled by default.
        /// </summary>
        public bool IncludedFailureMessage { get; set; } = true;
    }
}
