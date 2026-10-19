SigLIP image encoder (model):
- Model: siglip-base-patch16-224
- Maintainer: Google
- License: Apache-2.0
- Source: https://huggingface.co/google/siglip-base-patch16-224

ONNX export and runtime: Xenova (vision_model.onnx). The model is downloaded on demand by the provided script and is not redistributed in this repository.

Additional image decoding:

- Magick.NET / ImageMagick (Magick.NET-Q8-AnyCPU)
  - License: Apache-2.0
  - Used for all image decoding (JPEG, PNG, WEBP, TIFF, HEIC/HEIF, RAW), resizing, perceptual-hash preprocessing, thumbnail generation and CLIP/SigLIP preprocessing; HEIC and RAW rely on ImageMagick delegate libraries.

- libheif
  - License: LGPL (dynamically linked/native delegate)
  - ImageMagick may dynamically use libheif for HEIC/HEIF support. libheif is included dynamically by Magick/ImageMagick when available on the platform.