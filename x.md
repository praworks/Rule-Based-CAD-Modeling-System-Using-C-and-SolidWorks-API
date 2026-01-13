===============================================================================
AI-CAD DIAGNOSTIC LOG FORMAT (EXPECTED OUTPUT)
Purpose:
- Teach Copilot what to log for each build run.
- Make failures diagnosable: exact prompt, exact LLM request/response, timings,
  correlation IDs, SW state snapshots, and step-by-step execution results.
Log Rules:
- Every line starts with: <timestamp> <level> [RunId=...] [Component] message
- Every LLM call must include a unique RequestId (no reuse).
- Always log: prompts sent, raw LLM replies, parsed outputs, and validation.
- On error: include exception, stack trace, lastStepIndex, lastOp, SW state.

Start
===============================================================================

<TS> <LEVEL> [RunId=<RUN_ID>] [HEADER] BuildRunStart
<TS> <LEVEL> [RunId=<RUN_ID>] [HEADER] AppVersion=<...> Commit=<...> Machine=<...> User=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [HEADER] Settings ProviderPriority=<...> SampleMode=<...> PromptRefinement=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [HEADER] Timeouts DecomposeMs=<...> ExpandMs=<...> Retries=<...> AntiBurstWaitMs=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [HEADER] SolidWorks ActiveDoc=<...> Units=<...> Template=<...>

───────────────────────────────────────────────────────────────────────────────
PHASE 0 — UI / PIPELINE
───────────────────────────────────────────────────────────────────────────────
<TS> <LEVEL> [RunId=<RUN_ID>] [UI] Build clicked
<TS> <LEVEL> [RunId=<RUN_ID>] [Wrapper] BuildRequested forwarded
<TS> <LEVEL> [RunId=<RUN_ID>] [SwAddin] BuildRequested received; starting build
<TS> <LEVEL> [RunId=<RUN_ID>] [TaskpaneWpf] RunBuildFromPromptAsync invoked
<TS> <LEVEL> [RunId=<RUN_ID>] [TaskpaneWpf] BuildFromPromptAsync entered
<TS> <LEVEL> [RunId=<RUN_ID>] [UserPrompt] "<RAW_USER_PROMPT>"

───────────────────────────────────────────────────────────────────────────────
PHASE 1 — LLM CLASSIFY (OPTIONAL)
───────────────────────────────────────────────────────────────────────────────
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] RequestStart RequestId=<LLM_REQ_ID>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] Provider=<...> Model=<...> Endpoint=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] Prompt (SYSTEM):
<SYSTEM_PROMPT_TEXT>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] Prompt (USER):
<USER_PROMPT_TEXT>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] RequestMeta PromptTokens=<...> MaxTokens=<...> Temperature=<...>

<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] ResponseReceived RequestId=<LLM_REQ_ID> elapsedMs=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Classify] RawReply:
<RAW_LLM_REPLY_TEXT_OR_JSON>
<TS> <LEVEL> [RunId=<RUN_ID>] [Classify] Parsed Category="<CATEGORY>" Confidence=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [Classify] Validation ok=<true|false> reason="<...>"

───────────────────────────────────────────────────────────────────────────────
PHASE 2 — LLM DECOMPOSE → FEATURE TASKS
───────────────────────────────────────────────────────────────────────────────
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] RequestStart RequestId=<LLM_REQ_ID>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] Provider=<...> Model=<...> Endpoint=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] Prompt (SYSTEM):
<SYSTEM_PROMPT_TEXT>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] Prompt (USER):
<USER_PROMPT_TEXT_WITH_CATEGORY_IF_ANY>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] RequestMeta PromptTokens=<...> MaxTokens=<...> Temperature=<...>

<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] ResponseReceived RequestId=<LLM_REQ_ID> elapsedMs=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Decompose] RawReply:
<RAW_LLM_REPLY_JSON>
<TS> <LEVEL> [RunId=<RUN_ID>] [Decompose] Parsed FeatureTasks count=<N>
<TS> <LEVEL> [RunId=<RUN_ID>] [Decompose] Task[0]=<JSON>
<TS> <LEVEL> [RunId=<RUN_ID>] [Decompose] Task[1]=<JSON>
<TS> <LEVEL> [RunId=<RUN_ID>] [Decompose] Validation ok=<true|false> reason="<...>"
<TS> <LEVEL> [RunId=<RUN_ID>] [Decompose] Normalization applied=<true|false> changes="<type→op, defaults, units>"

───────────────────────────────────────────────────────────────────────────────
PHASE 3 — EXECUTION (PER FEATURE TASK)
───────────────────────────────────────────────────────────────────────────────
<TS> <LEVEL> [RunId=<RUN_ID>] [STATUS] Executing SolidWorks operation plan…
<TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] TopLevelResolved tasks=<N> placeholderSteps=<N>

FOR EACH FEATURE TASK:
  ────────────────────────────────────────────────────────────────────────────
  FEATURE <k> — <feature_type>
  ────────────────────────────────────────────────────────────────────────────
  <TS> <LEVEL> [RunId=<RUN_ID>] [Feature] Start indexOffset=<GLOBAL_STEP_INDEX> task=<JSON>

  (A) LLM EXPAND FEATURE → OP STEPS
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] RequestStart RequestId=<LLM_REQ_ID> feature=<feature_type>
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] Provider=<...> Model=<...> TimeoutMs=<...>
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] Prompt (SYSTEM):
  <SYSTEM_PROMPT_TEXT>
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] Prompt (CONTEXT):
  <CURRENT_MODEL_STATE_SUMMARY_JSON>   // keep short; full state only at DEBUG
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] Prompt (FEATURE TASK):
  <FEATURE_TASK_JSON>

  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] ResponseReceived RequestId=<LLM_REQ_ID> elapsedMs=<...>
  <TS> <LEVEL> [RunId=<RUN_ID>] [LLM/Expand] RawReply:
  <RAW_LLM_REPLY_JSON>
  <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] FeatureThinking="<thinking>"
  <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] FeatureSteps count=<M>
  <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] FeatureStep[0]=<JSON>
  ...
  <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] Validation ok=<true|false> reason="<...>"

  (B) EXECUTE OP STEPS IN SOLIDWORKS
  FOR j IN 0..M-1:
    <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] StepStart globalIndex=<G> op=<op> params=<JSON>
    <TS> <LEVEL> [RunId=<RUN_ID>] [StepExecutor] StepEnd globalIndex=<G> op=<op> success=<true|false> elapsedMs=<...>
    <TS> <LEVEL> [RunId=<RUN_ID>] [SW] Result selection=<...> createdFeature=<...> createdEntity=<...> warnings=<...>

  <TS> <LEVEL> [RunId=<RUN_ID>] [Feature] End feature=<feature_type> stepsExecuted=<M> elapsedMs=<...>

───────────────────────────────────────────────────────────────────────────────
PHASE 4 — RUN SUMMARY
───────────────────────────────────────────────────────────────────────────────
<TS> <LEVEL> [RunId=<RUN_ID>] [STATUS] Completed success=<true|false>
<TS> <LEVEL> [RunId=<RUN_ID>] [Timing] TotalMs=<...> ClassifyMs=<...> DecomposeMs=<...> ExpandMs=<...> SolidWorksMs=<...>
<TS> <LEVEL> [RunId=<RUN_ID>] [Artifacts] SavedPlanPath=<...> TempDir=<...> FeedbackLogged=<true|false>

ON ERROR (MUST INCLUDE):
<TS> <ERROR> [RunId=<RUN_ID>] [ERROR] Stage=<Classify|Decompose|Expand|Execute> feature=<...> stepIndex=<...> op=<...>
<TS> <ERROR> [RunId=<RUN_ID>] [ERROR] Message="<exception message>"
<TS> <ERROR> [RunId=<RUN_ID>] [ERROR] StackTrace:
<stack trace>
<TS> <ERROR> [RunId=<RUN_ID>] [ERROR] LastKnownModelState (DEBUG):
<FULL_MODEL_STATE_JSON>
===============================================================================


===============================================================================
EXAMPLE: DIAGNOSTIC LOG FOR "Make M10x1.5 100mm Thread bar"
===============================================================================
2026-01-13 10:12:41.427 [INFO] [RunId=R20260113_101241_001] [HEADER] BuildRunStart
2026-01-13 10:12:41.427 [INFO] [RunId=R20260113_101241_001] [HEADER] AppVersion=1.3.0 Commit=abc123
2026-01-13 10:12:41.428 [INFO] [RunId=R20260113_101241_001] [HEADER] Settings ProviderPriority=groq,local,gemini SampleMode=one PromptRefinement=disabled
2026-01-13 10:12:41.428 [INFO] [RunId=R20260113_101241_001] [HEADER] Timeouts DecomposeMs=15000 ExpandMs=120000 Retries=1 AntiBurstWaitMs=1000
2026-01-13 10:12:41.429 [INFO] [RunId=R20260113_101241_001] [HEADER] SolidWorks ActiveDoc="(none)" Units="MMGS" Template="Part"

───────────────────────────────────────────────────────────────────────────────
PHASE 0 — UI / PIPELINE
───────────────────────────────────────────────────────────────────────────────
2026-01-13 10:12:41.430 [INFO] [RunId=R20260113_101241_001] [UI] Build clicked
2026-01-13 10:12:41.430 [INFO] [RunId=R20260113_101241_001] [Wrapper] BuildRequested forwarded
2026-01-13 10:12:41.431 [INFO] [RunId=R20260113_101241_001] [SwAddin] BuildRequested received; starting build
2026-01-13 10:12:41.431 [INFO] [RunId=R20260113_101241_001] [TaskpaneWpf] BuildFromPromptAsync entered
2026-01-13 10:12:41.431 [INFO] [RunId=R20260113_101241_001] [UserPrompt] "Make M10x1.5 100mm Thread bar"

───────────────────────────────────────────────────────────────────────────────
PHASE 1 — CLASSIFY
───────────────────────────────────────────────────────────────────────────────
2026-01-13 10:12:41.450 [INFO] [RunId=R20260113_101241_001] [LLM/Classify] RequestStart RequestId=LLM-C-72e0f2
2026-01-13 10:12:41.450 [DEBUG] [RunId=R20260113_101241_001] [LLM/Classify] Prompt (SYSTEM):
You are a CAD classifier. Output JSON: {"category": "...", "confidence": 0..1}
2026-01-13 10:12:41.450 [DEBUG] [RunId=R20260113_101241_001] [LLM/Classify] Prompt (USER):
Make M10x1.5 100mm Thread bar
2026-01-13 10:12:42.110 [INFO] [RunId=R20260113_101241_001] [LLM/Classify] ResponseReceived RequestId=LLM-C-72e0f2 elapsedMs=660
2026-01-13 10:12:42.110 [DEBUG] [RunId=R20260113_101241_001] [LLM/Classify] RawReply:
{"category":"Threadbar","confidence":0.92}
2026-01-13 10:12:42.110 [INFO] [RunId=R20260113_101241_001] [Classify] Category="Threadbar" Confidence=0.92

───────────────────────────────────────────────────────────────────────────────
PHASE 2 — DECOMPOSE
───────────────────────────────────────────────────────────────────────────────
2026-01-13 10:12:42.120 [INFO] [RunId=R20260113_101241_001] [LLM/Decompose] RequestStart RequestId=LLM-D-a9c55a
2026-01-13 10:12:42.120 [DEBUG] [RunId=R20260113_101241_001] [LLM/Decompose] Prompt (SYSTEM):
You are a CAD planning agent. Output ONLY JSON ARRAY of feature tasks...
2026-01-13 10:12:42.120 [DEBUG] [RunId=R20260113_101241_001] [LLM/Decompose] Prompt (USER):
Category=Threadbar. Make M10x1.5 100mm Thread bar
2026-01-13 10:12:42.760 [INFO] [RunId=R20260113_101241_001] [LLM/Decompose] ResponseReceived RequestId=LLM-D-a9c55a elapsedMs=640
2026-01-13 10:12:42.760 [DEBUG] [RunId=R20260113_101241_001] [LLM/Decompose] RawReply:
[
  {"feature_type":"base","intent":"create_cylinder","params":{"diameter":10,"depth":100}},
  {"feature_type":"thread","intent":"create_thread","params":{"diameter":10,"pitch":1.5,"length":100,"handedness":"right","type":"metric"}}
]
2026-01-13 10:12:42.761 [INFO] [RunId=R20260113_101241_001] [Decompose] FeatureTasks count=2
2026-01-13 10:12:42.761 [INFO] [RunId=R20260113_101241_001] [Decompose] Validation ok=true

───────────────────────────────────────────────────────────────────────────────
PHASE 3 — EXECUTE
───────────────────────────────────────────────────────────────────────────────
2026-01-13 10:12:42.770 [INFO] [RunId=R20260113_101241_001] [STATUS] Executing SolidWorks operation plan…
2026-01-13 10:12:42.771 [INFO] [RunId=R20260113_101241_001] [StepExecutor] TopLevelResolved tasks=2 placeholderSteps=2

FEATURE 0 — base
2026-01-13 10:12:42.780 [INFO] [RunId=R20260113_101241_001] [LLM/Expand] RequestStart RequestId=LLM-E-BASE-1b18c0 feature=base
2026-01-13 10:12:42.780 [DEBUG] [RunId=R20260113_101241_001] [LLM/Expand] Prompt (FEATURE TASK):
{"feature_type":"base","intent":"create_cylinder","params":{"diameter":10,"depth":100}}
2026-01-13 10:12:43.100 [INFO] [RunId=R20260113_101241_001] [LLM/Expand] ResponseReceived RequestId=LLM-E-BASE-1b18c0 elapsedMs=320
2026-01-13 10:12:43.100 [DEBUG] [RunId=R20260113_101241_001] [LLM/Expand] RawReply:
{"thinking":"Create a base cylindrical shaft...","steps":[{"op":"new_part"}, ... ]}
2026-01-13 10:12:43.100 [INFO] [RunId=R20260113_101241_001] [StepExecutor] FeatureSteps count=7

2026-01-13 10:12:43.105 [INFO] [RunId=R20260113_101241_001] [StepExecutor] StepStart globalIndex=0 op=new_part params={}
2026-01-13 10:12:43.190 [INFO] [RunId=R20260113_101241_001] [StepExecutor] StepEnd globalIndex=0 op=new_part success=true elapsedMs=85

... (steps 1..6) ...

FEATURE 1 — thread
2026-01-13 10:12:43.750 [INFO] [RunId=R20260113_101241_001] [LLM/Expand] RequestStart RequestId=LLM-E-THREAD-92f0c4 feature=thread
2026-01-13 10:12:43.940 [INFO] [RunId=R20260113_101241_001] [LLM/Expand] ResponseReceived RequestId=LLM-E-THREAD-92f0c4 elapsedMs=190
2026-01-13 10:12:44.011 [INFO] [RunId=R20260113_101241_001] [StepExecutor] StepStart globalIndex=8 op=thread params={diameter:10,pitch:1.5,length:100,type:metric,handedness:right}
2026-01-13 10:12:44.280 [INFO] [RunId=R20260113_101241_001] [StepExecutor] StepEnd globalIndex=8 op=thread success=true elapsedMs=269

2026-01-13 10:12:44.290 [INFO] [RunId=R20260113_101241_001] [STATUS] Completed success=true
2026-01-13 10:12:44.290 [INFO] [RunId=R20260113_101241_001] [Timing] TotalMs=2863 ClassifyMs=660 DecomposeMs=640 ExpandMs=510 SolidWorksMs=1053
===============================================================================
END


RUNTIME ORDER (what actually happens)

User types prompt
  ↓
Taskpane UI: Build clicked
  ↓
Wrapper forwards BuildRequested
  ↓
SwAddin receives request → starts build
  ↓
Load settings (provider priority, timeouts, few-shot enabled)
  ↓
LLM Call #1: Classify → Category (e.g., Threadbar)
  ↓
LLM Call #2: Decompose → Feature Tasks
   ├─ Task 0: base (create_cylinder)
   └─ Task 1: thread (create_thread)
  ↓
────────────────────────────────────────────────────────────
Task 0 (BASE) runtime sequence
────────────────────────────────────────────────────────────
Pick few-shot for Task 0 (CylinderFewShot)
  ↓
Build create_cylinder prompt = System Prompt + CylinderFewShot + Task 0
  ↓
LLM Call #3: Expand Task 0 → SolidWorks ops (steps)
  ↓
StepExecutor runs Task 0 steps in SolidWorks
  ↓
SolidWorks updates part (base cylinder created)
  ↓
Model State updates (now includes the base feature)
  ↓
────────────────────────────────────────────────────────────
Task 1 (THREAD) runtime sequence
────────────────────────────────────────────────────────────
Pick few-shot for Task 1 (ThreadFewShot)
  ↓
Build create_thread prompt = System Prompt + ThreadFewShot + Task 0 Model State + Task 1
  ↓
LLM Call #4: Expand Task 1 → SolidWorks ops (steps)
  ↓
StepExecutor runs Task 1 steps in SolidWorks
  ↓
SolidWorks updates part (thread created)
  ↓
────────────────────────────────────────────────────────────
Update Model Properties

Completed / Error


Refactor this SOLIDWORKS add-in build pipeline to enforce the exact runtime order below. Keep behavior the same except where needed to match the order. Add structured logging and ensure provider fallback works.

TARGET RUNTIME ORDER (MUST MATCH):
1) User types prompt Ex. "Make M10x1.5 100mm Thread bar"
2) Taskpane UI: Build clicked
3) Wrapper forwards BuildRequested
4) SwAddin receives request → starts build
5) Load settings (provider priority, timeouts, few-shot enabled)
6) LLM Call #1: Classify → Category
7) LLM Call #2: Decompose → Feature Tasks [Task0 base, Task1 thread]
8) For each feature task in order:
   8.1) Pick few-shot for this task (Ex. CylinderFewShot for create_cylinder, Ex. ThreadFewShot for create_thread)
   8.2) Build feature prompt:
        - For Task0: System Prompt + Ex. CylinderFewShot + Task0
        - For Task1: System Prompt + Ex. ThreadFewShot + ModelState(from Task0) + Task1
   8.3) LLM Expand → steps
   8.4) StepExecutor executes steps in SolidWorks
   8.5) Update ModelState snapshot after execution
9) Update model properties (material/description/etc.)
10) Completed / Error

NON-NEGOTIABLES:
- Separate LLM calls for classify, decompose, and each feature expansion.
- ModelState for Task1 must be taken AFTER Task0 is executed.
- Few-shot selection must be feature-aware (feature_type + intent).
- Always try provider fallback (groq → local → gemini) for EACH LLM call; “marked dead” must not prevent trying others.
- Normalize task params before expansion (if params.type exists, map to params.op).
- Add correlation IDs: RunId for build + RequestId per LLM call; include in logs.

DELIVERABLES:
- Show proposed class/module structure (e.g., BuildOrchestrator, FewShotSelector, PromptBuilder, ProviderRouter, ModelStateProvider).
- Provide updated method signatures and where they are called.
- Provide code changes for the main entry points (TaskpaneWpf.BuildFromPromptAsync, SwAddin.BuildRequested handler, LlmPlanService, StepExecutor).
- Include an example log proving the runtime order above (with RunId/RequestId).

Start by identifying current control flow, then implement the refactor in small steps with minimal churn.
