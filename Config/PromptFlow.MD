You are Codex working in an existing C# SOLIDWORKS “AI-CAD” codebase. Your job is to refactor the pipeline and prompts from:

  CLASSIFY → DECOMPOSE → EXECUTE

to:

  DECOMPOSE → EXECUTE

You must modify both orchestration code AND the prompt catalog/contracts so runtime behavior matches the new 2-stage workflow.

========================
GOAL BEHAVIOR (REQUIRED)
========================

A) Remove CLASSIFY completely from the runtime flow.
   - No calls to LlmPlanService.ClassifyAndDescribe in the main build path.
   - No PromptStageRouter routes for CLASSIFY used by the build flow.
   - Result.Category may be left as "Unknown" or removed from BuildResult if safe, but do not break callers.

B) DECOMPOSE stage must do exactly TWO things:
   1) Ask for a short “description” ONLY if the request is unclear.
   2) Decompose into ordered features with dependencies.
   DECOMPOSE must NOT request feature parameters/dimensions/edge selections.
   DECOMPOSE must NOT emit executable ops/steps.

C) EXECUTE stage must be driven strictly by the decomposed feature list.
   - It runs per-feature, in order.
   - It requests/clarifies inputs for ONLY the current feature (not future ones).
   - It must never re-decompose or change the feature list.
   - It must not invent missing dimensions/edge selections; instead it must ask for them (structured).

D) Mandatory example rule:
   If user asks: “Make cube and apply chamfer”
   DECOMPOSE must produce features exactly:
     1) Base: Extrude
     2) Dependent: Chamfer (depends on Extrude)

========================
FILES TO ANALYZE/CHANGE
========================

1) Services/BuildOrchestrator.cs
   - Remove CLASSIFY block and stage metadata.
   - Start at DECOMPOSE using the user prompt.
   - Ensure logging stages are only DECOMPOSE and EXECUTE.
   - Keep BuildResult fields stable unless safe to change and all references updated.

2) Services/LlmPlanService.cs
   - Ensure GenerateWithPriority stage routing supports only DECOMPOSE and EXECUTE in the main flow.
   - Keep ClassifyAndDescribe if other code uses it, but the orchestrator must not call it.
   - Update DecomposeByFeature parsing if the schema changes (see “Prompt Contracts” below).

3) Services/PromptStageRouter (or wherever stage→(systemPromptKey, templateKey) is mapped)
   - Remove or stop using CLASSIFY mapping for the main pipeline.
   - Ensure DECOMPOSE maps to decompose_system + decompose_template.
   - Ensure EXECUTE maps to execute_system + execute_template.

4) Config/PromptCatalog.json (+ any prompt integrity validators)
   - Update DECOMPOSE contract to “Ask for description + Decompose” and forbid parameter-asking.
   - Update EXECUTE contract to “request inputs per decomposed feature” (structured) and forbid re-decomposition.

5) Services/PromptSelectionValidator (or equivalent)
   - Update invariants to reflect the new DECOMPOSE schema and the removal of CLASSIFY from the build pipeline.

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
