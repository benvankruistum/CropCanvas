# Architecture

## Pattern: MVVM

The application follows the Model-View-ViewModel pattern using CommunityToolkit.Mvvm with source generators (`[ObservableProperty]`, `[RelayCommand]`).

## Layer Overview

```
┌─────────────────────────────────────────────────┐
│  Views (XAML)                                    │
│  ├── MainWindow.xaml — UI layout                │
│  └── MainWindow.xaml.cs — Property sync          │
├─────────────────────────────────────────────────┤
│  ViewModels                                      │
│  └── MainViewModel — State + command dispatch    │
├─────────────────────────────────────────────────┤
│  Controls                                        │
│  ├── CropOverlay — Interactive crop control      │
│  ├── CropOverlayRenderer — Visual rendering      │
│  └── CropInteractionHandler — Mouse handling     │
├─────────────────────────────────────────────────┤
│  Services                                        │
│  ├── ImageService — Image I/O + EXIF handling    │
│  ├── ComfyUIService — Local AI outpainting       │
│  ├── StabilityAIService — Cloud AI outpainting   │
│  ├── ComfyUIWorkflowBuilder — Workflow JSON      │
│  ├── PaddingCalculator — Crop padding math       │
│  ├── SettingsService — JSON persistence          │
│  └── ScreenService — Screen resolution           │
├─────────────────────────────────────────────────┤
│  Config                                          │
│  ├── ImageConfig — Extensions, sizes             │
│  ├── ComfyUIConfig — API settings, defaults      │
│  └── StabilityAIConfig — API limits              │
├─────────────────────────────────────────────────┤
│  Models                                          │
│  ├── AppSettings — Persistence DTO               │
│  └── ImageItem — Observable image metadata       │
├─────────────────────────────────────────────────┤
│  Resources                                       │
│  └── Strings.resx — Localization (EN/NL/DE/FR)   │
└─────────────────────────────────────────────────┘
```

## Key Design Decisions

### Normalized Coordinates (0.0-1.0)
The crop overlay works in normalized coordinates relative to the image dimensions. This makes the system resolution-independent — window resizing, DPI scaling, and coordinate mapping between display and original pixels all work correctly.

### BitmapImage for EXIF
WPF's `BitmapImage` automatically handles EXIF rotation. Both display and crop operations use `BitmapImage` to ensure consistency.

### Three-Tier Image Loading
1. **Dimensions only** — Small decode (32px) just to get aspect ratio
2. **Thumbnail** — 150px wide for sidebar
3. **Display** — 1400px wide for the canvas
4. **Full resolution** — Only loaded at crop time

### IOutpaintProvider Interface
Both `ComfyUIService` and `StabilityAIService` implement `IOutpaintProvider`, allowing the ViewModel to work with either provider without branching.

### Service Separation
- **CropOverlayRenderer** — Pure rendering logic, no state
- **CropInteractionHandler** — Mouse input processing, no rendering
- **PaddingCalculator** — Pure math, no dependencies
- **ComfyUIWorkflowBuilder** — JSON construction, isolated from API calls

## Data Flow

```
User Interaction
    ↓
CropOverlay (display coords)
    ↓ ↑ (two-way binding)
MainViewModel (normalized 0.0–1.0)
    ↓
ImageService.CropAndSave() ←── normal crop
    or
OutpaintAsync() ←── if crop extends beyond image
    ├── PaddingCalculator
    ├── ImageService.CropToBytes() (visible portion only)
    └── IOutpaintProvider.OutpaintAsync()
        ├── ComfyUIService (local)
        └── StabilityAIService (cloud)
```

## Localization

Uses .NET's built-in `ResourceManager` with `.resx` files:
- `Strings.resx` — English (default/fallback)
- `Strings.nl.resx` — Dutch
- `Strings.de.resx` — German
- `Strings.fr.resx` — French

Language is switched by setting `Thread.CurrentThread.CurrentUICulture`.
