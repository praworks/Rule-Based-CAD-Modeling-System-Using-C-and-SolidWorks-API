using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace Services
{
    public enum StepState
    {
        Pending,
        Running,
        Success,
        Error
    }

    /// <summary>
    /// Orchestrates the build lifecycle as a sequence of numbered steps and emits progress events
    /// that the Taskpane UI can subscribe to (karaoke-style highlight of the current line).
    ///
    /// Design:
    /// - Uses async/await for network and long-running steps to avoid blocking the UI thread.
    /// - Emits <see cref="ProgressChanged"/> before/after steps with optional percent values.
    /// - For CAD engine steps (9-13) raises <see cref="OnCadActionRequested"/> so the caller
    ///   (which owns `swApp`/`swModel`) can perform SolidWorks API calls on the UI thread.
    /// </summary>
    public class KaraokeStyleStatus
    {
        // ProgressChanged: stepIndex (1-based), status message, step state, percent (0-100 or null)
        public event Action<int, string, StepState, int?> ProgressChanged;

        // CAD action requested for steps that must run against SolidWorks API. The handler
        // should perform the CAD operations and return a Task. The handler receives the
        // step index, swApp and swModel objects and the cancellation token.
        public event Func<int, ISldWorks, ModelDoc2, CancellationToken, Task> OnCadActionRequested;

        private readonly HttpClient _http = new HttpClient();

        public KaraokeStyleStatus()
        {
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        private void Raise(int idx, string msg, StepState state, int? pct = null)
        {
            try { ProgressChanged?.Invoke(idx, msg, state, pct); } catch { }
        }

        /// <summary>
        /// Run the full build lifecycle for the provided prompt. Use cancellation token to abort.
        /// CAD steps will invoke <see cref="OnCadActionRequested"/> when reached.
        /// </summary>
        public async Task RunBuildLifecycleAsync(string prompt, ISldWorks swApp, ModelDoc2 swModel, CancellationToken ct)
        {
            var steps = new Dictionary<int, (string msg, string phase)>
            {
                [1] = ("Got your request","UI Trigger"),
                [2] = ("Preparing inputs","Data Prep"),
                [3] = ("Connecting to AI","Network"),
                [4] = ("Sending request to AI","Network"),
                [5] = ("Waiting for AI response","Async Wait"),
                [6] = ("AI responded","Network"),
                [7] = ("Reading AI response","Parsing"),
                [8] = ("Checking parameters","Validation"),
                [9] = ("Building sketch","CAD Engine"),
                [10] = ("Adding features","CAD Engine"),
                [11] = ("Applying constraints","Geometry"),
                [12] = ("Running checks","Validation"),
                [13] = ("Saving model","IO"),
                [14] = ("Updating UI","Sync"),
                [15] = ("Complete","Finalization")
            };

            // Step 1: quick UI ack
            Raise(1, steps[1].msg, StepState.Running, 0);
            await Task.Yield();
            Raise(1, steps[1].msg, StepState.Success, 100);

            // Step 2: Preparing inputs (example: fetch few-shot examples)
            Raise(2, steps[2].msg, StepState.Running, 0);
            JArray fewShot = null;
            try
            {
                await Task.Run(() =>
                {
                    // Placeholder: integrate with MongoFeedbackStore or other store here.
                    // Example: fewShot = MongoFeedbackStore.LoadFewShotExamples(prompt);
                    fewShot = new JArray();
                }, ct);
                Raise(2, steps[2].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(2, $"{steps[2].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 3: Connecting to AI (health check)
            Raise(3, steps[3].msg, StepState.Running, 0);
            var aiBase = "http://127.0.0.1:1234";
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(8));
                    var healthRes = await _http.GetAsync(aiBase + "/health", cts.Token).ConfigureAwait(false);
                    if (!healthRes.IsSuccessStatusCode) throw new HttpRequestException($"Health check failed: {healthRes.StatusCode}");
                    Raise(3, steps[3].msg, StepState.Success, 100);
                }
            }
            catch (Exception ex)
            {
                Raise(3, $"{steps[3].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            string aiResponseRaw = null;

            // Step 4: Sending request to AI
            Raise(4, steps[4].msg, StepState.Running, 0);
            try
            {
                var payload = new JObject
                {
                    ["prompt"] = prompt ?? string.Empty,
                    ["few_shot"] = fewShot ?? new JArray(),
                    ["max_tokens"] = 2048
                };

                using (var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json"))
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(60));
                    var resp = await _http.PostAsync(aiBase + "/generate", content, cts.Token).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    aiResponseRaw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Raise(4, steps[4].msg, StepState.Success, 100);
                }
            }
            catch (Exception ex)
            {
                Raise(4, $"{steps[4].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 5: Waiting for AI response (we already awaited the response above, but keep semantic step)
            Raise(5, steps[5].msg, StepState.Running, 50);
            await Task.Yield();
            Raise(5, steps[5].msg, StepState.Success, 100);

            // Step 6: AI responded
            Raise(6, steps[6].msg, StepState.Running, 0);
            if (string.IsNullOrWhiteSpace(aiResponseRaw))
            {
                Raise(6, "Empty AI response", StepState.Error, null);
                return;
            }
            Raise(6, steps[6].msg, StepState.Success, 100);

            // Step 7: Reading AI response (parse JSON)
            Raise(7, steps[7].msg, StepState.Running, 0);
            JToken parsed = null;
            try
            {
                parsed = JToken.Parse(aiResponseRaw);
                // Example: expect an array of operations at parsed["operations"] or root array
                Raise(7, steps[7].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(7, $"{steps[7].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 8: Checking parameters
            Raise(8, steps[8].msg, StepState.Running, 0);
            try
            {
                // Placeholder validation: ensure parsed is not null and contains expected fields
                if (parsed == null) throw new Exception("Parsed response is null");
                // Real validation logic should inspect geometry params, units, planes etc.
                Raise(8, steps[8].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(8, $"{steps[8].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // CAD steps (9-13) will delegate to OnCadActionRequested if available.
            for (int s = 9; s <= 13; s++)
            {
                Raise(s, steps[s].msg, StepState.Running, 0);
                try
                {
                    if (OnCadActionRequested != null)
                    {
                        // Invoke handler and await completion — the handler should perform required swApp/swModel calls.
                        await OnCadActionRequested.Invoke(s, swApp, swModel, ct).ConfigureAwait(false);
                        Raise(s, steps[s].msg, StepState.Success, 100);
                    }
                    else
                    {
                        // No CAD handler attached; signal success but log as a no-op.
                        Raise(s, steps[s].msg + " (no CAD handler)", StepState.Success, 100);
                    }
                }
                catch (Exception ex)
                {
                    // Step 10 (Adding features) is particularly important to handle errors.
                    Raise(s, $"{steps[s].msg}: {ex.Message}", StepState.Error, null);
                    return;
                }
            }

            // Step 12: Running checks
            Raise(12, steps[12].msg, StepState.Running, 0);
            try
            {
                // Caller may have run model checks in prior CAD steps; add a light check here.
                await Task.Run(() => { /* optional model checks */ }, ct);
                Raise(12, steps[12].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(12, $"{steps[12].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 13: Saving model
            Raise(13, steps[13].msg, StepState.Running, 0);
            try
            {
                if (swModel != null)
                {
                    // Delegate actual save to a CAD handler if available
                    if (OnCadActionRequested != null)
                    {
                        await OnCadActionRequested.Invoke(13, swApp, swModel, ct).ConfigureAwait(false);
                        Raise(13, steps[13].msg, StepState.Success, 100);
                    }
                    else
                    {
                        Raise(13, steps[13].msg + " (no CAD handler)", StepState.Success, 100);
                    }
                }
                else
                {
                    Raise(13, "No active model to save", StepState.Error, null);
                    return;
                }
            }
            catch (Exception ex)
            {
                Raise(13, $"{steps[13].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 14: Updating UI
            Raise(14, steps[14].msg, StepState.Running, 0);
            try
            {
                // Let caller update taskpane/UI when ProgressChanged events fire.
                await Task.Yield();
                Raise(14, steps[14].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(14, $"{steps[14].msg}: {ex.Message}", StepState.Error, null);
                return;
            }

            // Step 15: Complete
            Raise(15, steps[15].msg, StepState.Running, 0);
            try
            {
                // Finalization: e.g., log success to MongoDB (caller may subscribe to ProgressChanged and log)
                await Task.Yield();
                Raise(15, steps[15].msg, StepState.Success, 100);
            }
            catch (Exception ex)
            {
                Raise(15, $"{steps[15].msg}: {ex.Message}", StepState.Error, null);
            }
        }
    }
}
