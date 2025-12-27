using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swcommands;
using AICAD.Services;
using Services;
using Newtonsoft.Json.Linq;

namespace AICAD.UI
{
    public partial class TextToCADTaskpaneWpf : UserControl
    {
        // Throttle native focus calls to avoid focus-fighting loops
        private DateTime _lastEnsureNativeFocusUtc = DateTime.MinValue;
        private readonly object _ensureNativeFocusLock = new object();

        private ObservableCollection<StepViewModel> _steps = new ObservableCollection<StepViewModel>();
        private readonly ISldWorks _swApp;
        public class PromptChangedEventArgs : EventArgs
        {
            public string Text { get; }
            public PromptChangedEventArgs(string text) { Text = text; }
        }

        /// <summary>
        /// Raised when the user clicks the Build button (before internal build runs).
        /// </summary>
        public event EventHandler BuildRequested;

        private async Task<bool> CheckLlmProvidersAsync()
        {
            var anyOk = false;
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);

                    // 1) Ollama (user can override OLLAMA_ENDPOINT)
                    var ollamaEndpoint = System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.User)
                                         ?? System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                         ?? System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                         ?? "http://127.0.0.1:11434";
                    try
                    {
                        AppendStatusLine("[LLM-STATUS] Checking Ollama: " + ollamaEndpoint);
                        var u = new UriBuilder(ollamaEndpoint) { Path = "/api/tags" }.Uri.ToString();
                        var resp = await http.GetAsync(u).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            anyOk = true;
                            var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            try
                            {
                                var j = JObject.Parse(txt);
                                var models = j["models"] as JArray;
                                if (models != null && models.Count > 0)
                                {
                                    var names = models.Select(m => (m["model"] ?? m["name"])?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(5);
                                    AppendStatusLine("[LLM-STATUS] Ollama OK — models: " + string.Join(", ", names));
                                }
                                else AppendStatusLine("[LLM-STATUS] Ollama OK — no models listed");
                            }
                            catch { AppendStatusLine("[LLM-STATUS] Ollama OK — response parsed but no model list"); }
                        }
                        else
                        {
                            AppendStatusLine($"[LLM-STATUS] Ollama returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendStatusLine("[LLM-STATUS] Ollama check failed: " + ex.Message);
                    }

                    // 2) Generic local endpoint (LOCAL_LLM_ENDPOINT)
                    // 2a) LM Studio (optional local provider)
                    var lmStudioEndpoint = System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.User)
                                         ?? System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                         ?? System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                         ?? "http://127.0.0.1:8080";
                    try
                    {
                        AppendStatusLine("[LLM-STATUS] Checking LM Studio: " + lmStudioEndpoint);
                        // Try a few common model-list paths
                        var tried = false;
                        foreach (var p in new[] { "/v1/models", "/api/models", "/models", "/v1/model/list" })
                        {
                            try
                            {
                                var u = new UriBuilder(lmStudioEndpoint) { Path = p }.Uri.ToString();
                                var r = await http.GetAsync(u).ConfigureAwait(false);
                                tried = true;
                                if (r.IsSuccessStatusCode)
                                {
                                    anyOk = true;
                                    var txt = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    AppendStatusLine("[LLM-STATUS] LM Studio OK (" + p + ")");
                                    break;
                                }
                            }
                            catch { }
                        }
                        if (!tried)
                        {
                            // fallback: try GET to base endpoint
                            try
                            {
                                var r3 = await http.GetAsync(lmStudioEndpoint).ConfigureAwait(false);
                                if (r3.IsSuccessStatusCode) anyOk = true;
                                AppendStatusLine($"[LLM-STATUS] LM Studio responded {(int)r3.StatusCode}");
                            }
                            catch (Exception ex)
                            {
                                AppendStatusLine("[LLM-STATUS] LM Studio check failed: " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendStatusLine("[LLM-STATUS] LM Studio check failed: " + ex.Message);
                    }

                    var localEndpoint = System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.User)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                        ?? "http://127.0.0.1:1234";
                    var dispLocal = GetEndpointDisplayName(localEndpoint);
                    try
                    {
                        AppendStatusLine("[LLM-STATUS] Checking " + dispLocal + ": " + localEndpoint);
                        // Try common OpenAI-style models list first
                        var modelsUrl = new UriBuilder(localEndpoint) { Path = "/v1/models" }.Uri.ToString();
                        var r2 = await http.GetAsync(modelsUrl).ConfigureAwait(false);
                        if (r2.IsSuccessStatusCode)
                        {
                            anyOk = true;
                            var txt2 = await r2.Content.ReadAsStringAsync().ConfigureAwait(false);
                            try
                            {
                                var j2 = JObject.Parse(txt2);
                                if (j2["data"] is JArray arr && arr.Count > 0)
                                {
                                    var names = arr.Children().Select(c => c["id"]?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(5);
                                    AppendStatusLine("[LLM-STATUS] " + dispLocal + " OK — models: " + string.Join(", ", names));
                                }
                                else AppendStatusLine("[LLM-STATUS] " + dispLocal + " OK — models endpoint returned no data");
                            }
                            catch { AppendStatusLine("[LLM-STATUS] " + dispLocal + " OK — models endpoint returned non-JSON or unexpected shape"); }
                        }
                        else
                        {
                            // As a fallback, try a simple GET to the base endpoint to check connectivity
                            try
                            {
                                var r3 = await http.GetAsync(localEndpoint).ConfigureAwait(false);
                                if (r3.IsSuccessStatusCode) anyOk = true;
                                AppendStatusLine($"[LLM-STATUS] {dispLocal} responded {(int)r3.StatusCode}");
                            }
                            catch (Exception ex)
                            {
                                AppendStatusLine("[LLM-STATUS] " + dispLocal + " check failed: " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendStatusLine("[LLM-STATUS] " + dispLocal + " check failed: " + ex.Message);
                    }

                    // 3) Groq/cloud provider
                    var groqKey = System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.User)
                                  ?? System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.Process)
                                  ?? System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrWhiteSpace(groqKey))
                    {
                        try
                        {
                            AppendStatusLine("[LLM-STATUS] Checking Groq API");
                            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/v1/models");
                            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", groqKey);
                            var r = await http.SendAsync(req).ConfigureAwait(false);
                            if (r.IsSuccessStatusCode)
                            {
                                anyOk = true;
                                var txt = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                                try
                                {
                                    var j = JObject.Parse(txt);
                                    if (j["data"] is JArray arr && arr.Count > 0)
                                    {
                                        var names = arr.Children().Select(c => c["id"]?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(5);
                                        AppendStatusLine("[LLM-STATUS] Groq OK — models: " + string.Join(", ", names));
                                    }
                                    else AppendStatusLine("[LLM-STATUS] Groq OK — no models listed");
                                }
                                catch { AppendStatusLine("[LLM-STATUS] Groq OK — response parsed but no model list"); }
                            }
                            else
                            {
                                AppendStatusLine($"[LLM-STATUS] Groq returned {(int)r.StatusCode} {r.ReasonPhrase}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendStatusLine("[LLM-STATUS] Groq check failed: " + ex.Message);
                        }
                    }
                    else
                    {
                        AppendStatusLine("[LLM-STATUS] Groq API key not configured");
                    }
                }
            }
            catch { }

            return anyOk;
        }

        /// <summary>
        /// Raised whenever the prompt text changes.
        /// </summary>
        public event EventHandler<PromptChangedEventArgs> PromptTextChanged;

        /// <summary>
        /// Raised when user requests to apply properties to the active model.
        /// </summary>
        public event EventHandler ApplyPropertiesRequested;
        private bool _isModified = false;
        private AICAD.Services.ILlmClient _client;
        private FileDbLogger _fileLogger;
        private MongoLogger _mongoLogger;
        private IGoodFeedbackStore _goodStore;
        private StatusWindow _statusWindow;
        private TimeSpan _lastLlm = TimeSpan.Zero;
        private TimeSpan _lastTotal = TimeSpan.Zero;
        private string _lastError;
        private string _lastPrompt;
        private string _lastReply;
        private string _lastModel;
        private string _lastDbStatus;
        private bool? _lastDbLogged;
        private string _lastRunId;
        private bool _isBuilding = false;
        private System.Threading.CancellationTokenSource _buildCts;
        private bool _lastRunCreatedModel = false;
        private string _lastCreatedModelTitle = null;
        private IStepStore _stepStore;
        private SeriesManager _seriesManager;
        private string _selectedSeries;
        private int _nextSequence;
        // LLM progress timer and stopwatch (single-line progress updates)
        private System.Windows.Forms.Timer _llmProgressTimer = null;
        private System.Diagnostics.Stopwatch _llmProgressStopwatch = null;
        private double _llmAverageSeconds = 32.0; // seconds used for ETA (loaded from settings)
        private const double _llmEmaAlpha = 0.2; // EMA smoothing factor
        // Force using only good_feedback from a specific MongoDB (when true)
        private readonly bool _forceUseOnlyGoodFeedback = true;
        private readonly string _forcedGoodFeedbackMongoUri = "mongodb+srv://prashan2011th_db_user:Uobz3oeAutZMRuCl@rule-based-cad-modeling.dlrnkre.mongodb.net/";
        // Temporary hard-coded behavior: force local-only mode and disable few-shot when using local LLM.
        // Set to false to restore previous multi-provider behavior.
        private readonly bool FORCE_LOCAL_ONLY = false;

        public TextToCADTaskpaneWpf(ISldWorks swApp)
        {
            InitializeComponent();
            _swApp = swApp;

            // Bind steps collection to the UI ItemsControl
            try
            {
                StepsItemsControl.ItemsSource = _steps;
            }
            catch { }

            // Wire up New Bottom button
            if (this.FindName("btnNewBottom") is Button btnNewBottom)
                btnNewBottom.Click += BtnNewBottom_Click;

            // Load adaptive LLM average from settings (persisted between runs)
            try
            {
                var saved = Services.SettingsManager.GetDouble("LLM_AvgSeconds", 32.0);
                _llmAverageSeconds = saved > 0 ? saved : 32.0;
                AddinStatusLogger.Log("TaskpaneWpf", $"Loaded LLM average seconds={_llmAverageSeconds}");
            }
            catch { }

            // Make sure TextBoxes are focusable and accept keyboard input when the host is clicked
            try
            {
                // 1. Register the Anti-Steal Hook when the control loads
                this.Loaded += (s, e) =>
                {
                    try
                    {
                        prompt.Focusable = true;
                        typeDescriptionTextBox.Focusable = true;
                        this.Focusable = true;

                        // --- NEW: REGISTER HOOK ---
                        var source = PresentationSource.FromVisual(this) as HwndSource;
                        if (source != null)
                        {
                            source.AddHook(ChildHwndSourceHook);
                            AppendKeyLog("Loaded: Hook registered successfully");
                        }
                        // --------------------------

                        // Force focus to description box logic (keep existing)
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                typeDescriptionTextBox.Focus();
                                System.Windows.Input.Keyboard.Focus(typeDescriptionTextBox);
                                typeDescriptionTextBox.CaretIndex = typeDescriptionTextBox.Text?.Length ?? 0;
                                AppendKeyLog("Loaded: forced TypeDesc focus");
                            }
                            catch { }
                        }), System.Windows.Threading.DispatcherPriority.Input);
                    }
                    catch (Exception ex) { AppendKeyLog("Loaded Error: " + ex.Message); }
                };

                // 2. Unregister hook when unloaded (Good practice)
                this.Unloaded += (s, e) =>
                {
                    try
                    {
                        var source = PresentationSource.FromVisual(this) as HwndSource;
                        source?.RemoveHook(ChildHwndSourceHook);
                    }
                    catch { }
                };
                // If user clicks anywhere in the WPF control, try to move focus into the clicked TextBox
                // (handles tricky host focus capture when hosted inside SolidWorks WinForms panes).
                this.PreviewMouseDown += (s, e) =>
                {
                    try
                    {
                        var src = e.OriginalSource as System.Windows.DependencyObject;
                        var clickedTextBox = FindAncestor<System.Windows.Controls.TextBox>(src);
                        if (clickedTextBox != null)
                        {
                            // Preserve special placeholder/clearing behavior for the main prompt
                            if (clickedTextBox == prompt)
                            {
                                FocusPrompt();
                            }
                            else
                            {
                                // Focus any other textbox (e.g. typeDescriptionTextBox) so keyboard input is routed
                                this.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        clickedTextBox.Focus();
                                        System.Windows.Input.Keyboard.Focus(clickedTextBox);
                                        clickedTextBox.CaretIndex = clickedTextBox.Text?.Length ?? 0;
                                    }
                                    catch { }
                                }), System.Windows.Threading.DispatcherPriority.Input);
                            }

                            e.Handled = false;
                            return;
                        }

                        // Fallback: if click is inside the prompt area bounds, ensure prompt receives focus
                        var promptContainer = prompt;
                        if (promptContainer != null)
                        {
                            var pt = e.GetPosition(promptContainer);
                            if (pt.X >= 0 && pt.X <= promptContainer.ActualWidth && pt.Y >= 0 && pt.Y <= promptContainer.ActualHeight)
                            {
                                FocusPrompt();
                                e.Handled = false;
                            }
                        }
                    }
                    catch { }
                };

                // Log key events inside the WPF TextBox to help diagnose whether key messages reach WPF
                try
                {
                    prompt.PreviewKeyDown += (s, e) => { try { AppendKeyLog($"Prompt.PreviewKeyDown: Key={e.Key}, IsRepeat={e.IsRepeat}"); } catch { } };
                    prompt.KeyDown += (s, e) => { try { AppendKeyLog($"Prompt.KeyDown: Key={e.Key}, KeyStates={e.KeyStates}"); } catch { } };
                    prompt.TextChanged += (s, e) => { try { AppendKeyLog($"Prompt.TextChanged: Length={prompt.Text?.Length}"); } catch { } };
                    prompt.PreviewTextInput += (s, e) => { try { AppendKeyLog($"Prompt.PreviewTextInput: Text='{e.Text}' Handled={e.Handled}"); } catch { } };
                    prompt.TextInput += (s, e) => { try { AppendKeyLog($"Prompt.TextInput: Text='{e.Text}' Handled={e.Handled}"); } catch { } };
                    prompt.GotKeyboardFocus += (s, e) =>
                    {
                        try
                        {
                            AppendKeyLog("Prompt.GotKeyboardFocus");
                            // Defer native focus to avoid fighting WPF's internal focus handling
                            this.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { EnsureNativeFocus(prompt); } catch { }
                            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                        }
                        catch { }
                    };
                    prompt.LostKeyboardFocus += (s, e) => { try { AppendKeyLog("Prompt.LostKeyboardFocus"); } catch { } };
                    // Also instrument the type description textbox to diagnose focus/keyboard issues
                        try
                        {
                            // Basic handlers
                            typeDescriptionTextBox.PreviewKeyDown += (s, e) => { try { AppendKeyLog($"TypeDesc.PreviewKeyDown: Key={e.Key}, IsRepeat={e.IsRepeat}"); } catch { } };
                            typeDescriptionTextBox.KeyDown += (s, e) => { try { AppendKeyLog($"TypeDesc.KeyDown: Key={e.Key}, KeyStates={e.KeyStates}"); } catch { } };
                            typeDescriptionTextBox.TextChanged += (s, e) => { try { AppendKeyLog($"TypeDesc.TextChanged: Length={typeDescriptionTextBox.Text?.Length}"); } catch { } };
                            typeDescriptionTextBox.PreviewTextInput += (s, e) => { try { AppendKeyLog($"TypeDesc.PreviewTextInput: Text='{e.Text}' Handled={e.Handled}"); } catch { } };
                            typeDescriptionTextBox.TextInput += (s, e) => { try { AppendKeyLog($"TypeDesc.TextInput: Text='{e.Text}' Handled={e.Handled}"); } catch { } };
                            typeDescriptionTextBox.GotKeyboardFocus += (s, e) =>
                            {
                                try
                                {
                                    AppendKeyLog("TypeDesc.GotKeyboardFocus");
                                    this.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        try { EnsureNativeFocus(typeDescriptionTextBox); } catch { }
                                    }), System.Windows.Threading.DispatcherPriority.Input);
                                }
                                catch { }
                            };
                            typeDescriptionTextBox.LostKeyboardFocus += (s, e) => { try { AppendKeyLog("TypeDesc.LostKeyboardFocus"); } catch { } };

                            // Avoid aggressive mouse/touch-driven focus grabs which can steal
                            // keyboard input from the main prompt when hosted in SolidWorks.
                            // Rely on normal focus behavior and explicit focus helpers instead.
                        }
                        catch { }
                }
                catch { }
            }
            catch { }
            // Set initial version
            var ver = GetAddinVersion();
            lblVersion.Content = ver;

            // Wire up event handlers
            shapePreset.SelectionChanged += ShapePreset_SelectionChanged;
            prompt.TextChanged += Prompt_TextChanged;

            // Raise PromptTextChanged for external listeners
            prompt.TextChanged += (s, e) => { try { PromptTextChanged?.Invoke(this, new PromptChangedEventArgs(prompt.Text)); } catch { } };

            // Build click: if building, treat as Stop request; otherwise start build
            build.Click += async (s, e) =>
            {
                try
                {
                    // Check if multiple builds are allowed
                    var allowMultiple = System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.User)
                                        ?? System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.Machine);
                    if (_isBuilding)
                    {
                        // User clicked Stop
                        try { _buildCts?.Cancel(); } catch { }
                        AppendStatusLine(StatusConsole.StatusPrefix + " Stop requested by user");
                        // Reset UI immediately so user can prepare a new prompt
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                try { build.Content = "Build"; build.Background = new SolidColorBrush(Colors.DodgerBlue); build.Foreground = new SolidColorBrush(Colors.White); build.IsEnabled = true; } catch { }
                            });
                        }
                        catch { }
                        try { _isBuilding = false; } catch { }
                        // Clear any pending created-model state since run was stopped
                        try { _lastRunCreatedModel = false; _lastCreatedModelTitle = null; Dispatcher.Invoke(()=>{ try{ applyPropertiesButton.IsEnabled = false; } catch{} }); } catch { }
                        try { SetRealTimeStatus("Stopped", Colors.DodgerBlue); } catch { }
                        return;
                    }
                    if (string.IsNullOrEmpty(allowMultiple) || allowMultiple == "0" || allowMultiple.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        // Do not disable fully; we'll switch to Stop state instead
                    }
                    // Initialize cancellation token for this run and update UI to Stop state
                    try { _buildCts?.Dispose(); } catch { }
                    _buildCts = new System.Threading.CancellationTokenSource();
                    Dispatcher.Invoke(() =>
                    {
                        try { build.Content = "Stop"; build.Background = new SolidColorBrush(Colors.IndianRed); } catch { }
                    });
                    // Immediate visual feedback so users know the click was received
                    SetRealTimeStatus("Build clicked", Colors.DodgerBlue);
                    try { AICAD.Services.LocalLogger.Log("WPF: build.Click invoked"); } catch { }
                }
                catch { }
                try { BuildRequested?.Invoke(this, EventArgs.Empty); } catch { }
                await BuildFromPromptAsync();
            };
            btnHistory.Click += BtnHistory_Click;
            // Use FindName to avoid field resolution issues during compile
            var btnStatusBtn = FindName("btnStatus") as Button;
            if (btnStatusBtn != null)
            {
                btnStatusBtn.Click += BtnStatus_Click;
            }
            btnSettings.Click += BtnSettings_Click;
            btnThumbUp.Click += async (s, e) => await SubmitFeedbackAsync(true);
            btnThumbDown.Click += async (s, e) => await SubmitFeedbackAsync(false);
            addSeriesButton.Click += AddSeriesButton_Click;
            seriesComboBox.SelectionChanged += SeriesComboBox_SelectionChanged;
            saveWithNameButton.Click += SaveWithNameButton_Click;
            applyPropertiesButton.Click += ApplyPropertiesButton_Click;

            try
            {
                var swEventPtr = _swApp as SldWorks;
                if (swEventPtr != null)
                {
                    swEventPtr.ActiveModelDocChangeNotify += new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnActiveModelDocChanged);
                    swEventPtr.FileOpenPostNotify += new DSldWorksEvents_FileOpenPostNotifyEventHandler(OnFileOpenPostNotify);
                    swEventPtr.FileNewNotify2 += new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNewNotify2);
                    swEventPtr.DocumentLoadNotify2 += new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocumentLoadNotify2);
                }
            }
            catch { }

            SetLlmStatus("Idle", Colors.DimGray);
            SetDbStatus("Unknown", Colors.DimGray);
            SetSwStatus("Idle", Colors.DimGray);
            SetTimes(null, null);
            SetLastError(null);
                // Ensure karaoke status starts gray (animated to initial color)
                try { AnimateKaraokeToColor(Colors.Gray, 0); } catch { }
                    try { InitProgressPanel(); } catch { }

                // Initialize karaoke-style status service and wire UI updates
                try
                {
                    _karaokeService = new KaraokeStyleStatus();

                    _karaokeService.ProgressChanged += (idx, msg, state, pct) =>
                    {
                        try
                        {
                            // Update the karaoke status text and animate
                            UpdateKaraokeStatus(msg);
                            try { AnimateKaraokeToColor(Colors.DodgerBlue, 250); } catch { }

                            // Ensure UI list contains enough entries and update the indexed step
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    if (idx <= 0) return;
                                    // grow list to include this index
                                    while (_steps.Count < idx)
                                    {
                                        var label = $"Step {_steps.Count + 1}";
                                        _steps.Add(new StepViewModel { Label = label, State = StepState.Pending, Percent = null, Message = string.Empty });
                                    }

                                    var vm = _steps[idx - 1];
                                    vm.Message = msg ?? vm.Message;
                                    try
                                    {
                                        var name = state.ToString();
                                        if (name.Equals("Running", StringComparison.OrdinalIgnoreCase)) vm.State = StepState.Running;
                                        else if (name.Equals("Success", StringComparison.OrdinalIgnoreCase)) vm.State = StepState.Success;
                                        else if (name.Equals("Error", StringComparison.OrdinalIgnoreCase)) vm.State = StepState.Error;
                                    }
                                    catch { }
                                    vm.Percent = pct;

                                    // Update header counter
                                    try
                                    {
                                        var completed = _steps.Count(s => s.State == StepState.Success);
                                        if (_taskCountText != null) _taskCountText.Text = $"{completed}/{_steps.Count}";
                                    }
                                    catch { }

                                    // Update overall progress bar if percent present
                                    try { if (pct.HasValue) UpdateGenerationProgress(pct.Value); } catch { }
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        catch { }
                    };

                    // Minimal CAD action handler: log request on UI thread. Replace with real SolidWorks calls when available.
                    _karaokeService.OnCadActionRequested += (stepIndex, swApp2, swModel2, ct2) =>
                    {
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try { AppendStatusLine($"[Karaoke] CAD action requested for step {stepIndex}"); } catch { }
                            }));
                        }
                        catch { }
                        return Task.CompletedTask;
                    };
                }
                catch { }
            try { Services.AddinStatusLogger.OnLog += (line) => { try { AppendStatusLine(line); } catch { } }; Services.AddinStatusLogger.Log("Init", "Taskpane subscribed to AddinStatusLogger"); } catch { }
            try { InitDbAndStores(); } catch (Exception ex) { AppendDetailedStatus("DB:init", "call exception", ex); }
            try { InitNameEasy(); } catch (Exception ex) { AppendDetailedStatus("NameEasy", "init failed", ex); }
        }

        private void ShapePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = shapePreset.SelectedIndex;
            switch (idx)
            {
                case 1: PromptText = "Create a rectangular box 100 mm length, 50 mm width, 25 mm height"; break;
                case 2: PromptText = "Create a cylinder 40 mm diameter and 80 mm height"; break;
                case 3: PromptText = "Create a cube 10 mm"; break;
                default: PromptText = string.Empty; break;
            }
        }

        private void Prompt_TextChanged(object sender, TextChangedEventArgs e)
        {
            try { SetModified(true); } catch { }
        }

        /// <summary>
        /// Public access to the prompt text so host code can get/set it.
        /// </summary>
        public string PromptText
        {
            get => prompt.Text;
            set => prompt.Text = value ?? string.Empty;
        }
        
        private string MakeProgressBar(int pct, int width = 20)
        {
            try
            {
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                int filled = (int)Math.Round(pct / 100.0 * width);
                if (filled < 0) filled = 0;
                if (filled > width) filled = width;
                int empty = width - filled;
                return new string('■', filled) + new string('□', empty);
            }
            catch
            {
                return new string('■', width);
            }
        }

        // Backwards-compatible helpers for older code that referenced GetPromptText/SetPromptText
        public string GetPromptText() => PromptText;
        public void SetPromptText(string text) => PromptText = text;

        /// <summary>
        /// Public wrapper so external code can trigger a build request programmatically.
        /// </summary>
        public Task RunBuildFromPromptAsync() => BuildFromPromptAsync();

        // Public helpers to ensure focus can be moved into WPF textboxes from the WinForms host
        public void FocusPrompt()
        {
            try
            {
                // Clear placeholder text when first focusing
                try
                {
                    if (string.Equals((prompt.Text ?? string.Empty).Trim(), "Enter prompt...", StringComparison.OrdinalIgnoreCase))
                    {
                        prompt.Text = string.Empty;
                    }
                }
                catch { }

                // Defer keyboard focus to after the mouse event is processed to work around host focus quirks
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        prompt.Focus();
                        System.Windows.Input.Keyboard.Focus(prompt);
                        // Also ensure native Win32 focus is set to the WPF host window so keystrokes are delivered
                        EnsureNativeFocus(prompt);
                        prompt.CaretIndex = prompt.Text?.Length ?? 0;
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            catch { }
        }

        public void FocusTypeDescription()
        {
            try
            {
                // Defer focus similarly
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        typeDescriptionTextBox.Focus();
                        System.Windows.Input.Keyboard.Focus(typeDescriptionTextBox);
                        typeDescriptionTextBox.CaretIndex = typeDescriptionTextBox.Text?.Length ?? 0;
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch { }
        }

        // Helper: climb the visual tree to find an ancestor of type T
        private static T FindAncestor<T>(System.Windows.DependencyObject current) where T : System.Windows.DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void AppendKeyLog(string line)
        {
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AICAD_Keys.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("o") + " " + line + System.Environment.NewLine);
                try { AICAD.Services.AddinStatusLogger.Log("Key", line); } catch { }
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void EnsureNativeFocus(System.Windows.FrameworkElement fe)
        {
            try
            {
                // Throttle rapid repeat calls to avoid focus-fighting loops
                var now = DateTime.UtcNow;
                lock (_ensureNativeFocusLock)
                {
                    if ((now - _lastEnsureNativeFocusUtc).TotalMilliseconds < 300)
                        return;
                    _lastEnsureNativeFocusUtc = now;
                }

                // Always schedule the actual Win32 focus call for later (ApplicationIdle)
                // so WPF and the host (SolidWorks) can finish their internal focus work.
                var dispatcher = fe?.Dispatcher ?? this.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var src = PresentationSource.FromVisual(fe) as HwndSource;
                            if (src != null)
                            {
                                var hwnd = src.Handle;
                                // SetForegroundWindow(hwnd); // Commented out: can cause SW to fight back for embedded task panes
                                SetFocus(hwnd);
                                AppendKeyLog($"EnsureNativeFocus: hwnd={hwnd}");
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
            catch { }
        }

        // Handlers referenced from XAML. Minimal implementations to satisfy compilation
        // and preserve the previously wired behaviors.
        private void prompt_GotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                FocusPrompt();
            }
            catch { }
        }

        private void prompt_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                // Keep placeholder behavior: if empty, restore hint text (non-destructive)
                if (string.IsNullOrWhiteSpace(prompt.Text))
                {
                    prompt.Text = "Enter prompt...";
                }
            }
            catch { }
        }

        private void prompt_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // Ctrl+Enter triggers a build, mirror intuitive behavior for the user
                if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                {
                    // Check if multiple builds are allowed
                    var allowMultiple = System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.User)
                                        ?? System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", System.EnvironmentVariableTarget.Machine);
                    if (string.IsNullOrEmpty(allowMultiple) || allowMultiple == "0" || allowMultiple.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        try { build.IsEnabled = false; } catch { }
                    }
                    try { BuildRequested?.Invoke(this, EventArgs.Empty); } catch { }
                    _ = BuildFromPromptAsync();
                    e.Handled = true;
                }
            }
            catch { }
        }
        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_stepStore == null)
                {
                    MessageBox.Show("No step store available", "History", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                using (var dlg = new HistoryBrowser(_stepStore)) dlg.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "History", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_statusWindow == null || _statusWindow.IsDisposed)
                {
                    _statusWindow = new StatusWindow();
                    _statusWindow.CopyErrorClicked += StatusWindow_CopyErrorClicked;
                    _statusWindow.CopyRunClicked += StatusWindow_CopyRunClicked;
                }
                _statusWindow.Show();
                _statusWindow.BringToFront();
            }
            catch (Exception ex) { AppendDetailedStatus("UI", "Status window error", ex); }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show unified Settings dialog (includes NameEasy tab)
                using (var dlg = new global::AICAD.UI.SettingsDialog())
                {
                    dlg.ShowDialog();
                }
            }
            catch (Exception ex) { AppendDetailedStatus("UI", "Settings dialog error", ex); }
        }

        private void InitDbAndStores()
        {
            try
            {
                var baseDir = @"D:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API";
                _fileLogger = new FileDbLogger(baseDir);

                var mongoUri = System.Environment.GetEnvironmentVariable("MONGODB_URI")
                               ?? System.Environment.GetEnvironmentVariable("MONGO_LOG_CONN")
                               ?? string.Empty;
                // If forced mode is enabled, override any env var and use the provided Mongo URI
                if (_forceUseOnlyGoodFeedback && string.IsNullOrWhiteSpace(mongoUri))
                {
                    mongoUri = _forcedGoodFeedbackMongoUri;
                }
                var mongoDb = System.Environment.GetEnvironmentVariable("MONGODB_DB") ?? "TaskPaneAddin";
                var mongoCol = System.Environment.GetEnvironmentVariable("MONGODB_COLLECTION") ?? "SW";
                if (!string.IsNullOrWhiteSpace(mongoUri))
                {
                    try { _mongoLogger = new MongoLogger(mongoUri, mongoDb, mongoCol); } catch (Exception ex) { AppendDetailedStatus("DB:init", "MongoLogger ctor exception", ex); }
                }

                if (!string.IsNullOrWhiteSpace(mongoUri))
                {
                    try { _goodStore = new MongoFeedbackStore(mongoUri, mongoDb, "good_feedback"); }
                    catch (Exception ex) { AppendDetailedStatus("DB:init", "MongoFeedbackStore ctor exception", ex); }
                }
                if (_goodStore == null)
                {
                    try { _goodStore = new SqliteFeedbackStore(baseDir); }
                    catch (Exception ex) { AppendStatusLine("[DB:init] SqliteFeedbackStore ctor exception: " + ex.Message); _goodStore = new FileGoodFeedbackStore(baseDir); }
                }

                // If forcing only good_feedback, do not initialize StepStore (we'll use only the good feedback store)
                if (!_forceUseOnlyGoodFeedback)
                {
                    if (!string.IsNullOrWhiteSpace(mongoUri))
                    {
                        try { _stepStore = new MongoStepStore(mongoUri, mongoDb); }
                        catch (Exception ex) { AppendDetailedStatus("DB:init", "MongoStepStore ctor exception", ex); }
                    }
                    if (_stepStore == null)
                    {
                        try { _stepStore = new SqliteStepStore(baseDir); }
                        catch (Exception ex) { AppendStatusLine("[DB:init] SqliteStepStore ctor exception: " + ex.Message); }
                    }
                }

                if (_mongoLogger != null && _mongoLogger.IsAvailable)
                {
                    SetDbStatus("MongoDB ready", Colors.DarkGreen);
                    try
                    {
                        var info = Services.MongoLoggerExtensions.GetDebugInfo(_mongoLogger);
                        foreach (var line in info)
                        {
                            AppendStatusLine("[DB] " + line);
                        }
                    }
                    catch (Exception ex) { AppendDetailedStatus("DB", "debug info error", ex); }
                }
                else if (!string.IsNullOrWhiteSpace(_mongoLogger?.LastError))
                {
                    SetDbStatus("Mongo error: " + _mongoLogger.LastError, Colors.Firebrick);
                    AppendStatusLine("[DB] Mongo error: " + _mongoLogger.LastError);
                }
                else
                {
                    SetDbStatus("File/SQLite ready", Colors.DarkGreen);
                    AppendStatusLine("[DB] Logging using File/SQLite at: " + baseDir);
                }
            }
            catch (Exception ex)
            {
                SetDbStatus("Init error", Colors.Firebrick);
                AppendDetailedStatus("DB:init", "exception", ex);
            }
        }

        private void SetRealTimeStatus(string text, Color color)
        {
            // `lblRealTimeStatus` removed from UI; mirror status to logs instead.
            AppendStatusLine(StatusConsole.StatusPrefix + " " + (text ?? string.Empty));
        }

        private void UpdateGenerationProgress(int percent)
        {
            // Use the animated progress updater for a more natural feel.
            try { UpdateGenerationProgressAnimated(Math.Max(0, Math.Min(100, percent))); } catch { }
        }

        private void UpdateKaraokeStatus(string text)
        {
            try
            {
                var t = text ?? string.Empty;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (KaraokeStatus != null) KaraokeStatus.Text = t;
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        private void AnimateKaraokeToColor(Color targetColor, int durationMs = 400)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (KaraokeStatus == null) return;
                        var brush = KaraokeStatus.Foreground as SolidColorBrush;
                        if (brush == null)
                        {
                            brush = new SolidColorBrush(targetColor);
                            KaraokeStatus.Foreground = brush;
                        }
                        // If brush is frozen clone it so we can animate
                        if (brush.IsFrozen)
                        {
                            brush = brush.Clone();
                            KaraokeStatus.Foreground = brush;
                        }
                        var animation = new ColorAnimation
                        {
                            To = targetColor,
                            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        };
                        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        private CancellationTokenSource _karaokeCts;
        private CancellationTokenSource _progressCts;
        private TextBlock _taskCountText;
        private readonly Random _rand = new Random();
        private readonly System.Windows.Media.Color[] _karaokeColors = new[] { Colors.Gray, Colors.DarkOrange, Colors.DodgerBlue, Colors.DarkGreen };

        // Karaoke status service instance (produces step progress events)
        private KaraokeStyleStatus _karaokeService;
        // Line-based karaoke controls
        private System.Collections.Generic.List<TextBlock> _karaokeLineBlocks;
        private int _karaokeLineIndex;
        private bool _karaokeStopOnError;

        // Animate progress toward a target percent smoothly.
        private void UpdateGenerationProgressAnimated(int target)
        {
            try
            {
                // Cancel any existing animation
                try { _progressCts?.Cancel(); } catch { }
                _progressCts = new CancellationTokenSource();
                var token = _progressCts.Token;
                // Run animation without blocking UI thread
                #pragma warning disable CS4014
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // read current value on UI thread
                        int current = 0;
                        Dispatcher.Invoke(() =>
                        {
                            try { current = (int)Math.Round(generationProgressBar.Value); } catch { current = 0; }
                        });

                        // Approach the target with small random steps
                        while (!token.IsCancellationRequested && current != target)
                        {
                            var remaining = target - current;
                            var step = Math.Max(1, Math.Min(10, Math.Abs(remaining) / 6));
                            // bias to slower progress when large remaining
                            if (Math.Abs(remaining) > 30) step = Math.Max(2, step);
                            current += Math.Sign(remaining) * step;
                            // clamp
                            current = Math.Max(0, Math.Min(100, current));
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    generationProgressBar.Value = current;
                                    if (generationProgressText != null) generationProgressText.Text = current + "%";
                                }
                                catch { }
                            }));
                            // random small delay to look organic
                            await Task.Delay(60 + _rand.Next(0, 120), token).ConfigureAwait(false);
                        }

                        // ensure exact final value
                        if (!token.IsCancellationRequested)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    generationProgressBar.Value = target;
                                    if (generationProgressText != null) generationProgressText.Text = target + "%";
                                }
                                catch { }
                            }));
                        }
                    }
                    catch { }
                }, token);
                #pragma warning restore CS4014
            }
            catch { }
        }

        // Start a realistic progress phase: communicating, awaiting_response, executing, success, error
        private void StartProgressPhase(string phase)
        {
            switch ((phase ?? string.Empty).ToLowerInvariant())
            {
                case "communicating":
                    UpdateGenerationProgressAnimated(10 + _rand.Next(0, 6));
                    break;
                case "awaiting_response":
                    // move to mid-range while waiting
                    UpdateGenerationProgressAnimated(40 + _rand.Next(0, 11));
                    break;
                case "executing":
                    UpdateGenerationProgressAnimated(65 + _rand.Next(0, 11));
                    break;
                case "success":
                    UpdateGenerationProgressAnimated(100);
                    break;
                case "error":
                    UpdateGenerationProgressAnimated(0);
                    break;
                default:
                    // fallback to a direct value parse
                    int v;
                    if (int.TryParse(phase, out v)) UpdateGenerationProgressAnimated(v);
                    break;
            }
        }

        // Produce simple English karaoke lines for a given scenario.
        private string[] GenerateKaraokeLines(string scenario, string detail = null)
        {
            detail = detail ?? string.Empty;
            switch ((scenario ?? string.Empty).ToLowerInvariant())
            {
                case "communicating":
                    return new[] { "Connecting to AI service...", "Sending your request now.", "Please wait while we think." };
                case "awaiting_response":
                    return new[] { "Waiting for the AI to respond...", "Processing response...", "Almost there." };
                case "executing":
                    return new[] { "Running steps in SolidWorks...", "Applying changes to the model.", "This may take a moment." };
                case "success":
                    return new[] { "Model created successfully.", "You can view or edit the model now." };
                case "error_sw":
                    return new[] { $"SolidWorks error: {detail}", "Check the error details and try again." };
                case "error_llm":
                    return new[] { $"AI error: {detail}", "Check your API key, model, and quota then retry." };
                case "saving":
                    return new[] { "Saving the run and feedback...", "Done." };
                default:
                    return new[] { detail, string.Empty };
            }
        }

        // Convenience: show scenario-based karaoke (stops previous karaoke first)
        private void ShowKaraokeScenario(string scenario, string detail = null, int intervalMs = 700)
        {
            try
            {
                // Switch to line-based karaoke: stop any existing animation, init preset lines,
                // and start top-to-bottom activation. For error scenarios, we'll let callers
                // signal an error which will stop progression at the current line.
                StopKaraoke();
                InitKaraokeLines();
                StartKaraokeLines(intervalMs);
            }
            catch { }
        }

        public void StartKaraoke(IEnumerable<string> lines, int intervalMs = 700)
        {
            try
            {
                StopKaraoke();
                _karaokeCts = new CancellationTokenSource();
                var token = _karaokeCts.Token;
                var msgs = (lines ?? Array.Empty<string>()).ToArray();
                if (msgs.Length == 0) return;
                #pragma warning disable CS4014
                _ = Task.Run(async () =>
                {
                    try
                    {
                                while (!token.IsCancellationRequested)
                                {
                                for (int i = 0; i < msgs.Length; i++)
                                {
                                    if (token.IsCancellationRequested) break;
                                    var m = msgs[i];
                                    // animate to the next color for this message
                                    try { AnimateKaraokeToColor(_karaokeColors[i % _karaokeColors.Length], 350); } catch { }
                                    UpdateKaraokeStatus(m);
                                    try { await Task.Delay(intervalMs, token); } catch { break; }
                                }
                                }
                    }
                    catch { }
                }, token);
                #pragma warning restore CS4014
            }
            catch { }
        }

        public void StopKaraoke()
        {
            try
            {
                if (_karaokeCts != null && !_karaokeCts.IsCancellationRequested)
                {
                    try { _karaokeCts.Cancel(); } catch { }
                    try { _karaokeCts.Dispose(); } catch { }
                }
                _karaokeCts = null;
            }
            catch { }
        }

        // Initialize line-based karaoke: gather preset TextBlocks and reset colors
        private void InitKaraokeLines()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    _karaokeLineBlocks = new System.Collections.Generic.List<TextBlock>();
                    if (KaraokeLinesPanel != null)
                    {
                        foreach (var child in KaraokeLinesPanel.Children)
                        {
                            if (child is TextBlock tb && tb != KaraokeStatus)
                            {
                                try { tb.Foreground = new SolidColorBrush(Colors.Gray); } catch { }
                                _karaokeLineBlocks.Add(tb);
                            }
                        }
                    }
                    _karaokeLineIndex = 0;
                    _karaokeStopOnError = false;
                });
            }
            catch { }
        }

        // Activate preset lines top-to-bottom; each activated line turns blue. Stops if signaled.
        public void StartKaraokeLines(int intervalMs = 700)
        {
            try
            {
                // cancel any previous
                try { _karaokeCts?.Cancel(); } catch { }
                _karaokeCts = new CancellationTokenSource();
                var token = _karaokeCts.Token;
                if (_karaokeLineBlocks == null || _karaokeLineBlocks.Count == 0)
                {
                    // nothing to animate
                    return;
                }

                #pragma warning disable CS4014
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested && _karaokeLineIndex < _karaokeLineBlocks.Count)
                        {
                            if (_karaokeStopOnError) break;
                            var idx = _karaokeLineIndex;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    _karaokeLineBlocks[idx].Foreground = new SolidColorBrush(Colors.DodgerBlue);
                                }
                                catch { }
                            }));
                            _karaokeLineIndex++;
                            try { await Task.Delay(intervalMs, token).ConfigureAwait(false); } catch { break; }
                        }
                    }
                    catch { }
                }, token);
                #pragma warning restore CS4014
            }
            catch { }
        }

        // Signal an error at the current line: mark it red and stop progression
        public void SignalKaraokeError()
        {
            try
            {
                _karaokeStopOnError = true;
                if (_karaokeLineBlocks != null && _karaokeLineIndex < _karaokeLineBlocks.Count)
                {
                    var idx = _karaokeLineIndex;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { _karaokeLineBlocks[idx].Foreground = new SolidColorBrush(Colors.Firebrick); } catch { }
                    }));
                }
                try { _karaokeCts?.Cancel(); } catch { }
            }
            catch { }
        }

        private void SetLlmStatus(string text, Color color)
        {
            AppendStatusLine($"[LLM] {text}");
        }

        private void SetDbStatus(string text, Color color)
        {
            _lastDbStatus = text;
            AppendStatusLine($"[DB] {text}");
        }

        private void SetSwStatus(string text, Color color)
        {
            AppendStatusLine($"[SW] {text}");
        }

        private void SetTimes(TimeSpan? llm, TimeSpan? total)
        {
            if (llm.HasValue || total.HasValue)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (llm.HasValue) parts.Add($"LLM {llm.Value.TotalMilliseconds:F0} ms");
                if (total.HasValue) parts.Add($"Total {total.Value.TotalMilliseconds:F0} ms");
                AppendStatusLine($"[Timing] {string.Join(", ", parts)}");
            }
        }

        private string GetEndpointDisplayName(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return "Local HTTP LLM";
            try
            {
                var lmStudioEnv = System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.User)
                                   ?? System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                   ?? System.Environment.GetEnvironmentVariable("LMSTUDIO_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                   ?? "http://127.0.0.1:1234";
                if (!string.IsNullOrWhiteSpace(lmStudioEnv) && endpoint.IndexOf(lmStudioEnv, StringComparison.OrdinalIgnoreCase) >= 0) return "LM Studio";
                // common shorthand: if endpoint contains localhost:1234 or 127.0.0.1:1234 treat as LM Studio
                if (endpoint.IndexOf("127.0.0.1:1234", StringComparison.OrdinalIgnoreCase) >= 0 || endpoint.IndexOf("localhost:1234", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "LM Studio";
            }
            catch { }
            return "Local HTTP LLM";
        }

        private void SetLastError(string err)
        {
            _lastError = err;
            if (_statusWindow != null && !_statusWindow.IsDisposed)
            {
                _statusWindow.ErrorTextBox.Text = string.IsNullOrWhiteSpace(err) ? "—" : err;
                // _statusWindow.ErrorTextBox.ForeColor handled in StatusWindow
            }
            if (!string.IsNullOrWhiteSpace(err)) AppendStatusLine(StatusConsole.ErrorPrefix + " " + err);
        }

        private string BuildErrorCopyText()
        {
            if (string.IsNullOrWhiteSpace(_lastError)) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Error: {_lastError}");
            if (_lastLlm != TimeSpan.Zero || _lastTotal != TimeSpan.Zero)
            {
                sb.AppendLine($"LLM ms: {_lastLlm.TotalMilliseconds:F0}");
                sb.AppendLine($"Total ms: {_lastTotal.TotalMilliseconds:F0}");
            }
            if (!string.IsNullOrWhiteSpace(_lastPrompt)) sb.AppendLine($"Prompt: {_lastPrompt}");
            if (!string.IsNullOrWhiteSpace(_lastReply)) sb.AppendLine($"Reply: {_lastReply}");
            return sb.ToString();
        }

        private string BuildRunCopyText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(_lastModel)) sb.AppendLine($"Model: {_lastModel}");
            if (!string.IsNullOrWhiteSpace(_lastPrompt)) sb.AppendLine($"Prompt: {_lastPrompt}");
            if (!string.IsNullOrWhiteSpace(_lastReply)) sb.AppendLine($"Reply: {_lastReply}");
            if (_lastLlm != TimeSpan.Zero || _lastTotal != TimeSpan.Zero)
            {
                sb.AppendLine($"LLM ms: {_lastLlm.TotalMilliseconds:F0}");
                sb.AppendLine($"Total ms: {_lastTotal.TotalMilliseconds:F0}");
            }
            if (!string.IsNullOrWhiteSpace(_lastDbStatus)) sb.AppendLine($"DB: {_lastDbStatus}");
            if (_lastDbLogged.HasValue) sb.AppendLine($"DbLogged: {_lastDbLogged.Value}");
            if (!string.IsNullOrWhiteSpace(_lastError)) sb.AppendLine($"Error: {_lastError}");
            return sb.ToString();
        }

        private AICAD.Services.ILlmClient GetClient()
        {
            if (_client == null)
            {
                // If a local LLM endpoint is configured, prefer it.
                var llmMode = System.Environment.GetEnvironmentVariable("AICAD_LLM_MODE", System.EnvironmentVariableTarget.User)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_MODE", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("AICAD_LLM_MODE", System.EnvironmentVariableTarget.Machine)
                              ?? string.Empty;

                var localEndpoint = System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.User)
                                    ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                    ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Machine);

                var preferredModel = System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.User)
                                     ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.Process)
                                     ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.Machine)
                                     ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.User)
                                     ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Process)
                                     ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Machine)
                                     ?? "google/functiongemma-270m";

                if (!string.IsNullOrWhiteSpace(llmMode) && llmMode.Equals("local", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(localEndpoint))
                {
                    var systemPrompt = System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                       ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process)
                                       ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Machine)
                                       ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text.";
                    _client = new LocalHttpLlmClient(localEndpoint, preferredModel, systemPrompt);
                    var dispName = GetEndpointDisplayName(localEndpoint);
                    AppendStatusLine("[LLM] " + dispName + " client constructed; endpoint=" + localEndpoint + " model=" + preferredModel);
                }
                else
                {
                    // Prefer API keys from environment; do not hardcode keys in source.
                    var key = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.User)
                              ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.Machine)
                              ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.User)
                              ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.Machine)
                              ?? null;

                    _client = new GeminiClient(key, preferredModel);
                    AppendStatusLine("[LLM] Gemini client constructed; apiKeySource=" + (string.IsNullOrEmpty(key) ? "none" : "env"));
                }
            }
            return _client;
        }

        private async Task<string> GenerateWithFallbackAsync(string prompt)
        {
            Exception lastEx = null;

            // Preferred model same logic as GetClient
            var preferredModel = System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.User)
                                 ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.Process)
                                 ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", System.EnvironmentVariableTarget.Machine)
                                 ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.User)
                                 ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Process)
                                 ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Machine)
                                 ?? "google/functiongemma-270m";

            // 1) Try Ollama at localhost:11434 first (user preference)
            try
            {
                var ollamaEndpoint = System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.User)
                                     ?? System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                     ?? System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                     ?? "http://localhost:11434/v1/chat/completions";

                if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
                {
                    AppendStatusLine("[LLM] Trying Ollama endpoint: " + ollamaEndpoint);
                    var systemPrompt2 = System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Machine)
                                        ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text.";
                    var ollamaClient = new AICAD.Services.LocalHttpLlmClient(ollamaEndpoint, preferredModel, systemPrompt2);
                    try
                    {
                        var r = await ollamaClient.GenerateAsync(prompt);
                        _client = ollamaClient;
                        _lastModel = ollamaClient.Model;
                        AppendStatusLine("[LLM] Ollama succeeded: " + ollamaClient.Model);
                        return r;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        AppendStatusLine("[LLM] Ollama failed: " + ex.Message);
                        try { ollamaClient.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                AppendStatusLine("[LLM] Ollama attempt error: " + ex.Message);
            }

            // 2) Try Local HTTP LLM if explicitly configured via LOCAL_LLM_ENDPOINT
            try
            {
                var localEndpoint = System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.User)
                                    ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Process)
                                    ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", System.EnvironmentVariableTarget.Machine)
                                    ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(localEndpoint))
                {
                    var dispTry = GetEndpointDisplayName(localEndpoint);
                    AppendStatusLine("[LLM] Trying " + dispTry + ": " + localEndpoint);
                    var systemPrompt2 = System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.User)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Process)
                                        ?? System.Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", System.EnvironmentVariableTarget.Machine)
                                        ?? "You are a CAD planning agent. Output only raw JSON with a top-level 'steps' array for SolidWorks. No extra text.";
                    var localClient = new AICAD.Services.LocalHttpLlmClient(localEndpoint, preferredModel, systemPrompt2);
                    try
                    {
                        var r = await localClient.GenerateAsync(prompt);
                        _client = localClient;
                        _lastModel = localClient.Model;
                        AppendStatusLine("[LLM] " + dispTry + " succeeded: " + localClient.Model);
                        return r;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        AppendStatusLine("[LLM] " + dispTry + " failed: " + ex.Message);
                        try { localClient.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                AppendStatusLine("[LLM] Local attempt error: " + ex.Message);
            }

            // If we've been instructed to force local-only, do not attempt cloud providers.
            if (FORCE_LOCAL_ONLY)
            {
                if (lastEx != null)
                {
                    AppendStatusLine("[LLM] Force-local mode enabled; not attempting cloud providers.");
                    throw new InvalidOperationException("Local LLM failed and cloud fallback is disabled.", lastEx);
                }
                else
                {
                    AppendStatusLine("[LLM] Force-local mode enabled but no LOCAL_LLM_ENDPOINT configured.");
                    throw new InvalidOperationException("No local LLM endpoint configured and cloud fallback is disabled.");
                }
            }

            // 2) Try Gemini (Google) if key available
            if (!FORCE_LOCAL_ONLY)
            {
            try
            {
                var gemKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.User)
                             ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.Process)
                             ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY", System.EnvironmentVariableTarget.Machine)
                             ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.User)
                             ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.Process)
                             ?? System.Environment.GetEnvironmentVariable("OPENAI_API_KEY", System.EnvironmentVariableTarget.Machine)
                             ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(gemKey))
                {
                    AppendStatusLine("[LLM] Trying Gemini (API key from env)");
                    var gemClient = new AICAD.Services.GeminiClient(gemKey, preferredModel);
                    try
                    {
                        var r = await gemClient.GenerateAsync(prompt);
                        _client = gemClient;
                        _lastModel = gemClient.Model;
                        AppendStatusLine("[LLM] Gemini succeeded: " + gemClient.Model);
                        return r;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        AppendStatusLine("[LLM] Gemini failed: " + ex.Message);
                        try { gemClient.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                AppendStatusLine("[LLM] Gemini attempt error: " + ex.Message);
            }
            }

                // 3) Try Groq if key available
            try
            {
                var groqKey = System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.User)
                              ?? System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.Process)
                              ?? System.Environment.GetEnvironmentVariable("GROQ_API_KEY", System.EnvironmentVariableTarget.Machine)
                              ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(groqKey))
                {
                        AppendStatusLine("[LLM] Trying Groq (API key from env)");
                        try
                        {
                            var groqAdapter = new AICAD.Services.GroqLlmAdapter(groqKey);
                            var r = await groqAdapter.GenerateAsync(prompt);
                            _client = groqAdapter;
                            _lastModel = groqAdapter.Model;
                            AppendStatusLine("[LLM] Groq succeeded: " + groqAdapter.Model);
                            return r;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            AppendStatusLine("[LLM] Groq failed: " + ex.Message);
                        }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                AppendStatusLine("[LLM] Groq attempt error: " + ex.Message);
            }

            // All attempts failed
            if (lastEx != null)
            {
                AppendStatusLine("[LLM] All providers failed: " + lastEx.Message);
                throw new InvalidOperationException("All LLM providers failed. See status log for details.", lastEx);
            }

            throw new InvalidOperationException("No LLM provider configured (LOCAL_LLM_ENDPOINT, GEMINI_API_KEY or GROQ_API_KEY).");
        }

        private async Task BuildFromPromptAsync()
        {
            var text = (PromptText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                AppendStatusLine("Enter a prompt describing a simple box or cylinder in mm.");
                return;
            }

            // Prevent re-entry if a build is already running
            if (_isBuilding)
            {
                AppendStatusLine(StatusConsole.StatusPrefix + " Build already in progress — re-entry prevented");
                return;
            }
            _isBuilding = true;

            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            string reply = null;
            string errText = null;
            TimeSpan llmMs = TimeSpan.Zero;
            StepExecutionResult exec = null;
            try
            {
                // Keep Build button enabled so the user can request Stop
                try { build.IsEnabled = true; } catch { }
                _lastPrompt = text;
                AppendStatusLine("> " + text);
                // Kick off progress bar animation (realistic phase)
                StartProgressPhase("communicating");
                // Start a run section so status console groups this build attempt
                var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                _lastRunId = runId;
                AppendStatusLine($"[Run:{runId}] ----- Build Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -----");
                // Run quick LLM provider health-check and report status before showing communicating state
                bool providersOk1 = false;
                try { providersOk1 = await CheckLlmProvidersAsync().ConfigureAwait(false); } catch { providersOk1 = false; }
                if (!providersOk1)
                {
                    AppendStatusLine("[ERROR] No reachable LLM providers detected. Aborting build.");
                    SetRealTimeStatus("No LLM providers available", Colors.DarkRed);
                    try { _llmProgressTimer?.Stop(); _llmProgressTimer?.Dispose(); _llmProgressTimer = null; } catch { }
                    _isBuilding = false;
                    try { AppendStatusLine($"[Run:{runId}] ----- Build End: success=False totalMs={totalSw.ElapsedMilliseconds} error=No LLM providers available -----"); } catch { }
                    return;
                }
                SetRealTimeStatus("Communicating with LLM…", Colors.DarkOrange);
                    // Do NOT auto-apply material/description/mass here — user should apply manually
                    AppendStatusLine(StatusConsole.StatusPrefix + " Model created. Please set Material and Description manually and click 'Apply Properties' to finalize Mass (will remain 0.000 until linked).");
                        ShowKaraokeScenario("communicating");
                    SetLlmStatus("Sending…", Colors.DarkOrange);
                SetLastError(null);
                SetTimes(null, null);

                // Determine whether to apply few-shot examples (user-configurable via env var AICAD_USE_FEWSHOT)
                bool useFewShot = true;
                try
                {
                    var v = System.Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", System.EnvironmentVariableTarget.User)
                            ?? System.Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", System.EnvironmentVariableTarget.Process)
                            ?? System.Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", System.EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrEmpty(v))
                    {
                        if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase)) useFewShot = false;
                    }
                }
                catch { useFewShot = true; }

                // If forcing local-only mode, do not feed few-shot examples to the local LLM.
                if (FORCE_LOCAL_ONLY)
                {
                    useFewShot = false;
                }

                // Optional override to force using only the hardcoded static few-shot examples
                bool forceStaticFewShot = false;
                try
                {
                    var vs = System.Environment.GetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", System.EnvironmentVariableTarget.User)
                             ?? System.Environment.GetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", System.EnvironmentVariableTarget.Process)
                             ?? System.Environment.GetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", System.EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrEmpty(vs) && (vs == "1" || vs.Equals("true", StringComparison.OrdinalIgnoreCase))) forceStaticFewShot = true;
                }
                catch { }

                StringBuilder fewshot = null;
                if (useFewShot)
                {
                    fewshot = new StringBuilder()
                        .Append("Examples:")
                        .Append("\nInput: Box 100x50x25 mm")
                        .Append("\nOutput:{\n  \"steps\":[\n    {\"op\":\"new_part\"},\n    {\"op\":\"select_plane\",\"name\":\"Front Plane\"},\n    {\"op\":\"sketch_begin\"},\n    {\"op\":\"rectangle_center\",\"cx\":0,\"cy\":0,\"w\":100,\"h\":50},\n    {\"op\":\"sketch_end\"},\n    {\"op\":\"extrude\",\"depth\":25,\"type\":\"boss\"}\n  ]\n}")
                        .Append("\nInput: Cylinder 40 dia x 80 mm")
                        .Append("\nOutput:{\n  \"steps\":[\n    {\"op\":\"new_part\"},\n    {\"op\":\"select_plane\",\"name\":\"Front Plane\"},\n    {\"op\":\"sketch_begin\"},\n    {\"op\":\"circle_center\",\"cx\":0,\"cy\":0,\"diameter\":40},\n    {\"op\":\"sketch_end\"},\n    {\"op\":\"extrude\",\"depth\":80}\n  ]\n}");

                    // Signal we're applying few-shot examples so the status console can group them underneath
                    SetRealTimeStatus("Applying few-shot examples…", Colors.DarkOrange);

                    if (forceStaticFewShot)
                    {
                        AddinStatusLogger.Log("FewShot", "Forced static few-shot mode enabled; skipping DB examples");
                    }
                    else
                    {
                        if (_goodStore != null)
                        {
                            var extras = _goodStore.GetRecentFewShots(2);
                            try
                            {
                                AddinStatusLogger.Log("FewShot", $"GoodStore returned {extras.Count} examples");
                                int i = 0;
                                foreach (var s in extras)
                                {
                                    i++;
                                    AddinStatusLogger.Log("FewShot", $"GoodStore example {i}:");
                                    AddinStatusLogger.Log("FewShot", s);
                                    fewshot.Append(s);
                                }
                            }
                            catch (Exception ex) { AddinStatusLogger.Error("FewShot", "GoodStore logging failed", ex); }
                        }
                        // If configured to use only the good_feedback store, skip step-store few-shots
                        if (!_forceUseOnlyGoodFeedback && _stepStore != null)
                        {
                            var more = _stepStore.GetRelevantFewShots(text, 3);
                            try
                            {
                                AddinStatusLogger.Log("FewShot", $"StepStore returned {more.Count} examples");
                                int j = 0;
                                foreach (var s in more)
                                {
                                    j++;
                                    AddinStatusLogger.Log("FewShot", $"StepStore example {j}:");
                                    AddinStatusLogger.Log("FewShot", s);
                                    fewshot.Append(s);
                                }
                            }
                            catch (Exception ex) { AddinStatusLogger.Error("FewShot", "StepStore logging failed", ex); }
                        }
                    }
                }

                var sysPrompt =
                    "You are a CAD planning agent. Convert the user request into a step plan JSON for SOLIDWORKS. " +
                    "Supported ops: new_part; select_plane{name}; sketch_begin; rectangle_center{cx,cy,w,h}; circle_center{cx,cy,r|diameter}; sketch_end; extrude{depth,type?}. " +
                    "Units are millimeters; output ONLY raw JSON with a top-level 'steps' array. No markdown or extra text.\n" + (useFewShot ? fewshot.ToString() : string.Empty) + "\nNow generate plan for: ";
                try { AddinStatusLogger.Log("FewShot", $"Final few-shot prompt length={(fewshot==null?0:fewshot.Length)}"); } catch { }
                // Notify user when few-shot examples are not being included
                try
                {
                    if (!useFewShot)
                    {
                        AppendStatusLine("[FewShot] Final few-shot prompt length=0 — feature disabled in settings");
                    }
                    else if (fewshot == null || fewshot.Length == 0)
                    {
                        AppendStatusLine("[FewShot] Final few-shot prompt length=0 — no few-shot examples available");
                    }
                }
                catch { }
                var llmSw = System.Diagnostics.Stopwatch.StartNew();
                // If forcing local-only, do not include few-shot examples in the prompt.
                var finalPrompt = sysPrompt + (useFewShot ? fewshot.ToString() : string.Empty) + "\nNow generate plan for: " + text + "\nJSON:";
                if (FORCE_LOCAL_ONLY)
                {
                    finalPrompt = sysPrompt + "\nNow generate plan for: " + text + "\nJSON:";
                }
                // Start LLM progress estimation timer (500ms interval) so status console shows an animated single-line progress
                try
                {
                    try { _llmProgressTimer?.Stop(); _llmProgressTimer?.Dispose(); } catch { }
                    _llmProgressStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    _llmProgressTimer = new System.Windows.Forms.Timer();
                    _llmProgressTimer.Interval = 500;
                    _llmProgressTimer.Tick += (s, ev) =>
                    {
                        try
                        {
                            var elapsed = _llmProgressStopwatch?.Elapsed.TotalSeconds ?? 0.0;
                            var pct = (int)Math.Floor((elapsed / _llmAverageSeconds) * 100.0);
                            if (pct < 0) pct = 0;
                            if (pct > 99) pct = 99; // cap at 99% until LLM responds
                            var bar = MakeProgressBar(pct, 20);
                            AppendStatusLine(StatusConsole.FormatLlmProgress(bar, pct));
                        }
                        catch { }
                    };
                    try { _llmProgressTimer.Start(); } catch { }
                }
                catch { }
                // mark formal run-level step states and initialize fixed UI steps
                try
                {
                    InitializeStepProgress(new[] {
                        "Got your request",
                        "Preparing inputs",
                        "Connecting to AI",
                        "Sending request to AI",
                        "Waiting for AI response",
                        "AI responded",
                        "Reading AI response",
                        "Checking parameters",
                        "Building sketch",
                        "Adding features",
                        "Applying constraints",
                        "Running checks",
                        "Saving model",
                        "Updating UI",
                        "Complete"
                    });
                    // Initialize step entries without hardcoded percent values.
                    // Percents will be updated from runtime events (LLM timer, op callbacks, executor updates).
                    SetStepProgress("Got your request", null, StepState.Pending);
                    SetStepProgress("Preparing inputs", null, StepState.Pending);
                    SetStepProgress("Connecting to AI", null, StepState.Pending);

                // LM Studio fallback is handled in GenerateWithFallbackAsync; no local LM call here.
                    SetStepProgress("Sending request to AI", null, StepState.Pending);
                    SetStepProgress("Waiting for AI response", null, StepState.Pending);
                    SetStepProgress("AI responded", null, StepState.Pending);
                    SetStepProgress("Reading AI response", null, StepState.Pending);
                    SetStepProgress("Checking parameters", null, StepState.Pending);
                    SetStepProgress("Building sketch", null, StepState.Pending);
                    SetStepProgress("Adding features", null, StepState.Pending);
                    SetStepProgress("Applying constraints", null, StepState.Pending);
                    SetStepProgress("Running checks", null, StepState.Pending);
                    SetStepProgress("Saving model", null, StepState.Pending);
                    SetStepProgress("Updating UI", null, StepState.Pending);
                    SetStepProgress("Complete", null, StepState.Pending);
                }
                catch { }

                // Before sending the prompt, run a quick provider status check and abort if none available.
                bool providersOk2 = false;
                try { providersOk2 = await CheckLlmProvidersAsync().ConfigureAwait(false); } catch { providersOk2 = false; }
                if (!providersOk2)
                {
                    AppendStatusLine("[ERROR] No reachable LLM providers detected. Aborting build.");
                    SetRealTimeStatus("No LLM providers available", Colors.DarkRed);
                    try { _llmProgressTimer?.Stop(); _llmProgressTimer?.Dispose(); _llmProgressTimer = null; } catch { }
                    _isBuilding = false;
                    try { AppendStatusLine($"[Run:{_lastRunId}] ----- Build End: success=False totalMs={totalSw.ElapsedMilliseconds} error=No LLM providers available -----"); } catch { }
                    return;
                }

                reply = await GenerateWithFallbackAsync(finalPrompt);
                // LLM responded; finalize progress line to Done (100%) and update EMA
                try
                {
                    try { _llmProgressTimer?.Stop(); _llmProgressTimer?.Dispose(); _llmProgressTimer = null; } catch { }
                    try
                    {
                        if (_llmProgressStopwatch != null)
                        {
                            _llmProgressStopwatch.Stop();
                            var observed = _llmProgressStopwatch.Elapsed.TotalSeconds;
                            // Update EMA: new = alpha*observed + (1-alpha)*old
                            try
                            {
                                var old = _llmAverageSeconds;
                                var nw = _llmEmaAlpha * observed + (1.0 - _llmEmaAlpha) * old;
                                if (nw > 0 && !double.IsNaN(nw) && !double.IsInfinity(nw))
                                {
                                    _llmAverageSeconds = nw;
                                    try { Services.SettingsManager.SetDouble("LLM_AvgSeconds", _llmAverageSeconds); } catch { }
                                    AddinStatusLogger.Log("TaskpaneWpf", $"Updated LLM average: observed={observed:F1}s newAvg={_llmAverageSeconds:F2}s");
                                }
                            }
                            catch { }
                            int pct = 100;
                            var bar = MakeProgressBar(pct, 20);
                            AppendStatusLine(StatusConsole.FormatLlmProgressDone(bar, pct));
                        }
                    }
                    catch { }
                    _llmProgressStopwatch = null;
                }
                catch { }
                // Update step UI to reflect LLM response
                try
                {
                    SetStepProgress("Sending request to AI", 100, StepState.Success);
                    SetStepProgress("Waiting for AI response", 100, StepState.Success);
                    SetStepProgress("AI responded", 100, StepState.Success);
                    SetStepProgress("Reading AI response", 30, StepState.Running);
                    SetStepProgress("Checking parameters", null, StepState.Pending);
                    SetStepProgress("Building sketch", null, StepState.Pending);
                    SetStepProgress("Adding features", null, StepState.Pending);
                    SetStepProgress("Applying constraints", null, StepState.Pending);
                    SetStepProgress("Running checks", null, StepState.Pending);
                    SetStepProgress("Saving model", null, StepState.Pending);
                    SetStepProgress("Updating UI", null, StepState.Pending);
                    SetStepProgress("Complete", null, StepState.Pending);

                }
                catch { }
                // Respect user Stop requests (cancellation) after LLM returns
                if (_buildCts?.Token.IsCancellationRequested == true) throw new OperationCanceledException();
                llmSw.Stop();
                var client = _client ?? GetClient();
                // LLM returned — advance progress realistically
                StartProgressPhase("awaiting_response");
                llmMs = llmSw.Elapsed;
                _lastReply = reply;
                AppendStatusLine(reply);
                SetRealTimeStatus("Received response from LLM", Colors.DarkGreen);
                StopKaraoke();
                ShowKaraokeScenario("awaiting_response");
                SetLlmStatus("OK", Colors.DarkGreen);

                SetRealTimeStatus("Executing plan…", Colors.DarkOrange);
                StartProgressPhase("executing");
                ShowKaraokeScenario("executing");
                SetSwStatus("Working…", Colors.DarkOrange);
                var attempt = 0;
                var maxAttempts = 2;
                string planJson = ExtractRawJson(reply);
                Newtonsoft.Json.Linq.JObject planDoc = null;
                for (; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        planDoc = Newtonsoft.Json.Linq.JObject.Parse(planJson);
                    }
                    catch (Exception ex)
                    {
                        errText = "plan-parse: " + ex.Message;
                        break;
                    }

                    // Honor Stop requests before executing plan
                    if (_buildCts?.Token.IsCancellationRequested == true) throw new OperationCanceledException();

                    // Keep the fixed pipeline steps only. Do NOT replace the taskpane step list
                    // with the detailed plan steps returned by the LLM. The application relies
                    // on the high-level pipeline initialized earlier (e.g. "Got your request",
                    // "Preparing inputs", ..., "Complete"). Per-user request we leave that list
                    // intact so the taskpane never shows low-level ops such as "new_part" or
                    // "select_plane". Individual plan ops will still update the high-level
                    // progress via UpdateHigherLevelFromOp(op, pct) below.

                    // mark read/checked progress
                    try { SetStepProgress("Reading AI response", 100, StepState.Success); SetStepProgress("Checking parameters", 50, StepState.Running); } catch { }

                        // Preset sequence removed; UI will update from real execution progress

                        // CRITICAL: Execute on UI thread - SolidWorks COM calls MUST be on UI thread
                        exec = Dispatcher.Invoke(() => Services.StepExecutor.Execute(planDoc, _swApp, (pct, op, idx) =>
                    {
                        try
                        {
                            // update overall progress bar
                            try { generationProgressBar.Value = Math.Max(0, Math.Min(100, pct)); } catch { }
                            try { generationProgressText.Text = pct.ToString() + "%"; } catch { }

                            // Update step entry if available
                            if (idx.HasValue && idx.Value >= 0 && idx.Value < _steps.Count)
                            {
                                var vm = _steps[idx.Value];
                                vm.Percent = pct;
                                vm.State = pct >= 100 ? StepState.Success : StepState.Running;
                                vm.Timestamp = DateTime.Now;
                            }
                            else
                            {
                                // If no index, mark nearest running step by matching op
                                for (int i = 0; i < _steps.Count; i++)
                                {
                                    if (string.Equals(_steps[i].Label, op, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _steps[i].Percent = pct;
                                        _steps[i].State = pct >= 100 ? StepState.Success : StepState.Running;
                                        _steps[i].Timestamp = DateTime.Now;
                                        break;
                                    }
                                }
                            }

                            // Also update higher-level fixed steps based on op name
                            try { UpdateHigherLevelFromOp(op ?? string.Empty, pct); } catch { }

                            // Update header counter based on dynamic steps
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        var completed = _steps.Count(s => s.State == StepState.Success);
                                        if (_taskCountText != null) _taskCountText.Text = $"{completed}/{_steps.Count}";
                                    }
                                    catch { }
                                }));
                            }
                            catch { }
                        }
                        catch { }
                    }));
                    if (exec.Success) break;

                    var errDoc = new JObject
                    {
                        ["last_plan"] = SafeJson(planJson),
                        ["errors"] = new JArray(exec.Log)
                    };
                    var corrective =
                        "Your previous plan failed in SOLIDWORKS. Fix the plan based on this error log and output only corrected JSON.\n" +
                        errDoc.ToString() +
                        "\nRemember: output only JSON with steps; use Front Plane and mm units.";
                    try
                    {
                        if (exec.CreatedNewPart && !exec.Success && _swApp != null && !string.IsNullOrWhiteSpace(exec.ModelTitle))
                        {
                            // Close document on UI thread
                            Dispatcher.Invoke(() => _swApp.CloseDoc(exec.ModelTitle));
                        }
                    }
                    catch { }

                    var llmFixSw = System.Diagnostics.Stopwatch.StartNew();
                    if (_buildCts?.Token.IsCancellationRequested == true) throw new OperationCanceledException();
                    var fixedPlan = await client.GenerateAsync(corrective);
                    if (_buildCts?.Token.IsCancellationRequested == true) throw new OperationCanceledException();
                    llmFixSw.Stop();
                    llmMs += llmFixSw.Elapsed;
                    planJson = ExtractRawJson(fixedPlan);
                }

                if (exec != null && exec.Success)
                {
                    AppendStatusLine("Model created.");
                    // Record that a model was created by this run so properties may be applied
                    try { _lastRunCreatedModel = exec.CreatedNewPart; _lastCreatedModelTitle = exec.ModelTitle; } catch { }
                    try { Dispatcher.Invoke(()=>{ applyPropertiesButton.IsEnabled = (exec.CreatedNewPart); }); } catch { }
                    StopKaraoke();
                    ShowKaraokeScenario("success");
                    SetRealTimeStatus("Creating model…", Colors.DarkOrange);
                    StartProgressPhase("success");
                    SetSwStatus("OK", Colors.DarkGreen);
                    try { SetModified(false); } catch { }
                    // Reset prompt and UI so user can enter a new prompt immediately
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            try { prompt.Text = string.Empty; } catch { }
                            try { FocusPrompt(); } catch { }
                            try { build.Content = "Build"; build.Background = new SolidColorBrush(Colors.DodgerBlue); build.Foreground = new SolidColorBrush(Colors.White); build.IsEnabled = true; } catch { }
                        });
                        try { _isBuilding = false; } catch { }
                    }
                    catch { }
                }
                else
                {
                    var swError = (exec != null && exec.Log.Count > 0 && exec.Log[exec.Log.Count - 1].ContainsKey("error"))
                        ? exec.Log[exec.Log.Count - 1].Value<string>("error")
                        : (errText ?? "Unknown error");
                    AppendStatusLine("SOLIDWORKS error: " + swError);
                    // Signal an error to the line-based karaoke: mark current line and stop advancing
                    try { SignalKaraokeError(); } catch { StopKaraoke(); }
                    SetRealTimeStatus("Error: " + swError, Colors.Firebrick);
                    SetSwStatus("Error", Colors.Firebrick);
                    if (string.IsNullOrWhiteSpace(errText)) errText = swError;
                    SetLastError(swError);
                    try
                    {
                        if (exec != null && exec.CreatedNewPart && _swApp != null && !string.IsNullOrWhiteSpace(exec.ModelTitle))
                        {
                            // Close document on UI thread
                            Dispatcher.Invoke(() => _swApp.CloseDoc(exec.ModelTitle));
                        }
                    }
                    catch { }
                }
                _lastLlm = llmMs;
                _lastTotal = totalSw.Elapsed;
                SetTimes(llmMs, totalSw.Elapsed);

                try
                {
                    _lastRunId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                    if (_stepStore != null)
                    {
                        var ok = await _stepStore.SaveRunWithStepsAsync(
                            _lastRunId,
                            text,
                            _lastModel ?? "gemini",
                            ExtractRawJson(_lastReply),
                            exec,
                            llmMs,
                            totalSw.Elapsed,
                            errText);
                        if (!ok && !string.IsNullOrWhiteSpace(_stepStore.LastError))
                        {
                            SetDbStatus("StepStore error: " + _stepStore.LastError, Colors.Firebrick);
                        }
                    }
                }
                catch { }
            }
            catch (OperationCanceledException)
            {
                errText = "Build cancelled by user";
                AppendStatusLine("Error: Build cancelled by user");
                SetRealTimeStatus("Cancelled", Colors.Firebrick);
                SetLlmStatus("Cancelled", Colors.Firebrick);
                SetSwStatus("Cancelled", Colors.Firebrick);
                SetLastError(errText);
                _lastLlm = llmMs;
                _lastTotal = totalSw.Elapsed;
                SetTimes(llmMs, totalSw.Elapsed);
                if (_fileLogger != null)
                {
                    await _fileLogger.LogAsync(text, reply, _lastModel ?? "gemini", llmMs, totalSw.Elapsed, errText);
                }
            }
            catch (Exception ex)
            {
                errText = ex.Message + "\n" + ex.StackTrace;
                AppendStatusLine("Error: " + ex.Message);
                SetRealTimeStatus("Error: " + ex.Message, Colors.Firebrick);
                SetLlmStatus("Error", Colors.Firebrick);
                SetSwStatus("Error", Colors.Firebrick);
                SetLastError(ex.Message);
                _lastLlm = llmMs;
                _lastTotal = totalSw.Elapsed;
                SetTimes(llmMs, totalSw.Elapsed);
                // Log full exception details to file
                if (_fileLogger != null)
                {
                    await _fileLogger.LogAsync(text, reply, _lastModel ?? "gemini", llmMs, totalSw.Elapsed, errText);
                }
            }
            finally
            {
                // Ensure LLM progress timer is stopped and mark progress Done/cleared
                try
                {
                    if (_llmProgressTimer != null)
                    {
                        try { _llmProgressTimer.Stop(); } catch { }
                        try { _llmProgressTimer.Dispose(); } catch { }
                        _llmProgressTimer = null;
                    }
                    if (_llmProgressStopwatch != null)
                    {
                        try { _llmProgressStopwatch.Stop(); } catch { }
                        try { AppendStatusLine(StatusConsole.FormatProgressDone(MakeProgressBar(100, 20), 100)); } catch { }
                        _llmProgressStopwatch = null;
                    }
                }
                catch { }
                totalSw.Stop();
                try
                {
                    SetRealTimeStatus("Saving feedback to database…", Colors.DarkOrange);
                    SetDbStatus("Logging…", Colors.DarkOrange);
                    bool logged = false;
                    if (_mongoLogger != null && _mongoLogger.IsAvailable)
                    {
                        logged = await _mongoLogger.LogAsync(text, reply, _lastModel ?? "gemini", llmMs, totalSw.Elapsed, errText);
                        if (!logged && !string.IsNullOrWhiteSpace(_mongoLogger.LastError))
                        {
                            SetDbStatus("Mongo log error: " + _mongoLogger.LastError, Colors.Firebrick);
                        }
                        else if (logged)
                        {
                            try
                            {
                                var doc = new MongoDB.Bson.BsonDocument
                                {
                                    { "runId", _lastRunId ?? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") },
                                    { "timestamp", DateTime.UtcNow },
                                    { "prompt", text },
                                    { "model", _lastModel ?? "gemini" },
                                    { "plan", ExtractRawJson(_lastReply ?? "{}") },
                                    { "exec", MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonArray>(ExecLogToJson(exec).ToString()) },
                                    { "success", exec?.Success ?? false },
                                    { "llmMs", (long)llmMs.TotalMilliseconds },
                                    { "totalMs", (long)totalSw.Elapsed.TotalMilliseconds },
                                    { "error", errText ?? string.Empty }
                                };
                                await _mongoLogger.InsertAsync("SW_Runs", doc);
                            }
                            catch { }
                        }
                    }

                    if (!logged && _fileLogger != null)
                    {
                        logged = await _fileLogger.LogAsync(text, reply, _lastModel ?? "gemini", llmMs, totalSw.Elapsed, errText);
                        if (string.IsNullOrEmpty(_lastRunId)) _lastRunId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                        try
                        {
                            var doc = new JObject
                            {
                                ["runId"] = _lastRunId,
                                ["timestamp"] = DateTime.UtcNow,
                                ["prompt"] = text,
                                ["model"] = _lastModel ?? "gemini",
                                ["plan"] = SafeJson(ExtractRawJson(_lastReply)),
                                ["exec"] = ExecLogToJson(exec),
                                ["success"] = exec?.Success ?? false,
                                ["llmMs"] = (long)llmMs.TotalMilliseconds,
                                ["totalMs"] = (long)totalSw.Elapsed.TotalMilliseconds,
                                ["error"] = errText ?? string.Empty
                            };
                            await _fileLogger.InsertAsync("SW_Runs", doc);
                        }
                        catch { }
                    }

                    _lastDbLogged = logged;
                    SetDbStatus(logged ? "Logged" : "Log error", logged ? Colors.DarkGreen : Colors.Firebrick);
                    SetRealTimeStatus(logged ? "Completed" : "Error logging", logged ? Colors.DarkGreen : Colors.Firebrick);
                    // Close the run section with a footer summarizing the attempt
                    try
                    {
                        AppendStatusLine($"[Run:{_lastRunId}] ----- Build End: success={(exec?.Success ?? false)} totalMs={(long)totalSw.Elapsed.TotalMilliseconds} error={(errText ?? string.Empty).Replace("\r", " ").Replace("\n", " ")} -----");
                    }
                    catch { }
                    UpdateGenerationProgress(logged ? 100 : 0);
                    // Smoothly animate karaoke color: blue on success, gray on failure
                    try { AnimateKaraokeToColor(logged ? Colors.DodgerBlue : Colors.Gray, 600); } catch { }
                }
                catch
                {
                    SetDbStatus("Log error (exception)", Colors.Firebrick);
                    SetRealTimeStatus("Error logging", Colors.Firebrick);
                }
                // Re-enable build UI and clear re-entry guard
                try { _isBuilding = false; } catch { }
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { build.Content = "Build"; build.Background = new SolidColorBrush(Colors.DodgerBlue); build.Foreground = new SolidColorBrush(Colors.White); } catch { }
                        try { build.IsEnabled = true; } catch { }
                    });
                }
                catch { }
                try { _buildCts?.Dispose(); _buildCts = null; } catch { }
            }
        }

        private void SetModified(bool modified)
        {
            // Track modified state but do not touch removed UI element
            _isModified = modified;
        }

        private void InitNameEasy()
        {
            _seriesManager = new SeriesManager();
            LoadSeriesFromDatabase();
            UpdatePreview();
        }

        private void LoadSeriesFromDatabase()
        {
            var series = _seriesManager?.GetAllSeries() ?? new List<string>();
            seriesComboBox.Items.Clear();
            foreach (var id in series)
            {
                seriesComboBox.Items.Add(id);
            }

            if (seriesComboBox.Items.Count > 0)
            {
                seriesComboBox.SelectedIndex = 0;
            }
            else
            {
                seriesComboBox.Items.Add("ASM");
                seriesComboBox.SelectedIndex = 0;
            }
        }

        private void SeriesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                _selectedSeries = seriesComboBox.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(_selectedSeries) || _seriesManager == null)
                {
                    nextSequenceLabel.Content = "Next Sequence: --";
                    previewTextBox.Text = string.Empty;
                    saveWithNameButton.IsEnabled = false;
                    applyPropertiesButton.IsEnabled = false;
                    return;
                }

                _seriesManager.AddSeries(_selectedSeries, "Auto-added", "0000");
                _nextSequence = _seriesManager.GetNextSequence(_selectedSeries);
                var partName = _seriesManager.GeneratePartName(_selectedSeries, _nextSequence);
                nextSequenceLabel.Content = $"Next Sequence: {_nextSequence:0000}";
                previewTextBox.Text = partName;
                saveWithNameButton.IsEnabled = true;
                applyPropertiesButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                saveWithNameButton.IsEnabled = false;
                applyPropertiesButton.IsEnabled = false;
                nextSequenceLabel.Content = "Next Sequence: --";
                previewTextBox.Text = string.Empty;
                AddinLogger.Error("Naming", "Failed to update preview", ex);
            }
        }

        private void AddSeriesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_seriesManager == null) return;
                if (!ShowAddSeriesDialog(out var seriesId, out var description, out var format)) return;

                if (_seriesManager.AddSeries(seriesId, description, format))
                {
                    LoadSeriesFromDatabase();
                    seriesComboBox.SelectedItem = seriesId;
                    SetRealTimeStatus($"Added series {seriesId}", Colors.DarkGreen);
                }
                else
                {
                    SetRealTimeStatus($"Series {seriesId} already exists", Colors.DarkOrange);
                }
            }
            catch (Exception ex)
            {
                AppendDetailedStatus("Series", "Add series failed", ex);
            }
        }

        private void SaveWithNameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_seriesManager == null)
                {
                    SetRealTimeStatus("Series manager not ready", Colors.Firebrick);
                    return;
                }

                var seriesId = _selectedSeries;
                if (string.IsNullOrWhiteSpace(seriesId))
                {
                    SetRealTimeStatus("Select a series first", Colors.Firebrick);
                    return;
                }

                var partName = previewTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(partName))
                {
                    SetRealTimeStatus("No part name", Colors.Firebrick);
                    return;
                }

                var material = (materialComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? materialComboBox.Text;
                var typeDesc = typeDescriptionTextBox.Text ?? string.Empty;

                var doc = _swApp?.ActiveDoc as IModelDoc2;
                if (doc == null)
                {
                    SetRealTimeStatus("No active document", Colors.Firebrick);
                    return;
                }

                // Enforce: properties may only be applied to a model created by the add-in's last successful run
                try
                {
                    if (!_lastRunCreatedModel)
                    {
                        SetRealTimeStatus("No created model to apply properties to", Colors.Firebrick);
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(_lastCreatedModelTitle))
                    {
                        try
                        {
                            var title = doc.GetTitle();
                            if (!string.Equals(title, _lastCreatedModelTitle, StringComparison.OrdinalIgnoreCase))
                            {
                                SetRealTimeStatus("Active document does not match the created model", Colors.Firebrick);
                                return;
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // Use SaveFileDialog
                var sfd = new System.Windows.Forms.SaveFileDialog
                {
                    Title = "Save Part As",
                    FileName = partName + ".SLDPRT",
                    Filter = "Part files (*.sldprt)|*.sldprt",
                    DefaultExt = "sldprt"
                };

                if (sfd.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    SetRealTimeStatus("Save cancelled", Colors.DarkOrange);
                    return;
                }

                var fullPath = sfd.FileName;

                // Save using SaveAs4
                int errors = 0;
                int warnings = 0;
                bool saved = doc.Extension.SaveAs(fullPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, 
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);

                if (!saved || errors != 0)
                {
                    SetRealTimeStatus($"Save failed (errors={errors}, warnings={warnings})", Colors.Firebrick);
                    return;
                }

                // Set properties after successful save
                SetPartPropertiesOnDocument(doc, material, typeDesc, partName);

                // Commit sequence
                _seriesManager.CommitSequence(seriesId, _nextSequence, partName, fullPath);
                SetRealTimeStatus($"Saved as {partName}", Colors.DarkGreen);
                UpdatePreview();

                // Rebuild to apply material
                doc.ForceRebuild3(false);
            }
            catch (Exception ex)
            {
                AppendDetailedStatus("SaveWithName", "Failed", ex);
                SetRealTimeStatus("Error saving", Colors.Firebrick);
            }
        }

        private void SetPartPropertiesOnDocument(IModelDoc2 doc, string material, string description, string partName)
        {
            try
            {
                var custPropMgr = doc.Extension.CustomPropertyManager[""];
                if (custPropMgr != null)
                {
                    if (!string.IsNullOrEmpty(material))
                    {
                        custPropMgr.Add3("Material", (int)swCustomInfoType_e.swCustomInfoText, material, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                        try { AddinStatusLogger.Log("TaskpaneWpf", $"Set Material: {material}"); } catch { }

                        // Apply material to part model (can be disabled at runtime via env var AICAD_APPLY_MATERIAL=0)
                        try
                        {
                            var applyMat = System.Environment.GetEnvironmentVariable("AICAD_APPLY_MATERIAL") ?? "1";
                            if (applyMat != "0")
                            {
                                var partDoc = doc as PartDoc;
                                if (partDoc != null)
                                {
                                    string database = "solidworks materials.sldmat";
                                    partDoc.SetMaterialPropertyName2("", database, material);
                                    try { AddinStatusLogger.Log("TaskpaneWpf", $"Applied material to model: {material}"); } catch { }
                                }
                            }
                            else
                            {
                                try { AddinStatusLogger.Log("TaskpaneWpf", "Skipping material application due to AICAD_APPLY_MATERIAL=0"); } catch { }
                            }
                        }
                        catch (Exception matEx)
                        {
                            try { AddinStatusLogger.Error("TaskpaneWpf", "Material application to model failed", matEx); } catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(description))
                    {
                        custPropMgr.Add3("Description", (int)swCustomInfoType_e.swCustomInfoText, description, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }

                    string filename = System.IO.Path.GetFileNameWithoutExtension(doc.GetPathName());
                    if (!string.IsNullOrEmpty(filename))
                    {
                        custPropMgr.Add3("Mass", (int)swCustomInfoType_e.swCustomInfoText, $"\"SW-Mass@{filename}.SLDPRT\"", (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }

                    if (!string.IsNullOrEmpty(partName))
                    {
                        custPropMgr.Add3("PartNo", (int)swCustomInfoType_e.swCustomInfoText, partName, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("TaskpaneWpf", "Error setting custom properties", ex); } catch { }
            }
        }

        private void ApplyPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Let host/UI listeners know properties apply was requested
                try { ApplyPropertiesRequested?.Invoke(this, EventArgs.Empty); } catch { }

                var partName = previewTextBox.Text?.Trim();
                var material = (materialComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? materialComboBox.Text;
                var typeDesc = typeDescriptionTextBox.Text ?? string.Empty;

                var doc = _swApp?.ActiveDoc as IModelDoc2;
                if (doc == null)
                {
                    SetRealTimeStatus("No active document", Colors.Firebrick);
                    return;
                }

                SetPartPropertiesOnDocument(doc, material, typeDesc, partName);
                doc.ForceRebuild3(false);
                SetRealTimeStatus("Properties applied", Colors.DarkGreen);
            }
            catch (Exception ex)
            {
                AppendDetailedStatus("ApplyProps", "Failed", ex);
                SetRealTimeStatus("Error applying properties", Colors.Firebrick);
            }
        }

        public void LoadFromProperties(string material, string description, string mass, string partNo)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Load material - match from list or set as text
                    if (!string.IsNullOrEmpty(material))
                    {
                        var items = materialComboBox.Items.Cast<ComboBoxItem>();
                        var match = items.FirstOrDefault(i => i.Content?.ToString()?.Equals(material, StringComparison.OrdinalIgnoreCase) == true);
                        if (match != null)
                        {
                            materialComboBox.SelectedItem = match;
                        }
                        else
                        {
                            // Material not in list, set as custom text (editable combobox)
                            materialComboBox.Text = material;
                        }
                    }
                    else
                    {
                        materialComboBox.SelectedIndex = 0; // Default to first material
                    }

                    // Load description
                    typeDescriptionTextBox.Text = description ?? string.Empty;

                    // Load weight (mass) - ensure it updates even if empty
                    weightTextBox.Text = mass ?? "0.000";

                    // Don't overwrite preview with partNo - preview is for generated names
                    // If we want to show existing partNo somewhere, add a separate field
                    
                    try { AddinStatusLogger.Log("TaskpaneWpf", $"Loaded properties: Mat={material}, Desc={description}, Mass={mass}"); } catch { }
                });
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("TaskpaneWpf", "LoadFromProperties failed", ex); } catch { }
            }
        }

        private bool TryApplyPropertiesToActiveModel(string partName, string material, string description, string weight, out string status)
        {
            status = string.Empty;
            try
            {
                var model = _swApp?.IActiveDoc2;
                if (model == null)
                {
                    status = "No active document";
                    return false;
                }

                var props = model.Extension?.get_CustomPropertyManager("");
                if (props == null)
                {
                    status = "No property manager";
                    return false;
                }

                void SetProp(string name, string value)
                {
                    props.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value ?? string.Empty, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                }

                if (!string.IsNullOrWhiteSpace(partName)) SetProp("Part Number", partName);
                SetProp("Material", material ?? string.Empty);
                SetProp("Description", description ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(weight)) SetProp("Weight", weight);

                model.ForceRebuild3(false);
                return true;
            }
            catch (Exception ex)
            {
                status = ex.Message;
                return false;
            }
        }

        private bool ShowAddSeriesDialog(out string seriesId, out string description, out string format)
        {
            seriesId = string.Empty;
            description = string.Empty;
            format = "0000";

            var dialog = new Window
            {
                Title = "Add Series",
                Width = 340,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this)
            };

            var panel = new StackPanel { Margin = new Thickness(10) };
            var idLabel = new TextBlock { Text = "Series ID (e.g., ASM)", FontWeight = FontWeights.Bold };
            var idBox = new TextBox { Margin = new Thickness(0, 4, 0, 10) };
            var descLabel = new TextBlock { Text = "Description" };
            var descBox = new TextBox { Margin = new Thickness(0, 4, 0, 10) };
            var fmtLabel = new TextBlock { Text = "Sequence format (e.g., 0000)" };
            var fmtBox = new TextBox { Text = "0000", Margin = new Thickness(0, 4, 0, 12) };

            panel.Children.Add(idLabel);
            panel.Children.Add(idBox);
            panel.Children.Add(descLabel);
            panel.Children.Add(descBox);
            panel.Children.Add(fmtLabel);
            panel.Children.Add(fmtBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 70 };
            buttonPanel.Children.Add(ok);
            buttonPanel.Children.Add(cancel);
            panel.Children.Add(buttonPanel);

            dialog.Content = panel;

            string pendingSeries = null;
            string pendingDesc = null;
            string pendingFmt = null;

            ok.Click += (_, __) =>
            {
                var id = idBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    MessageBox.Show(dialog, "Please enter a series ID.", "NameEasy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                pendingSeries = id;
                pendingDesc = descBox.Text.Trim();
                pendingFmt = string.IsNullOrWhiteSpace(fmtBox.Text) ? "0000" : fmtBox.Text.Trim();
                dialog.DialogResult = true;
                dialog.Close();
            };
            cancel.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };

            var result = dialog.ShowDialog() == true;
            if (result)
            {
                seriesId = pendingSeries ?? string.Empty;
                description = pendingDesc ?? string.Empty;
                format = pendingFmt ?? "0000";
            }

            return result;
        }

        private string GetAddinVersion()
        {
            try
            {
                var asm = typeof(TextToCADTaskpaneWpf).Assembly;
                var fileVer = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location)?.FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVer)) return "v" + fileVer;
                var ver = asm.GetName().Version?.ToString();
                return string.IsNullOrWhiteSpace(ver) ? "v?" : "v" + ver;
            }
            catch { return "v?"; }
        }

        private int OnActiveModelDocChanged()
        {
            try
            {
                SetModified(false);
            }
            catch { }
            return 0;
        }

        private int OnFileOpenPostNotify(string fileName)
        {
            try { SetModified(false); } catch { }
            return 0;
        }

        private int OnFileNewNotify2(object newDoc, int docType, string templateName)
        {
            try { SetModified(false); } catch { }
            return 0;
        }

        private int OnDocumentLoadNotify2(string docTitle, string docPath)
        {
            try { SetModified(false); } catch { }
            return 0;
        }

        private static JToken SafeJson(string json)
        {
            try { return JToken.Parse(json); } catch { return (json ?? string.Empty); }
        }

        private async Task SubmitFeedbackAsync(bool up)
        {
            try
            {
                if (_fileLogger == null && _mongoLogger == null)
                {
                    SetDbStatus("Feedback not saved (no logger)", Colors.DarkOrange);
                    return;
                }
                var fb = new JObject
                {
                    ["runId"] = _lastRunId ?? string.Empty,
                    ["timestamp"] = DateTime.UtcNow,
                    ["thumb"] = up ? "up" : "down",
                    ["prompt"] = _lastPrompt ?? string.Empty,
                    ["model"] = _lastModel ?? string.Empty
                };
                bool ok = false;
                if (_mongoLogger != null && _mongoLogger.IsAvailable)
                {
                    try
                    {
                        var bdoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonDocument>(AICAD.Services.JsonUtils.SerializeCompact(fb));
                        ok = await _mongoLogger.InsertAsync("Feedback", bdoc);
                    }
                    catch { ok = false; }
                }
                if (!ok && _fileLogger != null)
                {
                    ok = await _fileLogger.InsertAsync("Feedback", fb);
                }
                if (up && _goodStore != null)
                {
                    string plan = ExtractRawJson(_lastReply ?? "{}");
                    var saved = await _goodStore.SaveGoodAsync(_lastRunId, _lastPrompt, _lastModel, plan, null);
                    if (!saved && !string.IsNullOrWhiteSpace(_goodStore.LastError))
                    {
                        SetDbStatus("GoodStore error: " + _goodStore.LastError, Colors.Firebrick);
                    }
                }
                if (_stepStore != null)
                {
                    var s2ok = await _stepStore.SaveFeedbackAsync(_lastRunId, up, null);
                    if (!s2ok && !string.IsNullOrWhiteSpace(_stepStore.LastError))
                    {
                        SetDbStatus("StepStore fb error: " + _stepStore.LastError, Colors.Firebrick);
                    }
                }
                SetDbStatus(ok ? "Feedback saved" : "Feedback error", ok ? Colors.DarkGreen : Colors.Firebrick);
            }
            catch (Exception ex)
            {
                SetDbStatus("Feedback error: " + ex.Message, Colors.Firebrick);
            }
        }

        private string ExtractRawJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "{}";
            var t = text.Trim();

            if (t.StartsWith("```"))
            {
                var newline = t.IndexOf('\n');
                if (newline >= 0)
                {
                    t = t.Substring(newline + 1);
                    var fence = t.LastIndexOf("```", StringComparison.Ordinal);
                    if (fence >= 0) t = t.Substring(0, fence);
                    t = t.Trim();
                }
            }

            int start = t.IndexOf('{');
            if (start >= 0)
            {
                int depth = 0;
                bool inString = false;
                for (int i = start; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c == '"')
                    {
                        inString = !inString;
                    }
                    if (!inString)
                    {
                        if (c == '{') depth++;
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                return t.Substring(start, i - start + 1);
                            }
                        }
                    }
                }
                return t.Substring(start).Trim();
            }

            return t;
        }

        private void StatusWindow_CopyErrorClicked(object sender, EventArgs e)
        {
            try
            {
                var text = BuildErrorCopyText();
                if (!string.IsNullOrEmpty(text))
                {
                    System.Windows.Forms.Clipboard.SetText(text);
                    AppendStatusLine("[UI] Error copied to clipboard");
                }
            }
            catch (Exception ex) { AppendDetailedStatus("UI", "Copy error failed", ex); }
        }

        private void StatusWindow_CopyRunClicked(object sender, EventArgs e)
        {
            try
            {
                var text = BuildRunCopyText();
                if (!string.IsNullOrEmpty(text))
                {
                    System.Windows.Forms.Clipboard.SetText(text);
                    AppendStatusLine("[UI] Run copied to clipboard");
                }
            }
            catch (Exception ex) { AppendDetailedStatus("UI", "Copy run failed", ex); }
        }

        private void MirrorStatusToTempFile(string line)
        {
            // Disabled: user requested stopping temporary mirror logging to disk.
            return;
        }

        private void AppendDetailedStatus(string category, string message, Exception ex)
        {
            try
            {
                var header = string.IsNullOrWhiteSpace(message) ? $"[{category}]" : $"[{category}] {message}";
                AppendStatusLine(header);
                if (ex != null)
                {
                    var full = ex.ToString();
                    var lines = full.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var ln in lines)
                    {
                        AppendStatusLine("  " + ln.Trim());
                    }
                }
            }
            catch { }
        }

        private JArray ExecLogToJson(StepExecutionResult exec)
        {
            var arr = new JArray();
            if (exec?.Log != null)
            {
                foreach (var entry in exec.Log)
                {
                    arr.Add(entry);
                }
            }
            return arr;
        }

        // --- Progressive status UI helpers ---
        // Preset-based visual progress removed. UI now uses the dynamic `_steps` collection
        // driven by `KaraokeStyleStatus.ProgressChanged` events. Header counter updated
        // elsewhere to reflect completed steps.

        private void InitProgressPanel()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (ProgressStatusPanel == null) return;
                        ProgressStatusPanel.Children.Clear();

                        // Header with task counter
                        try
                        {
                            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                            var heading = new TextBlock { Text = "Task", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.DarkSlateGray) };
                            Grid.SetColumn(heading, 0);
                            _taskCountText = new TextBlock { Text = $"0/{_steps.Count}", HorizontalAlignment = HorizontalAlignment.Right, Foreground = new SolidColorBrush(Colors.DimGray) };
                            Grid.SetColumn(_taskCountText, 1);
                            headerGrid.Children.Add(heading);
                            headerGrid.Children.Add(_taskCountText);
                            ProgressStatusPanel.Children.Add(headerGrid);
                        }
                        catch { }

                        // Ensure the dynamic steps ItemsControl is present
                        try { ProgressStatusPanel.Children.Add(StepsItemsControl); } catch { }
                        // KaraokeStatus text block follows the step list
                        try { if (KaraokeStatus != null) ProgressStatusPanel.Children.Add(KaraokeStatus); } catch { }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        // Initialize a fixed ordered list of high-level steps for the taskpane progress UI.
        private void InitializeStepProgress(IEnumerable<string> labels)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _steps.Clear();
                        foreach (var l in labels ?? Array.Empty<string>())
                        {
                            _steps.Add(new StepViewModel { Label = l, State = StepState.Pending, Percent = null, Message = string.Empty });
                        }
                        try { if (_taskCountText != null) _taskCountText.Text = $"0/{_steps.Count}"; } catch { }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        // Set a named step's percent and optional state (thread-safe)
        private void SetStepProgress(string label, int? percent = null, StepState? state = null)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var vm = _steps.FirstOrDefault(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase));
                        if (vm == null) return;
                        if (percent.HasValue) vm.Percent = Math.Max(0, Math.Min(100, percent.Value));
                        if (state.HasValue) vm.State = state.Value;
                        try { var completed = _steps.Count(s => s.State == StepState.Success); if (_taskCountText != null) _taskCountText.Text = $"{completed}/{_steps.Count}"; } catch { }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        // Simple mapping from low-level op names to our high-level step labels
        private void UpdateHigherLevelFromOp(string op, int pct)
        {
            if (string.IsNullOrWhiteSpace(op)) return;
            var o = op.ToLowerInvariant();
            try
            {
                if (o.Contains("sketch") || o.Contains("rectangle") || o.Contains("circle"))
                {
                    SetStepProgress("Building sketch", pct, pct >= 100 ? StepState.Success : StepState.Running);
                }
                else if (o.Contains("extrude") || o.Contains("boss") || o.Contains("feature"))
                {
                    SetStepProgress("Adding features", pct, pct >= 100 ? StepState.Success : StepState.Running);
                }
                else if (o.Contains("select_plane") || o.Contains("plane"))
                {
                    SetStepProgress("Checking parameters", pct, pct >= 100 ? StepState.Success : StepState.Running);
                }
                else if (o.Contains("constraint") || o.Contains("mate"))
                {
                    SetStepProgress("Applying constraints", pct, pct >= 100 ? StepState.Success : StepState.Running);
                }
                else if (o.Contains("save") || o.Contains("saveas") || o.Contains("save_part"))
                {
                    SetStepProgress("Saving model", pct, pct >= 100 ? StepState.Success : StepState.Running);
                }
            }
            catch { }
        }

        private void AppendStatusLine(string line)
        {
            try
            {
                var ts = DateTime.Now.ToString("HH:mm:ss");
                // Always mirror to temp file for debugging
                try { MirrorStatusToTempFile($"{ts} {line}"); } catch { }

                // If the external status window exists, write there as before
                if (_statusWindow != null && !_statusWindow.IsDisposed)
                {
                    Action write = () =>
                    {
                        var rtb = _statusWindow.StatusConsole;
                        // Colorize certain categories for readability on dark background
                        try
                        {
                            var color = System.Drawing.Color.Gainsboro;
                            var l = (line ?? string.Empty);
                            if (l.StartsWith("[ERROR", StringComparison.OrdinalIgnoreCase) || l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) || l.IndexOf(" ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                color = System.Drawing.Color.OrangeRed; // high-contrast red
                            }
                            else if (l.StartsWith("[FewShot", StringComparison.OrdinalIgnoreCase) || l.IndexOf("FewShot", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                color = System.Drawing.Color.DodgerBlue; // bright blue
                            }
                            else if (l.StartsWith(StatusConsole.StatusPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                color = System.Drawing.Color.Gold; // visible gold/orange
                            }
                            else if (l.StartsWith("[Run:", StringComparison.OrdinalIgnoreCase))
                            {
                                color = System.Drawing.Color.Cyan; // run headers in cyan
                            }
                            else if (l.StartsWith("[LLM]", StringComparison.OrdinalIgnoreCase))
                            {
                                if (l.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0) color = System.Drawing.Color.LimeGreen;
                                else if (l.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0) color = System.Drawing.Color.OrangeRed;
                                else color = System.Drawing.Color.LightSkyBlue;
                            }
                            else if (l.StartsWith(StatusConsole.ProgressPrefix, StringComparison.OrdinalIgnoreCase) || l.StartsWith(StatusConsole.LlmProgressPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                color = System.Drawing.Color.LightGreen;
                            }
                            rtb.SelectionColor = color;
                        }
                        catch { }

                        // If this is a progress line and the last line is also a progress line, overwrite it inline
                        try
                        {
                            if (((line ?? string.Empty).StartsWith(StatusConsole.ProgressPrefix, StringComparison.OrdinalIgnoreCase) || (line ?? string.Empty).StartsWith(StatusConsole.LlmProgressPrefix, StringComparison.OrdinalIgnoreCase)) && rtb.Lines.Length > 0)
                            {
                                var last = rtb.Lines[rtb.Lines.Length - 1] ?? string.Empty;
                                if (last.IndexOf(StatusConsole.ProgressPrefix, StringComparison.OrdinalIgnoreCase) >= 0 || last.IndexOf(StatusConsole.LlmProgressPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    int firstChar = rtb.GetFirstCharIndexFromLine(rtb.Lines.Length - 1);
                                    if (firstChar >= 0)
                                    {
                                        rtb.SelectionStart = firstChar;
                                        rtb.SelectionLength = rtb.TextLength - firstChar;
                                        rtb.SelectedText = $"{ts} {line}\n";
                                        rtb.SelectionStart = rtb.TextLength;
                                        rtb.ScrollToCaret();
                                        // already written
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }

                        rtb.AppendText($"{ts} {line}\n");
                        rtb.SelectionStart = rtb.TextLength;
                        rtb.ScrollToCaret();
                    };

                    try
                    {
                        if (_statusWindow.InvokeRequired) _statusWindow.Invoke((Action)write);
                        else write();
                    }
                    catch { }
                }
            }
            catch { }

            // Also render into the taskpane progressive status panel (karaoke-driven)
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(line)) return;
                        // Mirror simple karaoke status text to the taskpane
                        UpdateKaraokeStatus(line);
                        // Update header counter if present
                        try
                        {
                            var completed = _steps.Count(s => s.State == StepState.Success);
                            if (_taskCountText != null) _taskCountText.Text = $"{completed}/{_steps.Count}";
                        }
                        catch { }
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        private void BtnNewBottom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new BlankWebView2Window();
                win.Show();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to open WebView2 window: {ex.Message}", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private const int WM_GETDLGCODE = 0x0087;
        private const int DLGC_WANTALLKEYS = 0x0004;
        private const int DLGC_WANTCHARS = 0x0080;

        private IntPtr ChildHwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // This hook tells SolidWorks: "I want all keys and characters. Do not steal them!"
            if (msg == WM_GETDLGCODE)
            {
                try { AppendKeyLog("Hook: WM_GETDLGCODE - Anti-Steal Active"); } catch { }
                handled = true;
                return new IntPtr(DLGC_WANTALLKEYS | DLGC_WANTCHARS);
            }
            return IntPtr.Zero;
        }
    }
}