# Prompt Refinement Feature

## Overview
The AI-CAD system now includes an optional **Prompt Refinement** feature that automatically improves and clarifies user prompts before sending them to the main CAD generation AI.

## How It Works

### 1. User Input
Users enter a brief prompt like:
- `"box"`
- `"cyl r=20"`
- `"rectangular container"`

### 2. Refinement Process
If enabled, a dedicated LLM expands and clarifies the prompt:
- **Input**: `"box"`
- **Refined**: `"Create a rectangular box with width 50mm, height 50mm, and depth 100mm"`

### 3. CAD Generation
The refined prompt is then sent to the main CAD generation LLM to create the SolidWorks step plan.

## Configuration

### Settings Location
Navigate to **Settings** → **AI Provider Settings** → **Prompt Refinement (Optional)**

### Available Options
1. **Disabled (Default)** - Use raw user input without refinement
2. **💻 Local LM Studio** - Use your local LM Studio instance
3. **☁️ Google Gemini** - Use Google's Gemini API
4. **⚡ Groq** - Use Groq's fast inference API (recommended for speed)

### Environment Variable
- **Variable**: `PROMPT_REFINE_PROVIDER`
- **Values**: `disabled`, `local`, `gemini`, `groq`
- **Default**: `disabled`

## Refinement Behavior

The refinement LLM is instructed to:
- ✅ Add missing dimensions with reasonable defaults
- ✅ Always specify units (millimeters)
- ✅ Clarify shape types (box, cylinder, etc.)
- ✅ Fix grammar and spelling errors
- ✅ Expand abbreviations
- ✅ Keep output concise but complete

### Example Transformations

| Original Input | Refined Output |
|----------------|----------------|
| `box` | `Create a rectangular box with width 50mm, height 50mm, and depth 100mm` |
| `cyl r=20` | `Create a cylinder with radius 20mm and height 100mm` |
| `rect 30x40` | `Create a rectangular box with width 30mm, height 40mm, and depth 50mm` |

## Performance Considerations

- **Extra AI Call**: Refinement adds one additional LLM request before the main CAD generation
- **Recommended Providers**: 
  - ⚡ **Groq** - Fastest (< 1 second)
  - 💻 **Local LM** - Fast if you have good hardware
  - ☁️ **Gemini** - Moderate speed

## When to Use

### Enable Refinement When:
- Users provide very brief or ambiguous prompts
- You want consistent dimension specifications
- Working with non-technical users

### Keep Disabled When:
- Users already provide detailed prompts
- Speed is critical (every second counts)
- You want full control over exact input

## Status Console Output

When refinement is active, you'll see:
```
[Refine] Improving prompt using groq...
[Refine] Original: box
[Refine] Refined: Create a rectangular box with width 50mm, height 50mm, and depth 100mm
> Create a rectangular box with width 50mm, height 50mm, and depth 100mm
[Run:20250127123045123] ----- Build Start: 2025-01-27 12:30:45 -----
```

## Error Handling

If refinement fails:
- ⚠️ Error is logged to status console
- ✅ System automatically falls back to original user input
- ✅ Build continues normally

Example:
```
[Refine] Error: API connection failed, using original prompt
> box
```

## Implementation Details

### Code Location
- **Settings UI**: `UI/SettingsWindow.xaml` (lines 378-403)
- **Settings Logic**: `UI/SettingsWindow.xaml.cs`
  - `LoadApiButton_Click()` - Loads saved refinement provider
  - `SaveApiKeysButton_Click()` - Saves refinement provider choice
- **Refinement Logic**: `UI/TextToCADTaskpaneWpf.xaml.cs`
  - `RefinePromptAsync(string rawPrompt)` - Performs the refinement
  - `BuildFromPromptAsync()` - Integrates refinement into build flow

### Integration Flow
```
User Input → RefinePromptAsync() → Refined Prompt → GenerateWithFallbackAsync() → CAD Plan → Execute
```

## Testing

To test the feature:

1. **Enable in Settings**:
   - Open Settings
   - Go to "AI Provider Settings"
   - Set "Refinement Provider" to `Groq` (or your preferred provider)
   - Click "Save All AI Settings"

2. **Restart SolidWorks**

3. **Test with Simple Input**:
   - Enter: `box`
   - Press Enter or click Build
   - Check status console for refined output

4. **Verify Behavior**:
   - Status should show: `[Refine] Original: box`
   - Followed by: `[Refine] Refined: <detailed prompt>`

## Future Enhancements

Potential improvements:
- Custom refinement templates per shape type
- User-configurable default dimensions
- History of refinements for review
- A/B testing refined vs. raw prompts
- Learning from user edits to improve refinement

## Troubleshooting

### Issue: Refinement Not Working
- ✅ Check that `PROMPT_REFINE_PROVIDER` is not set to `disabled`
- ✅ Verify API keys are configured for chosen provider
- ✅ Restart SolidWorks after changing settings

### Issue: Slow Performance
- ⚡ Switch to Groq (fastest provider)
- 💻 Use Local LM if you have GPU acceleration
- ❌ Or disable refinement for maximum speed

### Issue: Poor Refinement Quality
- 🔧 Try a different provider (Gemini tends to be more detailed)
- 🔧 Check status console for actual refined output
- 🔧 Consider disabling if refinements are not helpful
