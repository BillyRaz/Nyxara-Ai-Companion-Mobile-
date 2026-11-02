Place local speech assets here.

Expected defaults:
- Whisper model: Assets/StreamingAssets/Speech/ggml-tiny.bin
- Piper voices: keep anywhere local, then point PiperTtsService to the .onnx voice file

The whisper.unity package already includes native plugins. You only need the model file.

Downloaded Piper voices in this project:
- Assets/StreamingAssets/Speech/PiperVoices/en_US-amy-medium.onnx
- Assets/StreamingAssets/Speech/PiperVoices/en_US-lessac-medium.onnx
