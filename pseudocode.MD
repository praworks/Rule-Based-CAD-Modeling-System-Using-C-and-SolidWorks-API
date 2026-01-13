Plan, dont excecute. Refactor this SOLIDWORKS add-in build pipeline to enforce the exact runtime order below. Keep behavior the same except where needed to match the order. Add structured logging and ensure provider fallback works.

TARGET RUNTIME ORDER (MUST MATCH):
1) User types prompt Ex. "Make M10x1.5 100mm Thread bar"
2) Taskpane UI: Build clicked
3) Wrapper forwards BuildRequested
4) SwAddin receives request → starts build
5) Load settings (provider priority, timeouts, few-shot enabled)
6) LLM Call #1: 
  Classify → "category" (e.g., Threadbar) 
  "decompose" base: Shaft, Feature1: thread. No Ops required at this step. Refer to Config\DecomposeTemplates.json
  "description" (ex. "M10x1.5 100mm Thread bar")
7) For each decomposed task in order (Task0: Shaft, Task1: Thread):
   7.1) LLM Call #2 Build feature prompt: 
        - For Task0: System Prompt Config\PromptTemplates.json + Ex. ShaftFewShot 
        StepExecutor executes steps in SolidWorks
   7.2) LLM Call #3 Build feature prompt:
        - For Task1: System Prompt Config\PromptTemplates.json + Ex. ThreadFewShot + ModelState(from Task0)
        StepExecutor executes steps in SolidWorks
8) Update model properties (material/description/etc.)
9) Completed / Error

NON-NEGOTIABLES:

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



