# SherpaONNXUnity Documentation

[中文](./README.zh.md) | [Package README](./README.md)

## Installation

- Unity 2021.3 LTS or newer.
- Package name: `com.eitan.sherpa-onnx-unity`
- Git URL:

```text
https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm
```

OpenUPM scoped registry:

```text
Name: OpenUPM
URL: https://package.openupm.com
Scope: com.eitan.sherpa-onnx-unity
```

## Quick Start

1. Install the package.
2. Import **SherpaONNXUnity Sample** from Package Manager.
3. Open a scene under `Samples~/Collection`.
4. Select or download a model in `Window/SherpaONNX/Model Manager`.
5. Press Play.

For scene-based workflows, add `SherpaMicrophoneInput` and a module component, set `Model Id`, bind audio input when needed, and subscribe to UnityEvents.

## Editor Tools

| Tool | Menu |
|---|---|
| Model Manager | `Window/SherpaONNX/Model Manager` |
| Profiler | `Window/SherpaONNX/SherpaONNX Profiler` |
| Welcome | `Help/SherpaONNX/Welcome` |
| Component shortcuts | `GameObject/SherpaONNX/...` |

## Feature Guides

### <a id="speech-recognition"></a>Speech Recognition

Transcribes speech to text. Use realtime recognition for microphone streams and offline recognition for recorded audio.

- Components: `RealtimeSpeechRecognizerComponent`, `OfflineSpeechRecognizerComponent`
- API: `SpeechRecognition.TranscribeAsync(...)`, `SpeechRecognition.SpeechTranscriptionAsync(...)`
- Offline component seam: `TranscribeSamplesAsync(...)`, `WarmUpAsync(...)`, `CancelAndDrainAsync(...)`, and `DisposeModuleAsync()`
- Offline diagnostics: `ModelLoadDuration`, `WarmUpDuration`, `ActiveTranscriptionCount`, `PendingSegmentCount`, `DroppedSegmentCount`, and `BusySegmentCount`
- Successful offline `TranscriptionResult` values expose `Timings`, a managed-side breakdown for semaphore wait, worker dispatch, stream creation, `AcceptWaveform`, the offline `Decode` call, result materialization, post-processing, disposal, and worker/module totals. `OfflineDecodeCall` is the observed C API call span, not GPU kernel time; uninstrumented/non-success paths report `IsAvailable == false`.
- Samples: `RealtimeSpeechRecognition`, `OfflineSpeechRecognition`

### <a id="speech-synthesis"></a>Speech Synthesis

Generates speech from text, including standard TTS and prompt-driven zero-shot synthesis.

- Components: `SpeechSynthesizerComponent`, `ZeroShotSpeechSynthesisComponent`
- API: `SpeechSynthesis.GenerateAsync(...)`, `SpeechSynthesis.GenerateZeroShotAsync(...)`
- Samples: `SpeechSynthesis`, `ZeroShotSpeechSynthesis`

### <a id="spoken-language-identification"></a>Spoken Language Identification

Detects the spoken language from an audio clip or sample buffer.

- Component: `SpokenLanguageIdentificationComponent`
- API: `SpokenLanguageIdentification.IdentifyAsync(...)`
- Sample: `SpokenLanguageIdentification`

### <a id="keyword-spotting"></a>Keyword Spotting

Detects wake words or configured keywords from streaming or recorded audio.

- Component: `KeywordSpottingComponent`
- API: `KeywordSpotting.StreamDetect(...)`, `KeywordSpotting.DetectAsync(...)`
- Sample: `KeywordSpotting`

### <a id="punctuation"></a>Punctuation

Restores punctuation and casing for recognized text.

- Component: `PunctuationComponent`
- API: `Punctuation.AddPunctuationAsync(...)`
- Sample: `Punctuation`

### <a id="speaker-identification"></a>Speaker Identification

Identifies or labels speakers through speaker embeddings. Use this with speaker-analysis workflows when you need to associate speech with known speakers.

- APIs: speaker embedding and speaker analysis APIs
- Related component: `SpeakerDiarizationComponent`
- Related sample: `SpeakerDiarization`

### <a id="speaker-diarization"></a>Speaker Diarization

Segments an audio clip by speaker turns and returns speaker-labeled time ranges.

- Component: `SpeakerDiarizationComponent`
- API: `SpeakerDiarization.DiarizeAsync(...)`
- Sample: `SpeakerDiarization`

### <a id="speaker-verification"></a>Speaker Verification

Compares speaker embeddings to verify whether two speech segments are likely from the same speaker.

- APIs: speaker embedding and verification APIs
- Related component: `SpeakerDiarizationComponent`
- Related sample: `SpeakerDiarization`

### <a id="source-separation"></a>Source Separation

Separates mixed audio into stems, such as vocals and accompaniment depending on the selected model.

- Component: `SourceSeparationComponent`
- API: `SourceSeparation.SeparateAsync(...)`
- Sample: `SourceSeparation`

### <a id="audio-tagging"></a>Audio Tagging

Classifies audio events such as music, environmental sounds, or other acoustic classes.

- Component: `AudioTaggingComponent`
- API: `AudioTagging.TagAsync(...)`, `AudioTagging.TagStreamAsync(...)`
- Sample: `AudioTagging`

### <a id="voice-activity-detection"></a>Voice Activity Detection

Detects speech boundaries and speaking state from audio streams.

- Component: `VoiceActivityDetectionComponent`
- API: `VoiceActivityDetection.StreamDetect(...)`, `VoiceActivityDetection.FlushAsync()`
- Sample: `VoiceActivityDetection`

### <a id="speech-enhancement"></a>Speech Enhancement

Denoises and enhances speech audio.

- Component: `SpeechEnhancementComponent`
- API: `SpeechEnhancement.EnhanceAsync(...)`, `SpeechEnhancement.ProcessStreamingAsync(...)`
- Sample: `SpeechEnhancement`

## Runtime Configuration

```csharp
SherpaONNXUnityAPI.SetAutoDownloadModels(false);
SherpaONNXUnityAPI.SetFetchLatestManifest(true);
SherpaONNXUnityAPI.SetDownloadAttemptTimeoutSeconds(600);
SherpaONNXUnityAPI.SetAllowInsecureModelDownload(false);
SherpaONNXUnityAPI.SetForceModelHashValidation(false);
SherpaONNXUnityAPI.SetGithubProxy("https://your-proxy/");
SherpaONNXUnityAPI.ClearChecksumCache();
```

Environment overrides:

- `SHERPA_ONNX_FETCH_LATEST_MANIFEST`
- `SHERPA_ONNX_AUTO_DOWNLOAD`
- `SHERPA_ONNX_AUTO_DELETE_CORRUPTED_MODELS`
- `SHERPA_ONNX_DOWNLOAD_ATTEMPT_TIMEOUT_SECONDS`
- `SHERPA_ONNX_ALLOW_INSECURE_MODEL_DOWNLOAD`
- `SHERPA_ONNX_FORCE_MODEL_HASH_VALIDATION`
- `SHERPA_ONNX_GITHUB_PROXY`
- `SHERPA_ONNX_CHECKSUM_CACHE_DIR`
- `SHERPA_ONNX_CHECKSUM_CACHE_TTL_SECONDS`
- `SHERPA_ONNX_LOGGING_ENABLED`
- `SHERPA_ONNX_LOGGING_LEVEL`
- `SHERPA_ONNX_LOGGING_TRACE_STACKS`

Call `SherpaONNXUnityAPI.ApplyEnvironmentOverridesFromProcess()` after changing process environment variables at runtime.

## Custom Models

Runtime registration:

```csharp
SherpaONNXUnityAPI.RegisterCustomModel(metadata);
SherpaONNXUnityAPI.RegisterCustomModels(models);
```

Speech-recognition callers should resolve model semantics through the same SSOT used by the runtime:

```csharp
SherpaONNXSpeechRecognitionModelSpec spec =
    await SherpaONNXUnityAPI.ResolveSpeechRecognitionModelAsync(modelId, cancellationToken);
```

`spec.CanInitialize` is true only for a registered Model Definition with a supported topology. Official checksum entries contribute download URL/hash data only; a checksum-only (`DistributionOnly`) record cannot initialize a native recognizer. Local custom settings are the explicit user-authorized override layer.

Package-owned speech-recognition definitions may be stored in the versioned Resource manifest at
`Runtime/Resources/SherpaONNX/ModelDefinitions/asr-model-definitions.v1.json`. The JSON stores checkpoint facts only. A named C# Runtime Profile owns online/offline mode, native model type, runtime family, decoder policy, and required file/directory roles. Resolve these facts through `ResolveSpeechRecognitionModelAsync`; do not parse the package manifest from consuming code.

Manifest-backed specs expose `RuntimeProfileId`, `DefinitionProvenance`, and `RequiredFiles`. `DefinitionProvenance` identifies the source, manifest kind, schema version, and SHA-256 of the exact raw manifest bytes. `RequiredFiles` carries both the semantic role and whether the path must be a file or directory; `RequiredFileKeys` remains as a compatibility view.

`SherpaONNXUnityAPI.IsOnlineModel` is retained only as an obsolete model-name heuristic for source compatibility. It does not consult the registry and must not be used for eligibility or runtime configuration.

Minimum **legacy custom model manifest** entry (this is not the package Model Definition schema above):

```json
{
  "modelId": "your-model-id",
  "moduleType": 1,
  "moduleTypeHint": "SpeechRecognition",
  "downloadUrl": "https://your.cdn/path/to/model.zip",
  "downloadFileHash": "sha256-hex",
  "modelTypeHint": "",
  "runtimeFamilyHint": "",
  "fileBindings": [],
  "numberOfSpeakers": 0,
  "sampleRate": 16000
}
```

Legacy custom entries may provide explicit `modelTypeHint`, `runtimeFamilyHint`, and `fileBindings`. They remain a compatibility and user-override path; package-owned checkpoint facts belong in the versioned Model Definition manifest and obtain runtime semantics from a named Runtime Profile.

## Platform Notes

- `0.1.3-exp.4` updates sherpa-onnx native libraries to v1.13.0.
- iOS uses bundled static native libraries for Unity iOS builds.
- Android `arm64-v8a` is recommended for production.
- Android `armeabi-v7a` remains available but may be unstable for some upstream native model paths.
