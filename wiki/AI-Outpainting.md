# AI Outpainting

Outpainting uses AI to extend an image beyond its original boundaries. This is useful when your desired crop ratio doesn't match the image — the AI generates the missing areas.

## How It Works

1. Select an AI provider in the toolbar (ComfyUI or Stability AI)
2. Drag the crop selection **beyond the image edges** — red warning areas appear with an "AI" label
3. Click **"AI + Crop"** at the bottom
4. The app sends only the visible portion of the image + padding dimensions to the AI
5. The AI generates the extended image
6. The result replaces the current display — you can now crop normally

## ComfyUI (Local, Free)

### Setup
1. Download and install [ComfyUI](https://github.com/comfyanonymous/ComfyUI)
2. Download an inpainting checkpoint:
   ```bash
   # Example: SD 1.5 Inpainting (~4GB)
   curl -L -o ComfyUI/models/checkpoints/sd-v1-5-inpainting.ckpt \
     https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-inpainting/resolve/main/sd-v1-5-inpainting.ckpt
   ```
3. Start ComfyUI (runs on `127.0.0.1:8188` by default)
4. In BackgroundCropper, select "ComfyUI" as AI provider

### How It Connects
- REST API for image upload, workflow submission, and result download
- WebSocket for real-time progress (step-by-step generation feedback)
- Falls back to polling if WebSocket is unavailable

### Workflow
The app automatically builds a ComfyUI workflow with:
- `CheckpointLoaderSimple` — loads the inpainting model
- `ImagePadForOutpaint` — pads the image with feathering (40px)
- `VAEEncodeForInpaint` — prepares for inpainting
- `KSampler` — generates content (25 steps, euler sampler, CFG 7.0)
- `VAEDecode` + `SaveImage` — produces the final result

### Requirements
- NVIDIA GPU recommended (8GB+ VRAM)
- First generation is slower (model loading), subsequent ones are faster

## Stability AI (Cloud)

### Setup
1. Create an account at [platform.stability.ai](https://platform.stability.ai)
2. Generate an API key
3. In BackgroundCropper, select "Stability AI" and enter your key

### Cost
~4 credits ($0.04 USD) per outpaint operation. No charge for failed operations.

### Automatic Adjustments
The app handles Stability AI's constraints automatically:
- **Max padding**: 2000px per direction — images are scaled down if needed
- **Aspect ratio**: Must be between 1:2.5 and 2.5:1 — extra padding is added if needed (e.g., for ultrawide 32:9 ratios)
- **Max total pixels**: 9.4 megapixels — images are scaled proportionally

### API Limits
- Image formats: JPEG, PNG, WebP
- Max request size: 10 MB
- Rate limit: 150 requests per 10 seconds

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "ComfyUI not reachable" | Ensure ComfyUI is running on port 8188 |
| "No model found" | Download an inpainting checkpoint to ComfyUI/models/checkpoints/ |
| "Stability AI error 400" | Check your API key and ensure the image meets format requirements |
| Timeout after 3 minutes | The image or padding may be too large — try a smaller extension |
| Aspect ratio warning | For ultrawide screens, the app adds vertical padding automatically |
