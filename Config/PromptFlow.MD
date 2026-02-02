========================
PROMPT CONTRACTS (MUST IMPLEMENT)
========================

DECOMPOSE output schema MUST be RAW JSON and MUST be one of:

Option 1 (preferred, structured):
{
  "description": "<one sentence intent OR empty string>",
  "needs_description": false,
  "question": "",
  "features": [
    {
      "feature_type": "extrude",
      "role": "base",
      "intent": "create cube/prismatic body",
      "depends_on": []
    },
    {
      "feature_type": "chamfer",
      "role": "dependent",
      "intent": "bevel edges",
      "depends_on": [0]
    }
  ]
}

If the request is unclear, DECOMPOSE must instead return:
{
  "description": "",
  "needs_description": true,
  "question": "<single concise question asking for a short description only>",
  "features": []
}

Rules for DECOMPOSE:
- DO NOT ask for dimensions, units, planes, or edge selection.
- DO NOT include "steps", "op", or "thinking".
- Keep feature list minimal and dependency-aware.
- Canonicalize feature_type strings to match execution routing:
  - cube → feature_type: "extrude" (role: base)
  - chamfer → feature_type: "chamfer" (role: dependent)

EXECUTE output schema MUST be RAW JSON and MUST be one of:

1) Clarification needed for the CURRENT feature only:
{
  "clarification_needed": true,
  "feature_index": <int>,
  "feature_type": "<string>",
  "questions": [
    "<question 1>",
    "<question 2>"
  ]
}

2) Executable plan for the CURRENT feature only (existing contract):
{
  "thinking": "<geometric reasoning>",
  "steps": [
    {"op":"...", "params":{...}}
  ]
}

Rules for EXECUTE:
- Must ONLY execute the provided current feature task.
- Must ONLY ask for inputs required for the current feature.
- Must NEVER ask for inputs for future features.
- Must NEVER re-decompose or output a new features list.
- Must NOT invent missing critical parameters; ask via clarification_needed.

========================
RUNTIME FLOW (MUST MATCH)
========================

- BuildOrchestrator.Run:
  1) Stage=DECOMPOSE → call DecomposeByFeature(userPrompt)
  2) If needs_description=true → return early with Success=false and surface question in BuildResult.Error (or a new field if existing UI supports it) WITHOUT executing.
  3) Else, iterate features[] in order:
     - Stage=EXECUTE (per feature)
     - Call PlanFeatureSubtask(featureTask, modelFacts, ...)
     - If returned JSON contains clarification_needed=true → return early with Success=false and surface questions to UI (store in StepExecutionResult.Clarification or BuildResult.Error) and DO NOT continue.
     - Else execute steps as today.

========================
ACCEPTANCE CHECKS
========================

1) “Make cube and apply chamfer”
   - Decompose returns 2 features: extrude(base), chamfer(dependent depends_on extrude)
2) No CLASSIFY log section and no classify prompt selection in traces.
3) DECOMPOSE never emits steps/op/thinking and never asks for dimensions.
4) EXECUTE asks for missing parameters per feature only, via clarification_needed.
5) Existing specialized execute system prompts (e.g., chamfer-specific) are still used via GetExecuteSystemPromptForFeatureType.

Implement changes with minimal disruption, update any parsing and validators accordingly, and keep outputs RAW JSON (no markdown).
