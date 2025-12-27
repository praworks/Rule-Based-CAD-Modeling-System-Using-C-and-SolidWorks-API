# NameEasy Features Implementation Summary

## Overview
All core NameEasy features have been successfully integrated into the AI-CAD-December project. This document outlines the implemented features and their locations.

## Implemented Features

### 1. Service Layer (Services/)
- **SeriesManager.cs** - SQLite-based series/sequence management with history tracking
  - Database path from registry: `HKCU\Software\AI-CAD\NameEasy\DatabasePath`
  - Seed data: ASM, FAB, MCH, SHT, PUR, HRD series
  - Operations: GetAllSeries, GetNextSequence, CommitSequence, GetHistory, AddSeries

- **SettingsManager.cs** - Registry-based configuration
  - Database path storage and retrieval
  - Auto-create directory structure

- **AddinLogger.cs** - File-based logging
  - Logs to: `%LOCALAPPDATA%\AI-CAD\Logs\addin-{date}.log`

- **Win32SaveAsAutofill.cs** - Windows API automation for Save As dialog
  - FindWindow, SetWindowText for auto-filling file names

### 2. Event Handling (SwAddin.cs)
- **Document Events**
  - FileNewNotify2: Tracks new unsaved parts
  - ActiveDocChangeNotify: Rehooks events, syncs UI on document switch
  - CommandCloseNotify: Detects Save As (command 548 for properties dialog)

- **Part-Level Events**
  - RegenPostNotify: Syncs UI after rebuild/regeneration
  - HookDocRegenForActiveDocument: Attaches regen handler to active part
  - UnhookDocRegen: Cleanup on document change

### 3. Property Management (SwAddin.cs)
- **GetPartMass()** - Retrieves mass with SW-Mass linked property resolution
  - Uses Get4() with resolved parameter
  - Parses numeric value from resolved string

- **GetCustomProperty()** - Generic property getter
  - Returns resolved value if available

- **SetPartProperties()** - Comprehensive property setter
  - Sets Material, Description, Mass (linked to SW-Mass), PartNo
  - Applies material to model via SetMaterialPropertyName2
  - Uses swCustomPropertyDeleteAndAdd for clean updates

- **SyncUiFromActiveDocument()** - Reads properties and updates UI
  - Calls GetPartMass, GetCustomProperty
  - Updates taskpane via LoadFromProperties

### 4. Naming UI (UI/TextToCADTaskpaneWpf.xaml.cs)
- **Series Selection**
  - Dropdown populated from SeriesManager.GetAllSeries()
  - Sequence preview updates on series/material/description changes

- **Save With Name Workflow**
  - SaveFileDialog with prefilled part name
  - SaveAs4 for reliable file saving
  - SetPartPropertiesOnDocument: Material application + SW-Mass linking
  - CommitSequence: Records series/sequence/name/path
  - ForceRebuild3: Applies material to model

- **Apply Properties**
  - Sets properties without saving file
  - Material application to active model
  - SW-Mass linking for automatic mass calculation

- **Add Series**
  - Dialog for creating custom series (ID, description, format)
  - Validates series ID uniqueness
  - Adds to database and refreshes UI

- **LoadFromProperties()** - Populates UI from document
  - Material dropdown selection or text entry
  - Description, mass, PartNo fields

### 5. Settings Dialog (UI/NameEasySettingsDialog.cs)
- **Database Path Configuration**
  - Folder browser for selecting database location
  - Creates directory if not exists
  - Saves to registry via SettingsManager
  - Prompts for SolidWorks restart via ExitApp()

### 6. Material Database Integration
- **Material Application**
  - Uses "solidworks materials.sldmat" database
  - PartDoc.SetMaterialPropertyName2("", database, material)
  - Applied on Save With Name and Apply Properties

### 7. Mass Property Linking
- **SW-Mass Linking**
  - Mass property set as: `"SW-Mass@{filename}.SLDPRT"`
  - Automatically calculates from model geometry
  - Resolved value retrieved via Get4 with resolved parameter

## Key Workflows

### New Part Workflow
1. User creates new part in SolidWorks
2. FileNewNotify2 fires → sets _isNewUnsavedPart flag
3. User opens naming taskpane
4. User selects series → sequence auto-increments
5. User enters material, description → preview updates
6. User clicks "Save With Name"
7. SaveFileDialog shows with prefilled name
8. User confirms path
9. SaveAs4 saves file
10. SetPartPropertiesOnDocument applies material + links SW-Mass
11. CommitSequence records to history
12. ForceRebuild3 applies material to model

### Existing Part Workflow
1. User opens existing part
2. ActiveDocChangeNotify fires → SyncUiFromActiveDocument
3. GetCustomProperty reads Material, Description, PartNo
4. GetPartMass retrieves resolved SW-Mass value
5. LoadFromProperties populates taskpane UI
6. User modifies properties
7. User clicks "Apply Properties"
8. SetPartPropertiesOnDocument updates custom properties
9. ForceRebuild3 updates model

### Settings Workflow
1. User clicks Settings button
2. NameEasySettingsDialog shows current database path
3. User browses to new folder
4. Dialog validates and saves path to registry
5. Prompt asks to restart SolidWorks
6. If Yes, _swApp.ExitApp() closes SolidWorks

## Database Schema
```sql
CREATE TABLE Series (
    SeriesId TEXT PRIMARY KEY,
    Description TEXT NOT NULL,
    SequenceFormat TEXT NOT NULL,
    NextSequence INTEGER NOT NULL
);

CREATE TABLE History (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SeriesId TEXT NOT NULL,
    Sequence INTEGER NOT NULL,
    PartName TEXT NOT NULL,
    FilePath TEXT,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY(SeriesId) REFERENCES Series(SeriesId)
);
```

## Registry Configuration
- **Base Key**: `HKCU\Software\AI-CAD\NameEasy`
- **DatabasePath**: Full path to series.db file
- **Default**: `%LOCALAPPDATA%\AI-CAD\NameEasy\series.db`

## Material List
Predefined materials in naming UI:
- Steel, Mild
- Aluminum 1060 Alloy
- Stainless Steel
- Brass
- Copper
- Bronze
- Titanium
- ABS PC
- Polycarbonate
- Polypropylene
- PVC Rigid
- Nylon 6/10
- Custom (user-editable)

## Testing Checklist
- [x] Build compiles without errors
- [ ] SeriesManager creates database on first run
- [ ] Settings dialog updates registry
- [ ] New part shows default series
- [ ] Save With Name creates file with correct properties
- [ ] Material applies to model visually
- [ ] Mass property links to SW-Mass
- [ ] Regen updates mass value in taskpane
- [ ] Apply Properties updates existing part
- [ ] Add Series creates new series in database
- [ ] History tracks all saved parts
- [ ] Active document change syncs UI
- [ ] Properties dialog close triggers sync

## Known Limitations
- _pendingPartName field declared but not used (CS0414 warning)
- MSB3539 warning about BaseIntermediateOutputPath (harmless)
- Material database must be "solidworks materials.sldmat" (hardcoded)
- Mass linking assumes .SLDPRT extension

## Future Enhancements
- Assembly naming support (.SLDASM)
- Drawing naming support (.SLDDRW)
- Custom material database selection
- Batch renaming of parts
- Export history to CSV
- Undo last sequence commit
- Series templates/presets
