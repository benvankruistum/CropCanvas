# Crop Tool

## Aspect Ratio

The crop selection is always locked to a fixed aspect ratio. By default, it matches your screen resolution.

### Auto-detect
Your screen resolution is shown in the toolbar (e.g., "5120x1440"). The crop ratio is automatically calculated (e.g., 32:9).

### Custom Ratio
Check "Custom" to enter a manual ratio. Type the width and height values.

### Presets
Quick buttons for common ratios:
- **16:9** — Standard widescreen
- **21:9** — Ultrawide
- **16:10** — MacBook/laptop displays
- **4:3** — Classic/tablet

## Crop Interaction

### Moving
Click and drag anywhere inside the crop selection to move it.

### Resizing
Drag any **corner handle** (small blue squares) to resize. The opposite corner stays anchored. The aspect ratio is always preserved.

### Edge Snapping
Hold **Shift** while dragging or resizing. When any edge of the crop comes within 20 pixels of an image edge, it snaps to that edge.

- **Shift + Drag**: crop position snaps to left/right/top/bottom edges
- **Shift + Resize**: the resized edge snaps to the nearest image boundary

## Rule of Thirds Grid

A dashed grid divides the crop into 9 equal sections. Uses dual-layer rendering (dark shadow + white dashes) for visibility on any background.

## Output

### Format
Choose JPEG or PNG in the toolbar dropdown.

### Quality
JPEG quality is set to 95 by default (configurable in settings.json).

### File Naming
Output files are named `{original}_crop.jpg` (or `.png`). If a file already exists, a number suffix is added: `{original}_crop_1.jpg`.

### Resolution
The crop is extracted from the **original** image pixels, not the scaled display. A crop on a 50MP image produces a full-resolution output.
