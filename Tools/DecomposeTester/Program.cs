using System;
using System.IO;
using Newtonsoft.Json.Linq;

Console.WriteLine("Decompose flow tester (local JSON-based)\n");

var userRequest = args.Length > 0 ? string.Join(' ', args) : "Create a small bracket with two holes";

Console.WriteLine($"User request:\n{userRequest}\n");

var catalogPath = Path.Combine(Environment.CurrentDirectory, "Config", "PromptCatalog.json");
if (!File.Exists(catalogPath))
{
    // Try to find by walking up (similar to PromptCatalog.LocateCatalogPath)
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    bool found = false;
    for (; dir != null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "Config", "PromptCatalog.json");
        if (File.Exists(candidate)) { catalogPath = candidate; found = true; break; }
    }
    if (!found)
    {
        Console.WriteLine($"Could not find Config/PromptCatalog.json. Please run from repo or set AICAD_PROMPT_TEMPLATES.");
        return;
    }
}

JObject catalog = null;
try { catalog = JObject.Parse(File.ReadAllText(catalogPath)); } catch (Exception ex) { Console.WriteLine("Failed to parse PromptCatalog.json: " + ex.Message); return; }

var sysPrompts = catalog["systemPrompts"] as JObject;
var templates = catalog["templates"] as JObject;

var decomposeSystem = sysPrompts?[("decompose_system")]?.ToString() ?? string.Empty;
var decomposeTemplate = templates?[("decompose_template")]?.ToString() ?? string.Empty;

if (string.IsNullOrWhiteSpace(decomposeSystem))
    Console.WriteLine("Resolved system prompt: <empty> (missing in PromptCatalog or env)\n");
else
    Console.WriteLine("Resolved system prompt preview:\n" + decomposeSystem.Substring(0, Math.Min(800, decomposeSystem.Length)) + "\n\n");

string userPrompt;
if (string.IsNullOrWhiteSpace(decomposeTemplate))
{
    Console.WriteLine("Warning: 'decompose_template' missing in PromptCatalog; using simple fallback.\n");
    userPrompt = decomposeSystem + "\n\nUSER REQUEST:\n" + userRequest + "\n\nReturn the decomposition JSON object now.";
}
else
{
    userPrompt = decomposeTemplate.Replace("{systemPrompt}", decomposeSystem + "\n\n").Replace("{userRequest}", userRequest ?? string.Empty);
}

Console.WriteLine("Built user prompt (preview):\n" + (userPrompt?.Length > 1200 ? userPrompt.Substring(0,1200) + "..." : userPrompt ?? "<empty>") + "\n\n");

var groqModel = Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "llama-3.3-70b-versatile";
var payload = new JObject
{
    ["model"] = groqModel,
    ["messages"] = new JArray {
        new JObject { ["role"] = "system", ["content"] = (decomposeSystem ?? string.Empty) },
        new JObject { ["role"] = "user", ["content"] = (userPrompt ?? string.Empty) }
    },
    ["temperature"] = 0.1,
    ["max_tokens"] = 4096,
    ["stream"] = false
};

Console.WriteLine("Simulated Groq payload (preview):\n" + payload.ToString(Newtonsoft.Json.Formatting.Indented).Substring(0, Math.Min(2000, payload.ToString().Length)));

Console.WriteLine("\nDone.");
