# CropCanvas

Crop images to your exact screen ratio with AI-powered outpainting

## Features

- **Aspect ratio locked cropping** — Automatically detects your screen resolution and locks the crop selection to that exact ratio
- **Preset ratios** — Quick buttons for 16:9, 21:9, 16:10, 4:3, or enter a custom ratio
- **Rule of thirds grid** — Visual guide overlay for optimal composition
- **Shift+drag snapping** — Hold Shift while dragging to snap the crop to image edges (20px threshold)
- **AI Outpainting** — Extend images beyond their original boundaries using:
  - **ComfyUI** (local, free) — Requires a local ComfyUI installation with an inpainting model
  - **Stability AI** (cloud, ~$0.04/image) — Requires an API key from [platform.stability.ai](https://platform.stability.ai)
- **Batch-ready workflow** — Browse a folder of images, crop them one by one, output to a dedicated folder
- **Cropped indicator** — Green checkmark badge on already-cropped images
- **Multi-language** — Available in English, Dutch, German, and French
- **High-res output** — Crops from the original image pixels, not the scaled display
- **Dark theme** — Catppuccin Mocha color scheme with WCAG 2.1 AA contrast compliance

## Screenshot

*Coming soon*

## Requirements

- Windows 10/11
- .NET 8.0 Runtime (or build from source with .NET 8 SDK)
- For AI outpainting:
  - **ComfyUI**: Local installation at `127.0.0.1:8188` with an inpainting checkpoint
  - **Stability AI**: API key (get one at [platform.stability.ai](https://platform.stability.ai))

## Installation

### Option 1: Download Release
Download the latest release from the [Releases](../../releases) page.

### Option 2: Build from Source
```bash
git clone https://github.com/yourusername/CropCanvas.git
cd CropCanvas
dotnet build CropCanvas/CropCanvas.csproj
dotnet run --project CropCanvas/CropCanvas.csproj
```

## Quick Start

1. **Select a source folder** — Click the folder button and choose a directory with images
2. **Select an image** — Click a thumbnail in the sidebar
3. **Adjust the crop** — Drag to move, use corner handles to resize (ratio stays locked)
4. **Crop** — Click the "Crop" button at the bottom to save
5. **Select an output folder** — Choose where cropped images are saved

### Using AI Outpainting

1. Select an AI provider (ComfyUI or Stability AI) in the toolbar
2. Drag the crop selection **beyond the image edges** — red areas with "AI" label appear
3. Click "AI + Crop" — the AI fills in the missing areas, then you can crop the result

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Shift + Drag | Snap crop edges to image boundaries |
| Shift + Resize | Snap resized edge to nearest image edge |

## AI Outpainting Setup

### ComfyUI (Local, Free)

1. Install [ComfyUI](https://github.com/comfyanonymous/ComfyUI)
2. Download an inpainting model (e.g., `sd-v1-5-inpainting.ckpt`) to `ComfyUI/models/checkpoints/`
3. Start ComfyUI (default port 8188)
4. In CropCanvas, select "ComfyUI" as AI provider

### Stability AI (Cloud)

1. Create an account at [platform.stability.ai](https://platform.stability.ai)
2. Generate an API key
3. In CropCanvas, select "Stability AI" and enter your API key
4. Cost: ~4 credits ($0.04) per outpaint operation

## Configuration

Settings are stored in `%APPDATA%\CropCanvas\settings.json` and persist between sessions:
- Source and output folder paths
- Aspect ratio preference
- Output format (JPEG/PNG) and quality
- AI provider selection and API key
- Language preference

## Architecture

The project follows the MVVM pattern with clean service separation:

```
CropCanvas/
├── Config/           # Configuration constants
├── Controls/         # CropOverlay + renderer + interaction handler
├── Converters/       # XAML value converters
├── Models/           # Data models (AppSettings, ImageItem)
├── Resources/        # Localization strings (EN/NL/DE/FR)
├── Services/         # Business logic services
│   ├── Interfaces/   # Service abstractions
│   └── ...           # Image, ComfyUI, StabilityAI, Settings, Screen
├── ViewModels/       # MVVM ViewModels
└── Views/            # XAML views
```

## Tech Stack

- **WPF** (.NET 8, C#)
- **CommunityToolkit.Mvvm** for MVVM pattern
- **Tabler Icons** for UI iconography
- **System.Text.Json** for settings persistence

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push to the branch
5. Open a Pull Request

## License

MIT License - see [LICENSE](LICENSE) for details.
