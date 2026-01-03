/// <summary>
/// Enhanced Step-by-Step Execution Guide
/// 
/// PROBLEM: 
/// User says "Create cube with 1mm chamfer" 
/// → AI generates plan with 2 steps (extrude + chamfer)
/// → Step 1 (extrude) succeeds ✓
/// → Step 2 (chamfer) fails ✗ 
/// → Currently: Entire build fails, user loses the cube
///
/// SOLUTION: 
/// Execute each step independently, report success/failure individually,
/// continue creating parts even if some features fail.
/// </summary>

/*
 * IMPLEMENTATION STEPS:
 * 
 * 1. DECOMPOSE COMPLEX PROMPTS
 *    - Input: "Create cube with 1mm chamfer at edges"
 *    - Decompose into:
 *      Step 1: "Create cube 50x50x50mm on Top Plane centered at origin"
 *      Step 2: "Add 1mm chamfer to all external edges"
 * 
 * 2. EXECUTE WITH FAULT ISOLATION
 *    - Execute Step 1 with try-catch
 *    - Report: "✓ Cube created successfully"
 *    - Execute Step 2 independently
 *    - If fails: "✗ Chamfer failed, but cube is ready to use"
 * 
 * 3. CONTINUE-ON-ERROR LOGIC
 *    - Don't stop at first failure
 *    - Track success/failure per step
 *    - Allow user to manually fix failures
 * 
 * MODIFIED CODE LOCATIONS:
 * ========================
 * 
 * File: Services/StepExecutor.cs
 * Method: Execute() - Line 22
 * Change: Instead of "return result" on first failure, continue to next step
 * 
 * File: UI/TextToCADTaskpaneWpf.xaml.cs
 * Method: BuildFromPromptAsync() - Line 2157
 * Change: Add step decomposition before execution
 * 
 * File: Services/StepDecomposer.cs (NEW)
 * Purpose: Break complex prompts into manageable steps
 */

// EXAMPLE: Decompose prompt into steps
public class StepDecomposer
{
    /// <summary>
    /// Takes a complex prompt and breaks it into smaller independent steps
    /// </summary>
    public static List<string> DecomposePrompt(string userPrompt)
    {
        var steps = new List<string>();
        
        // Pattern 1: "Create X with Y"
        if (userPrompt.Contains(" with "))
        {
            var parts = userPrompt.Split(new[] { " with " }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                steps.Add(parts[0].Trim());  // "Create cube 50x50x50mm"
                steps.Add("Then " + parts[1].Trim());  // "Then 1mm chamfer at edges"
                return steps;
            }
        }
        
        // Pattern 2: "Create X, then Y, then Z"
        if (userPrompt.Contains(", then "))
        {
            var parts = userPrompt.Split(new[] { ", then " }, StringSplitOptions.None);
            foreach (var part in parts)
                steps.Add(part.Trim());
            return steps;
        }
        
        // Pattern 3: "Create X. Add Y. Apply Z"
        if (userPrompt.Contains(". "))
        {
            var parts = userPrompt.Split(new[] { ". " }, StringSplitOptions.None);
            foreach (var part in parts)
                if (!string.IsNullOrEmpty(part.Trim()))
                    steps.Add(part.Trim());
            return steps;
        }
        
        // Default: single step
        steps.Add(userPrompt);
        return steps;
    }
}

// EXAMPLE: Enhanced StepExecutor with continue-on-error
public class EnhancedStepExecution
{
    /*
    PSEUDO-CODE FOR TextToCADTaskpaneWpf.xaml.cs:
    
    private async Task BuildFromPromptAsync()
    {
        var text = PromptText.Trim();
        
        // NEW: Decompose complex prompts
        var steps = StepDecomposer.DecomposePrompt(text);
        
        if (steps.Count > 1)
        {
            AppendStatusLine($"📋 Decomposed into {steps.Count} steps:");
            for (int i = 0; i < steps.Count; i++)
                AppendStatusLine($"  {i + 1}. {steps[i]}");
        }
        
        var allResults = new List<StepResult>();
        
        // Execute each step independently
        foreach (var step in steps)
        {
            try
            {
                // Send this step to LLM to get JSON plan
                var plan = await GeneratePlanFromStepAsync(step);
                
                // MODIFIED: Pass continueOnError=true
                var exec = StepExecutor.Execute(
                    plan, 
                    _swApp, 
                    progressCallback,
                    continueOnError: true  // NEW PARAMETER
                );
                
                // Report individual step result
                if (exec.Success)
                {
                    AppendStatusLine($"✓ Step succeeded: {step}");
                    SetStepProgress(step, 100, StepState.Success);
                }
                else
                {
                    AppendStatusLine($"✗ Step failed: {step}");
                    AppendStatusLine($"  Error: {string.Join("; ", exec.Log.Select(l => l["error"]))}");
                    SetStepProgress(step, 100, StepState.Failed);
                }
                
                allResults.Add(new StepResult { 
                    StepText = step, 
                    Success = exec.Success, 
                    Errors = exec.Log
                });
            }
            catch (Exception ex)
            {
                AppendStatusLine($"✗ Exception in step: {ex.Message}");
                allResults.Add(new StepResult { 
                    StepText = step, 
                    Success = false, 
                    ErrorMessage = ex.Message 
                });
            }
        }
        
        // Summary
        var successes = allResults.Count(r => r.Success);
        AppendStatusLine($"✓ {successes}/{steps.Count} steps succeeded");
        
        // Mark build as success if AT LEAST ONE step succeeded
        if (successes > 0)
        {
            SetRealTimeStatus($"{successes} of {steps.Count} steps completed", Colors.DarkOrange);
        }
        else
        {
            SetRealTimeStatus("All steps failed", Colors.DarkRed);
        }
    }
    */
}

// MODIFIED: Services/StepExecutor.cs - Add continueOnError parameter
/*
    public static StepExecutionResult Execute(
        JObject plan, 
        ISldWorks swApp, 
        Action<int, string, int?> progressCallback = null,
        bool continueOnError = false)  // NEW PARAMETER
    {
        var result = new StepExecutionResult();
        
        // ... existing code ...
        
        for (int i = 0; i < steps.Count; i++)
        {
            var s = NormalizeStep(steps[i]);
            string op = s.Value<string>("op") ?? string.Empty;
            var log = new JObject { ["step"] = i, ["op"] = op };
            
            try
            {
                // Execute operation
                switch (op)
                {
                    case "new_part":
                        // ... execute ...
                        log["success"] = true;
                        break;
                    case "extrude":
                        // ... execute ...
                        log["success"] = true;
                        break;
                    case "chamfer":
                        // ... execute ...
                        // If fails, set log["success"] = false
                        break;
                }
            }
            catch (Exception ex)
            {
                log["success"] = false;
                log["error"] = ex.Message;
                
                if (!continueOnError)
                {
                    result.Success = false;
                    result.Log.Add(log);
                    return result;  // ORIGINAL: Stop on first error
                }
                else
                {
                    // NEW: Track error but continue
                    result.Log.Add(log);
                    result.Success = false;  // Mark run as having failures
                    continue;  // Process next step
                }
            }
            
            result.Log.Add(log);
        }
        
        // NEW: Check if ANY steps succeeded
        var anySuccess = result.Log.Any(l => l["success"]?.Value<bool>() == true);
        result.Success = anySuccess;  // Return true if at least one step worked
        return result;
    }
*/

// UI UPDATE: Show per-step status
/*
    TextToCADTaskpaneWpf.xaml: Add to ProgressStatusPanel
    
    <StackPanel Orientation="Vertical" Spacing="4">
        <TextBlock x:Name="StepsHeader" Text="Steps:" FontWeight="Bold"/>
        <ItemsControl x:Name="StepsItemsControl">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border BorderBrush="LightGray" BorderThickness="0,0,0,1" Padding="4">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="20"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="60"/>
                            </Grid.ColumnDefinitions>
                            
                            <!-- Status Icon -->
                            <Ellipse Grid.Column="0" Width="16" Height="16" 
                                     Fill="{Binding Status, Converter={StaticResource StatusToColorConverter}}"/>
                            
                            <!-- Step Text -->
                            <TextBlock Grid.Column="1" Text="{Binding StepText}" 
                                       Margin="8,0,0,0" VerticalAlignment="Center"/>
                            
                            <!-- Result -->
                            <TextBlock Grid.Column="2" Text="{Binding ResultText}" 
                                       Foreground="{Binding Status, Converter={StaticResource StatusToColorConverter}}"
                                       TextAlignment="Right"/>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
*/

public class StepResult
{
    public string StepText { get; set; }
    public bool Success { get; set; }
    public List<JObject> Errors { get; set; }
    public string ErrorMessage { get; set; }
}

// SUMMARY OF CHANGES
/*
 * 1. Add StepDecomposer class to break prompts into steps
 * 2. Modify StepExecutor.Execute() to accept continueOnError parameter
 * 3. Update BuildFromPromptAsync() to:
 *    - Decompose prompt into steps
 *    - Execute each step independently
 *    - Report success/failure per step
 *    - Show UI updates for each step
 * 4. Update UI to show per-step progress with status icons
 * 5. Mark build as success if ANY steps succeeded (not ALL)
 * 
 * RESULT:
 * ✓ User: "Create cube with 1mm chamfer"
 * ✓ Step 1: Cube created successfully
 * ✗ Step 2: Chamfer failed (edge selection issue)
 * → User can manually fix Step 2 or keep the cube
 */
