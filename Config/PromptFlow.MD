========================
PromptFlow.MD (COMPATIBLE WITH CURRENT C# EXECUTOR)
===================================================

This document defines the **exact prompt + JSON contracts** that the LLM must follow so that the existing C# pipeline can parse and execute plans without runtime errors.

This contract is designed to be compatible with:

* `BuildOrchestrator.Run` stage flow (DECOMPOSE → EXECUTE-per-feature)
* `LlmPlanService` JSON parsing (expects `features[]`, `needs_description`, etc.)
* `StepExecutor` and per-op handlers (expects `steps[]` and **top-level parameters**, not nested `params`)

---

1. DECOMPOSE STAGE CONTRACT

---

### DECOMPOSE Output (RAW JSON only)

DECOMPOSE must return **exactly ONE JSON object** in one of the following shapes.

#### A) Normal (features produced)

{
"description": "<one sentence intent OR empty string>",
"needs_description": false,
"question": null,
"features": [
{
"feature_type": "<canonical_feature_type>",
"params": { }
}
]
}

#### B) Unclear request (ask for short description only)

{
"description": "",
"needs_description": true,
"question": "<ONE concise question asking for a short description only>",
"features": []
}

### DECOMPOSE Rules (STRICT)

* Output **RAW JSON only** (no markdown, no prose).
* Do NOT output: `"steps"`, `"op"`, `"command"`, `"thinking"`.
* Do NOT ask for: dimensions, units, planes, sketch details, edge/face selection.
* `features` must be minimal and ordered. Put base geometry first, then dependent features.
* Each feature object MUST include:

  * `feature_type` (string)
  * `params` (object; may be empty `{}`)

Optional fields may be included (C# should ignore them), but keep minimal:

* `intent`, `role`, `depends_on` are allowed if you already support them.
* If included, they MUST NOT replace `feature_type` and `params`.

### Canonical Feature Types (recommended for routing)

Use ONLY these canonical strings wherever possible:

* extrude
* extrude_cut
* revolve
* sweep
* loft
* fillet
* chamfer
* hole
* thread
* pocket

### Canonicalization (MANDATORY)

If user says:

* cube / box / block  → use `feature_type: "extrude"` (params may include a hint like `{ "shape": "cube" }`)
* bevel / chamfer     → use `feature_type: "chamfer"`
* round / fillet      → use `feature_type: "fillet"`
  Never invent a new feature_type like `"cube"` unless you also have a matching execute prompt and routing for it.

---

2. EXECUTE STAGE CONTRACT

---

EXECUTE is called once per feature (current feature only). It must return RAW JSON only.

### EXECUTE Output must be ONE of the following:

#### A) Clarification needed (for CURRENT feature only)

{
"clarification_needed": true,
"feature_index": <int>,
"feature_type": "<string>",
"questions": [
"<question 1>",
"<question 2>"
]
}

Rules:

* Questions must ONLY ask for inputs required to execute THIS feature.
* Do NOT ask about future features.

#### B) Executable Plan (CURRENT feature only) — C# EXECUTOR COMPATIBLE

{
"steps": [
{ "op": "<allowed_op>", "<param1>": <value>, "<param2>": <value> }
]
}

CRITICAL: Step parameter contract (must match C# handlers)

* Each step MUST be a JSON object.
* Each step MUST contain `"op"`.
* Operation parameters MUST be **top-level keys on the step object**.
* DO NOT nest parameters inside `"params"`.
* DO NOT use `"command"`.

### Allowed Ops

The runtime supplies an allowed-ops list in the execute prompt. The LLM MUST use ONLY those ops.

### Minimum parameter expectations (common handlers)

The following are the typical required keys expected by existing handlers:

* select_plane:

  * requires `name` (string)
  * use SolidWorks plane names exactly (examples):

    * "Front Plane"
    * "Top Plane"
    * "Right Plane"

* rectangle_center:

  * requires numeric: `cx`, `cy`, `w`, `h`

* circle_center:

  * requires numeric: `cx`, `cy`, `r`

* extrude:

  * requires numeric: `depth`

* extrude_cut:

  * requires numeric: `depth`

If a required key is missing for a chosen op, EXECUTE must return `clarification_needed:true` instead of guessing.

### Robust primitive example (cube-like prism)

If feature is `extrude` and intent is a cube/prismatic body, a valid plan is:

{
"steps": [
{ "op": "new_part" },
{ "op": "select_plane", "name": "Front Plane" },
{ "op": "sketch_begin" },
{ "op": "rectangle_center", "cx": 0, "cy": 0, "w": 1, "h": 1 },
{ "op": "sketch_end" },
{ "op": "extrude", "depth": 1 },
{ "op": "zoom_to_fit" }
]
}

Notes:

* This format is compatible with a C# executor that reads handler parameters from top-level keys.
* No nested `"params"` object is used.

---

3. RUNTIME FLOW (MUST MATCH IMPLEMENTATION)

---

BuildOrchestrator.Run:

1. Stage = DECOMPOSE

   * call DecomposeByFeature(userPrompt)
2. If `needs_description == true`

   * return early with Success=false
   * surface `question` to the UI (BuildResult.Error or a dedicated field)
   * DO NOT execute
3. Else, iterate `features[]` in order:

   * Stage = EXECUTE (per feature)
   * call PlanFeatureSubtask(featureTask, modelFacts, ...)
   * If returned JSON contains `clarification_needed == true`

     * return early with Success=false
     * surface `questions` to UI (StepExecutionResult.Clarification or BuildResult.Error)
     * DO NOT continue to later features
   * Else

     * execute `steps[]` through StepExecutor
     * stop immediately on first failing step (existing behavior)

---

4. ACCEPTANCE CHECKS

---

1. “Make cube and apply chamfer”

* DECOMPOSE returns 2 features:

  * feature 0: extrude (base)
  * feature 1: chamfer (dependent)

Example:
{
"description": "Create a cube and bevel its edges",
"needs_description": false,
"question": null,
"features": [
{ "feature_type": "extrude", "params": { "shape": "cube" } },
{ "feature_type": "chamfer", "params": { } }
]
}

2. DECOMPOSE never emits steps/op/thinking and never asks for dimensions/units/planes/selection.

3. EXECUTE never emits nested `"params"` inside steps and never uses `"command"`.

4. If plane name / depth / geometry is required and missing, EXECUTE returns `clarification_needed:true` for the CURRENT feature only.

5. Feature prompt routing remains compatible:

* Specialized prompts like `execute_chamfer`, `execute_fillet`, etc. may be used when present.
* If a specialized prompt is missing, fallback must be to `execute_system` (not to unrelated generic prompts).

---

5. NON-NEGOTIABLE COMPATIBILITY RULE

---

If your C# StepExecutor/handlers read parameters from top-level keys (e.g., `step["name"]`, `step["depth"]`), then EXECUTE plans MUST NOT use nested `"params"`.

If nested `"params"` is present, SOLIDWORKS execution may fail with errors such as:

* "Missing plane name"
* missing extrusion depth
* missing sketch dimensions

Therefore: steps MUST be flat objects with top-level parameters.
