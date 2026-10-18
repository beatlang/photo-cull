PhotoCull

Open source, privacy-first photo curation tools. Fully local — no cloud, no telemetry, no accounts. The tool will never auto-delete photos.

Local models

This app can use a local ONNX image embedding model (SigLIP). The repository includes scripts/download-models.ps1 to download the Xenova-exported ONNX model into %LOCALAPPDATA%\PhotoCull\models\siglip-vision.onnx. The downloader is the only network access; the app itself runs fully offline once the model is present.

Image formats supported

- ImageSharp-handled: JPEG, PNG, WEBP, TIFF
- Magick.NET (when available): HEIC/HEIF, RAW formats (CR2, CR3, DNG, NEF, ARW, ORF, RAF) — requires native delegates (libheif, raw decoders) available to ImageMagick/Magick.NET on the host system.