# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http.keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.4-exp.11] - 2026-08-27

### Changed
- Resolved speech-recognition specs now expose defensive metadata snapshots instead of shared mutable registry objects.
- Package realtime/offline samples now classify models through `ResolveSpeechRecognitionModelAsync` and the resolved `CanInitialize`/`IsOnline` semantics.
- `SherpaONNXUnityAPI.IsOnlineModel` remains as an obsolete compatibility-only name heuristic; new code must use the async resolved-spec API.

### Fixed
- Offline cancellation is checked immediately after the non-interruptible native `Decode` call returns, before a stale result is materialized or published.
- Duplicate normalized Model Definitions in the same module source now fail explicitly instead of silently retaining the first definition.
- Named ABI layout constants remove repeated raw offsets from the offline-result interop tests without changing the pinned native contract.

### Notes
- Experimental entries `0.1.4-exp.3` through `0.1.4-exp.10` describe local development checkpoints. Pin an immutable commit only after this review candidate completes Unity validation.
- No native DLL, native ABI, provider policy, model payload, or model download behavior changed.

## [0.1.4-exp.10] - 2026-08-24

### Added
- Added manifest-backed Model Definitions for the X-ASR 480 ms streaming Zipformer checkpoint and the FireRedASR2-AED INT8 offline checkpoint.
- Added an offline FireRed ASR Runtime Profile that reuses the existing `fire_red_asr` native configuration with exact encoder, decoder, and tokens bindings.
- Added an external-data Qwen3-ASR Runtime Profile and explicit file roles for the accepted third-party 1.7B ONNX artifact, including preflight checks for both `.onnx.data` files.

### Changed
- The X-ASR checkpoint reuses the existing online Zipformer runtime semantics; the additional `bpe.model` remains an artifact fact and is not a native required input.
- The third-party Qwen3-ASR 1.7B definition reuses the existing offline Qwen3 native path only after a static graph-contract comparison with the working 0.6B artifact.

### Notes
- `Ready` remains a static definition and local-file preflight result. Native initialization and transcription are still authoritative.
- No native ABI, native binary, provider fallback policy, model download behavior, or scene default was changed.

## [0.1.4-exp.9] - 2026-08-24

### Fixed
- Aligned the managed offline recognizer result layout with the complete sherpa-onnx v1.13.0/v1.13.6 C API struct. The stale layout read `tokens_arr` as token durations.
- Missing native timestamp or duration arrays now produce empty managed arrays, and corrupt native result counts are rejected before allocation.

### Notes
- The public transcription result remains intentionally narrow: text, tokens, timestamps, durations, and managed timing diagnostics. This release does not expose the additional native language, emotion, event, log-probability, or segment fields.
- No native ABI or native binary was changed; this release corrects the managed interop declaration for the pinned native ABI.

## [0.1.4-exp.8] - 2026-08-24

### Added
- Added immutable, structured CUDA provider diagnostics with preflight and post-initialization loaded-module snapshots.
- Exposed the latest CUDA diagnostic snapshot through SpeechRecognition, OfflineSpeechRecognizerComponent, and RealtimeSpeechRecognizerComponent.

### Changed
- Kept the legacy CUDA bool/string checks as compatibility shims over the structured snapshot implementation.
- CUDA diagnostics now preserve dependency paths, observed relevant native modules, file versions, missing entries, process ID, stage, and UTC timestamp without changing the native ABI.

### Notes
- A passed snapshot confirms provider/module loading at the sampling point; it does not claim that every ONNX operator executed on the GPU.

## [0.1.4-exp.7] - 2026-08-23

### Added
- Added an immutable `SpeechRecognition.TranscriptionTimings` breakdown to successful offline transcription results, covering module semaphore wait, worker dispatch, stream creation, waveform acceptance, the offline Decode call, result materialization, post-processing, stream disposal, worker total, and module total.

### Changed
- `SpeechRecognition.TranscriptionResult` now exposes `Timings`; its constructor accepts the timing value as a final optional parameter so existing callers remain source-compatible.
- `OfflineSpeechRecognizerComponent` continues to preserve its single-request, cancellation-and-drain, and recognizer-residency behavior while passing the instrumented result through unchanged.

### Notes
- `OfflineDecodeCall` is a managed observation of the sherpa offline Decode C API call boundary. It is not GPU kernel time and does not claim operator-level attribution.
- Non-success paths that do not complete the instrumented offline pipeline keep `Timings.IsAvailable == false`.

## [0.1.4-exp.6] - 2026-08-23

### Added
- Added a strict, versioned package JSON Model Definition manifest and C# Runtime Profile registry for the Zipformer bilingual baseline, Nemotron 3.5 560 ms int8, Qwen3-ASR 0.6B int8, and FunASR Nano int8 checkpoints.
- Added manifest provenance and typed required-file diagnostics to resolved speech-recognition specs.
- Added the `com.unity.nuget.newtonsoft-json@3.2.2` runtime dependency for deterministic manifest parsing.

### Changed
- Checkpoint facts for the four migrated models now come only from the package Resource manifest; mode, topology, runtime family, decoder, and file/directory requirements come only from Runtime Profiles.
- The remaining ASR catalog continues to use the legacy C# definitions, loaded after package JSON definitions; checksum data remains a distribution-only overlay.
- Invalid package manifests fail the package definition source as a whole instead of falling through to model-name inference.

### Fixed
- Local validation can now distinguish a required file from a required directory and report the manifest profile, source, schema, and content SHA used by runtime resolution.
- Preserved manifest-backed definition semantics and provenance when checksum URL/hash records are overlaid.
- Retried transient package `Resources.Load` misses after Editor import instead of caching a missing Model Definition for the whole domain.

## [0.1.4-exp.5] - 2026-08-23

### Added
- Added an explicit built-in Model Definition for `sherpa-onnx-funasr-nano-int8-2025-12-30`, including its offline topology, runtime family, sample rate, and four native file bindings.

### Changed
- Resolved speech-recognition specs now derive required file roles from explicit bindings when present, keeping package resolution, local catalog eligibility, and native configuration aligned.

### Fixed
- Prevented the FunASR Nano int8 model from being rejected as incomplete due to generic `model.onnx` and `tokens.txt` requirements.
- Ensured explicit tokenizer-directory bindings are validated and passed to FunASR Nano and Qwen3 native configs as directories instead of relying on directory-name fallback.

## [0.1.4-exp.4] - 2026-08-23

### Added
- Added the offline speech-recognizer wrapper contract used by the validation scene: direct sample transcription, warm-up, cancellation-and-drain, pending-queue clearing, and lifecycle diagnostics.
- Added strict single-request accounting with Busy, active, pending, dropped, and completed-lifecycle state exposed for validation and experiments.

### Changed
- Offline transcription now consumes a resolved offline model configuration instead of inferring online/offline compatibility from the model ID in the component.
- Async component disposal waits for queued work and native module disposal, while the existing synchronous `DisposeModule()` entry point remains compatible.

### Fixed
- Fixed missing `OfflineSpeechRecognizerComponent` APIs that prevented the consumer project's offline validation controller from compiling.
- Prevented recognizer teardown from disposing a native module while an offline decode is still draining.

## [0.1.4-exp.3] - 2026-08-23

### Added
- Added a source-aware, immutable speech-recognition model spec and one async resolution API for model semantics, runtime family, decoder, mode, and required file roles.
- Added explicit registration-source and resolution-level diagnostics for built-in, custom, and distribution-only catalog entries.

### Changed
- Built-in Model Definitions now always load before checksum Distribution Records are overlaid by exact module and model ID.
- Registry indexes now include module type and use explicit module-loaded state; local custom definitions may override, while remote custom manifests cannot silently replace built-ins.
- Speech recognition and validation tooling consume the same resolved spec instead of independently inferring topology or online/offline state from model names.

### Fixed
- Fixed remote checksum data hiding the built-in Nemotron definition and causing `metadata missing` before initialization.
- Prevented checksum-only entries and unregistered local directories from reaching native initialization.
- Fixed EditMode test compilation on Unity 2022 by avoiding an unavailable NUnit attribute and using the package's actual speech-synthesis module enum name.

## [0.1.4-exp.2] - 2026-08-23

### Added
- Added built-in metadata and explicit model-file bindings for the sherpa-onnx Nemotron-3.5 560 ms int8 streaming model.

### Fixed
- Select `greedy_search` for online NeMo-family transducers. The native NeMo recognizer terminates the host process when configured with the previously forced `modified_beam_search` mode.

## [0.1.4-exp.1] - 2026-08-20

### Added
- Added CPU/CUDA execution-provider selection for Speech Recognition components.
- Added optional one-shot silent warm-up with load and warm-up timing diagnostics.
- Added strict Windows CUDA prerequisite and loaded-provider checks; CUDA failures are reported instead of selecting CPU automatically.
- Added a manually triggered, SHA-256-pinned installer for the official sherpa-onnx v1.13.6 / ONNX Runtime 1.27.1 CUDA 13.x runtime.

### Changed
- Windows CUDA runtime files are installed into the consuming project's `Assets/Plugins` directory and are no longer bundled in this package.
- Windows desktop speech recognition continues to resolve the FP32 model files; int8 selection remains mobile-only.
- Package metadata now identifies the Apache-2.0 package license and the `wavman` fork repository.

## [0.1.3-exp.4] - 2026-05-07

### Added
- Added iOS platform support using iOS-targeted sherpa-onnx and ONNX Runtime static native libraries.
- Added documentation coverage for `SourceSeparationComponent`, `SpeakerDiarizationComponent`, the split realtime/offline speech recognition components, and the expanded runtime configuration API.

### Changed
- Updated bundled sherpa-onnx native libraries to v1.13.0.
- Updated the iOS native plugin layout to use bundled static libraries.
- Refreshed the bilingual package documentation and restored readable Chinese documentation text.

### Fixed
- Documented the missing runtime environment variables for download timeout, insecure download policy, and strict model hash validation.

## [0.1.3-exp.3] - 2026-03-22

### Changed
- Updated bundled sherpa-onnx native libraries to v1.12.32 across all supported platforms (Android arm64-v8a/armeabi-v7a/x86/x86_64, Windows x64, Linux x64, macOS, iOS).

### Fixed
- Recompiled Windows x64 native plugin (`sherpa-onnx-c-api.dll`) for the correct x64 architecture; previously it was incorrectly built as ARM64, causing a load failure in Unity on Windows x64. ([#12](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity/issues/12))

### Known Issues
- On Android `armeabi-v7a` (32-bit), some upstream native create/init paths may still be unstable for specific models or modules even though initialization is allowed and advisory warnings are reported.
- `arm64-v8a` remains the recommended Android target for production deployments.

## [0.1.3-exp.2] - 2026-03-21

### Added
- Added runtime controls for model preparation and download security: download-attempt timeout, insecure-download toggle, and strict hash-validation toggle (ScriptableObject settings, environment overrides, and `SherpaONNXUnityAPI` accessors).
- Expanded built-in ASR model metadata catalog with additional sherpa-onnx model IDs to improve out-of-box coverage.
- Added tests for new environment overrides and strict/non-strict hash-validation behavior in model preparation.
- Added shared Android 32-bit runtime advisory reporting across runtime modules so packages can surface `armeabi-v7a` risk information without blocking initialization.

### Changed
- Updated bundled sherpa-onnx native libraries to v1.12.31 across supported platforms.
- Validated Android runtime behavior against the refreshed native binaries, including follow-up checks on 32-bit (`armeabi-v7a`) and 64-bit (`arm64-v8a`) device paths.
- Improved model preparation metadata validation flow to support async hash population with cancellation-aware networking.
- Updated editor settings UX and localization wiring for runtime properties, including the new download/hash security options and more robust localized property rendering.

### Fixed
- Fixed native config initialization for multiple modules so default string fields are preserved when marshaling C# structs into sherpa-onnx/ONNX Runtime create calls.
- Fixed strict-hash validation messaging and non-strict fallback behavior so missing hashes can proceed with explicit warnings instead of hard failure when strict mode is disabled.

### Known Issues
- On Android `armeabi-v7a` (32-bit), some upstream native create/init paths may still be unstable for specific models or modules even though initialization is allowed and advisory warnings are reported.
- `arm64-v8a` remains the recommended Android target for production deployments.

## [0.1.3-exp.1] - 2026-02-05

### Added
- Customizable model preparation pipeline with `PrepareOptions`, `PrepareContext`, and `PrepareResult`, plus structured error codes surfaced through `PrepareAndLoadModelWithResultAsync(...)`.
- Editor support for custom model catalogs and bindings, enabling user-defined model entries in the model manager/settings.
- Runtime override APIs for `SHERPA_ONNX_*` environment variables via `SherpaONNXUnityAPI` and runtime settings.
- New Zero-Shot Speech Synthesis sample prompt assets ("Samantha").
- Additional tests covering prepare rollback and runtime environment overrides.

### Changed
- Updated sherpa-onnx native dependency to v1.12.23 and refreshed platform binaries.
- Expanded `SpeechRecognitionModelType` coverage to improve model selection.
- Model preparation now uses the new result-based API; `PrepareAndLoadModelAsync(...)` is replaced by `PrepareAndLoadModelWithResultAsync(...)`.

### Fixed
- Resolved FunASR model initialization failures.
- Guarded Unity path resolution to run on the main thread to avoid threading errors.

## [0.1.2-exp.3] - 2025-12-08

### Changed
- Restructured the overall codebase, refactoring several module implementations to improve readability, maintainability, and architectural robustness.
- Optimized performance across core components to reduce overhead and improve runtime efficiency.
- Updated the native Sherpa-ONNX dependency to v1.12.19, aligning with the latest upstream changes for enhanced compatibility and feature support.
- Improved editor window responsiveness with better rendering performance and smoother interaction flow.
- Refined the editor UI for a cleaner and more polished user experience.
### Added
- Introduced the SherpaONNX Profiler editor tool, enabling real-time performance monitoring and activity tracking for all Sherpa ONNX modules.
- Integrated a new logging system within the profiler to trace module behavior and assist with debugging, diagnostics, and performance analysis.

## [0.1.2-exp.2] - 2025-11-23

### Added
- Editor localization (English/Chinese) plus tailored inspectors and menu entries for every SherpaONNX Mono component.
- Drop-in MonoBehaviour components for ASR, VAD, punctuation, keyword spotting, audio tagging, speech enhancement, TTS, and zero-shot TTS, with shared microphone input streaming.
- New sample scenes for **Audio Tagging** and **Zero-Shot Speech Synthesis**, including prompt assets, progress UI, and updated demo scripts.
- `SherpaONNXUnityAPI` exposes runtime toggles (`SetAutoDownloadModels`, `SetFetchLatestManifest`, checksum cache helpers) so developers can apply the issue #4 recommendation directly from code.

### Changed
- Reorganized native plugins under `Runtime/Plugins` and refreshed sherpa-onnx c-api binaries across Android (ARMv7/ARM64/x86/x64), Windows x64, Linux x64, macOS, and iOS while trimming obsolete x86 DLLs.
- Expanded model constants/resolvers and added `ModelFileResolver` helpers so modules can prepare newer ASR/TTS/audio-tagging families with adaptive threading and clearer feedback.
- Updated editor tooling (model manager, settings provider, runtime settings utility) with localization and more robust streaming transcription deduplication.

### Fixed
- Hardened microphone chunking and streaming queues in demo scripts and the speech recognizer component to avoid duplicate transcripts and race conditions during teardown.
- Unified decompression helper and progress tracking to reduce stalls when preparing models from downloads.

## [0.1.2-exp.1] - 2025-10-28

### Added
- Integrated **Audio Tagging** module from Sherpa-ONNX for sound event recognition.
- Released Unity demo scene showcasing **zero-shot TTS** via ZipVoice (experimental feature).

### Changed
- Supplemented model metadata for various modules to enhance compatibility.
- Optimized decompression process; default extraction remains via SharpZipLib for cross-platform support.

### Fixed
- Resolved IL2CPP build issues by upgrading Sherpa-ONNX to v1.12.15 and restructuring native plugin directories. (https://github.com/EitanWong/com.eitan.sherpa-onnx-unity/issues/2)

### Known Issues
- **ZipVoice TTS Demo**:
  - Chinese prompts may produce intermittent white noise or distorted audio.
  - English prompts may fail during synthesis setup, leading to crashes.
  - Suspected cause: Inconsistent handling of eSpeak-NG data or voice selection in the native layer.
  - Note: This feature is **not production-ready** and is provided for evaluation purposes only.

## [0.1.1-exp.3] - 2025-10-17

### Changed
- Used a self-optimized and compiled sherpa-onnx.dll .NET library to enhance P/Invoke security and compatibility with the iOS platform. (https://github.com/EitanWong/sherpa-onnx/tree/unity)
- **License**: The project license has been changed from MIT to **Apache 2.0**.
- **Model Management Overhaul**:
  - The model registry (`SherpaONNXModelRegistry`) is now fully asynchronous, fetching the latest model list from GitHub releases instead of relying on a local, static manifest. This ensures access to the newest models without package updates.
  - The model downloader (`SherpaFileDownloader`) has been completely rewritten for improved robustness, featuring chunked downloading, automatic retry logic, network health checks, and adaptive concurrency.
  - Simplified `SherpaONNXModelMetadata`, removing the need to declare individual model files. The system now dynamically detects files after extraction.
- **iOS Integration**: Renamed iOS native libraries from `*.a` to `lib*.a` to align with standard conventions, improving compatibility with build systems.

### Added
- Added the functionality to fetch the model list from sherpa-onnx online by default.
- **Automatic English Casing**: ASR results in English are now automatically converted to proper sentence case (e.g., "hello world" becomes "Hello world."). This includes smart handling of proper nouns, acronyms (like "USA"), and contractions (like "it's").
- **Adaptive Performance**: Introduced `ThreadingUtils` to automatically adjust the number of threads used by ONNX models based on the device's CPU cores, memory, and platform (mobile/desktop), optimizing performance and power consumption.

### Fixed
- Fixed a bug that caused the offline speech recognition to crash on Android. (https://github.com/EitanWong/com.eitan.sherpa-onnx-unity/issues/1)
- **Microphone Lifecycle**: Improved microphone handling in demo scenes to ensure proper cleanup and prevent resource leaks when the application quits or the scene is destroyed.
- **Model Initialization**: Refactored module initialization to be more robust, providing clearer success/failure feedback and ensuring that native handles are correctly managed.

### Improved
- Improved the stability of model loading by using reflection to confirm that the model has been loaded.
- **Editor UX**:
  - The "SherpaONNX Models" editor window now uses a high-performance virtualized scroll list, allowing it to display thousands of models without freezing or slowing down.
  - A loading spinner is now displayed while fetching the model manifest, providing better user feedback.
  - Language filtering is now more intelligent, supporting multi-language models and providing a cleaner UI.
- **Demo Scenes**: All demo scenes now fetch the model list asynchronously and display a "Fetching..." message to the user, improving the initial user experience.

### Known Issues
- There is a problem with the official native library of sherpa-onnx on the iOS platform, which causes crashes. Waiting for subsequent updates to fix it.

## [0.1.1-exp.2] - 2025-09-21

### Added
- **SpokenLanguageIdentification Module** - Identifies the language from a list of candidates in a given audio clip.
  - Supports both streaming and batch processing.
  - Includes a demo scene for interactive testing.
- **Custom Keyword Support** - Added functionality to the `KeywordSpotting` module to support custom keywords.
  - Currently available for Chinese language models.

### Changed
- Updated sherpa-onnx to [v1.12.14](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.12.14).

## [0.1.1-exp.1] - 2025-08-06

### Added
- **SpeechEnhancement Module** - Complete noise reduction system using GTCRN models
  - In-place audio processing for zero-GC design and optimal performance
  - Real-time streaming and batch processing support
  - Multiple processing methods: synchronous, asynchronous, and Span-based
  - Support for float arrays, Span<float>, and buffer segments
  - Thread-safe processing with proper resource management
  - GTCRN model integration with hash verification

- **KeywordSpotting Module** - Voice-activated keyword detection system
  - Event-driven keyword detection with `OnKeywordDetected` event
  - Stream-based processing with concurrent audio queue
  - Both streaming and batch detection methods
  - Thread-safe processing with ArrayPool optimization
  - Support for Chinese and English keyword models
  - Real-time audio processing with background thread management

- **Comprehensive Demo Applications**
  - **SpeechEnhancementExample**: Interactive demo with real-time enhancement
    - Model dropdown selection with automatic registry integration
    - Real-time recording with performance monitoring
    - Enhancement comparison toggle for A/B testing
    - Automatic playback after recording completion
    - UI state management with proper visibility controls
  - **KeywordSpottingExample**: Voice activation demo with keyword detection

- **Model Registry Enhancements**
  - Added GTCRN speech enhancement model constants with hash verification
  - Added keyword spotting model metadata tables for Chinese and English
  - Enhanced model type detection for new module types
  - Improved model utility functions for better identification

### Changed
- Updated sherpa-onnx to v1.12.7
- Simplified platform library dependencies, removing unsupported architectures for Unity
- Enhanced `SherpaONNXModuleType` enum with `KeywordSpotting` and `SpeechEnhancement` types
- Improved model download URL generation for new module types
- Enhanced `UnityLogger` with better error handling and disposal safety

### Technical Improvements
- **Performance Optimizations**
  - Internal bool variables for real-time audio processing instead of UI component access
  - Zero-allocation processing with in-place array modifications
  - Thread-safe concurrent processing with proper locking mechanisms
  - Optimized UI updates with conditional visibility management
- **Architecture Enhancements**
  - Extended model registry with proper module type filtering
  - Better error handling and resource management across all modules
  - Improved thread safety with concurrent audio processing
- **Code Quality**
  - Enhanced documentation with comprehensive XML comments
  - Better separation of concerns in UI and processing logic
  - Improved resource disposal patterns

## [0.1.0-exp.1] - 2025-07-28

### Added
- Initial release of SherpaONNXUnity package
- Offline speech recognition (ASR) functionality using sherpa-onnx
- Text-to-speech (TTS) synthesis capabilities
- Voice Activity Detection (VAD) module
- Speaker diarization support
- Audio enhancement features
- Cross-platform native library support:
  - Windows (x86, x64)
  - macOS (Intel, Apple Silicon)
  - Linux (x64, ARM64)
  - Android (ARM64, ARMv7, x86, x64)
- Automatic model management system with download and verification
- Unity integration components:
  - `SherpaONNXAnchor` main scene component
  - `SherpaONNXModule` base class system
  - `SpeechRecognition` module
  - `VoiceActivityDetection` module
- Sample collection with example scenes and scripts
- Assembly definitions for runtime, editor, tests, and samples
- Model registry system for automated model handling
- Real-time audio processing with low latency
- Batch processing capabilities for audio files
- Unity Test Framework integration
- Editor tools and extensions
- OpenUPM package registry support

### Technical Details
- Unity 2021.3 LTS minimum requirement
- Native sherpa-onnx library integration
- ONNX Runtime dependency
- Streaming audio processing pipeline
- Thread-safe audio buffer management
- Automatic memory management for models
- Hash-based model integrity verification
- StreamingAssets integration for model storage

### Documentation
- Comprehensive README with quick start guide
- Code examples for common use cases
- Architecture documentation
- Platform compatibility matrix
- Performance guidelines
- Troubleshooting section

### Known Issues
- iOS platform support is in development
- Large model files may require significant download time on slow connections
- Memory usage scales with model complexity
