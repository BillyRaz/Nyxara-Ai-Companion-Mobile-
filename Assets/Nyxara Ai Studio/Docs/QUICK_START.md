# Quick Start

## 1. Import

Import the Nyxara AI Studio package into your Unity project.

---

## 2. Install Dependencies

Download the optional external systems you want to use:

- LLMUnity (Local LLM)
- Whisper (Speech-to-Text)
- Piper (Text-to-Speech, optional)

(See README for links)

---

## 3. Run Setup Wizard

Open:
Nyxara AI → Studio → Setup Wizard

Use the wizard to browse to the files, folders, or supported Whisper ZIP files you already downloaded.

- Select your `.gguf` file for LLM
- Select your downloaded Whisper integration folder or `.zip` if you want the wizard to import the Whisper Unity package
- Select your Whisper model file
- Optionally select your Piper runtime folder
- Optionally select your Piper voice `.onnx`

Click install for each dependency or use `Install All`.

The Whisper package source supports:
- `.unitypackage`
- `Assets/...`
- embedded `package.json` package content, including `Packages/.../package.json`

If you already installed integrations manually, you can also use `Validate & Bind Installed Integrations` in Setup Wizard to repair Nyxara bindings without reinstalling.

The wizard only copies files, folders, or supported Whisper ZIP files you manually select. It does not download anything or connect to external services.

---

## 4. Open Studio

Open:
Nyxara AI → Studio

---

## 5. Verify External Paths

Go to the **Status** tab.

The wizard should already update the paths automatically.

Verify status:
- LLM Model Status → Found
- Whisper Model Status → Found
- Voice Output Status → Ready (if configured)

---

## 6. Setup Character

- Assign your ARKit-compatible character
- Confirm face renderer is detected

---

## 7. Build

Click:

Build Studio  
→ Apply Rig (if needed)  
→ Finalize Companion Root Prefab  

---

## 8. Test

- Enter Play Mode  
- Trigger input (mic or test input)  
- Verify AI response (text)

If Piper is configured:
- Verify voice playback + lip sync

---

## 9. Validate

Use:

- Status tab  
- Diagnostics  
- Lip Sync test  
- Full System Test  

---

## Notes

- Voice output is optional.  
- If Piper is not configured, Nyxara AI Studio will still work using text-based responses.  
- If dependencies are not installed, Nyxara AI Studio will still import and run, but AI, speech-to-text, and voice features will remain disabled until configured.
- Nyxara Setup Wizard does not download or auto-connect to anything outside the Unity project. It only copies files, folders, or supported Whisper ZIP files you manually choose.
