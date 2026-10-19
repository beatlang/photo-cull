![PCull logo](src/PCull.App/Assets/PCullLogo.svg)

pCull

Open source, privacy-first photo curation tools. Fully local — no cloud, no telemetry, no accounts. The tool will never auto-delete photos.

Local models

This app requires a local ONNX image embedding model (SigLIP). On startup it checks and loads the model from `%LOCALAPPDATA%\pCull\models\siglip-vision.onnx`; if the model is missing or cannot be loaded, pCull displays an error and exits. Run `scripts\download-models.ps1` to download the Xenova-exported ONNX model. Alternatively, set `PCULL_MODEL` to a valid ONNX model file. The downloader is the only network access; the app itself runs fully offline once the model is present.

Similar-photo sets are recalculated on each completed scan. Grouping uses EXIF capture time when available, perceptual image hashes, and the local model's image embeddings; filesystem creation times are not used as capture times.

The local image database retains entries only for photos found in the latest completed scan. Cached features and user decisions for photos in that scan are kept; entries for paths no longer found are removed.

Grouping thresholds can be adjusted from the gear button in the main window and are saved to `%LOCALAPPDATA%\pCull\settings.json`. The defaults are a 2-second capture-time window, pHash and dHash distances of 8 bits, and embedding cosine similarity of 0.90. Changes apply to the next scan. A match on any signal joins photos; links can chain, so review larger sets carefully.

When upgrading from an earlier release, copy the contents of its local app-data folder into `%LOCALAPPDATA%\pCull` before launching this version to keep the local model and settings.

Grouping checks criteria in order and stops at the first match: capture-time window, dHash, pHash, then AI embedding similarity. Hashes and embeddings are loaded only when earlier criteria did not match. The details panel shows the criterion that connected each directly matched photo in red, previously checked non-matches in green, later skipped checks in blue, and unavailable measurements in gray. Set membership can also be transitive through a chain of direct matches.

Workflow: choose a folder, start analysis, review the resulting sets, then use **Delete marked** after confirming the preview. Photos are kept unless explicitly marked **Delete**; older Review or Favorite decisions are treated as Keep. Use `K` to keep, `D` or `Delete` to mark deletion, arrow keys to navigate, and `C` to toggle comparison.

Starting a scan clears the previous result sets. To run an optimized build from VS Code without attaching the debugger, select the **pCull.App (Release, no debugger)** launch configuration in Run and Debug, or use **Tasks: Run Task** and select **Run app (Release, no debugger)**. Both build in Release and start the app without attaching a debugger.

Scans analyze photos in parallel (2–4 workers) and decode JPEGs at reduced size (shrink-on-load) for thumbnails, hashes and embeddings. Perceptual hash and embedding values can therefore differ slightly from full-resolution decoding; cached values from earlier scans are kept as-is.

Image formats supported

- Magick.NET (all decoding): JPEG, PNG, WEBP, TIFF
- HEIC/HEIF, RAW formats (CR2, CR3, DNG, NEF, ARW, ORF, RAF) — requires native delegates (libheif, raw decoders) available to ImageMagick/Magick.NET on the host system.