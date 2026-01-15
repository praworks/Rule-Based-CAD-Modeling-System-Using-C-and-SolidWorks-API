# Prompt Flow Overview

This flowchart tracks how prompts move from configuration files into LLM requests. The goal is to make it easy to trace which configuration entry ends up in a system/user message for each CAD pipeline stage (CLASSIFY, DECOMPOSE, EXECUTE) and clarification helpers.

## Flowchart

```mermaid
graph TD
  A[Config/PromptCatalog.json\n(system prompts + templates)] --> B[PromptCatalog.cs]
  B --> C[PromptHandler.cs]
  C --> D[Stage Orchestration\n(LlmPlanService, BuildOrchestrator, ClarificationService)]

  D --> Classify[CLASSIFY stage\n`BuildClassificationPrompt`, categories list]
  D --> Decompose[DECOMPOSE stage\n`BuildFeatureDecomposePrompt`]
  D --> Execute[EXECUTE stage\n`BuildFeaturePlanPrompt` / other plan prompts]
  D --> Clarify[Clarification helper\n`BuildRefinePrompt`, `BuildThreadSubtaskPrompt`, etc.]

  Classify --> E[LLM clients\nLocalHttp/Groq/Gemini]
  Decompose --> E
  Execute --> E
  Clarify --> E

  E --> F[LlmTraceLogger + Exchange window]
```

## How the flow works

1. **Configuration source (`Config/PromptCatalog.json`)**  
   Contains every system prompt key (`default`, `classify`, `decompose`, etc.) plus reusable template fragments. `PromptCatalog.cs` lazily loads this JSON so the rest of the app can ask for prompts by name.

2. **`PromptHandler.cs`**  
   Wraps the catalog and exposes stage-specific helpers:
   - `DEFAULT_SYSTEM_PROMPT` / `EXECUTE_SYSTEM_PROMPT` for feature execution (the long SOLIDWORKS planning prompt).
   - `CLASSIFY_SYSTEM_PROMPT` and `DEFAULT_DECOMPOSE_SYSTEM_PROMPT` for the CAF stages.
   - Template builders like `BuildClassificationPrompt`, `BuildFeatureDecomposePrompt`, `BuildFeaturePlanPrompt`, `BuildRefinePrompt`, etc., combine user text, template tokens, and any per-request context (facts, feature JSON, categories).

3. **Stage orchestration**  
   Layers such as `LlmPlanService`, `BuildOrchestrator`, and `ClarificationService` choose the appropriate helper based on the current pipeline stage (or clarification intent). These helpers generate:
   - A system message taken directly from the catalog (e.g., `PromptHandler.CLASSIFY_SYSTEM_PROMPT`)
   - A user message built from stage-specific templates or JSON payloads

4. **LLM clients**  
   The constructed system+user prompt pair is sent through one of the clients (`LocalHttpLlmClient`, `GroqLlmAdapter`, or `GeminiClient`). Each client adds the required request metadata and calls `LlmTraceLogger.LogSend` / `LogRecv`.

5. **Trace buffer / UI**  
   `LlmTraceLogger` now maintains an in-memory buffer that the Exchange window replays when opened and updates live, so the entire SEND/RECV conversation becomes visible with a WhatsApp-style layout when the dev flag is on.

## Reference files

- `Config/PromptCatalog.json`: single source for system prompts and prompt templates.
- `Config/PromptTemplates.json`: auxiliary templates used by `PromptHandler.BuildTemplatePrompt`.
- `Services/PromptCatalog.cs`: safely locates/loads the catalog.
- `Services/PromptHandler.cs`: exposes helpers and ties every stage to the catalog entries.
- `Services/LlmPlanService.cs` / `Services/BuildOrchestrator.cs` / `Services/ClarificationService.cs`: call the prompt helpers, choose stage-specific behavior, and relay prompts to clients.
- `Services/LocalHttpLlmClient.cs`, `Services/GroqLlmClient.cs`, `Services/GeminiClient.cs`: the actual LLM request senders that log via `Services/Logging/LlmTraceLogger.cs`.

Feel free to refer to this chart when adjusting prompts or introducing new stages.
