## Architecture & Performance Notes

1. **The "One-Sentence Rule":** Build the browser as a fast virtualized viewer/editor of document state, and build the server as the owner of heavy PDF processing and final file generation.
2. **Data Storage (Database vs. File System):** Need to discuss where to save annotations, bookmarks, tags, and extracted text. 
    * *Performance Rule:* Heavy data (like full extracted text for full-text search) should be saved in a database (e.g., PostgreSQL, ElasticSearch). Lightweight structural metadata (page count, dimensions) should be saved as JSON in the file system for instant retrieval by the viewer. Do not bundle them together to avoid bloated network payloads.
3. **Annotation Version Management & Thumbnails:** When saving new annotations into a new folder, copying thousands of thumbnails is highly I/O intensive. Use base thumbnails as fallbacks where possible.
4. **Thread Blocking:** Heavy tasks (Linearization, Exporting, Document Splitting, Thumbnail generation) must run in an asynchronous background queue (e.g., `BackgroundService`) to avoid blocking API threads.

## Web APIs - Upload (Asynchronous)

1. Client uploads a PDF file to the server.
2. Server saves the PDF document to a uniquely named folder.
3. **Immediate Return:** Return `202 Accepted` to the client immediately so the UI is not blocked.
4. **Background Processing Triggered:**
    * If the PDF is not linearized, run QPDF to linearize it (crucial for partial byte-range fetching).
    * Extract lightweight document and page metadata (dimensions, count).
    * Begin generating thumbnails in background batches.

## Web APIs - Download (Byte-Range Fetching)

1. Endpoint returns the original physical PDF binary for the viewer.
2. **Performance Rule:** Must ensure `enableRangeProcessing: true` is set in ASP.NET Core.
    * PDF.js requests the file.
    * The server responds with `Accept-Ranges: bytes` and `206 Partial Content`, sending only the first chunk.
    * PDF.js parses the linearized index.
    * As the user scrolls, PDF.js fires new HTTP requests with headers like `Range: bytes=150000-250000` to fetch exactly the byte chunks needed for the visible pages.
3. Since the base PDF document is immutable, add aggressive caching headers so the browser doesn’t keep re-downloading identical byte ranges.

## Web APIs - Get Thumbnails

1. Client requests thumbnails by providing a list of page numbers or a specific range.
2. **Performance Rule:** Do *not* zip all thumbnails on-the-fly upon request, as this will time out on large 3000-page PDFs.
3. **Architecture:** Use a background thumbnail worker to lazily generate thumbnails and periodically update a cached ZIP file in batches. 
4. Requests fall back to generating single missing thumbnails on-demand or returning them from the partially/fully completed ZIP asynchronously.

## Web APIs - Get Metadata (Structural)

1. Client submits a request to get metadata to initialize the UI.
2. Server returns lightweight PDF structural metadata (page count) and page metadata (width, height, rotation) in one fast request.
3. *Note:* Used immediately by the frontend to build the virtualized DOM placeholders and calculate correct scroll heights.

## Web APIs - Get PDF Supporting Data

1. Client submits a request for extra PDF supporting data (e.g., extracted text, search indices).
2. Server retrieves this heavy data from the database and returns it. This is a separate, lazy-loaded call to avoid blocking the initial document view.

## Web APIs - Save Annotations (Optimistic UI Sync)

1. Client submits new annotations continuously in the background.
2. Server saves the annotations (either to JSON or database).
3. **Background Update:** Server updates *only* the specific thumbnails affected by the new annotations.
4. Server invalidates/deletes the existing zipped thumbnail cache so the background worker can rebuild it.

## Web APIs - PDF Structural Operations 

1. Client makes structural operations (Insert Blank Page, Delete Page, Rotate Page, Reorder) and clicks Save.
2. Server receives the operation sequence and saves it as logical operation records in the database or JSON.
3. Server updates the document's lightweight page metadata to reflect the new logical order.
4. **Performance Rule:** Do *not* rebuild or modify the massive PDF binary immediately. Leave the physical PDF intact until the user requests an Export.

## Web APIs - Export

1. Client clicks the "Export" or "Download Final PDF" button.
2. Server receives the request.
3. Server checks if a finalized version already exists for this specific revision (filename + range + version #).
    * If it exists, immediately return a signed download URL.
    * If it does not exist, return a **Job Id** immediately (asynchronous process) so the user is not blocked.
4. A background worker processes the job: merges pages, flattens annotations onto the PDF binary, re-linearizes the result, and saves it.
5. Client polls a Job Status API and downloads the final compiled file when ready.

## Web APIs - Copy

1. Client sends a Copy request.
2. Server creates a background worker to handle the copy task and returns a Job Id immediately to prevent UI blocking.
3. Background worker creates a new folder/document logic, and copies:
    * The original physical PDF file.
    * The selected version's annotations.
    * Bookmarks and tags where the version number is less than or equal to the selected version number.
4. Client polls a Job Status API and uses a UI banner to notify the user of the result.

## Web APIs - Split Document

1. Client sends a Split request indicating the split parameters.
2. **Performance Rule:** Processing a split on a 300MB PDF will block the API. The server creates a background job and returns a Job Id immediately.
3. Background worker:
    * **New PDF Document:**
        * creates a new folder
        * copies the required byte-ranges (the second split part) to the new folder
        * migrates the relevant extracted text, annotations, bookmarks, and tags
    * **Existing PDF Document:**
        * creates a new version/revision record
        * update extracted text, annotations, bookmarks, and tags
4. Client polls the Job Status API and retrieves the new logical document IDs and version numbers upon completion.

## Web APIs - Save Bookmarks & Tags

1. Client submits a new bookmark or tag.
2. Server saves the entity to the database (allows for easy querying and cross-document text search).
