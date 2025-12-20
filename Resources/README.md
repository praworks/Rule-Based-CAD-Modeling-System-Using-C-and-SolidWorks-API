Taskpane Icon

Place your taskpane icon here so the add-in can pick it up automatically.

Accepted filenames (first found wins):
- taskpane_icon.bmp (recommended)
- taskpane_icon.png

Recommended specs for SolidWorks taskpane tab:
- Size: 20 x 20 pixels (small tab icon)
- Format: 24-bit BMP with magenta (RGB 255,0,255) for transparency
- Optional: PNG may work in newer SolidWorks versions, but BMP is safest

Override path (optional):
- Set environment variable AICAD_TASKPANE_ICON to a full file path. Example:
  - Windows (User scope):
    setx AICAD_TASKPANE_ICON "C:\\Icons\\my_ai_cad_icon.bmp"

Notes:
- The file will be read at runtime from the add-in load folder (bin/Debug or bin/Release). Ensure the file is copied there or placed under this Resources folder.
- If the icon is missing, SolidWorks shows its default taskpane icon.
