# Prototype Demonstration Guide

## Purpose of This Note
This document is prepared to make the prototype demonstration efficient for both the presenter and evaluators.
It summarizes the implemented functionality, the recommended live demonstration sequence, and the source locations for each subsystem so that any technical question can be traced quickly to the relevant code.

## Project Summary
This project is a rule-based AI-assisted CAD modeling system built with C# and the SolidWorks API.
Its workflow is:

1. Accept a natural language CAD request from the user.
2. Decompose the request into smaller feature-level tasks.
3. Convert those tasks into executable CAD steps.
4. Execute the steps inside SolidWorks.
5. Validate the result and record run history or feedback.

## Recommended Demonstration Flow

### 1. Natural Language Input Through the SolidWorks Taskpane
Demonstration:
- Open the taskpane.
- Enter a request such as `Create a 100 mm cube with 10 mm fillet on all edges`.
- Click `Build`.

Explanation:
- The system accepts a plain English request from the user instead of requiring direct manual feature creation.
- This is the entry point of the prototype from the user side.

Source reference:
- [UI/TextToCADTaskpaneWpf.xaml](UI/TextToCADTaskpaneWpf.xaml)
- [UI/TextToCADTaskpaneWpf.xaml.cs](UI/TextToCADTaskpaneWpf.xaml.cs)

What to explain from the code:
- `TextToCADTaskpaneWpf.xaml`: shows the user-facing taskpane controls such as prompt input, material, description, build button, history, and feedback.
- `TextToCADTaskpaneWpf.xaml.cs`: shows how the UI receives the prompt and triggers the build workflow.

### 2. Prompt Decomposition into Feature Tasks
Demonstration:
- Explain that the input is not executed as one monolithic instruction.
- Mention that the request is first broken into smaller feature-level tasks such as base geometry, cuts, and edge treatments.

Explanation:
- Decomposition improves reliability and makes the CAD generation pipeline easier to control and validate.
- It also allows complex requests to be handled in a structured sequence.

Source reference:
- [Services/StepDecomposer.cs](Services/StepDecomposer.cs)
- [Services/BuildOrchestrator.cs](Services/BuildOrchestrator.cs)

What to explain from the code:
- `StepDecomposer.cs`: shows the logic for splitting a complex prompt into smaller steps or sub-requests.
- `BuildOrchestrator.cs`: shows how decomposed tasks are passed into the next execution stages.

### 3. Build Orchestration and Sequential CAD Execution
Demonstration:
- Explain that after decomposition, the system creates an execution plan and runs each step in order.
- Show the orchestration and execution files if evaluators ask for the main control flow.

Explanation:
- The orchestration layer controls the overall pipeline.
- The execution layer runs the CAD steps sequentially inside SolidWorks using the registered operation handlers.

Source reference:
- [Services/BuildOrchestrator.cs](Services/BuildOrchestrator.cs)
- [Services/StepExecutor.cs](Services/StepExecutor.cs)

What to explain from the code:
- `BuildOrchestrator.cs`: shows the main control flow from input to planning to execution.
- `StepExecutor.cs`: shows that each CAD step is executed sequentially and sent to the correct operation handler.

### 4. Support for Multiple SolidWorks Feature Operations
Demonstration:
- Mention the currently supported operations:
  `extrude`, `extrude_cut`, `revolve`, `sweep`, `loft`, `fillet`, `chamfer`, `hole`, `pocket`, and `thread`.
- If needed, show that these operations are mapped through the registry and implemented in dedicated handlers.

Explanation:
- The prototype is not limited to one hard-coded model type.
- It supports multiple parametric feature operations through a modular handler architecture.

Source reference:
- [Services/Operations/OperationRegistry.cs](Services/Operations/OperationRegistry.cs)
- [Services/Operations/PartFeatures](Services/Operations/PartFeatures)

What to explain from the code:
- `OperationRegistry.cs`: shows the list of supported operations and how operation names are mapped to handlers.
- `PartFeatures` folder: contains the actual implementation of feature operations such as extrude, fillet, chamfer, hole, sweep, loft, revolve, and thread.

### 5. Sketching, Dimensioning, and Engineering Metadata
Demonstration:
- Mention sketch support such as rectangle, circle, line, arc, and dimensions.
- Show that the UI also includes material, description, and naming-related fields.

Explanation:
- The system supports both geometric creation and engineering context.
- This includes sketch-level preparation, dimensions, units, material assignment, and description metadata.

Source reference:
- [Services/Operations/Sketching](Services/Operations/Sketching)
- [Services/Operations/Utilities/UtilityHandlers.cs](Services/Operations/Utilities/UtilityHandlers.cs)
- [UI/TextToCADTaskpaneWpf.xaml](UI/TextToCADTaskpaneWpf.xaml)

What to explain from the code:
- `Sketching` folder: shows sketch entity creation and dimension-related logic before 3D feature generation.
- `UtilityHandlers.cs`: shows handling of units, material, description, and other engineering properties.
- `TextToCADTaskpaneWpf.xaml`: shows the UI fields where material, description, and related metadata are entered or displayed.

### 6. Validation, History, and Feedback Storage
Demonstration:
- Explain that the system checks whether expected geometry or metadata changes actually occurred.
- Show the history or feedback functionality if requested.
- Mention that the backend uses configured storage for run history and feedback.

Explanation:
- This improves robustness and supports later analysis of successful or failed generations.
- It also shows that the prototype includes more than front-end interaction; it includes traceability and storage.

Source reference:
- [Services/ExecutionValidator.cs](Services/ExecutionValidator.cs)
- [UI/HistoryBrowser.cs](UI/HistoryBrowser.cs)
- [UI/TextToCADTaskpaneWpf.xaml.cs](UI/TextToCADTaskpaneWpf.xaml.cs)
- [UI/SettingsWindow.xaml.cs](UI/SettingsWindow.xaml.cs)

What to explain from the code:
- `ExecutionValidator.cs`: shows how the system checks whether the expected result was actually produced after execution.
- `HistoryBrowser.cs`: shows how previous runs and executed steps can be reviewed.
- `TextToCADTaskpaneWpf.xaml.cs`: shows how feedback and run-related UI actions are connected.
- `SettingsWindow.xaml.cs`: shows where backend/database configuration is managed.

## High-Level Code Map
- UI entry point:
  [UI/TextToCADTaskpaneWpf.xaml.cs](UI/TextToCADTaskpaneWpf.xaml.cs)
  Explain: this is the main entry point from the SolidWorks taskpane into the prototype workflow.
- Prompt decomposition:
  [Services/StepDecomposer.cs](Services/StepDecomposer.cs)
  Explain: this file converts one complex request into smaller manageable task segments.
- Pipeline orchestration:
  [Services/BuildOrchestrator.cs](Services/BuildOrchestrator.cs)
  Explain: this file coordinates the major phases of the generation pipeline.
- Step execution:
  [Services/StepExecutor.cs](Services/StepExecutor.cs)
  Explain: this file executes the generated steps one after another inside SolidWorks.
- Operation routing:
  [Services/Operations/OperationRegistry.cs](Services/Operations/OperationRegistry.cs)
  Explain: this file connects operation names from the plan to concrete SolidWorks handlers.
- Feature handlers:
  [Services/Operations/PartFeatures](Services/Operations/PartFeatures)
  Explain: these files implement the actual CAD feature operations.
- Validation:
  [Services/ExecutionValidator.cs](Services/ExecutionValidator.cs)
  Explain: this file checks whether the generated model matches the intended operation result.
- Settings and backend configuration:
  [UI/SettingsWindow.xaml.cs](UI/SettingsWindow.xaml.cs)
  Explain: this file contains the configuration flow for providers, database, and environment settings.

## Short Technical Positioning
- Nature of system:
  Hybrid AI-assisted and rule-based CAD workflow.
- Role of AI:
  Interpret the user request and generate structured tasks or steps.
- Role of rules and handlers:
  Execute validated CAD operations through explicit SolidWorks API handlers.
- Role of backend:
  Store run history, feedback, and related execution traces.

## Suggested Closing Statement
The current prototype demonstrates a complete path from natural language input to SolidWorks feature execution, along with decomposition, validation, and storage support. The goal of this document is simply to keep the technical review efficient by making each demonstrated function directly traceable to its implementation.
