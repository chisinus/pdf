# Large PDF Web App Implementation Guide

## 1. Core Architecture Strategy

To achieve acceptable performance in the browser for very large PDF documents (e.g., 2800+ pages, 300MB), the system relies on a heavily decoupled, lazy-loading architecture.

### The "One-Sentence Rule"
**Build the browser as a fast virtualized viewer/editor of document state, and build the server as the owner of heavy PDF processing and final file generation.**

* **Frontend (PDF.js):** Virtualized PDF viewer, lazy page rendering, annotation overlay, local edit state, and page/window cache.
* **Backend (.NET Web API):** Byte-range PDF serving, QPDF linearization, page metadata extraction, asynchronous thumbnail generation, and structural operation persistence.

### Non-Goals (What to Avoid)
* Loading all pages into the DOM.
* Rendering all pages at once or generating all thumbnails in the browser.
* Rewriting the full PDF binary in the browser after every edit.
* Blocking API threads for large file processing.

---

## 2. Frontend Implementation (PDF.js)

### 2.1 PDF.js Foundation & Incremental Loading
* **Viewer Engine:** Use `pdfjs-dist` configured with Web Workers for off-main-thread parsing.
* **Byte-Range Fetching:** Ensure the viewer is configured for incremental fetch so it only requests the binary chunks of the PDF it needs to render the current viewport.

### 2.2 Virtualized Page List & Rendering
* **DOM Management:** Represent every page as a lightweight placeholder based on pre-fetched metadata. Avoid a DOM node explosion.
* **Visibility Tracking:** Use `IntersectionObserver` to track scroll position and visible pages. 
* **Rendering Rules:** 
  * Render *only* visible pages + a small buffer (2-5 pages above and below).
  * Cancel old render tasks during fast scrolling.
  * Aggressively unmount/recycle `<canvas>` nodes for pages that scroll far out of view.

### 2.3 Jump-to-Page Navigation
* Implement `jumpToPage(pageNumber)` using prefix-sum offsets calculated from page metadata.
* Scroll immediately to the offset and only render the target page, rather than forcing the engine to parse all intermediate pages.

### 2.4 Annotation & Editing Overlays
* **Separation of Concerns:** Keep annotations entirely separate from the PDF binary and the PDF.js canvas.
* **Overlay Layer:** Draw annotations on an absolutely positioned transparent `<canvas>` or DOM overlay sitting on top of the PDF.js page canvas.
* **Optimistic UI:** Apply edits (highlights, shapes, structural page reorders) to the local model instantly and sync to the backend in the background.

---

## 3. Backend Implementation (.NET Core)

### 3.1 Serving the File (Byte-Range Support)
* The endpoint serving the PDF must support HTTP Range requests (`206 Partial Content`, `Accept-Ranges: bytes`). This is mandatory for PDF.js to load documents incrementally.

### 3.2 Linearization (Fast Web View) via QPDF
PDF linearization places the xref tables at the beginning of the file, allowing PDF.js to render the first page before downloading the rest.

* **The Workaround:** Since free native C# libraries do not support linearization, bundle `qpdf.exe` locally.
* **Process Isolation:** Use `Process.Start` to call `qpdf.exe` rather than calling a DLL via P/Invoke. PDFs are notoriously complex, and malformed files can cause segmentation faults in C-libraries. Using the `.exe` ensures that if QPDF crashes, your ASP.NET Core Web API stays alive.

### 3.3 Asynchronous Processing Queue
Do not process large PDFs synchronously on upload. 
1. Accept the upload and save it to an `uploads/{GUID}` folder.
2. Return `202 Accepted` immediately.
3. Queue a background worker (e.g., `BackgroundService` or Hangfire) to run the processing pipeline.

### 3.4 The Background Processing Pipeline
When a document is queued, the background worker should sequentially:
1. **Check Linearization:** Verify if the file is linearized. If not, run `qpdf.exe`.
2. **Extract Metadata:** Use a library like `PdfSharpCore` to extract page count, dimensions, and rotation. Save this as a single `pages-metadata.json` to prevent thousands of file system hits.
3. **Generate Thumbnails:** Offload CPU-heavy thumbnail generation.

### 3.5 Thumbnail Generation Strategy
Because PDF.js rendering thousands of thumbnails in the browser would crash the client, thumbnails must be generated on the server.
* **Engine:** Use locally bundled Ghostscript (via Magick.NET) or PdfiumViewer.
* **Delivery Architecture (Lazy + Worker + Cached ZIP):** 
  * Create thumbnails in batches in the background.
  * Bundle thumbnails into a ZIP file.
  * The frontend requests batched pages, and the backend either serves them from the compiled ZIP or falls back to generating missing thumbnails on-the-fly.

---

## 4. API & Data Model Checklist

### 4.1 Required Data Models
* **Document:** `id`, `file name`, `file size`, `page count`, `is linearized`.
* **Page Metadata:** `document id`, `page number`, `width`, `height`, `rotation`.
* **Annotation:** `id`, `document id`, `page number`, `type` (highlight, ink, etc.), `geometry`, `content`.
* **Structural Operation:** `type` (insert page, rotate page, split), `payload`, `author`.

### 4.2 Core Endpoints
* **Upload:** `POST /api/pdf/upload` -> Returns 202.
* **Download:** `GET /api/download/:id` -> Must support `Accept-Ranges`.
* **Metadata:** `GET /api/documents/:id/pages/meta` -> Returns parsed JSON metadata so the frontend can build virtual placeholders.
* **Thumbnails:** `GET /api/documents/:id/thumbnails?start=:x&end=:y` -> Returns pre-generated thumbnail URLs.
* **Annotations:** `GET`, `POST`, `PATCH`, `DELETE` for document annotations, heavily filtered by page range.

---

## 5. Editing and Final File Generation

### 5.1 Structural Edits
Treat structural edits (inserting, deleting, or reordering pages) as lightweight database operations. Update the logical document state on the client and server immediately, without actually mutating the source PDF.

### 5.2 Async Export Pipeline
When the user is finished editing, provide an export button.
1. Submit an export job containing the source PDF ID, the list of annotations, and the list of structural operations.
2. Run the heavy compilation process in a backend queue.
3. Merge pages, flatten annotations, and re-linearize the resulting file.
4. Notify the client when the compiled PDF is ready for download.

---

## 6. Rendering and Memory Hard Rules

1. **Never** render all pages at once.
2. **Never** keep all rendered canvases mounted in the DOM.
3. **Never** fetch all annotations for all 2800 pages at once.
4. **Never** generate all thumbnails in the browser on initial load.
5. **Never** rebuild the full PDF binary in the browser upon a user action.
6. **Always** prioritize rendering visible pages first, nearby pages second, and thumbnails third.
