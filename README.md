# SolidWorks Taskpane: AI-CAD

A SolidWorks taskpane add-in that turns natural language into simple 3D models.

Type a prompt like "box 100x50x25 mm" or "cylinder Ø40 x 80 mm" and it creates a new part with the requested features.

## Build
- Open `SolidWorks.TaskpaneCalculator.sln` in Visual Studio (or build with MSBuild).
- Requires Visual Studio with .NET Framework 4.7.2 targeting pack and SolidWorks Interop assemblies (already referenced in `References/`).
- Project targets x64 and embeds interop types.

## Register (Admin)
1. Build Debug.
2. Right-click `Register_Addin_Debug.bat` > Run as administrator.
3. Start SolidWorks; the add-in should load and show a taskpane named "AI-CAD".

## Unregister (Admin)
- Run `Unregister_Addin_Debug.bat` as administrator.

- `README.md` with usage notes.
	- Added `Clean.bat` to remove bin/obj/.vs in one go.

## Usage
- Set environment variable `GEMINI_API_KEY` with your Google API key (User scope).
- Open the "AI-CAD" taskpane.
- Pick a preset or type a prompt, e.g.:
	- "Create a rectangular box 120 mm by 60 mm by 30 mm"
	- "Create a cylinder 25 mm diameter and 70 mm height"
- Click "Build Model". A new part is created using a simple sketch and blind extrude (units in mm).

Notes:
- Supported shapes: box (length,width,height) and cylinder (diameter,height).
- Dimensions are interpreted in millimeters.

## Data storage (default: MongoDB)
- By default, the add-in will log runs and feedback to MongoDB when available.
- Configure via environment variables (User scope is fine):
	- `MONGODB_URI` — your connection string (e.g., `mongodb://localhost:27017` or Atlas SRV URI)
	- `MONGODB_DB` — database name (default: `TaskPaneAddin`)
	- `MONGODB_COLLECTION` — main collection for run logs (default: `SW`)
- Positive examples (thumb-up) are stored in `good_feedback` collection.
- If MongoDB is unavailable, the add-in falls back to:
	- File-based JSONL logs at `nl2cad.db.jsonl`
	- SQLite for run/step history and few-shot retrieval at `feedback.db`

	## Secrets

	- Do NOT commit API keys or other secrets into source control.
	- This project supports two safe ways to provide the Gemini/Google API key:
		1. Set an environment variable named `GEMINI_API_KEY` (preferred). On Windows (PowerShell):

			 $env:GEMINI_API_KEY = "your_api_key_here"

		2. Create a local file named `GEMINI_API_KEY.txt` containing a line like `GEMINI_API_KEY=your_api_key_here` and keep that file out of version control (it's already listed in `.gitignore`).

	- If you accidentally committed a key, rotate it immediately and remove the file from the repo using git history rewriting tools.

	## LLM / Gemini model errors

	- If you see errors like "models/gemini-1.5-flash is not found for API version v1beta" the requested model is not available for the API/method used. To fix:
		1. Check available models by calling the Generative Language API's ListModels endpoint or consult Google's model docs.
		2. Set a supported model via the `GEMINI_MODEL` environment variable (user/process/machine) or update the UI drop-down.
		3. The code defaults to `gemini-1.0` if no valid model is configured.

	Example PowerShell to set the env var for the current user:

	```powershell
	setx GEMINI_MODEL "gemini-1.5-pro"
	```

	If you're still getting 404s, call ListModels with your API key to see which models your key can access.

## OAuth (Desktop app / PKCE)

This add-in includes a Desktop OAuth helper for the Authorization Code + PKCE flow. The recommended flow is:

1. Create an OAuth Client in Google Cloud Console: `Credentials -> Create Credentials -> OAuth client -> Desktop app`.
2. Download the client JSON but do NOT commit it to source control. Instead, keep it outside the repo and use the `client_id` value in the add-in.
3. For local development, you can store the client JSON path in an environment variable `GOOGLE_OAUTH_CLIENT_JSON` and add it to your local `.gitignore`.
4. The project provides a helper: `Services.OAuthDesktopHelper.AuthorizeAsync(clientId, scopes)` which performs PKCE + loopback redirect and returns the token JSON string. Persist the refresh_token to Windows Credential Manager for subsequent runs.

A placeholder `client_oauth_placeholder.json` is included in the repo to document expected structure.

