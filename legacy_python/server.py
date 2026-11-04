"""FastAPI Backend Server for NUP Extractor UI."""

import asyncio
import os
import queue
import sys
import threading
import uuid
from typing import Any, Dict, Optional

import uvicorn
from fastapi import FastAPI, HTTPException, Query, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, StreamingResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

import nup_engine

app = FastAPI(title="NUP Extractor", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# In-memory storage for streaming tasks
PROGRESS_QUEUES: Dict[str, queue.Queue] = {}


class InspectRequest(BaseModel):
    file_path: str


class ExtractRequest(BaseModel):
    file_path: str
    dest_dir: str
    mode: str = "all"  # "all", "package", "subtree"
    package_index: Optional[int] = None
    subtree_path: Optional[str] = None
    subtree_dir_index: Optional[int] = None
    open_when_done: bool = True


class VerifyRequest(BaseModel):
    file_path: str
    deep: bool = True


class OpenFolderRequest(BaseModel):
    path: str


@app.get("/api/browse/file")
def api_browse_file():
    """Trigger native Windows file dialog to pick a .nup file."""
    path = nup_engine.pick_file_native()
    return {"path": path}


@app.get("/api/browse/folder")
def api_browse_folder():
    """Trigger native Windows folder dialog to pick destination folder."""
    path = nup_engine.pick_folder_native()
    return {"path": path}


@app.post("/api/inspect")
def api_inspect(req: InspectRequest):
    """Inspect .nup file metadata and return detailed info."""
    path = req.file_path.strip().strip('"').strip("'")
    if not os.path.isfile(path):
        raise HTTPException(status_code=404, detail=f"File not found: {path}")
    try:
        data = nup_engine.inspect_nup(path)
        # Default suggested output directory
        base_dir = os.path.dirname(os.path.abspath(path))
        default_out = os.path.join(base_dir, f"{data['primary_meta']['title_name']}_extracted")
        data["default_dest_dir"] = default_out
        return data
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))


@app.get("/api/tree")
def api_tree(
    file_path: str = Query(...),
    dir_index: int = Query(...),
    recursive: bool = Query(False),
    max_depth: int = Query(1)
):
    """Retrieve directory children for the tree explorer."""
    path = file_path.strip().strip('"').strip("'")
    if not os.path.isfile(path):
        raise HTTPException(status_code=404, detail="File not found")
    try:
        return nup_engine.get_dir_children(path, dir_index, recursive, max_depth)
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))


@app.get("/api/search")
def api_search(
    file_path: str = Query(...),
    q: str = Query(...)
):
    """Search for files matching query within the container."""
    path = file_path.strip().strip('"').strip("'")
    if not os.path.isfile(path):
        raise HTTPException(status_code=404, detail="File not found")
    try:
        results = nup_engine.search_files(path, q)
        return {"query": q, "results": results}
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))


@app.post("/api/extract")
def api_start_extract(req: ExtractRequest):
    """Initiate an extraction task and return task_id for streaming progress."""
    path = req.file_path.strip().strip('"').strip("'")
    if not os.path.isfile(path):
        raise HTTPException(status_code=404, detail="NUP file not found")

    dest = req.dest_dir.strip().strip('"').strip("'")
    if not dest:
        raise HTTPException(status_code=400, detail="Destination directory required")

    task_id = str(uuid.uuid4())
    q: queue.Queue = queue.Queue()
    PROGRESS_QUEUES[task_id] = q

    def _worker():
        try:
            def on_progress(p: Dict[str, Any]):
                q.put(p)

            res = nup_engine.extract_container(
                file_path=path,
                dest_dir=dest,
                mode=req.mode,
                package_index=req.package_index,
                subtree_path=req.subtree_path,
                subtree_dir_index=req.subtree_dir_index,
                progress_callback=on_progress
            )
            if req.open_when_done:
                nup_engine.open_folder_in_explorer(dest)
            q.put({"status": "done", "result": res})
        except Exception as ex:
            q.put({"status": "error", "error": str(ex)})

    thread = threading.Thread(target=_worker, daemon=True)
    thread.start()

    return {"task_id": task_id, "dest_dir": dest}


@app.get("/api/extract/progress/{task_id}")
async def api_extract_progress(task_id: str):
    """Server-Sent Events endpoint streaming extraction updates."""
    if task_id not in PROGRESS_QUEUES:
        raise HTTPException(status_code=404, detail="Task not found")

    q = PROGRESS_QUEUES[task_id]

    async def event_generator():
        import json
        loop = asyncio.get_event_loop()
        while True:
            # Poll from queue in thread-safe manner
            item = await loop.run_in_executor(None, q.get)
            yield f"data: {json.dumps(item)}\n\n"
            if item.get("status") in ("done", "error"):
                # Clean up task
                if task_id in PROGRESS_QUEUES:
                    del PROGRESS_QUEUES[task_id]
                break

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no"
        }
    )


@app.post("/api/verify")
def api_start_verify(req: VerifyRequest):
    """Initiate an integrity verification task."""
    path = req.file_path.strip().strip('"').strip("'")
    if not os.path.isfile(path):
        raise HTTPException(status_code=404, detail="NUP file not found")

    task_id = str(uuid.uuid4())
    q: queue.Queue = queue.Queue()
    PROGRESS_QUEUES[task_id] = q

    def _worker():
        try:
            def on_progress(p: Dict[str, Any]):
                q.put(p)

            res = nup_engine.verify_integrity(
                file_path=path,
                deep=req.deep,
                progress_callback=on_progress
            )
            q.put({"status": "done", "result": res})
        except Exception as ex:
            q.put({"status": "error", "error": str(ex)})

    thread = threading.Thread(target=_worker, daemon=True)
    thread.start()

    return {"task_id": task_id}


@app.get("/api/verify/progress/{task_id}")
async def api_verify_progress(task_id: str):
    """Server-Sent Events endpoint streaming verification updates."""
    if task_id not in PROGRESS_QUEUES:
        raise HTTPException(status_code=404, detail="Task not found")

    q = PROGRESS_QUEUES[task_id]

    async def event_generator():
        import json
        loop = asyncio.get_event_loop()
        while True:
            item = await loop.run_in_executor(None, q.get)
            yield f"data: {json.dumps(item)}\n\n"
            if item.get("status") in ("done", "error"):
                if task_id in PROGRESS_QUEUES:
                    del PROGRESS_QUEUES[task_id]
                break

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive"
        }
    )


@app.post("/api/open-folder")
def api_open_folder(req: OpenFolderRequest):
    """Open folder in Windows Explorer."""
    path = req.path.strip().strip('"').strip("'")
    if not os.path.exists(path):
        raise HTTPException(status_code=404, detail="Path does not exist")
    opened = nup_engine.open_folder_in_explorer(path)
    return {"opened": opened, "path": path}


# Serve static web files
def get_base_dir() -> str:
    if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
        return sys._MEIPASS
    return os.path.dirname(os.path.abspath(__file__))

static_dir = os.path.join(get_base_dir(), "static")
os.makedirs(static_dir, exist_ok=True)
app.mount("/", StaticFiles(directory=static_dir, html=True), name="static")


def start_server(host: str = "127.0.0.1", port: int = 8765):
    uvicorn.run(app, host=host, port=port, log_level="warning")


if __name__ == "__main__":
    start_server()

