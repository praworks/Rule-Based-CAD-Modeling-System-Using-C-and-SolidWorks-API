# Rule‑Based CAD Modeling System — End‑to‑End Map

## 1) Birds‑eye Overview

**Goal:** Turn a plain‑English request (e.g., “Create a 150×150×10 plate with 4 corner holes”) into a valid, safe C# SolidWorks API routine that builds the part — then learn from each run.

**Top‑level stages:**

1. Capture request → 2) Recall similar cases → 3) Build prompt → 4) LLM generates C# → 5) Static checks + sandbox compile/run → 6) Show result + collect feedback → 7) Log everything → 8) Nightly improvement.

---

## 2) Component Map

* **Task‑Pane UI (SolidWorks add‑in, C# WinForms)**

  * Input box, “Generate”/“Undo”, live log, code preview, success/fail indicator, thumbs‑up/down.
* **Similarity Search (Vector DB, local)**

  * Stores embeddings of `{prompt, enriched prompt, code, status}`; returns top‑k exemplars.
* **Prompt Builder (NLP layer)**

  * Merges user text + examples + system rules (API schemas, safety constraints) into one structured prompt.
* **LLM Engine (cloud REST)**

  * Returns C# SolidWorks API code.
* **Security Gate + Sandbox**

  * Static validation of API calls (schema & overload checks), whitelist, compile in isolated process, run.
* **Local Fallback Macros**

  * Rule‑based templates for common parts when cloud is down.
* **Data Logging (Cloud NoSQL + embeddings updater)**

  * Writes events, metrics, feedback; refreshes vector index.

---

## 3) Happy‑Path Flow (Sequence)

1. **User** types request in Task‑Pane → clicks **Generate**.
2. **UI** sends minimal request to **Similarity Search** → get top‑k examples.
3. **Prompt Builder** assembles: user text + retrieved examples + instructions (API schemas, safety rules, unit conventions).
4. **LLM Engine** returns **C# snippet**.
5. **Security Gate** performs static checks:

   * API whitelist & overload arity (e.g., FeatureExtrusion2 23‑parameter pattern)
   * Forbidden namespaces (IO, network, registry, P/Invoke, etc.)
   * Unit sanity (m vs mm), null/selection checks, sketch pre‑conditions.
6. **Sandbox** compiles & runs against SolidWorks; captures stdout/stderr and feature results.
7. **UI** displays:

   * Model preview (part created/updated),
   * Code panel,
   * Structured log (steps, timings, any warnings),
   * Feedback controls (👍 / 👎, brief note).
8. **Logger** stores `{prompt → code → result}` + metrics; **Embeddings Updater** adds new pair to vector index.

---

## 4) Error/Fallback Flows

* **LLM offline / quota:** UI suggests **Local Fallback Macros** for common features (plate, boss, hole pattern, nut, bolt).
* **Static check fails:** Show precise rule violated (e.g., wrong overload count) → keep user in code preview with “Fix & Retry”.
* **Compile error:** Show compiler diagnostics; store sample for future few‑shot repair prompts.
* **SolidWorks run‑time error:** Abort safely, revert last feature, capture SW error code, preserve model state.

---

## 5) Data Contracts

**Request event**

* `id, timestamp, userId, swVersion, locale`
* `rawPrompt, enrichedPromptHash, retrievedExamples[]`

**Generation result**

* `codeHash, codeBlob`
* `staticChecks {passed, failures[]}`
* `compile {ok, ms, diagnostics[]}`
* `execute {ok, ms, swOps[], featureIds[], warnings[]}`

**Feedback & metrics**

* `userRating {up|down}`, `notes`
* `latency {promptBuild, llm, compile, run}`, `retries`

---

## 6) Security & Safety Gates

* **Static rules:** API whitelist, overload signatures, units in meters, no file/network/registry.
* **Sandboxing:** Isolated AppDomain/process, limited permissions, timeouts, cancellation tokens.
* **Execution scope:** Feature scope/merge flags checked; sketch/selection pre‑existence validated.
* **Privacy:** Remove proprietary names from logs; opt‑out toggle disables payload logging.

---

## 7) Continuous Improvement Loop (Nightly)

* Aggregate logs → compute: compile success %, run success %, average latency, top failures, top prompts.
* Auto‑tune **Prompt Templates** (e.g., enforce exact overloads, add unit guards, selection patterns).
* Refresh **Vector Index** with new successful pairs; prune stale/low‑quality examples.
* Produce dashboard: trend lines, Pareto of failure causes, suggested macro additions.

---

## 8) Minimal Deployment Diagram

* **Client workstation** (SolidWorks + add‑in): UI, Prompt Builder, Static Checks, Sandbox, Vector DB.
* **Cloud**: LLM endpoint (REST), NoSQL log store, nightly job (ETL + embedding refresh), dashboard.

---

## 9) MVP Scope (what to build first)

* Task‑Pane UI + logs
* Prompt builder with 3 example retrievals (k=3)
* 5 essential feature templates (plate, boss‑extrude, cut‑extrude, hole wizard pattern, revolve)
* Static validator covering: FeatureExtrusion2, FeatureCut3, sketch start/end, unit conversions
* Local vector store + cloud logging

---

## 10) KPIs

* **Accuracy:** % of requests producing the intended feature without manual edits
* **Time:** Median time to usable part vs manual modeling
* **Stability:** Crash‑free runs per 100 executions
* **UX:** CSAT from thumbs & quick survey

---

## 11) Text‑only Diagram (quick reference)

```
[User]
  |
  v
[Task‑Pane UI] --captures--> [Similarity Search]
  |                              |
  |                            (top‑k)
  v                              v
[Prompt Builder] ---> [LLM Engine (cloud)]
                          |
                       (C# code)
                          v
                 [Security Gate + Sandbox]
                          |
                     (run & logs)
                          v
                        [UI]
                          |
                       (feedback)
                          v
               [Logger + Embeddings Updater]
```

---

### Notes & Assumptions

* Length units are meters in API; UI can accept mm and convert.
* Overload enforcement is part of the static checker to avoid runtime dialog prompts.
* Undo uses SolidWorks feature rollback with labeled rollbacks per generation.
