Drop local LLM models here if you want Unity builds to carry them.

Current project default:
- Preferred file: Assets/StreamingAssets/Models/Qwen2.5-7B-Instruct-1M-Q4_K_M.gguf
- Fallback file: the original external path configured in CompanionStackDefaults.cs

If you want a self-contained project, copy your GGUF into this folder and update:
- Nyxara.AICompanion.Configuration.CompanionStackDefaults.QwenModelPath
