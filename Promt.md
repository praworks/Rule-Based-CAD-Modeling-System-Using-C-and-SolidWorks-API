You are working in a C# SOLIDWORKS Taskpane Add-in. Refactor the build pipeline to enforce the EXACT runtime order below. Keep behavior the same unless needed to match the order and to fix provider fallback / logging. Do not add new external dependencies.

================================================================================
TARGET RUNTIME ORDER (MUST MATCH)
================================================================================
1) User types prompt
2) Taskpane UI: Build clicked
3) Wrapper forwards BuildRequested
4) SwAddin receives request → starts build
5) Load settings (provider priority, timeouts, few-shot enabled)
6) LLM Call #1: Classify → Category + short description
7) LLM Call #2: Decompose → Feature Tasks (Task0 base, Task1 thread/chamfer/etc.)
8) For each feature task in order:
   8.1) Pick few-shot for this task (feature_type + intent aware)
   8.2) Build feature prompt
        - Task0: SystemPrompt + FewShot(Task0) + FeatureTask(Task0)
        - Task1+: SystemPrompt + FewShot(TaskN) + ModelState(after TaskN-1) + FeatureTask(TaskN)
   8.3) LLM Expand(TaskN) → steps
   8.4) StepExecutor executes steps in SolidWorks
   8.5) Update ModelState snapshot after execution
9) Update model properties (material/description/etc.)
10) Completed / Error

================================================================================
HARD GATING RULE (NON-NEGOTIABLE)
================================================================================
- Task1 (and any TaskN>0) must NOT expand or execute until Task0 has completed
  successfully in SolidWorks.
- “Task0 completed successfully” means:
  1) All Task0 steps executed with success=true
  2) SolidWorks model rebuild succeeded (ForceRebuild3 or equivalent success)
  3) ModelState snapshot AFTER Task0 confirms geometry exists:
     - bodies.count > 0 OR
     - total_faces > 0 OR
     - feature tree contains Boss-Extrude* (or expected base feature)
- If Task0 fails:
  - Abort immediately (do not call LLM for Task1)
  - Return success=false
  - Log failure context: failedTaskIndex, failedStepIndex, lastOp, ModelState

================================================================================
NON-NEGOTIABLE REQUIREMENTS
================================================================================
A) No extra decomposition calls after step (7).
   - Decompose must run exactly once per build run unless the user explicitly retries.
B) Separate LLM calls for classify, decompose, and each feature expansion.
C) ModelState for TaskN must be captured AFTER TaskN-1 steps finish executing.
D) Few-shot selection MUST be feature-aware:
   - key: (stage=expand, feature_type, intent)
   - example: (base, create_cylinder) → CylinderFewShot
   - example: (thread, create_thread) → ThreadFewShot
E) Provider fallback must occur for EACH LLM call:
   - Try groq → local → gemini
   - A provider “marked dead” must skip only that provider, not abort the call
   - Even if groq is dead, chamfer/thread expansion must still run using local/gemini
F) Normalize feature task params before expansion:
   - if params.type exists and params.op missing → set params.op=params.type
G) Structured logging with correlation IDs:
   - RunId (per build)
   - RequestId (per LLM call)
   - Include RunId/RequestId on every log line for LLM + execution paths

================================================================================
DELIVERABLES (MUST PROVIDE)
================================================================================
1) Proposed module structure (minimal churn, small classes):
   - BuildOrchestrator (single owner of runtime order)
   - ProviderRouter (fallback + dead tracking)
   - FewShotSelector (feature-aware selection)
   - PromptBuilder (classify/decompose/expand prompts)
   - ModelStateProvider (read SW model state snapshot)
   - StepExecutor (execute only; NO LLM calls inside)
2) Updated method signatures and call sites.
3) Code changes for key entry points:
   - TaskpaneWpf.BuildFromPromptAsync (entry)
   - Wrapper/SwAddin BuildRequested handler
   - LlmPlanService (classify/decompose/expand logic)
   - StepExecutor (execution only; remove any decomposition calls)
4) Example diagnostic log proving runtime order is correct:
   - includes RunId + RequestId per LLM call
   - proves Task1 expand starts only after Task0 execution + model state update
5) Output patch-style diffs per file.

================================================================================
IMPLEMENTATION NOTES
================================================================================
- Keep current JSON schemas and supported ops.
- Don’t change UI behavior.
- Use existing logging framework/style; only extend it with RunId/RequestId + fields.
- Do not introduce new dependencies.
- If any feature expansion fails, final result must be consistent:
  - either success=false, OR explicit partial failure flag (must be consistent and logged).

================================================================================
START NOW
================================================================================
(1) Identify current control flow and where it violates the target order.
(2) Refactor in small steps:
    - Create BuildOrchestrator and move control flow there
    - Add ProviderRouter fallback
    - Add FewShotSelector by feature_type+intent
    - Enforce Task0 → ModelState → Task1 gating
    - Add structured logging (RunId/RequestId)
(3) Provide code diff for each step.

Search for and fix these known bad patterns:
- Any code path that logs "Requesting LLM feature decomposition" during execution
- Any code path where StepExecutor triggers decomposition/LLM calls
- Any path where "marked dead" provider stops the pipeline instead of fallback