"""NUP Extraction & Inspection Engine.

Wraps the reference NUP library with high-level functions for:
- Container metadata & package inspection
- Base64 icon extraction
- Interactive tree traversal & fast file search
- Robust multi-package & subtree extraction with real-time callbacks
- Integrity checking (Fast & Deep SHA-256 validation)
- Native Windows File & Folder pickers
"""

from __future__ import annotations

import base64
import hashlib
import os
import subprocess
import sys
import threading
from dataclasses import dataclass
from typing import Any, Callable, Dict, List, Optional, Tuple

import nup.access as NA
import nup.format as NF
import nup.read as NR


def format_bytes(size: int) -> str:
    """Format bytes into human-readable string (KB, MB, GB)."""
    if size < 1024:
        return f"{size} B"
    elif size < 1024 * 1024:
        return f"{size / 1024:.2f} KB"
    elif size < 1024 * 1024 * 1024:
        return f"{size / (1024 * 1024):.2f} MB"
    else:
        return f"{size / (1024 * 1024 * 1024):.2f} GB"


def pick_file_native(title: str = "Select NUP Container") -> Optional[str]:
    """Open a native Windows file open dialog to select a .nup file."""
    result: list[Optional[str]] = [None]

    def _worker():
        try:
            import tkinter as tk
            from tkinter import filedialog
            root = tk.Tk()
            root.withdraw()
            root.attributes("-topmost", True)
            path = filedialog.askopenfilename(
                title=title,
                filetypes=[
                    ("NUP Game Containers", "*.nup"),
                    ("All files", "*.*")
                ]
            )
            root.destroy()
            if path:
                result[0] = os.path.normpath(path)
        except Exception as e:
            print(f"File dialog error: {e}", file=sys.stderr)

    t = threading.Thread(target=_worker)
    t.start()
    t.join(timeout=60)
    return result[0]


def pick_folder_native(title: str = "Select Destination Folder") -> Optional[str]:
    """Open a native Windows folder browser dialog."""
    result: list[Optional[str]] = [None]

    def _worker():
        try:
            import tkinter as tk
            from tkinter import filedialog
            root = tk.Tk()
            root.withdraw()
            root.attributes("-topmost", True)
            path = filedialog.askdirectory(title=title)
            root.destroy()
            if path:
                result[0] = os.path.normpath(path)
        except Exception as e:
            print(f"Folder dialog error: {e}", file=sys.stderr)

    t = threading.Thread(target=_worker)
    t.start()
    t.join(timeout=60)
    return result[0]


def open_folder_in_explorer(path: str) -> bool:
    """Open a directory or highlight a file in Windows File Explorer."""
    try:
        norm = os.path.normpath(path)
        if os.path.exists(norm):
            os.startfile(norm)
            return True
    except Exception as e:
        print(f"Could not open explorer: {e}", file=sys.stderr)
    return False


def inspect_nup(file_path: str) -> Dict[str, Any]:
    """Inspect a .nup file and extract full metadata, package records, and stats."""
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"File not found: {file_path}")

    with NR.NupReader(file_path) as r:
        s = r.super
        uuid_hex = s["uuid"].hex()
        # Formatted UUID: 8-4-4-4-12 style
        formatted_uuid = (
            f"{uuid_hex[:8]}-{uuid_hex[8:12]}-{uuid_hex[12:16]}-"
            f"{uuid_hex[16:20]}-{uuid_hex[20:]}"
        )

        packages_data = []
        primary_title_name: Optional[str] = None
        primary_publisher: Optional[str] = None
        primary_display_version: Optional[str] = None
        primary_icon_b64: Optional[str] = None

        total_uncompressed = 0
        total_compressed = 0

        for pkg in r.packages:
            meta = NA.app_meta(r, pkg)
            
            # Extract basic metadata
            title_name_b = meta.get(NF.TLV_TITLE_NAME)
            title_name = title_name_b.decode("utf-8", "replace") if title_name_b else None

            publisher_b = meta.get(NF.TLV_PUBLISHER)
            publisher = publisher_b.decode("utf-8", "replace") if publisher_b else None

            disp_ver_b = meta.get(NF.TLV_DISPLAY_VERSION)
            disp_ver = disp_ver_b.decode("utf-8", "replace") if disp_ver_b else None

            b_id = NA.build_id(r, pkg)
            build_id_hex = b_id.hex().upper() if b_id else None

            src_dump_b = meta.get(NF.TLV_SOURCE_DUMP)
            src_dump = src_dump_b.decode("utf-8", "replace") if src_dump_b else None

            # Localized titles
            loc_titles = {}
            for lang_b, name_b in NA.localized_titles(r, pkg).items():
                try:
                    loc_titles[lang_b.decode("utf-8")] = name_b.decode("utf-8")
                except Exception:
                    pass

            # Icon extraction (convert to Base64 data URI)
            icon_data_uri = None
            icon_bytes = meta.get(NF.TLV_ICON)
            if icon_bytes:
                # Detect MIME type (PNG vs JPEG)
                mime = "image/png" if icon_bytes.startswith(b"\x89PNG") else "image/jpeg"
                icon_data_uri = f"data:{mime};base64,{base64.b64encode(icon_bytes).decode('ascii')}"

            # Set primary metadata from base package or first package
            if pkg.kind == NF.KIND_BASE or primary_title_name is None:
                if title_name:
                    primary_title_name = title_name
                if publisher:
                    primary_publisher = publisher
                if disp_ver:
                    primary_display_version = disp_ver
                if icon_data_uri:
                    primary_icon_b64 = icon_data_uri

            # Walk package tree to calculate sizes and file counts
            pkg_file_count, pkg_uncompressed, pkg_compressed = _calculate_pkg_stats(r, pkg.root_dir)
            total_uncompressed += pkg_uncompressed
            total_compressed += pkg_compressed

            # Subtree root pointers
            has_exefs = pkg.exefs_dir != NF.NO_DIR
            has_romfs = pkg.romfs_dir != NF.NO_DIR

            packages_data.append({
                "index": pkg.index,
                "kind": pkg.kind,
                "kind_name": pkg.kind_name,
                "title_id": f"{pkg.title_id:016X}",
                "title_id_int": pkg.title_id,
                "version": pkg.version,
                "version_str": pkg.version_str,
                "display_version": disp_ver or pkg.version_str,
                "title_name": title_name or f"Title {pkg.title_id:016X}",
                "publisher": publisher or "Unknown Publisher",
                "build_id": build_id_hex,
                "source_dump": src_dump,
                "localized_titles": loc_titles,
                "has_icon": bool(icon_bytes),
                "icon_preview": icon_data_uri,
                "has_exefs": has_exefs,
                "has_romfs": has_romfs,
                "root_dir_index": pkg.root_dir,
                "exefs_dir_index": pkg.exefs_dir,
                "romfs_dir_index": pkg.romfs_dir,
                "file_count": pkg_file_count,
                "uncompressed_bytes": pkg_uncompressed,
                "uncompressed_formatted": format_bytes(pkg_uncompressed),
                "compressed_bytes": pkg_compressed,
                "compressed_formatted": format_bytes(pkg_compressed),
            })

        # Calculate overall compression ratio
        compression_ratio = 0.0
        if total_uncompressed > 0:
            compression_ratio = round((1 - (s["file_size"] / total_uncompressed)) * 100, 1)

        filename = os.path.basename(file_path)
        base_name = os.path.splitext(filename)[0]

        return {
            "file_path": os.path.abspath(file_path),
            "file_name": filename,
            "base_name": base_name,
            "file_size": s["file_size"],
            "file_size_formatted": format_bytes(s["file_size"]),
            "superblock": {
                "magic": "NUP1",
                "header_size": s["header_size"],
                "header_version": s["header_version"],
                "toc_offset": s["toc_offset"],
                "toc_size": s["toc_size"],
                "toc_sha256": s["toc_sha256"].hex(),
                "blob_offset": s["blob_offset"],
                "package_count": s["package_count"],
                "uuid": formatted_uuid,
            },
            "primary_meta": {
                "title_name": primary_title_name or base_name,
                "publisher": primary_publisher or "Nintendo Switch Dump",
                "display_version": primary_display_version or "1.0.0",
                "icon": primary_icon_b64,
            },
            "packages": packages_data,
            "stats": {
                "total_dirs": r.dir_count,
                "total_files": r.file_count,
                "total_uncompressed_bytes": total_uncompressed,
                "total_uncompressed_formatted": format_bytes(total_uncompressed),
                "compression_ratio": max(0.0, compression_ratio),
            }
        }


def _calculate_pkg_stats(r: NR.NupReader, dir_index: int) -> Tuple[int, int, int]:
    """Calculate file count, uncompressed bytes, and extent bytes for a subtree."""
    count = 0
    uncompressed = 0
    compressed = 0
    stack = [dir_index]

    while stack:
        di = stack.pop()
        for child in NA.iter_children(r, di):
            if child.is_dir:
                stack.append(child.index)
            else:
                count += 1
                _, size, _, _, esz, _ = r.files[child.index]
                uncompressed += size
                compressed += esz

    return count, uncompressed, compressed


def get_dir_children(
    file_path: str,
    dir_index: int,
    recursive: bool = False,
    max_depth: int = 1,
    current_depth: int = 0
) -> Dict[str, Any]:
    """Fetch directory nodes and file items for lazy-loaded tree explorer."""
    with NR.NupReader(file_path) as r:
        if dir_index >= r.dir_count:
            raise IndexError("Directory index out of bounds")

        dname = r.dirs[dir_index][0].decode("utf-8", "replace")
        children = []

        for child in NA.iter_children(r, dir_index):
            name = child.name.decode("utf-8", "replace")
            if child.is_dir:
                sub_children = []
                if recursive and current_depth < max_depth:
                    sub = get_dir_children(
                        file_path, child.index, recursive, max_depth, current_depth + 1
                    )
                    sub_children = sub.get("children", [])
                
                # Check if folder has items
                has_kids = r.dirs[child.index][2] > 0
                children.append({
                    "type": "dir",
                    "index": child.index,
                    "name": name,
                    "has_children": has_kids,
                    "child_count": r.dirs[child.index][2],
                    "children": sub_children
                })
            else:
                fi = child.index
                _, size, sha, eoff, esz, codec = r.files[fi]
                children.append({
                    "type": "file",
                    "index": fi,
                    "name": name,
                    "size": size,
                    "size_formatted": format_bytes(size),
                    "extent_size": esz,
                    "extent_size_formatted": format_bytes(esz),
                    "codec": "zstd" if codec == NF.CODEC_ZSTD else "raw",
                    "sha256": sha.hex()
                })

        return {
            "type": "dir",
            "index": dir_index,
            "name": dname or "root",
            "children": children
        }


def search_files(file_path: str, query: str, limit: int = 100) -> List[Dict[str, Any]]:
    """Fast search across all files in the container."""
    query_lower = query.lower()
    results = []

    with NR.NupReader(file_path) as r:
        for fi, (name_b, size, sha, eoff, esz, codec) in enumerate(r.files):
            name = name_b.decode("utf-8", "replace")
            if query_lower in name.lower():
                results.append({
                    "index": fi,
                    "name": name,
                    "size": size,
                    "size_formatted": format_bytes(size),
                    "extent_size": esz,
                    "extent_size_formatted": format_bytes(esz),
                    "codec": "zstd" if codec == NF.CODEC_ZSTD else "raw",
                    "sha256": sha.hex()
                })
                if len(results) >= limit:
                    break

    return results


def extract_single_file(file_path: str, file_index: int, dest_path: str) -> str:
    """Extract and decompress a single file directly to dest_path."""
    with NR.NupReader(file_path) as r:
        if file_index >= r.file_count:
            raise IndexError("File index out of bounds")
        data = r.read_file(file_index)
        os.makedirs(os.path.dirname(dest_path), exist_ok=True)
        with open(dest_path, "wb") as f:
            f.write(data)
        return dest_path


def extract_container(
    file_path: str,
    dest_dir: str,
    mode: str = "all",  # "all", "package", "subtree"
    package_index: Optional[int] = None,
    subtree_path: Optional[str] = None,
    subtree_dir_index: Optional[int] = None,
    progress_callback: Optional[Callable[[Dict[str, Any]], None]] = None
) -> Dict[str, Any]:
    """
    Extract contents from a .nup container.
    Supports:
    - mode="all": extracts all packages into structured dirs (base/, updates/<ver>/, dlc/<id>/<idx>/)
    - mode="package": extracts a single package
    - mode="subtree": extracts a specific folder/subtree
    """
    if not os.path.exists(file_path):
        raise FileNotFoundError(f"File not found: {file_path}")

    os.makedirs(dest_dir, exist_ok=True)

    with NR.NupReader(file_path) as r:
        # Pre-count total files for progress calculation
        files_to_extract: list[Tuple[int, str]] = []  # (file_index, relative_dest_path)

        if mode == "subtree":
            target_dir = subtree_dir_index
            if target_dir is None and package_index is not None and subtree_path:
                pkg = r.packages[package_index]
                ref = NA.resolve_path(r, pkg, subtree_path)
                if ref is None or not ref.is_dir:
                    raise ValueError(f"Subtree path not found or not a directory: {subtree_path}")
                target_dir = ref.index

            if target_dir is None:
                raise ValueError("Must provide subtree_dir_index or subtree_path")

            _gather_subtree_files(r, target_dir, "", files_to_extract)

        elif mode == "package":
            if package_index is None or package_index >= len(r.packages):
                raise ValueError(f"Invalid package index: {package_index}")
            pkg = r.packages[package_index]
            _gather_subtree_files(r, pkg.root_dir, "", files_to_extract)

        else:  # mode == "all"
            has_base = any(p.kind == NF.KIND_BASE for p in r.packages)
            only_one_pkg = len(r.packages) == 1

            for pkg in r.packages:
                if only_one_pkg:
                    rel_prefix = ""
                elif pkg.kind == NF.KIND_BASE:
                    rel_prefix = "base"
                elif pkg.kind == NF.KIND_UPDATE:
                    rel_prefix = os.path.join("updates", str(pkg.version))
                elif pkg.kind in (NF.KIND_ADDON, NF.KIND_ADDON_UPDATE):
                    rel_prefix = os.path.join("dlc", f"{pkg.title_id:016x}", str(pkg.index))
                else:
                    rel_prefix = f"pkg_{pkg.index}_{pkg.kind_name}"

                _gather_subtree_files(r, pkg.root_dir, rel_prefix, files_to_extract)

        total_files = len(files_to_extract)
        total_uncompressed = sum(r.files[fi][1] for fi, _ in files_to_extract)
        bytes_written = 0

        if progress_callback:
            progress_callback({
                "status": "starting",
                "current": 0,
                "total": total_files,
                "percent": 0.0,
                "file_name": "",
                "bytes_written": 0,
                "total_bytes": total_uncompressed
            })

        for i, (fi, rel_path) in enumerate(files_to_extract, start=1):
            target_full_path = os.path.join(dest_dir, rel_path)
            os.makedirs(os.path.dirname(target_full_path), exist_ok=True)

            fname = os.path.basename(target_full_path)
            data = r.read_file(fi)
            with open(target_full_path, "wb") as f:
                f.write(data)

            bytes_written += len(data)
            pct = round((i / total_files) * 100, 1) if total_files > 0 else 100.0

            if progress_callback:
                progress_callback({
                    "status": "extracting",
                    "current": i,
                    "total": total_files,
                    "percent": pct,
                    "file_name": rel_path.replace("\\", "/"),
                    "file_size": len(data),
                    "bytes_written": bytes_written,
                    "total_bytes": total_uncompressed
                })

        if progress_callback:
            progress_callback({
                "status": "completed",
                "current": total_files,
                "total": total_files,
                "percent": 100.0,
                "file_name": "Done",
                "bytes_written": bytes_written,
                "total_bytes": total_uncompressed,
                "dest_dir": dest_dir
            })

        return {
            "success": True,
            "total_files": total_files,
            "bytes_extracted": bytes_written,
            "bytes_formatted": format_bytes(bytes_written),
            "dest_dir": os.path.abspath(dest_dir)
        }


def _gather_subtree_files(
    r: NR.NupReader,
    dir_index: int,
    rel_base: str,
    out_list: list[Tuple[int, str]]
):
    """Recursively collect file indices and relative destination paths."""
    stack = [(dir_index, rel_base)]
    while stack:
        di, prefix = stack.pop()
        for child in NA.iter_children(r, di):
            name = child.name.decode("utf-8", "surrogateescape")
            child_rel = os.path.join(prefix, name) if prefix else name
            if child.is_dir:
                stack.append((child.index, child_rel))
            else:
                out_list.append((child.index, child_rel))


def verify_integrity(
    file_path: str,
    deep: bool = True,
    progress_callback: Optional[Callable[[Dict[str, Any]], None]] = None
) -> Dict[str, Any]:
    """
    Verify container conformance and SHA-256 hashes.
    deep=True decompresses and verifies SHA-256 of every file.
    """
    import nup.verify as NV
    problems = []

    try:
        r = NR.NupReader(file_path)
    except NF.NupError as e:
        return {"valid": False, "problems": [f"Header/TOC validation failed: {e}"]}

    try:
        s = r.super
        if s["toc_offset"] != NF.HEADER_SIZE:
            problems.append(f"TOC offset {s['toc_offset']:#x} != {NF.HEADER_SIZE:#x}")
        if s["file_size"] < s["blob_offset"]:
            problems.append("File size smaller than blob offset")

        total_files = r.file_count
        if deep:
            for fi in range(total_files):
                name, size, sha, eoff, esz, codec = r.files[fi]
                fname = name.decode("utf-8", "replace")
                try:
                    data = r.read_file(fi)
                    if hashlib.sha256(data).digest() != sha:
                        problems.append(f"File #{fi} ({fname}): SHA-256 mismatch")
                except Exception as ex:
                    problems.append(f"File #{fi} ({fname}): Read/decompress failed: {ex}")

                if progress_callback and (fi % 10 == 0 or fi == total_files - 1):
                    pct = round(((fi + 1) / total_files) * 100, 1)
                    progress_callback({
                        "status": "verifying",
                        "current": fi + 1,
                        "total": total_files,
                        "percent": pct,
                        "file_name": fname
                    })
    finally:
        r.close()

    return {
        "valid": len(problems) == 0,
        "problems": problems,
        "total_files_checked": r.file_count,
        "mode": "deep" if deep else "structure_only"
    }
