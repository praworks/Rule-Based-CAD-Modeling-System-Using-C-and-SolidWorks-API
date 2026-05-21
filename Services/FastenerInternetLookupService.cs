using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace AICAD.Services
{
    internal static class FastenerInternetLookupService
    {
        private const string NutChartUrl = "https://www.wermac.org/bolts/dimensions_hex-nuts_across-flats-and-heights_din-iso.html";
        private const string BoltChartUrl = "https://www.wermac.org/bolts/metricBDC.html";
        private const string DefaultBoltStandard = "ISO 4014";
        private const string Din931BoltStandard = "DIN 931";
        private const string Din933BoltStandard = "DIN 933";
        private const string Iso4017BoltStandard = "ISO 4017";
        private static readonly HttpClient _http = CreateSharedHttpClient();
        private static readonly ConcurrentDictionary<string, string> _htmlCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyDictionary<string, MetricBoltInfo> _localMetricBoltTable = CreateLocalMetricBoltTable();

        internal sealed class LookupResult
        {
            public bool Applied { get; set; }
            public string EnrichedPrompt { get; set; }
            public string Summary { get; set; }
        }

        private sealed class MetricNutInfo
        {
            public string Designation { get; set; }
            public double PitchMm { get; set; }
            public double WidthAcrossFlatsMaxMm { get; set; }
            public double WidthAcrossFlatsMinMm { get; set; }
            public double WidthAcrossCornersMinMm { get; set; }
            public double HeightMaxMm { get; set; }
            public double HeightMinMm { get; set; }
            public string SourceUrl { get; set; }
        }

        private sealed class MetricBoltInfo
        {
            public string Designation { get; set; }
            public double WidthAcrossFlatsMinMm { get; set; }
            public double WidthAcrossFlatsMaxMm { get; set; }
            public double HeadHeightMinMm { get; set; }
            public double HeadHeightMaxMm { get; set; }
            public string StandardName { get; set; }
            public bool UsedDefaultStandard { get; set; }
            public string SourceUrl { get; set; }
        }

        private sealed class BoltStandardSelection
        {
            public string CanonicalName { get; set; }
            public bool UsedDefault { get; set; }
            public bool UsesIsoAcrossFlats { get; set; }
        }

        internal static LookupResult TryEnrichPrompt(string userPrompt)
        {
            var original = userPrompt ?? string.Empty;
            if (string.IsNullOrWhiteSpace(original))
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Fastener lookup skipped: empty prompt."
                };
            }

            if (!IsEnabled())
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Fastener lookup disabled."
                };
            }

            var lower = original.ToLowerInvariant();
            var mentionsNut = ContainsNutKeyword(lower);
            var mentionsBolt = ContainsBoltKeyword(lower);
            if (!mentionsNut && !mentionsBolt)
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Fastener lookup skipped: no nut/bolt keyword detected."
                };
            }

            var designation = TryExtractMetricDesignation(original);
            if (string.IsNullOrWhiteSpace(designation))
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Fastener lookup skipped: no metric designation like M10 was detected."
                };
            }

            MetricNutInfo nutInfo = null;
            MetricBoltInfo boltInfo = null;
            var boltStandard = mentionsBolt ? ResolveBoltStandard(original) : null;

            try
            {
                if (mentionsNut || mentionsBolt)
                    nutInfo = TryGetMetricNutInfo(designation);
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(FastenerInternetLookupService), $"Nut lookup failed for {designation}", ex);
            }

            try
            {
                if (mentionsBolt)
                    boltInfo = TryResolveMetricBoltInfo(designation, boltStandard);
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(FastenerInternetLookupService), $"Bolt lookup failed for {designation}", ex);
            }

            if (nutInfo == null && boltInfo == null)
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = $"Fastener lookup found no online dimension row for {designation}."
                };
            }

            var enrichment = BuildEnrichmentBlock(original, designation, mentionsNut, mentionsBolt, nutInfo, boltInfo);
            var summary = BuildSummary(designation, mentionsNut, mentionsBolt, nutInfo, boltInfo);
            return new LookupResult
            {
                Applied = true,
                EnrichedPrompt = enrichment,
                Summary = summary
            };
        }

        private static string BuildEnrichmentBlock(string originalPrompt, string designation, bool mentionsNut, bool mentionsBolt, MetricNutInfo nutInfo, MetricBoltInfo boltInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine(originalPrompt.Trim());
            sb.AppendLine();
            sb.AppendLine("ONLINE FASTENER CONTEXT (auto-fetched; use only if it matches the user request):");

            if (mentionsNut && nutInfo != null)
            {
                sb.AppendLine($"- Standard interpretation: ISO 4032 metric hex nut size {designation}.");
                sb.AppendLine($"- Nut dimensions from online chart: width across flats {nutInfo.WidthAcrossFlatsMaxMm:0.###} mm max ({nutInfo.WidthAcrossFlatsMinMm:0.###} mm min), width across corners {nutInfo.WidthAcrossCornersMinMm:0.###} mm min, nut height {nutInfo.HeightMaxMm:0.###} mm max ({nutInfo.HeightMinMm:0.###} mm min).");
                sb.AppendLine($"- Modeling hint: interpret this as a hex nut body with across-flats size about {nutInfo.WidthAcrossFlatsMaxMm:0.###} mm and height about {nutInfo.HeightMaxMm:0.###} mm. If a center hole is required, ask one short clarification instead of assuming more geometry.");
                sb.AppendLine($"- Source: {nutInfo.SourceUrl}");
            }

            if (mentionsBolt && boltInfo != null)
            {
                sb.AppendLine($"- Standard interpretation: {boltInfo.StandardName} metric hex-head bolt size {designation}{(boltInfo.UsedDefaultStandard ? " (default standard)." : ".")}");
                sb.AppendLine($"- Bolt head dimensions from online chart: width across flats {boltInfo.WidthAcrossFlatsMaxMm:0.###} mm max ({boltInfo.WidthAcrossFlatsMinMm:0.###} mm min) and head height {boltInfo.HeadHeightMaxMm:0.###} mm max ({boltInfo.HeadHeightMinMm:0.###} mm min).");
                sb.AppendLine($"- Modeling hint: interpret this as a hex head per {boltInfo.StandardName} near {boltInfo.WidthAcrossFlatsMaxMm:0.###} mm across flats with head height about {boltInfo.HeadHeightMaxMm:0.###} mm, plus a cylindrical shank using the nominal {designation} diameter. If shank length is missing, ask one clarification instead of guessing.");
                sb.AppendLine($"- Source: {boltInfo.SourceUrl}");
            }

            sb.AppendLine("- If the user requested a different fastener standard or a non-hex fastener, ask one short clarification instead of guessing.");
            return sb.ToString().Trim();
        }

        private static string BuildSummary(string designation, bool mentionsNut, bool mentionsBolt, MetricNutInfo nutInfo, MetricBoltInfo boltInfo)
        {
            var parts = new List<string>();
            if (mentionsNut && nutInfo != null)
            {
                parts.Add($"nut {designation}: AF={nutInfo.WidthAcrossFlatsMaxMm:0.###}mm H={nutInfo.HeightMaxMm:0.###}mm");
            }

            if (mentionsBolt && boltInfo != null)
            {
                parts.Add($"bolt {designation} {boltInfo.StandardName}{(boltInfo.UsedDefaultStandard ? " default" : string.Empty)}: AF={boltInfo.WidthAcrossFlatsMaxMm:0.###}mm K={boltInfo.HeadHeightMaxMm:0.###}mm");
            }

            return parts.Count > 0
                ? "Fastener lookup applied: " + string.Join("; ", parts)
                : $"Fastener lookup applied for {designation}.";
        }

        private static MetricNutInfo TryGetMetricNutInfo(string designation)
        {
            var html = GetCachedHtml(NutChartUrl);
            if (string.IsNullOrWhiteSpace(html)) return null;

            foreach (var row in ExtractRows(html))
            {
                if (row.Count < 7) continue;
                if (!CellMatchesDesignation(row[0], designation)) continue;

                if (!TryParseInvariant(row[1], out var pitchMm)) continue;
                if (!TryParseInvariant(row[2], out var wafMaxMm)) continue;
                if (!TryParseInvariant(row[3], out var wafMinMm)) continue;
                if (!TryParseInvariant(row[4], out var cornersMinMm)) continue;
                if (!TryParseInvariant(row[5], out var heightMaxMm)) continue;
                if (!TryParseInvariant(row[6], out var heightMinMm)) continue;

                return new MetricNutInfo
                {
                    Designation = designation,
                    PitchMm = pitchMm,
                    WidthAcrossFlatsMaxMm = wafMaxMm,
                    WidthAcrossFlatsMinMm = wafMinMm,
                    WidthAcrossCornersMinMm = cornersMinMm,
                    HeightMaxMm = heightMaxMm,
                    HeightMinMm = heightMinMm,
                    SourceUrl = NutChartUrl
                };
            }

            return null;
        }

        internal sealed class ResolvedMetricBoltInfo
        {
            public string Designation { get; set; }
            public string StandardName { get; set; }
            public bool UsedDefaultStandard { get; set; }
            public double WidthAcrossFlatsMinMm { get; set; }
            public double WidthAcrossFlatsMaxMm { get; set; }
            public double HeadHeightMinMm { get; set; }
            public double HeadHeightMaxMm { get; set; }
            public string SourceUrl { get; set; }
        }

        internal static ResolvedMetricBoltInfo TryResolveMetricBoltInfo(string prompt)
        {
            var designation = TryExtractMetricDesignation(prompt);
            if (string.IsNullOrWhiteSpace(designation)) return null;

            var standardSelection = ResolveBoltStandard(prompt);
            var resolved = TryResolveMetricBoltInfo(designation, standardSelection);
            if (resolved == null) return null;

            return new ResolvedMetricBoltInfo
            {
                Designation = resolved.Designation,
                StandardName = resolved.StandardName,
                UsedDefaultStandard = resolved.UsedDefaultStandard,
                WidthAcrossFlatsMinMm = resolved.WidthAcrossFlatsMinMm,
                WidthAcrossFlatsMaxMm = resolved.WidthAcrossFlatsMaxMm,
                HeadHeightMinMm = resolved.HeadHeightMinMm,
                HeadHeightMaxMm = resolved.HeadHeightMaxMm,
                SourceUrl = resolved.SourceUrl
            };
        }

        private static MetricBoltInfo TryResolveMetricBoltInfo(string designation, BoltStandardSelection standardSelection)
        {
            var online = TryGetMetricBoltInfoFromOnlineTable(designation, standardSelection);
            if (online != null) return online;
            return TryGetMetricBoltInfoFromLocalTable(designation, standardSelection);
        }

        private static MetricBoltInfo TryGetMetricBoltInfoFromOnlineTable(string designation, BoltStandardSelection standardSelection)
        {
            var html = GetCachedHtml(BoltChartUrl);
            if (string.IsNullOrWhiteSpace(html)) return null;

            foreach (var row in ExtractRows(html))
            {
                if (row.Count < 5) continue;
                if (!CellMatchesDesignation(row[0], designation)) continue;

                if (!TryParseInvariant(row[1], out var wafMinMm)) continue;
                if (!TryParseInvariant(row[2], out var wafMaxMm)) continue;
                if (!TryParseInvariant(row[3], out var headMinMm)) continue;
                if (!TryParseInvariant(row[4], out var headMaxMm)) continue;

                var boltInfo = new MetricBoltInfo
                {
                    Designation = designation,
                    WidthAcrossFlatsMinMm = wafMinMm,
                    WidthAcrossFlatsMaxMm = wafMaxMm,
                    HeadHeightMinMm = headMinMm,
                    HeadHeightMaxMm = headMaxMm,
                    StandardName = standardSelection?.CanonicalName ?? DefaultBoltStandard,
                    UsedDefaultStandard = standardSelection?.UsedDefault ?? true,
                    SourceUrl = BoltChartUrl
                };

                ApplyBoltStandardOverrides(designation, boltInfo, standardSelection);
                return boltInfo;
            }

            return null;
        }

        private static MetricBoltInfo TryGetMetricBoltInfoFromLocalTable(string designation, BoltStandardSelection standardSelection)
        {
            if (string.IsNullOrWhiteSpace(designation)) return null;
            if (!_localMetricBoltTable.TryGetValue(designation, out var row) || row == null)
                return null;

            var boltInfo = CloneMetricBoltInfo(row);
            boltInfo.StandardName = standardSelection?.CanonicalName ?? DefaultBoltStandard;
            boltInfo.UsedDefaultStandard = standardSelection?.UsedDefault ?? true;
            boltInfo.SourceUrl = "built-in metric hex bolt dimensions";
            ApplyBoltStandardOverrides(designation, boltInfo, standardSelection);
            return boltInfo;
        }

        private static void ApplyBoltStandardOverrides(string designation, MetricBoltInfo boltInfo, BoltStandardSelection standardSelection)
        {
            if (boltInfo == null) return;

            var effectiveStandard = standardSelection ?? ResolveBoltStandard(string.Empty);
            boltInfo.StandardName = effectiveStandard.CanonicalName;
            boltInfo.UsedDefaultStandard = effectiveStandard.UsedDefault;

            if (!effectiveStandard.UsesIsoAcrossFlats) return;

            switch ((designation ?? string.Empty).ToUpperInvariant())
            {
                case "M10":
                    boltInfo.WidthAcrossFlatsMinMm = 15.57;
                    boltInfo.WidthAcrossFlatsMaxMm = 16.00;
                    break;
                case "M12":
                    boltInfo.WidthAcrossFlatsMinMm = 17.57;
                    boltInfo.WidthAcrossFlatsMaxMm = 18.00;
                    break;
                case "M14":
                    boltInfo.WidthAcrossFlatsMinMm = 20.16;
                    boltInfo.WidthAcrossFlatsMaxMm = 21.00;
                    break;
                case "M22":
                    boltInfo.WidthAcrossFlatsMinMm = 33.00;
                    boltInfo.WidthAcrossFlatsMaxMm = 34.00;
                    break;
            }
        }

        private static MetricBoltInfo CloneMetricBoltInfo(MetricBoltInfo source)
        {
            if (source == null) return null;
            return new MetricBoltInfo
            {
                Designation = source.Designation,
                WidthAcrossFlatsMinMm = source.WidthAcrossFlatsMinMm,
                WidthAcrossFlatsMaxMm = source.WidthAcrossFlatsMaxMm,
                HeadHeightMinMm = source.HeadHeightMinMm,
                HeadHeightMaxMm = source.HeadHeightMaxMm,
                StandardName = source.StandardName,
                UsedDefaultStandard = source.UsedDefaultStandard,
                SourceUrl = source.SourceUrl
            };
        }

        private static List<List<string>> ExtractRows(string html)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(html)) return rows;

            foreach (Match rowMatch in Regex.Matches(html, @"<tr\b[^>]*>(?<cells>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var cells = new List<string>();
                var cellBlock = rowMatch.Groups["cells"].Value;
                foreach (Match cellMatch in Regex.Matches(cellBlock, @"<t[dh]\b[^>]*>(?<value>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var decoded = WebUtility.HtmlDecode(StripTags(cellMatch.Groups["value"].Value))
                        .Replace("\u00A0", " ")
                        .Trim();
                    if (!string.IsNullOrWhiteSpace(decoded))
                        cells.Add(decoded);
                }

                if (cells.Count > 0)
                    rows.Add(cells);
            }

            return rows;
        }

        private static string StripTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var noTags = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            noTags = Regex.Replace(noTags, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(noTags, @"\s+", " ").Trim();
        }

        private static bool CellMatchesDesignation(string cell, string designation)
        {
            if (string.IsNullOrWhiteSpace(cell) || string.IsNullOrWhiteSpace(designation)) return false;
            var normalizedCell = Regex.Replace(cell, @"\*", string.Empty).Trim();
            return normalizedCell.Equals(designation, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseInvariant(string text, out double value)
        {
            return double.TryParse(
                (text ?? string.Empty).Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static string TryExtractMetricDesignation(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;

            var match = Regex.Match(prompt, @"\bM\s*(\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return "M" + match.Groups[1].Value;
        }

        internal static string ResolveBoltStandardName(string prompt)
        {
            return ResolveBoltStandard(prompt).CanonicalName;
        }

        private static BoltStandardSelection ResolveBoltStandard(string prompt)
        {
            var text = prompt ?? string.Empty;
            if (Regex.IsMatch(text, @"\bISO\s*4017\b", RegexOptions.IgnoreCase))
            {
                return new BoltStandardSelection
                {
                    CanonicalName = Iso4017BoltStandard,
                    UsedDefault = false,
                    UsesIsoAcrossFlats = true
                };
            }

            if (Regex.IsMatch(text, @"\bDIN\s*933\b", RegexOptions.IgnoreCase))
            {
                return new BoltStandardSelection
                {
                    CanonicalName = Din933BoltStandard,
                    UsedDefault = false,
                    UsesIsoAcrossFlats = false
                };
            }

            if (Regex.IsMatch(text, @"\bDIN\s*931\b", RegexOptions.IgnoreCase))
            {
                return new BoltStandardSelection
                {
                    CanonicalName = Din931BoltStandard,
                    UsedDefault = false,
                    UsesIsoAcrossFlats = false
                };
            }

            if (Regex.IsMatch(text, @"\bISO\s*4014\b", RegexOptions.IgnoreCase))
            {
                return new BoltStandardSelection
                {
                    CanonicalName = DefaultBoltStandard,
                    UsedDefault = false,
                    UsesIsoAcrossFlats = true
                };
            }

            return new BoltStandardSelection
            {
                CanonicalName = DefaultBoltStandard,
                UsedDefault = true,
                UsesIsoAcrossFlats = true
            };
        }

        private static IReadOnlyDictionary<string, MetricBoltInfo> CreateLocalMetricBoltTable()
        {
            return new Dictionary<string, MetricBoltInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["M2"] = CreateLocalBoltRow("M2", 3.82, 4.00, 1.28, 1.52),
                ["M3"] = CreateLocalBoltRow("M3", 5.32, 5.50, 1.88, 2.12),
                ["M4"] = CreateLocalBoltRow("M4", 6.78, 7.00, 2.68, 2.92),
                ["M5"] = CreateLocalBoltRow("M5", 7.78, 8.00, 3.35, 3.65),
                ["M6"] = CreateLocalBoltRow("M6", 9.78, 10.00, 3.85, 4.15),
                ["M8"] = CreateLocalBoltRow("M8", 12.73, 13.00, 5.15, 5.45),
                ["M10"] = CreateLocalBoltRow("M10", 16.73, 17.00, 6.22, 6.58),
                ["M12"] = CreateLocalBoltRow("M12", 18.67, 19.00, 7.32, 7.68),
                ["M14"] = CreateLocalBoltRow("M14", 21.67, 22.00, 8.62, 8.98),
                ["M16"] = CreateLocalBoltRow("M16", 23.67, 24.00, 9.82, 10.20),
                ["M18"] = CreateLocalBoltRow("M18", 26.67, 27.00, 11.28, 11.70),
                ["M20"] = CreateLocalBoltRow("M20", 29.67, 30.00, 12.28, 12.70),
                ["M22"] = CreateLocalBoltRow("M22", 31.61, 32.00, 13.78, 14.20),
                ["M24"] = CreateLocalBoltRow("M24", 35.38, 36.00, 14.78, 15.20),
                ["M27"] = CreateLocalBoltRow("M27", 40.00, 41.00, 16.65, 17.40),
                ["M30"] = CreateLocalBoltRow("M30", 45.00, 46.00, 18.28, 19.12),
                ["M33"] = CreateLocalBoltRow("M33", 49.00, 50.00, 20.58, 21.42),
                ["M36"] = CreateLocalBoltRow("M36", 53.80, 55.00, 22.08, 22.92),
                ["M39"] = CreateLocalBoltRow("M39", 58.80, 60.00, 24.58, 25.42),
                ["M42"] = CreateLocalBoltRow("M42", 63.10, 65.00, 25.58, 26.42),
                ["M45"] = CreateLocalBoltRow("M45", 68.10, 70.00, 27.58, 28.42),
                ["M48"] = CreateLocalBoltRow("M48", 73.10, 75.00, 29.58, 30.42),
                ["M52"] = CreateLocalBoltRow("M52", 78.10, 80.00, 32.50, 33.50),
                ["M56"] = CreateLocalBoltRow("M56", 82.80, 85.00, 34.50, 35.50),
                ["M60"] = CreateLocalBoltRow("M60", 87.80, 90.00, 37.50, 38.50),
                ["M64"] = CreateLocalBoltRow("M64", 92.80, 95.00, 39.50, 40.50)
            };
        }

        private static MetricBoltInfo CreateLocalBoltRow(string designation, double wafMinMm, double wafMaxMm, double headMinMm, double headMaxMm)
        {
            return new MetricBoltInfo
            {
                Designation = designation,
                WidthAcrossFlatsMinMm = wafMinMm,
                WidthAcrossFlatsMaxMm = wafMaxMm,
                HeadHeightMinMm = headMinMm,
                HeadHeightMaxMm = headMaxMm,
                StandardName = DefaultBoltStandard,
                UsedDefaultStandard = true,
                SourceUrl = "built-in metric hex bolt dimensions"
            };
        }

        internal static bool ContainsNutKeyword(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\bnuts?\b", RegexOptions.IgnoreCase);
        }

        internal static bool ContainsBoltKeyword(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\bbolts?\b", RegexOptions.IgnoreCase);
        }

        private static string GetCachedHtml(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (_htmlCache.TryGetValue(url, out var cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;

            var html = DownloadString(url);
            if (!string.IsNullOrWhiteSpace(html))
                _htmlCache[url] = html;
            return html;
        }

        private static string DownloadString(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                    request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    using (var response = _http.SendAsync(request).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            AddinStatusLogger.Log(nameof(FastenerInternetLookupService), $"HTTP {(int)response.StatusCode} while fetching {url}");
                            return null;
                        }

                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(FastenerInternetLookupService), $"Failed to fetch {url}", ex);
                return null;
            }
        }

        private static bool IsEnabled()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AICAD_FASTENER_WEB_LOOKUP", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("AICAD_FASTENER_WEB_LOOKUP", EnvironmentVariableTarget.User)
                          ?? Environment.GetEnvironmentVariable("AICAD_FASTENER_WEB_LOOKUP", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    if (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (env == "0" || env.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            catch { }

            return SettingsManager.GetBool("EnableFastenerInternetLookup", true);
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }
    }
}
