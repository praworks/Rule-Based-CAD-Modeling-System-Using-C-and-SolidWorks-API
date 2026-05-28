using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace AICAD.Services
{
    internal static class PipeInternetLookupService
    {
        private const string PipeScheduleChartUrl = "https://induspecs.com/pipe-schedule-chart/";
        private static readonly HttpClient _http = CreateSharedHttpClient();
        private static readonly ConcurrentDictionary<string, string> _htmlCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class LookupResult
        {
            public bool Applied { get; set; }
            public string EnrichedPrompt { get; set; }
            public string Summary { get; set; }
        }

        internal sealed class PipeDimensionInfo
        {
            public string NpsLabel { get; set; }
            public int Schedule { get; set; }
            public double OuterDiameterMm { get; set; }
            public double WallThicknessMm { get; set; }
            public double InnerDiameterMm { get; set; }
            public string SourceUrl { get; set; }
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
                    Summary = "Pipe lookup skipped: empty prompt."
                };
            }

            if (!IsEnabled())
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Pipe lookup disabled."
                };
            }

            if (!ContainsPipeKeyword(original))
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Pipe lookup skipped: no pipe/tube keyword detected."
                };
            }

            var schedule = TryExtractSchedule(original);
            if (!schedule.HasValue)
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Pipe lookup skipped: no pipe schedule like SCH 40 was detected."
                };
            }

            var nps = TryExtractNominalPipeSizeLabel(original);
            if (string.IsNullOrWhiteSpace(nps))
            {
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = "Pipe lookup skipped: no nominal pipe size like 1 inch was detected."
                };
            }

            try
            {
                var info = TryResolvePipeDimensions(nps, schedule.Value);
                if (info == null)
                {
                    return new LookupResult
                    {
                        Applied = false,
                        EnrichedPrompt = original,
                        Summary = $"Pipe lookup found no online row for NPS {nps} SCH {schedule.Value}."
                    };
                }

                return new LookupResult
                {
                    Applied = true,
                    EnrichedPrompt = BuildEnrichmentBlock(original, info),
                    Summary = $"Pipe lookup applied: NPS {info.NpsLabel} SCH {info.Schedule}: OD={info.OuterDiameterMm:0.###}mm ID={info.InnerDiameterMm:0.###}mm wall={info.WallThicknessMm:0.###}mm"
                };
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(PipeInternetLookupService), $"Pipe lookup failed for NPS {nps} SCH {schedule.Value}", ex);
                return new LookupResult
                {
                    Applied = false,
                    EnrichedPrompt = original,
                    Summary = $"Pipe lookup failed for NPS {nps} SCH {schedule.Value}: {ex.Message}"
                };
            }
        }

        internal static bool ContainsPipeKeyword(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\b(pipes?|tubes?|tubing|hollow\s+cylinder)\b", RegexOptions.IgnoreCase);
        }

        internal static int? TryExtractSchedule(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, @"\b(?:sch(?:edule)?\.?|schedule)\s*[-:]?\s*(\d{1,3})\b", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            if (!int.TryParse(match.Groups[1].Value, out var schedule)) return null;
            return schedule > 0 ? (int?)schedule : null;
        }

        internal static string TryExtractNominalPipeSizeLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var patterns = new[]
            {
                @"\b(?<size>\d+\s*[- ]\s*\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)\s*(?:inch|in\.?|[""\u2033])\s+(?:nps\s+)?(?:sch(?:edule)?\.?|schedule)\b",
                @"\b(?<size>\d+\s*[- ]\s*\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)\s*(?:inch|in\.?|[""\u2033])\s+(?:pipes?|tubes?|tubing)\b",
                @"\b(?:nps|pipe\s+size)\s*(?<size>\d+\s*[- ]\s*\d+/\d+|\d+/\d+|\d+(?:\.\d+)?)\b"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var normalized = NormalizeNpsLabel(match.Groups["size"].Value);
                if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
            }

            return null;
        }

        internal static PipeDimensionInfo TryParsePipeScheduleChartHtml(string html, string npsLabel, int schedule)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(npsLabel)) return null;
            if (schedule != 40 && schedule != 80) return null;

            foreach (var row in ExtractRows(html))
            {
                if (row.Count < 7) continue;
                var rowNps = NormalizeNpsLabel(row[0]);
                if (!string.Equals(rowNps, npsLabel, StringComparison.OrdinalIgnoreCase)) continue;

                if (!TryParseInvariant(row[1], out var odMm)) continue;
                var wallIndex = schedule == 40 ? 3 : 5;
                if (!TryParseInvariant(row[wallIndex], out var wallMm)) continue;

                var idMm = odMm - (2.0 * wallMm);
                if (odMm <= 0 || wallMm <= 0 || idMm <= 0) return null;

                return new PipeDimensionInfo
                {
                    NpsLabel = rowNps,
                    Schedule = schedule,
                    OuterDiameterMm = Round3(odMm),
                    WallThicknessMm = Round3(wallMm),
                    InnerDiameterMm = Round3(idMm),
                    SourceUrl = PipeScheduleChartUrl
                };
            }

            return null;
        }

        private static PipeDimensionInfo TryResolvePipeDimensions(string npsLabel, int schedule)
        {
            var html = GetCachedHtml(PipeScheduleChartUrl);
            return TryParsePipeScheduleChartHtml(html, npsLabel, schedule);
        }

        private static string BuildEnrichmentBlock(string originalPrompt, PipeDimensionInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine(originalPrompt.Trim());
            sb.AppendLine();
            sb.AppendLine("ONLINE PIPE CONTEXT (auto-fetched; use only if it matches the user request):");
            sb.AppendLine($"- Standard interpretation: NPS {info.NpsLabel} SCH {info.Schedule} pipe.");
            sb.AppendLine($"- Pipe dimensions from online chart: outer diameter {info.OuterDiameterMm:0.###} mm, wall thickness {info.WallThicknessMm:0.###} mm, inner diameter {info.InnerDiameterMm:0.###} mm.");
            sb.AppendLine($"- Modeling hint: interpret this as a hollow straight pipe. Include outer diameter {info.OuterDiameterMm:0.###} mm, inner diameter {info.InnerDiameterMm:0.###} mm, and the user-requested length/height in the feature intent.");
            sb.AppendLine($"- Source: {info.SourceUrl}");
            sb.AppendLine("- Do not ask for pipe OD or ID when this reference data matches the requested NPS and schedule.");
            sb.AppendLine("- Do not use the nominal pipe size as the modeled outside diameter.");
            return sb.ToString().Trim();
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

        private static string NormalizeNpsLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var text = WebUtility.HtmlDecode(value)
                .Replace("\u00A0", " ")
                .Replace("\u2033", "\"")
                .Replace("\"", string.Empty)
                .Trim();

            text = ReplaceUnicodeFractions(text);
            text = Regex.Replace(text, @"\s+", " ");

            var mixed = Regex.Match(text, @"^(?<whole>\d+)\s*[- ]\s*(?<num>\d+)\s*/\s*(?<den>\d+)$");
            if (mixed.Success)
                return $"{mixed.Groups["whole"].Value}-{mixed.Groups["num"].Value}/{mixed.Groups["den"].Value}";

            var fraction = Regex.Match(text, @"^(?<num>\d+)\s*/\s*(?<den>\d+)$");
            if (fraction.Success)
                return $"{fraction.Groups["num"].Value}/{fraction.Groups["den"].Value}";

            var number = Regex.Match(text, @"^\d+(?:\.\d+)?$");
            if (number.Success)
                return text;

            return null;
        }

        private static string ReplaceUnicodeFractions(string value)
        {
            return (value ?? string.Empty)
                .Replace("\u00BC", "1/4")
                .Replace("\u00BD", "1/2")
                .Replace("\u00BE", "3/4")
                .Replace("\u215B", "1/8")
                .Replace("\u215C", "3/8")
                .Replace("\u215D", "5/8")
                .Replace("\u215E", "7/8");
        }

        private static string StripTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var noTags = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            noTags = Regex.Replace(noTags, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(noTags, @"\s+", " ").Trim();
        }

        private static bool TryParseInvariant(string text, out double value)
        {
            var cleaned = Regex.Replace(text ?? string.Empty, @"[^0-9.\-]", string.Empty);
            return double.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static double Round3(double value)
        {
            return Math.Round(value, 3, MidpointRounding.AwayFromZero);
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
                            AddinStatusLogger.Log(nameof(PipeInternetLookupService), $"HTTP {(int)response.StatusCode} while fetching {url}");
                            return null;
                        }

                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(PipeInternetLookupService), $"Failed to fetch {url}", ex);
                return null;
            }
        }

        private static bool IsEnabled()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AICAD_PIPE_WEB_LOOKUP", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("AICAD_PIPE_WEB_LOOKUP", EnvironmentVariableTarget.User)
                          ?? Environment.GetEnvironmentVariable("AICAD_PIPE_WEB_LOOKUP", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    if (env == "1" || env.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (env == "0" || env.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            catch { }

            return SettingsManager.GetBool("EnablePipeInternetLookup", true);
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }
    }
}
