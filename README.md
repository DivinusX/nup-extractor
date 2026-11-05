# NUP Extractor

<p align="center">
  <img src="app_icon.ico" alt="NUP Extractor Logo" width="96" height="96">
</p>

<p align="center">
  <strong>A high-performance native desktop tool for inspecting, browsing, verifying, and extracting Nintendo Switch 1 & 2 clean-room <code>.nup</code> container archives.</strong>
</p>

<p align="center">
  🌐 <strong>Official Showcase &amp; Live Web Page:</strong> <a href="https://spin0zai.github.io/nup-extractor/"><strong>spin0zai.github.io/nup-extractor</strong></a>
</p>

<p align="center">
  <a href="https://spin0zai.github.io/nup-extractor/"><img src="https://img.shields.io/badge/Website-spin0zai.github.io%2Fnup--extractor-ff2a44?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Live Website"></a>
</p>

<p align="center">
  <a href="https://github.com/SPIN0ZAi/nup-extractor/releases/latest"><img src="https://img.shields.io/badge/Release-Latest-00d4ff.svg?style=flat-square" alt="Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green.svg?style=flat-square" alt="License"></a>
  <a href="https://dotnet.microsoft.com"><img src="https://img.shields.io/badge/.NET-10.0-purple.svg?style=flat-square" alt=".NET 10"></a>
  <a href="#"><img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D6.svg?style=flat-square" alt="Platform"></a>
  <a href="#"><img src="https://img.shields.io/badge/Tests-19%20Passing-success.svg?style=flat-square" alt="Tests"></a>
</p>

---

## 📸 Screenshots

<p align="center">
  <img src="pictures/extractor-main-UI.png" alt="NUP Extractor Main UI" width="95%">
  <br>
  <em>Container Workspace — Embedded game artwork, metadata, Title/Build IDs, multi-package tabs, interactive file tree, and extraction controls.</em>
</p>

<br>

<p align="center">
  <img src="pictures/main-UI.png" alt="Welcome Dropzone" width="48%">
  &nbsp;&nbsp;
  <img src="pictures/extractor-progress-UI.png" alt="Extraction Progress" width="48%">
</p>
<p align="center">
  <em>Left: Initial welcome dropzone screen &bull; Right: High-speed extraction modal with live progress & activity log.</em>
</p>

---

## ⚡ Overview

**NUP Extractor** is a standalone 64-bit Windows GUI utility built to handle **NX Package (`.nup`)** containers—a modern clean-room specification designed by [a55uka](https://github.com/a55uka/nup) for archiving unencrypted Nintendo Switch (1 and 2) game dumps.

Rather than relying on console scripts or heavy web runtimes, NUP Extractor is engineered as a **pure native Windows application (`WinExe`)** featuring:
- **Zero Command Prompt Window**: Launches directly into a sleek, hardware-accelerated desktop window.
- **Zero UI Freezing ("Not Responding")**: Employs 64-bit memory-mapped file I/O (`MemoryMappedFile`) and asynchronous background workers (`Task.Run`), ensuring the UI stays completely fluid at 60+ FPS even when extracting 30GB game dumps.
- **SIMD-Accelerated Streaming Decompression**: High-throughput Zstandard (`ZstdSharp`) pipeline with 128KB buffered writes.
- **In-Memory IPC (No Localhost / Sockets)**: Zero network ports, zero socket binding, and no Windows Firewall alerts.

---

## ✨ Features

- **🎨 Modern Switch Glassmorphic UI**: High-contrast obsidian theme with iconic Nintendo Joy-Con neon red and cyan accents, collapsible folder tree, and animated progress gauges.
- **🖼️ Rich Embedded Metadata**: Displays game icons (PNG/JPEG), Title Name, Publisher, Display Version, Title ID, and Build ID (NSO ModuleId) with 1-click clipboard copy buttons.
- **📦 Multi-Package Support**: Inspects all packages bundled inside the container:
  - **Base Game** (`base/`)
  - **Updates & Patches** (`updates/<version>/`)
  - **DLC & Add-ons** (`dlc/<title_id>/<index>/`)
- **📂 Interactive File Explorer**:
  - Live directory tree navigation with file counters and size badges.
  - Per-file compression codec badges (**RAW** vs **Zstd**) and compression ratio stats.
  - Sub-millisecond instant search across thousands of files.
- **⚡ Flexible Extraction Engine**:
  - **Full Game Dump**: Reconstructs the complete game directory hierarchy.
  - **Package Mode**: Extracts only the selected package tab.
  - **Subtree Mode**: Extracts specific folders (e.g. only `romfs/` or `exefs/`).
  - Native Windows File Explorer folder pickers with auto-open upon completion.
- **🛡️ Integrity Verification**:
  - Validates Superblock structure and Table of Contents (TOC) SHA-256 hash.
  - Deep-verifies individual SHA-256 checksums across every file extent.
- **🔄 In-Place Auto-Updater**:
  - Automatic background checks on startup or on-demand via the **Updates** button.
  - User controls: **Update Now**, **Remind Me Later**, or **Skip This Version**.
  - High-speed streaming download with live MB/s progress telemetry.
  - Seamless in-place replacement and automatic restart without leaving behind temporary files or modifying install directories.

---

## 🚀 Download & Installation

### Option 1: Direct Download (Recommended)
Download the latest pre-compiled standalone executable from the [Releases](https://github.com) page:

1. Download **`NUP_Extractor-v1.0.0-windows-x64.zip`**.
2. Extract the ZIP anywhere (e.g. Desktop).
3. Double-click **`NUP_Extractor.exe`** to run.

*(No .NET SDK or Python installation required—everything is self-contained!)*

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 / 11 (64-bit)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### 1. Clone the Repository
```powershell
git clone https://github.com/<your-username>/nup-extractor.git
cd nup-extractor
```

### 2. Run Automated Tests
```powershell
dotnet test NupExtractor.slnx
```

### 3. Build & Publish Standalone Executable
You can run the one-click build script:
```powershell
.\build_native.bat
```

Or run the .NET CLI command:
```powershell
dotnet publish src_native/NupExtractorApp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist_native
```

The resulting `NUP_Extractor.exe` will be generated in `dist_native/` (or the project root).

---

## 🧪 Automated Test Suite

The solution includes an extensive **xUnit** automated regression test suite (`tests_native/`):

| Test Case | Scope |
| :--- | :--- |
| `Test_Superblock_Parsing_ValidContainer` | Verifies Superblock fields, versioning, and file size alignment |
| `Test_Superblock_Validation_RejectsBadMagic` | Enforces fail-closed validation on invalid/corrupted magic bytes |
| `Test_TOC_Parsing_PackagesAndNodes` | Validates package table, directory indices, and file nodes |
| `Test_Metadata_Extraction` | Verifies Title Name, Publisher, Version, Build ID, and Icon extraction |
| `Test_Inspect_ReturnsCompleteContract` | Validates full DTO contract structure for frontend consumption |
| `Test_Tree_Children_Resolution` | Tests lazy-loaded folder hierarchy traversal |
| `Test_File_Search` | Validates sub-millisecond file search engine |
| `Test_Decompression_RawAndZstd` | Verifies SHA-256 checksum matches on RAW and Zstd decompressed extents |
| `Test_Full_Extraction_AllPackages` | End-to-end multi-package extraction and directory reconstruction |
| `Test_Package_Mode_Extraction` | Validates isolated single package extraction |
| `Test_Subtree_Mode_Extraction` | Validates subtree directory extraction |
| `Test_Integrity_Verification` | Validates Fast TOC and Deep SHA-256 verification modes |
| `Test_Serialization_Contract_ProducesCamelCase` | Enforces camelCase JSON contract between C# backend and UI |
| `Test_Extraction_ThrowsOnEmptyPath` | Validates defensive argument checking |
| `Test_GetDirChildren_OutOfBoundsThrows` | Validates boundary verification on directory indices |

---

## 🏛️ Architecture

```
nup-extractor/
├── src_native/                    # C# .NET 10 Native Application
│   ├── NupFormat.cs               # Binary layout, records, and constants
│   ├── NupReader.cs               # 64-bit MemoryMappedFile reader & stream decompressor
│   ├── NupEngine.cs               # Core extraction, search, inspection, and verification engine
│   ├── MainWindow.xaml            # Native WPF Window hosting WebView2
│   └── MainWindow.xaml.cs         # Direct in-process IPC message dispatcher
├── tests_native/                  # xUnit automated test suite (15 tests)
│   └── NupTests.cs                # Test specifications & test cases
├── static/                        # Embedded UI Assets (HTML5, CSS3, JS)
│   ├── index.html                 # Joy-Con themed responsive UI layout
│   ├── styles.css                 # Glassmorphic dark styling & animations
│   └── app.js                     # IPC event handling & reactive UI controller
├── pictures/                      # Screenshots showcase
│   ├── main-UI.png                # Dropzone welcome screen
│   ├── extractor-main-UI.png      # Workspace with loaded game container
│   └── extractor-progress-UI.png  # Live extraction modal with progress log
├── docs/                          # Format specifications
│   └── SPEC.md                    # Official NUP container format specification
├── .github/workflows/             # GitHub Actions CI/CD
│   └── build-and-release.yml      # Automated testing, building, and GitHub release pipeline
├── release/                       # Pre-packaged release bundles
│   ├── NUP_Extractor-v1.0.0-windows-x64.zip
│   └── SHA256SUMS.txt
├── build_native.bat               # 1-Click CI Build & Test script
├── NupExtractor.slnx              # Modern .NET solution file
├── LICENSE                        # MIT License
└── README.md                      # Project documentation
```

---

## 📄 License & Legal Notice

- **Software License**: Distributed under the [MIT License](LICENSE).
- **Format Specification**: NUP format specification by [a55uka](https://github.com/a55uka/nup) (MIT License).
- **Disclaimer**: This is a clean-room interoperability archive extraction tool. It does **not** contain cryptographic keys (`prod.keys`), proprietary Nintendo code, or copyrighted game files. All game dumps must be legally acquired and unencrypted before use.
