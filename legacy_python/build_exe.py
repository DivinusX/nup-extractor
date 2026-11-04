"""Build script to compile NUP Extractor into a standalone Windows .exe using PyInstaller."""

import os
import subprocess
import sys
import shutil

def build():
    print("[*] Starting PyInstaller build for NUP Extractor...")

    # Ensure app icon exists
    if not os.path.isfile("app_icon.ico"):
        print("[!] Generating app_icon.ico...")
        from PIL import Image, ImageDraw
        sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
        images = []
        for s in sizes:
            w, h = s
            img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            draw = ImageDraw.Draw(img)
            margin = int(w * 0.05)
            rad = int(w * 0.22)
            draw.rounded_rectangle([margin, margin, w - margin, h - margin], radius=rad, fill=(18, 20, 29, 255), outline=(255, 255, 255, 40), width=max(1, int(w * 0.02)))
            gap = int(w * 0.04)
            mid_x = w // 2
            jc_w = int(w * 0.16)
            jc_h = int(h * 0.45)
            jc_y = (h - jc_h) // 2
            draw.rounded_rectangle([mid_x - gap - jc_w, jc_y, mid_x - gap, jc_y + jc_h], radius=int(jc_w * 0.6), fill=(0, 212, 255, 255))
            draw.rounded_rectangle([mid_x + gap, jc_y, mid_x + gap + jc_w, jc_y + jc_h], radius=int(jc_w * 0.6), fill=(255, 42, 68, 255))
            images.append(img)
        images[0].save("app_icon.ico", format="ICO", sizes=[(img.width, img.height) for img in images], append_images=images[1:])

    hidden_imports = [
        "uvicorn",
        "uvicorn.logging",
        "uvicorn.loops",
        "uvicorn.loops.auto",
        "uvicorn.protocols",
        "uvicorn.protocols.http",
        "uvicorn.protocols.http.auto",
        "uvicorn.protocols.websockets",
        "uvicorn.protocols.websockets.auto",
        "uvicorn.lifespans",
        "uvicorn.lifespans.on",
        "uvicorn.lifespans.off",
        "fastapi",
        "starlette",
        "starlette.routing",
        "starlette.middleware",
        "starlette.staticfiles",
        "zstandard",
        "nup",
        "nup.format",
        "nup.read",
        "nup.access",
        "nup.meta",
        "nup.verify",
        "nup.pack",
        "nup.discover",
        "nup.edit",
        "tkinter",
        "tkinter.filedialog",
    ]

    cmd = [
        sys.executable,
        "-m",
        "PyInstaller",
        "--name=NUP_Extractor",
        "--onefile",
        "--icon=app_icon.ico",
        "--add-data=static;static",
        "--collect-all=nup",
        "--clean",
        "-y",
    ]

    for h in hidden_imports:
        cmd.extend(["--hidden-import", h])

    cmd.append("run.py")

    print("[*] Running command:")
    print(" ".join(cmd))
    res = subprocess.run(cmd)

    if res.returncode == 0:
        exe_path = os.path.abspath(os.path.join("dist", "NUP_Extractor.exe"))
        print("\n" + "=" * 60)
        print("  BUILD SUCCESSFUL!")
        print(f"  Executable created at: {exe_path}")
        print("=" * 60)
        # Copy to root directory for easy access
        target_root_exe = os.path.abspath("NUP_Extractor.exe")
        try:
            shutil.copy2(exe_path, target_root_exe)
            print(f"  Copied to project root: {target_root_exe}")
        except Exception as e:
            print(f"  Note: {e}")
    else:
        print(f"[!] Build failed with exit code: {res.returncode}")
        sys.exit(res.returncode)

if __name__ == "__main__":
    build()
