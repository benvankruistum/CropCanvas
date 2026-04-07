# Configuration

## Settings File

All settings are stored in:
```
%APPDATA%\BackgroundCropper\settings.json
```

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SourceFolderPath` | string | null | Last used source folder |
| `OutputFolderPath` | string | null | Last used output folder |
| `AspectRatioWidth` | int | 16 | Custom aspect ratio width |
| `AspectRatioHeight` | int | 9 | Custom aspect ratio height |
| `UseCustomAspectRatio` | bool | false | Use custom ratio instead of screen detection |
| `OutputFormat` | enum | Jpeg | Output format: `Jpeg` or `Png` |
| `JpegQuality` | int | 95 | JPEG compression quality (1-100) |
| `OutpaintProvider` | enum | Geen | AI provider: `Geen`, `ComfyUI`, or `StabilityAI` |
| `StabilityApiKey` | string | null | Stability AI API key |
| `Language` | string | "nl" | UI language: `nl`, `en`, `de`, `fr` |

### Example
```json
{
  "SourceFolderPath": "C:\\Photos\\Wallpapers",
  "OutputFolderPath": "C:\\Photos\\Cropped",
  "AspectRatioWidth": 32,
  "AspectRatioHeight": 9,
  "UseCustomAspectRatio": true,
  "OutputFormat": 0,
  "JpegQuality": 95,
  "OutpaintProvider": 1,
  "StabilityApiKey": "sk-...",
  "Language": "en"
}
```

## Language

Switch language via the dropdown in the toolbar. Available:
- **NL** — Nederlands (default)
- **EN** — English
- **DE** — Deutsch
- **FR** — Francais

The language setting takes effect immediately and persists between sessions.

## Supported Image Formats

- JPEG (.jpg, .jpeg)
- PNG (.png)
- BMP (.bmp)
- WebP (.webp) — requires Windows 10 1809+
