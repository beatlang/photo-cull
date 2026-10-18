PhotoCull

Open source, privacy-first photo curation tools. Fully local — no cloud, no telemetry, no accounts. The tool will never auto-delete photos.

Local models

This app requires a local ONNX image embedding model (SigLIP). On startup it checks and loads the model from `%LOCALAPPDATA%\PhotoCull\models\siglip-vision.onnx`; if the model is missing or cannot be loaded, PhotoCull displays an error and exits. Run `scripts\download-models.ps1` to download the Xenova-exported ONNX model. Alternatively, set `PHOTOCULL_MODEL` to a valid ONNX model file. The downloader is the only network access; the app itself runs fully offline once the model is present.

Similar-photo sets are recalculated on each completed scan. Grouping uses EXIF capture time when available, perceptual image hashes, and the local model's image embeddings; filesystem creation times are not used as capture times.

Image formats supported

- ImageSharp-handled: JPEG, PNG, WEBP, TIFF
- Magick.NET (when available): HEIC/HEIF, RAW formats (CR2, CR3, DNG, NEF, ARW, ORF, RAF) — requires native delegates (libheif, raw decoders) available to ImageMagick/Magick.NET on the host system.