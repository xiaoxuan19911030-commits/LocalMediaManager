# Person detection model

- Model: SSD-MobileNetV1-12 INT8
- Version: ONNX opset 12 model published by ONNX Model Zoo (`main`, retrieved 2026-07-23)
- Source: https://github.com/onnx/models/tree/main/validated/vision/object_detection_segmentation/ssd-mobilenetv1
- Model file: `ssd_mobilenet_v1_12-int8.onnx`
- Model license: MIT (declared by the model card)
- Repository license: Apache License 2.0
- SHA-256: `2B79E6A7FB1EC6A33F332B9B10D82D9DE4B7B49DCD26B5946921BB356895C954`
- Size: 9,540,809 bytes

The model is distributed with Local Media Manager and is loaded locally by the Bridge. Images,
video frames, detections, and scores are never uploaded. The implementation only consumes the
COCO `person` class and does not perform face recognition or identity recognition.

The model card and repository license must remain available in redistributed builds:

- https://github.com/onnx/models/blob/main/validated/vision/object_detection_segmentation/ssd-mobilenetv1/README.md
- https://github.com/onnx/models/blob/main/LICENSE
