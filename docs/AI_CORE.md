# Local AI Core

Local Media Manager uses one local ONNX Runtime integration for CPU inference. Models are registered in `AiModelRegistry`, loaded and reused by `AiModelSessionManager`, and run through the two-slot `AiInferenceQueue`.

Inference results are cached below the application data root at `AI\Cache`. Cache keys include the model id/version and source-file version information, so replacing a cover invalidates its prior result. React components and Bridge route handlers do not load models or create `InferenceSession` instances.

The first registered model is YuNet (`face-detection-yunet`, version `2023mar`). It performs local face detection only; it does not identify people or send image data to a service. Feature code consumes domain abstractions such as `IFaceDetectionService`, not ONNX tensors or model-specific output names.
