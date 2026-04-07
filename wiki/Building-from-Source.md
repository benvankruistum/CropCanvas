# Building from Source

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10/11 (WPF is Windows-only)

## Clone and Build

```bash
git clone https://github.com/yourusername/BackgroundCropper.git
cd BackgroundCropper
dotnet restore BackgroundCropper/BackgroundCropper.csproj
dotnet build BackgroundCropper/BackgroundCropper.csproj
```

## Run

```bash
dotnet run --project BackgroundCropper/BackgroundCropper.csproj
```

## Publish (Self-Contained)

Create a single-file executable that includes the .NET runtime:

```bash
dotnet publish BackgroundCropper/BackgroundCropper.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o publish/
```

The output is a single `BackgroundCropper.exe` (~150MB) that runs on any Windows 10/11 machine without .NET installed.

## Project Structure

```
BackgroundCropper.sln          # Solution file
BackgroundCropper/
├── BackgroundCropper.csproj   # Project file
├── App.xaml[.cs]              # Application entry + theme resources
├── Config/                    # Configuration constants
├── Controls/                  # Custom WPF controls
├── Converters/                # XAML value converters
├── Fonts/                     # Tabler Icons font
├── Models/                    # Data models
├── Resources/                 # Localization (.resx files)
├── Services/                  # Business logic
│   └── Interfaces/            # Service abstractions
├── ViewModels/                # MVVM ViewModels
└── Views/                     # XAML views + code-behind
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm | 8.4.1 | MVVM framework with source generators |

All other dependencies are built-in (.NET 8 / WPF / WinForms for dialogs).
