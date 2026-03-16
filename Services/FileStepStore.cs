using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal class FileStepStore : IStepStore
    {
        private readonly string _runsPath;
        private readonly string _feedbackPath;

        public string LastError { get; private set; }

        public FileStepStore(string baseDirectory, string runsFileName = "run_feedback.jsonl", string feedbackFileName = "run_feedback_feedback.jsonl")
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("Base directory is required", nameof(baseDirectory));
            Directory.CreateDirectory(baseDirectory);
            _runsPath = Path.Combine(baseDirectory, runsFileName);
            _feedbackPath = Path.Combine(baseDirectory, feedbackFileName);
        }

        public async Task<bool> SaveRunWithStepsAsync(
            string runKey,
            string prompt,
            string model,
            string planJson,
            StepExecutionResult exec,
            TimeSpan llm,
            TimeSpan total,
            string error)
        {
            try
            {
                var doc = new JObject
                {
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["run_key"] = runKey ?? string.Empty,
                    ["prompt"] = prompt ?? string.Empty,
                    ["model"] = model ?? string.Empty,
                    ["plan"] = planJson ?? string.Empty,
                    ["success"] = exec?.Success ?? false,
                    ["llm_ms"] = (long)llm.TotalMilliseconds,
                    ["total_ms"] = (long)total.TotalMilliseconds,
                    ["error"] = error ?? string.Empty,
                    ["steps"] = BuildStepsArray(planJson, exec)
                };

                using (var sw = new StreamWriter(_runsPath, true, Encoding.UTF8))
                {
                    await sw.WriteLineAsync(doc.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
                }

                LastError = null;
                AddinStatusLogger.Log("FileStepStore", "SaveRunWithStepsAsync succeeded run=" + (runKey ?? string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileStepStore", "SaveRunWithStepsAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> SaveFeedbackAsync(string runKey, bool up, string comment)
        {
            try
            {
                var doc = new JObject
                {
                    ["ts"] = DateTime.UtcNow.ToString("o"),
                    ["run_key"] = runKey ?? string.Empty,
                    ["thumb"] = up ? "up" : "down",
                    ["comment"] = comment ?? string.Empty
                };

                using (var sw = new StreamWriter(_feedbackPath, true, Encoding.UTF8))
                {
                    await sw.WriteLineAsync(doc.ToString(Newtonsoft.Json.Formatting.None)).ConfigureAwait(false);
                }

                LastError = null;
                AddinStatusLogger.Log("FileStepStore", "SaveFeedbackAsync succeeded run=" + (runKey ?? string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileStepStore", "SaveFeedbackAsync failed", ex);
                return false;
            }
        }

        public List<string> GetRelevantFewShots(string prompt, int max = 3)
        {
            var shots = new List<string>();
            try
            {
                var words = Tokenize(prompt);
                var candidates = new List<(int score, string prompt, string plan, string ts)>();
                foreach (var run in LoadRecentRunDocs(200))
                {
                    if (run.Value<bool?>("success") != true) continue;

                    var candidatePrompt = run.Value<string>("prompt") ?? string.Empty;
                    var candidatePlan = run.Value<string>("plan") ?? string.Empty;
                    var candidateTs = run.Value<string>("ts") ?? string.Empty;
                    candidates.Add((Score(words, candidatePrompt), candidatePrompt, candidatePlan, candidateTs));
                }

                foreach (var candidate in candidates.OrderByDescending(c => c.score).ThenByDescending(c => c.ts).Take(max))
                {
                    shots.Add("\nInput: " + candidate.prompt + "\nOutput:" + candidate.plan);
                }

                LastError = null;
                AddinStatusLogger.Log("FileStepStore", "GetRelevantFewShots succeeded count=" + shots.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileStepStore", "GetRelevantFewShots failed", ex);
            }

            return shots;
        }

        public List<RunRow> GetRecentRuns(int max = 50)
        {
            var list = new List<RunRow>();
            try
            {
                foreach (var doc in LoadRecentRunDocs(max))
                {
                    list.Add(new RunRow
                    {
                        RunKey = doc.Value<string>("run_key") ?? string.Empty,
                        Timestamp = doc.Value<string>("ts") ?? string.Empty,
                        Prompt = doc.Value<string>("prompt") ?? string.Empty,
                        Model = doc.Value<string>("model") ?? string.Empty,
                        Plan = doc.Value<string>("plan") ?? string.Empty,
                        Success = doc.Value<bool?>("success") == true,
                        LlmMs = doc.Value<long?>("llm_ms") ?? 0,
                        TotalMs = doc.Value<long?>("total_ms") ?? 0,
                        Error = doc.Value<string>("error") ?? string.Empty
                    });
                }

                LastError = null;
                AddinStatusLogger.Log("FileStepStore", "GetRecentRuns succeeded count=" + list.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileStepStore", "GetRecentRuns failed", ex);
            }

            return list;
        }

        public List<StepRow> GetStepsForRun(string runKey)
        {
            var list = new List<StepRow>();
            try
            {
                foreach (var doc in LoadRecentRunDocs(int.MaxValue))
                {
                    if (!string.Equals(doc.Value<string>("run_key"), runKey, StringComparison.Ordinal)) continue;

                    var steps = doc["steps"] as JArray;
                    if (steps == null) break;

                    foreach (var token in steps.OfType<JObject>().OrderBy(s => s.Value<int?>("step_index") ?? 0))
                    {
                        list.Add(new StepRow
                        {
                            StepIndex = token.Value<int?>("step_index") ?? 0,
                            Op = token.Value<string>("op") ?? string.Empty,
                            ParamsJson = token.Value<string>("params_json") ?? string.Empty,
                            Success = token.Value<bool?>("success") == true,
                            Error = token.Value<string>("error") ?? string.Empty
                        });
                    }

                    break;
                }

                LastError = null;
                AddinStatusLogger.Log("FileStepStore", "GetStepsForRun succeeded count=" + list.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileStepStore", "GetStepsForRun failed", ex);
            }

            return list;
        }

        private IEnumerable<JObject> LoadRecentRunDocs(int max)
        {
            if (!File.Exists(_runsPath)) yield break;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            var lines = File.ReadAllLines(_runsPath);

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject doc;
                try { doc = JObject.Parse(line); }
                catch { continue; }

                var runKey = doc.Value<string>("run_key") ?? string.Empty;
                if (!seen.Add(runKey)) continue;

                yield return doc;
                count++;
                if (count >= max) yield break;
            }
        }

        private static JArray BuildStepsArray(string planJson, StepExecutionResult exec)
        {
            var result = new JArray();
            if (string.IsNullOrWhiteSpace(planJson)) return result;

            try
            {
                var plan = JObject.Parse(planJson);
                var steps = plan["steps"] as JArray;
                if (steps == null) return result;

                var execMap = new Dictionary<int, JObject>();
                if (exec?.Log != null)
                {
                    foreach (var entry in exec.Log.OfType<JObject>())
                    {
                        var idx = entry.Value<int?>("step") ?? -1;
                        if (idx >= 0) execMap[idx] = entry;
                    }
                }

                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i] as JObject ?? new JObject();
                    var copy = step.DeepClone() as JObject ?? new JObject();
                    var op = copy.Value<string>("op") ?? string.Empty;
                    copy.Remove("op");
                    var execEntry = execMap.ContainsKey(i) ? execMap[i] : null;

                    result.Add(new JObject
                    {
                        ["step_index"] = i,
                        ["op"] = op,
                        ["params_json"] = JsonUtils.SerializeCompact(copy),
                        ["success"] = execEntry?.Value<bool?>("success") == true,
                        ["error"] = execEntry?.Value<string>("error") ?? string.Empty
                    });
                }
            }
            catch
            {
                // Keep an empty array if the plan is malformed.
            }

            return result;
        }

        private static HashSet<string> Tokenize(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return set;

            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            }

            foreach (var word in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length > 2) set.Add(word);
            }

            return set;
        }

        private static int Score(HashSet<string> words, string text)
        {
            if (words == null || words.Count == 0 || string.IsNullOrWhiteSpace(text)) return 0;

            var score = 0;
            var haystack = Tokenize(text);
            foreach (var word in words)
            {
                if (haystack.Contains(word)) score++;
            }

            return score;
        }
    }
}
