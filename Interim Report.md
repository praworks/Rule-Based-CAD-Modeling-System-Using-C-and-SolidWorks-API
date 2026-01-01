# Interim Report — Rule-Based CAD Modeling System

Date: 2026-01-01

## Project Overview

This project implements a rule-based CAD modeling system built on top of the SolidWorks API using C# and .NET. Its objective is to translate structured, natural-language or rule-based specifications into parametric SolidWorks models via an add-in that automates feature creation, assembly setup, and registration with the host CAD application.

## High-level Architecture

- Add-in core: a C# SolidWorks add-in that interfaces with SolidWorks COM APIs to create sketches, features, mates, and export artifacts. Primary entry points and registration logic are implemented in the add-in source.
- UI layer: WPF dialogs and tool windows providing user controls for rule input, parameter editing, and build/register workflows.
- Automation scripts: helper scripts and batch files to build, register, and debug the add-in on Windows (e.g., solution and batch build scripts).
- Services and tools: local services for data processing, prompt handling, and transformation of high-level rules into intermediate representations consumed by the add-in.
- Documentation and data: readme, ER diagrams, prompt presets, and example datasets that drive behavior and training examples.

## Key Components (files & folders)

- Solution: AI-CAD-December.sln — Visual Studio solution containing the add-in and supporting projects.
- Add-in code: SwAddin.cs — main add-in implementation responsible for lifecycle, registration/unregistration callbacks, and SolidWorks integration.
- Build scripts: BuildAndRegister.bat, Register_Addin_Debug.bat, Unregister_Addin_Debug.bat — used to compile and register the add-in for debugging and deployment.
- Project file: AI-CAD-December.csproj — project settings, NuGet references and build configuration.
- Scripts: folder `scripts/` containing PowerShell utilities and exporters used during development and data preparation.
- Data & docs: README.md, ER_Diagram.md, PromptPreset.json, nl2cad.db.jsonl — documentation and example inputs/outputs for rule translation.

## Data Flow and Processing

1. Input capture: rules are either provided via the UI or ingested from prompt/preset files. Rules are expressed as structured text or JSON-like commands.
2. Parsing & transformation: rules are parsed into an intermediate representation (IR) — a sequence of parametric operations (create sketch, extrude, cut, mate, set parameter).
3. Validation: IR is validated for dimensional consistency, constraints, and manufacturability checks.
4. Execution: the add-in issues SolidWorks API calls corresponding to IR operations (create sketches, apply features, set dimensions, create mates), tracking state to allow undo/redo and error recovery.
5. Output: resulting parts/assemblies are saved, and metadata (feature tree, parameters) are exported to logs or project artifacts.

## Build & Registration (Developer steps)

1. Open the solution `AI-CAD-December.sln` in Visual Studio (recommended: Windows). Ensure required NuGet packages restore.
2. Use MSBuild or the provided tasks to build in Debug configuration. Example command:

   msbuild AI-CAD-December.sln /t:Rebuild /p:Configuration=Debug /p:Platform=\"Any CPU\"

3. Register the add-in for SolidWorks using the build/register helper scripts. Run `BuildAndRegister.bat` or the register helper as Administrator when required.
4. For debugging, use `Register_Addin_Debug.bat` and attach Visual Studio to the SolidWorks process to step through add-in code.

## Testing & Validation

- Unit tests: project contains unit-style helpers for validating rule parsing and IR generation. These should be executed as part of a CI step where available.
- Integration testing: manual or scripted runs inside SolidWorks are used to validate feature creation, assembly behavior, and parameter propagation.
- Regression logs: build artifacts and aicad_log.txt capture runtime behavior and are used to triage feature regressions.

## Security & Data Hygiene

- Exclude compiled binaries, object files, and local build artifacts from distribution. The `bin/` and `obj/` folders contain platform-specific compiled outputs and should not be uploaded to external services.
- Remove or redact any credentials, API keys, or sensitive config before sharing project files.

## Known Limitations & Considerations

- Platform-specific: development and runtime require Windows and a compatible SolidWorks installation; COM registration is required for the add-in to load.
- Large datasets and binary assets: heavy model files or datasets should be provided separately or trimmed before ingestion by analysis tools.

## Next Steps

1. Complete end-to-end examples that map a textual rule to a saved assembly, and include expected parameter values.
2. Add automated integration tests that run the add-in in a headless or scripted SolidWorks environment (where licensing allows).
3. Produce a compact export (README + key source files) for external review or ingestion into analysis systems.

## Contact Points in the Codebase

- Entry/registration logic: SwAddin.cs
- Build automation: BuildAndRegister.bat, Clean.bat
- Prompts and datasets: PromptPreset.json, nl2cad.db.jsonl
- Logs: aicad_log.txt

---

This document summarizes the current technical state and the immediate engineering next steps. For further detail, specific code excerpts or walkthroughs can be provided on request.
