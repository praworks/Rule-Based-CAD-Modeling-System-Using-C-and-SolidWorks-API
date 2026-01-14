# Logging Guidelines

## Schema (logs and telemetry)
- Required fields: `correlationId`, `sessionId`, `documentId`, `operation`, `provider` (when applicable), `durationMs` (for timed ops), `result` (success|failure|retry|fallback), `errorCategory`, `userVisible` (bool for surfaced errors).
- Scope first: use `LoggingContext` + `logger.BeginScope(context.ToScopeDictionary())` to auto-attach fields.
- Telemetry events follow `{eventType:start|end|fail, correlationId, sessionId, documentId, operation, provider, durationMs, result, errorCategory, retry, fallback, timestampUtc}`.

## Level rules
- `Debug`: detailed prompts/results (after redaction) and branch decisions.
- `Information`: start/end of major operations, provider selection, gating decisions.
- `Warning`: recoverable retries/fallbacks, validation soft-failures.
- `Error`: failed operations, classified exceptions, user-visible failures.
- `Critical`: catastrophic/unrecoverable state.

## Safety and redaction
- Run all free-text through `LogRedactor.Sanitize`; large prompts are hashed with head + SHA-256 suffix.
- Do not log API keys, bearer tokens, full prompts, or large model outputs; use `promptHash`/length instead.
- Logging/telemetry must never throw; providers are best-effort.

## Correlation and scopes
- Create `LoggingContext` at the orchestrator boundary (one `correlationId` per request).
- Include session/document identifiers (SolidWorks active doc title/path) when available.
- Use nested scopes for sub-operations (`context.CloneForChild("Decompose")`, etc.).
- Propagate correlationId to outbound API calls and telemetry.

## Events and timing
- Use `OperationLogger.Start` to emit `Start/End/Fail` with timing.
- Major steps to instrument: classify, decompose, feature expand/execute, SolidWorks execution, LLM calls, data API calls.

## Exceptions
- Always classify via `ExceptionClassifier`; log `errorCategory`, `exceptionType`, `isTransient` (when known).
- Include a separate `userMessage` from `FriendlyErrorTranslator`; never log sensitive content.

## Sampling and limits
- Keep file sinks bounded (size/retention) and avoid synchronous IO on hot paths.
- Throttle noisy events (e.g., repeated provider failures) at `Warning` level; emit summary at `Error` when crossing thresholds.
