$env:AICAD_SYSTEM_PROMPT='You are a CAD planning agent. Output only raw JSON with a top-level "steps" array for SolidWorks. ALWAYS use op:"auto_dimension" for sketch dimensions and include numeric fields cx, cy, w, h in mm.'

# Run the existing probe
& "$PSScriptRoot\request_llm_by_priority.ps1"
