SigLIP image encoder (model):
- Model: siglip-base-patch16-224
- Maintainer: Google
- License: Apache-2.0
- Source: https://huggingface.co/google/siglip-base-patch16-224

ONNX export and runtime: Xenova (vision_model.onnx). The model is downloaded on demand by the provided script and is not redistributed in this repository.

Additional image decoding:

- Magick.NET / ImageMagick (Magick.NET-Q8-AnyCPU)
  - License: Apache-2.0
  - Used for decoding HEIC and RAW formats via ImageMagick delegating libraries.

- libheif
  - License: LGPL (dynamically linked/native delegate)
  - ImageMagick may dynamically use libheif for HEIC/HEIF support. libheif is included dynamically by Magick/ImageMagick when available on the platform.