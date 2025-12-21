using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using AICAD.Services;
using Newtonsoft.Json.Linq;

namespace AICAD.UI
{
    public partial class TextToCADTaskpaneWpf : UserControl
    {
        private readonly ISldWorks _swApp;
        private bool _isModified = false;
        private GeminiClient _client;
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
        private IStepStore _stepStore;

        public TextToCADTaskpaneWpf(ISldWorks swApp)
        {
            InitializeComponent();
            _swApp = swApp;

            // Set initial version
            var ver = GetAddinVersion();
            lblVersion.Content = ver;

            // Wire up event handlers
            shapePreset.SelectionChanged += ShapePreset_SelectionChanged;
            prompt.TextChanged += Prompt_TextChanged;
            build.Click += async (s, e) => await BuildFromPromptAsync();
            btnHistory.Click += BtnHistory_Click;
            btnStatus.Click += BtnStatus_Click;
            btnSettings.Click += BtnSettings_Click;
            btnThumbUp.Click += async (s, e) => await SubmitFeedbackAsync(true);
            btnThumbDown.Click += async (s, e) => await SubmitFeedbackAsync(false);

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
            try { Services.AddinStatusLogger.OnLog += (line) => { try { AppendStatusLine(line); } catch { } }; Services.AddinStatusLogger.Log("Init", "Taskpane subscribed to AddinStatusLogger"); } catch { }
            try { InitDbAndStores(); } catch (Exception ex) { AppendDetailedStatus("DB:init", "call exception", ex); }
        }

        private void ShapePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = shapePreset.SelectedIndex;
            switch (idx)
            {
                case 1: SetPromptText("Create a rectangular box 100 mm length, 50 mm width, 25 mm height"); break;
                case 2: SetPromptText("Create a cylinder 40 mm diameter and 80 mm height"); break;
                case 3: SetPromptText("Create a cube 10 mm"); break;
                default: SetPromptText(string.Empty); break;
            }
        }

        private void Prompt_TextChanged(object sender, TextChangedEventArgs e)
        {
            try { SetModified(true); } catch { }
        }

        private string GetPromptText() => prompt.Text;

        private void SetPromptText(string text) => prompt.Text = text ?? string.Empty;

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
                var baseDir = @"D:\SolidWorks API\7. SolidWorks Taskpane Text To CAD";
                _fileLogger = new FileDbLogger(baseDir);

                var mongoUri = System.Environment.GetEnvironmentVariable("MONGODB_URI")
                               ?? System.Environment.GetEnvironmentVariable("MONGO_LOG_CONN")
                               ?? string.Empty;
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
            if (lblRealTimeStatus != null)
            {
                lblRealTimeStatus.Content = text ?? string.Empty;
                lblRealTimeStatus.Foreground = new SolidColorBrush(color);
            }
            AppendStatusLine($"[Status] {text}");
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

        private void SetLastError(string err)
        {
            _lastError = err;
            if (_statusWindow != null && !_statusWindow.IsDisposed)
            {
                _statusWindow.ErrorTextBox.Text = string.IsNullOrWhiteSpace(err) ? "—" : err;
                // _statusWindow.ErrorTextBox.ForeColor handled in StatusWindow
            }
            if (!string.IsNullOrWhiteSpace(err)) AppendStatusLine($"[Error] {err}");
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

        private GeminiClient GetClient()
        {
            if (_client == null)
            {
                var key = "AIzaSyBUzKATKs5ea0mTSmGziZDnDdjaDK1RpjE";

                if (key == "REPLACE_WITH_YOUR_KEY_DO_NOT_COMMIT")
                {
                    AppendDetailedStatus("LLM", "API Key is not set. Please edit UI/TextToCADTaskpaneWpf.xaml.cs and replace the placeholder key.", null);
                    throw new InvalidOperationException("API Key is not set in the source code.");
                }

                var preferredModel = System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.User)
                                     ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Process)
                                     ?? System.Environment.GetEnvironmentVariable("GEMINI_MODEL", System.EnvironmentVariableTarget.Machine)
                                     ?? "gemini-1.0-pro";
                _client = new GeminiClient(key, preferredModel);
                AppendStatusLine("[LLM] Gemini client constructed with hardcoded API key.");
                
                var baseDir = @"D:\SolidWorks API\9. SolidWorks Taskpane Text To CAD";
                _fileLogger = new FileDbLogger(baseDir);

                var mongoUri = System.Environment.GetEnvironmentVariable("MONGODB_URI") ?? string.Empty;
                var mongoDb = System.Environment.GetEnvironmentVariable("MONGODB_DB") ?? "TaskPaneAddin";
                
                if (!string.IsNullOrWhiteSpace(mongoUri))
                {
                    try
                    {
                        _mongoLogger = new MongoLogger(mongoUri, mongoDb, "SW");
                        _goodStore = new MongoFeedbackStore(mongoUri, mongoDb, "good_feedback");
                        _stepStore = new MongoStepStore(mongoUri, mongoDb);
                        AppendStatusLine("[DB] MongoDB stores initialized.");
                    }
                    catch (Exception ex) { AppendDetailedStatus("DB", "Mongo store initialization failed", ex); }
                }

                if (_goodStore == null)
                {
                    try { _goodStore = new SqliteFeedbackStore(baseDir); }
                    catch { _goodStore = new FileGoodFeedbackStore(baseDir); }
                }
                if (_stepStore == null)
                {
                    try { _stepStore = new SqliteStepStore(baseDir); }
                    catch { }
                }

                if (_mongoLogger != null && _mongoLogger.IsAvailable)
                {
                    SetDbStatus("MongoDB ready", Colors.DarkGreen);
                }
                else
                {
                    SetDbStatus("File/SQLite ready", Colors.DarkGreen);
                }
            }
            return _client;
        }

        private async Task BuildFromPromptAsync()
        {
            var text = (GetPromptText() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                AppendStatusLine("Enter a prompt describing a simple box or cylinder in mm.");
                return;
            }

            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            string reply = null;
            string errText = null;
            TimeSpan llmMs = TimeSpan.Zero;
            StepExecutionResult exec = null;
            try
            {
                build.IsEnabled = false;
                _lastPrompt = text;
                AppendStatusLine("> " + text);
                SetRealTimeStatus("Communicating with LLM…", Colors.DarkOrange);
                SetLlmStatus("Sending…", Colors.DarkOrange);
                SetLastError(null);
                SetTimes(null, null);

                var fewshot = new StringBuilder()
                    .Append("Examples:")
                    .Append("\nInput: Box 100x50x25 mm")
                    .Append("\nOutput:{\n  \"steps\":[\n    {\"op\":\"new_part\"},\n    {\"op\":\"select_plane\",\"name\":\"Front Plane\"},\n    {\"op\":\"sketch_begin\"},\n    {\"op\":\"rectangle_center\",\"cx\":0,\"cy\":0,\"w\":100,\"h\":50},\n    {\"op\":\"sketch_end\"},\n    {\"op\":\"extrude\",\"depth\":25,\"type\":\"boss\"}\n  ]\n}")
                    .Append("\nInput: Cylinder 40 dia x 80 mm")
                    .Append("\nOutput:{\n  \"steps\":[\n    {\"op\":\"new_part\"},\n    {\"op\":\"select_plane\",\"name\":\"Front Plane\"},\n    {\"op\":\"sketch_begin\"},\n    {\"op\":\"circle_center\",\"cx\":0,\"cy\":0,\"diameter\":40},\n    {\"op\":\"sketch_end\"},\n    {\"op\":\"extrude\",\"depth\":80}\n  ]\n}");

                if (_goodStore != null)
                {
                    var extras = _goodStore.GetRecentFewShots(2);
                    foreach (var s in extras)
                    {
                        fewshot.Append(s);
                    }
                }
                if (_stepStore != null)
                {
                    var more = _stepStore.GetRelevantFewShots(text, 3);
                    foreach (var s in more) fewshot.Append(s);
                }

                var sysPrompt =
                    "You are a CAD planning agent. Convert the user request into a step plan JSON for SOLIDWORKS. " +
                    "Supported ops: new_part; select_plane{name}; sketch_begin; rectangle_center{cx,cy,w,h}; circle_center{cx,cy,r|diameter}; sketch_end; extrude{depth,type?}. " +
                    "Units are millimeters; output ONLY raw JSON with a top-level 'steps' array. No markdown or extra text.\n" + fewshot + "\nNow generate plan for: ";
                var client = GetClient();
                _lastModel = client?.Model;
                SetRealTimeStatus("Applying few-shot examples…", Colors.DarkOrange);
                var llmSw = System.Diagnostics.Stopwatch.StartNew();
                reply = await client.GenerateAsync(sysPrompt + fewshot.ToString() + "\nNow generate plan for: " + text + "\nJSON:");
                llmSw.Stop();
                llmMs = llmSw.Elapsed;
                _lastReply = reply;
                AppendStatusLine(reply);
                SetRealTimeStatus("Received response from LLM", Colors.DarkGreen);
                SetLlmStatus("OK", Colors.DarkGreen);

                SetRealTimeStatus("Executing plan…", Colors.DarkOrange);
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

                    exec = Services.StepExecutor.Execute(planDoc, _swApp);
                    if (exec.Success) break;

                    var errDoc = new JObject
                    {
                        ["last_plan"] = SafeJson(planJson),
                        ["errors"] = new JArray(exec.Log)
                    };
                    var corrective =
                        "Your previous plan failed in SOLIDWORKS. Fix the plan based on this error log and output only corrected JSON.\n" +
                        errDoc.ToString(Newtonsoft.Json.Formatting.None) +
                        "\nRemember: output only JSON with steps; use Front Plane and mm units.";
                    try
                    {
                        if (exec.CreatedNewPart && !exec.Success && _swApp != null && !string.IsNullOrWhiteSpace(exec.ModelTitle))
                        {
                            _swApp.CloseDoc(exec.ModelTitle);
                        }
                    }
                    catch { }

                    var llmFixSw = System.Diagnostics.Stopwatch.StartNew();
                    var fixedPlan = await client.GenerateAsync(corrective);
                    llmFixSw.Stop();
                    llmMs += llmFixSw.Elapsed;
                    planJson = ExtractRawJson(fixedPlan);
                }

                if (exec != null && exec.Success)
                {
                    AppendStatusLine("Model created.");
                    SetRealTimeStatus("Creating model…", Colors.DarkOrange);
                    SetSwStatus("OK", Colors.DarkGreen);
                    try { SetModified(false); } catch { }
                }
                else
                {
                    var swError = (exec != null && exec.Log.Count > 0 && exec.Log[exec.Log.Count - 1].ContainsKey("error"))
                        ? exec.Log[exec.Log.Count - 1].Value<string>("error")
                        : (errText ?? "Unknown error");
                    AppendStatusLine("SOLIDWORKS error: " + swError);
                    SetRealTimeStatus("Error: " + swError, Colors.Firebrick);
                    SetSwStatus("Error", Colors.Firebrick);
                    if (string.IsNullOrWhiteSpace(errText)) errText = swError;
                    SetLastError(swError);
                    try
                    {
                        if (exec != null && exec.CreatedNewPart && _swApp != null && !string.IsNullOrWhiteSpace(exec.ModelTitle))
                        {
                            _swApp.CloseDoc(exec.ModelTitle);
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
            catch (Exception ex)
            {
                errText = ex.Message;
                AppendStatusLine("Error: " + ex.Message);
                SetRealTimeStatus("Error: " + ex.Message, Colors.Firebrick);
                SetLlmStatus("Error", Colors.Firebrick);
                SetSwStatus("Error", Colors.Firebrick);
                SetLastError(ex.Message);
                _lastLlm = llmMs;
                _lastTotal = totalSw.Elapsed;
                SetTimes(llmMs, totalSw.Elapsed);
            }
            finally
            {
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
                }
                catch
                {
                    SetDbStatus("Log error (exception)", Colors.Firebrick);
                    SetRealTimeStatus("Error logging", Colors.Firebrick);
                }
                build.IsEnabled = true;
            }
        }

        private void SetModified(bool modified)
        {
            _isModified = modified;
            if (lblModified != null)
            {
                lblModified.Visibility = modified ? Visibility.Visible : Visibility.Collapsed;
            }
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
                        var bdoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<MongoDB.Bson.BsonDocument>(fb.ToString());
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
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TextToCAD_Status.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("o") + " " + line + System.Environment.NewLine);
            }
            catch { }
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

        private void AppendStatusLine(string line)
        {
            try
            {
                if (_statusWindow != null && !_statusWindow.IsDisposed)
                {
                    var ts = DateTime.Now.ToString("HH:mm:ss");
                    _statusWindow.StatusConsole.SelectionStart = _statusWindow.StatusConsole.TextLength;
                    _statusWindow.StatusConsole.AppendText($"{ts} {line}\n");
                    _statusWindow.StatusConsole.SelectionStart = _statusWindow.StatusConsole.TextLength;
                    _statusWindow.StatusConsole.ScrollToCaret();
                    try { MirrorStatusToTempFile($"{ts} {line}"); } catch { }
                    return;
                }

                try { MirrorStatusToTempFile(DateTime.Now.ToString("HH:mm:ss") + " " + line); } catch { }
            }
            catch { }
        }
    }
}