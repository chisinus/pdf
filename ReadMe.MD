# Large PDF Web App Implementation Checklist

## Goal

Build a web app that can open, view, annotate, and structurally edit very large PDF documents (2800+ pages, 300MB) with acceptable performance in the browser.

## Core Architecture Decision

Use this model:

- **Frontend**
  - virtualized PDF viewer
  - lazy page rendering
  - annotation overlay
  - local edit state
  - page/window cache

- **Backend**
  - byte-range PDF serving
  - page metadata extraction
  - thumbnail generation
  - annotation persistence
  - structural operation persistence
  - async export/final PDF generation

## Non-Goals

Avoid these patterns:

- loading all 2800 pages into the DOM
- rendering all pages at once
- rewriting the full PDF in the browser after every edit
- generating all thumbnails in the browser on first load
- assuming pages 1-50 or 51-100 map cleanly to binary file slices

---

# 1. Success Criteria

## Viewer Performance

- [ ] Initial view loads first visible page(s) quickly
- [ ] Browser does not freeze on large documents
- [ ] Scrolling remains smooth with thousands of pages
- [ ] Jump-to-page works without rendering all previous pages
- [ ] Only visible pages and nearby pages are rendered

## Editing Performance

- [ ] Annotations appear instantly in UI
- [ ] Annotation save is asynchronous and resilient
- [ ] Structural edits update UI immediately
- [ ] Structural edits do not rebuild the full PDF in browser

## Backend Performance

- [ ] PDF files are served with byte-range support
- [ ] Metadata can be fetched without full PDF processing on every request
- [ ] Export/finalize runs as async job
- [ ] Large files do not block request threads

---

# 2. High-Level System Design

## Frontend Responsibilities

- [ ] Display PDF using a library that supports incremental/range loading
- [ ] Build a virtualized page list
- [ ] Render only visible pages plus a small buffer
- [ ] Show annotations as a separate overlay layer
- [ ] Keep user edits as operations/deltas
- [ ] Sync changes to backend in background

## Backend Responsibilities

- [ ] Serve original PDF with HTTP range requests
- [ ] Store document metadata and page metadata
- [ ] Store annotations separately from PDF binary
- [ ] Store structural operations separately from PDF binary
- [ ] Generate thumbnails
- [ ] Build final edited PDF on demand or in async jobs

---

# 3. Frontend Checklist

## 3.1 PDF Viewer Foundation

- [ ] Choose viewer engine
  - [ ] Prefer PDF.js if building custom viewer workflow
  - [ ] Evaluate commercial SDKs if advanced editing is required
- [ ] Verify viewer supports:
  - [ ] byte-range loading
  - [ ] worker-based parsing/rendering
  - [ ] page-by-page rendering
  - [ ] cancelable render tasks

## 3.2 Virtualized Page List

- [ ] Create a scroll container for the document
- [ ] Represent every page as a lightweight placeholder
- [ ] Track page height/offset for each page
- [ ] Render only:
  - [ ] visible pages
  - [ ] a small buffer above
  - [ ] a small buffer below
- [ ] Unmount or recycle page canvases that are far offscreen
- [ ] Avoid a DOM node explosion

### Suggested Defaults

- [ ] Visible window: actual pages in viewport
- [ ] Buffer: 2 to 5 pages before and after
- [ ] Hard max active rendered pages: 8 to 15

## 3.3 Page Metadata

- [ ] Load page count early
- [ ] Load page dimensions early
- [ ] Store:
  - [ ] page number
  - [ ] width
  - [ ] height
  - [ ] rotation
  - [ ] optional label
- [ ] Build cumulative page offsets for virtual scrolling
- [ ] Support variable page sizes

## 3.4 Incremental Loading

- [ ] Use PDF URL that supports HTTP range requests
- [ ] Confirm partial content requests work in browser
- [ ] Ensure viewer is configured for incremental fetch
- [ ] Prefer linearized PDF when possible

## 3.5 Page Rendering

- [ ] Render pages individually, not whole document at once
- [ ] Render visible pages at normal quality
- [ ] Render thumbnails at lower quality
- [ ] Rerender page at higher scale only when needed for zoom
- [ ] Cancel old render tasks during fast scroll
- [ ] Prioritize visible pages over prefetch pages

## 3.6 Caching Strategy

### Metadata Cache

- [ ] Keep metadata for all pages in memory

### Render Cache

- [ ] Use LRU cache for rendered pages
- [ ] Keep only visible and nearby rendered pages
- [ ] Release offscreen canvases aggressively

### Thumbnail Cache

- [ ] Cache more thumbnails than full renders
- [ ] Evict least recently used thumbnails if memory grows

### Recommended Cache Targets

- [ ] Full page render cache: 8 to 15 pages
- [ ] Parsed page cache: 20 to 50 pages if supported
- [ ] Thumbnail cache: 100 to 300 items depending on memory

## 3.7 Scroll and Visibility Tracking

- [ ] Track visible pages from scroll position
- [ ] Use IntersectionObserver or scroll-based calculation
- [ ] Debounce heavy work
- [ ] Do not debounce actual page visibility too aggressively
- [ ] Trigger prefetch when approaching next page window

## 3.8 Jump-to-Page

- [ ] Build prefix-sum offsets for pages
- [ ] Implement `jumpToPage(pageNumber)`
- [ ] Scroll to offset immediately
- [ ] Render target page and nearby pages
- [ ] Preload annotations for nearby range

## 3.9 Annotation Layer

- [ ] Keep annotations separate from the PDF binary
- [ ] Draw annotations in overlay aligned to page viewport
- [ ] Support local optimistic updates
- [ ] Save in background
- [ ] Retry failed saves gracefully
- [ ] Keep annotation fetching page-ranged, not global

### Annotation Types to Support

- [ ] highlight
- [ ] note/comment
- [ ] free text
- [ ] rectangle
- [ ] ink
- [ ] stamp
- [ ] signature if needed

## 3.10 Structural Editing UI

- [ ] Treat structural edits as operations
- [ ] Update local document model immediately
- [ ] Recompute page order/count after operations
- [ ] Persist operations to backend
- [ ] Do not rebuild entire PDF in browser

### Structural Operations

- [ ] insert blank page
- [ ] delete page
- [ ] rotate page
- [ ] reorder page
- [ ] split document
- [ ] merge if needed

## 3.11 Thumbnails

- [ ] Use server-generated thumbnails if possible
- [ ] Lazy-load thumbnails in sidebar
- [ ] Virtualize thumbnail sidebar if showing many pages
- [ ] Do not render 2800 thumbnails at once

## 3.12 State Management

- [ ] Maintain one canonical client-side document state
- [ ] Separate:
  - [ ] original document metadata
  - [ ] current revision state
  - [ ] annotation state
  - [ ] structural operation list
  - [ ] render state
- [ ] Track dirty state
- [ ] Track sync status

## 3.13 Error Handling

- [ ] Show loading indicators per page
- [ ] Show render failure state per page
- [ ] Handle missing range support gracefully
- [ ] Handle annotation save failures gracefully
- [ ] Handle export failure with retry path
- [ ] Surface document corruption errors clearly

## 3.14 Frontend Testing

- [ ] Test with same-size real-world PDF
- [ ] Test fast scroll behavior
- [ ] Test jump-to-page at 1, middle, last page
- [ ] Test annotation add/edit/delete
- [ ] Test insert/delete/reorder page operations
- [ ] Test memory growth over long sessions
- [ ] Test on Chrome, Edge, Safari, Firefox if supported

---

# 4. Backend Checklist

## 4.1 File Storage and Serving

- [ ] Store original PDFs in durable storage
- [ ] Serve PDFs via endpoint or object storage with range support
- [ ] Confirm `Accept-Ranges: bytes`
- [ ] Confirm `206 Partial Content`
- [ ] Confirm proxies/CDN do not break range requests
- [ ] Add cache headers for immutable revisions

## 4.2 Linearization

- [ ] Decide whether uploaded PDFs should be linearized
- [ ] If yes, add linearization during ingestion or preprocessing
- [ ] Track whether each document is linearized

## 4.3 Document Metadata Extraction

- [ ] Extract on upload or first processing:
  - [ ] page count
  - [ ] page dimensions
  - [ ] page rotation
  - [ ] file size
  - [ ] linearized flag
- [ ] Persist metadata so it is not recomputed per request

## 4.4 Page Metadata API

- [ ] Provide API to fetch document metadata
- [ ] Provide API to fetch page metadata
- [ ] Support chunked metadata fetch if needed

### Minimum Response Data

- [ ] document id
- [ ] current revision id
- [ ] page count
- [ ] source PDF URL
- [ ] page width/height/rotation

## 4.5 Annotation Storage

- [ ] Store annotations separately from PDF
- [ ] Include:
  - [ ] annotation id
  - [ ] document id
  - [ ] revision id if versioned
  - [ ] page number
  - [ ] type
  - [ ] geometry
  - [ ] style
  - [ ] content
  - [ ] author/user id
  - [ ] timestamps
- [ ] Support page-ranged fetch
- [ ] Support create/update/delete

## 4.6 Structural Operation Storage

- [ ] Store structural operations as separate records
- [ ] Include:
  - [ ] operation id
  - [ ] document id
  - [ ] revision id
  - [ ] operation type
  - [ ] payload
  - [ ] author
  - [ ] timestamp
- [ ] Keep operation history auditable
- [ ] Reconstruct current logical document state from base PDF + operations

## 4.7 Revision Model

- [ ] Define document revision strategy
- [ ] Decide whether edits are:
  - [ ] auto-saved into current working revision
  - [ ] saved into named revisions
- [ ] Track current revision per document/user/session if needed
- [ ] Avoid mutating original source file directly

## 4.8 Thumbnail Generation

- [ ] Generate thumbnails asynchronously after upload
- [ ] Store per-page thumbnails in object storage or CDN-backed storage
- [ ] Provide thumbnail URL format or API
- [ ] Support lazy loading on frontend

## 4.9 Export / Final PDF Generation

- [ ] Build async export pipeline
- [ ] Input:
  - [ ] original PDF
  - [ ] current revision operations
  - [ ] annotations
- [ ] Output:
  - [ ] final PDF
  - [ ] optional flattened annotations
  - [ ] optional new linearized version
- [ ] Return job id immediately
- [ ] Provide job status endpoint
- [ ] Store generated export for download

## 4.10 Split / Merge / Insert Blank Page Processing

- [ ] Implement heavy PDF operations server-side
- [ ] Use a reliable PDF processing engine
- [ ] Ensure large files do not block API threads
- [ ] Run heavy operations in background worker queue

## 4.11 Worker / Queue System

- [ ] Add job queue for:
  - [ ] metadata extraction
  - [ ] thumbnail generation
  - [ ] export
  - [ ] split/merge jobs if asynchronous
- [ ] Record job status:
  - [ ] queued
  - [ ] running
  - [ ] completed
  - [ ] failed
- [ ] Add retry policy where appropriate

## 4.12 Database Design

- [ ] Create tables/collections for:
  - [ ] documents
  - [ ] document_pages_metadata
  - [ ] annotations
  - [ ] operations
  - [ ] revisions
  - [ ] export_jobs
  - [ ] exported_files
- [ ] Add indexes for:
  - [ ] document id
  - [ ] revision id
  - [ ] page number
  - [ ] created/updated timestamps

## 4.13 Security and Access Control

- [ ] Protect document access by authz rules
- [ ] Protect annotation APIs by authz rules
- [ ] Use signed URLs if serving files from object storage
- [ ] Validate upload content type and size
- [ ] Sanitize annotation content where needed
- [ ] Log document access and edits if required

## 4.14 Backend Observability

- [ ] Log:
  - [ ] file serve latency
  - [ ] range request behavior
  - [ ] metadata extraction time
  - [ ] export time
  - [ ] job failures
- [ ] Add metrics for:
  - [ ] PDF open time
  - [ ] export job duration
  - [ ] annotation API latency
  - [ ] thumbnail generation duration
- [ ] Add tracing for large jobs if possible

## 4.15 Backend Testing

- [ ] Test range request responses manually and automatically
- [ ] Test large file download through CDN/proxy
- [ ] Test metadata extraction on giant PDFs
- [ ] Test export correctness with annotations and inserted pages
- [ ] Test operation ordering and revision rebuild logic
- [ ] Test failure recovery for long-running jobs

---

# 5. Recommended Data Model

## 5.1 Document

- [ ] id
- [ ] file name
- [ ] original file path/url
- [ ] file size
- [ ] mime type
- [ ] page count
- [ ] is linearized
- [ ] created by
- [ ] created at
- [ ] updated at
- [ ] current revision id

## 5.2 Page Metadata

- [ ] document id
- [ ] page number
- [ ] width
- [ ] height
- [ ] rotation
- [ ] label
- [ ] thumbnail url

## 5.3 Annotation

- [ ] id
- [ ] document id
- [ ] revision id
- [ ] page number
- [ ] type
- [ ] geometry
- [ ] style
- [ ] content
- [ ] author id
- [ ] created at
- [ ] updated at
- [ ] deleted flag or deleted at

## 5.4 Structural Operation

- [ ] id
- [ ] document id
- [ ] revision id
- [ ] type
- [ ] payload
- [ ] author id
- [ ] created at

### Example Operation Types

- [ ] insertBlankPage
- [ ] deletePage
- [ ] rotatePage
- [ ] movePage
- [ ] splitDocument

## 5.5 Export Job

- [ ] id
- [ ] document id
- [ ] revision id
- [ ] status
- [ ] result file url
- [ ] error message
- [ ] created at
- [ ] started at
- [ ] finished at

---

# 6. Recommended API Checklist

## Document APIs

- [ ] `GET /documents/:id`
- [ ] `GET /documents/:id/pages/meta`
- [ ] `GET /documents/:id/revisions/current`

## Annotation APIs

- [ ] `GET /documents/:id/annotations?pageStart=:x&pageEnd=:y`
- [ ] `POST /documents/:id/annotations`
- [ ] `PATCH /documents/:id/annotations/:annotationId`
- [ ] `DELETE /documents/:id/annotations/:annotationId`

## Operation APIs

- [ ] `GET /documents/:id/operations?revisionId=:rev`
- [ ] `POST /documents/:id/operations`
- [ ] `DELETE /documents/:id/operations/:operationId` if undo/remove is supported

## Revision APIs

- [ ] `POST /documents/:id/revisions`
- [ ] `POST /documents/:id/revisions/:rev/save`
- [ ] `GET /documents/:id/revisions/:rev`

## Export APIs

- [ ] `POST /documents/:id/export`
- [ ] `GET /jobs/:jobId`
- [ ] `GET /exports/:exportId/download`

## Thumbnail APIs

- [ ] `GET /documents/:id/thumbnails/:pageNumber`
- [ ] or direct static/object-storage URLs

---

# 7. Rendering and Memory Rules

## Hard Rules

- [ ] Never render all pages at once
- [ ] Never keep all rendered canvases mounted
- [ ] Never fetch all annotations for all pages at once
- [ ] Never generate all thumbnails in browser on initial load
- [ ] Never rebuild full PDF on each user action

## Render Priorities

1. [ ] currently visible pages
2. [ ] next/previous nearby pages
3. [ ] thumbnails near visible area
4. [ ] far-away pages only when requested

## Canvas Rules

- [ ] Dispose canvases for far-away pages
- [ ] Reuse canvas nodes where possible
- [ ] Limit high-DPI renders
- [ ] Rerender only active page on zoom if needed

---

# 8. Recommended Frontend Component Breakdown

## Core Components

- [ ] `DocumentViewer`
- [ ] `VirtualPageList`
- [ ] `PageContainer`
- [ ] `PageCanvas`
- [ ] `AnnotationOverlay`
- [ ] `ThumbnailSidebar`
- [ ] `JumpToPageControl`
- [ ] `DocumentToolbar`
- [ ] `RevisionStatusIndicator`

## State/Services

- [ ] `DocumentStore`
- [ ] `RenderManager`
- [ ] `PageVisibilityTracker`
- [ ] `AnnotationService`
- [ ] `OperationService`
- [ ] `ThumbnailService`
- [ ] `ExportService`

---

# 9. Suggested Backend Services

## Core Services

- [ ] `DocumentService`
- [ ] `MetadataExtractionService`
- [ ] `PdfFileServingService`
- [ ] `AnnotationService`
- [ ] `OperationService`
- [ ] `RevisionService`
- [ ] `ThumbnailGenerationService`
- [ ] `ExportService`
- [ ] `JobQueueWorker`

---

# 10. Phased Delivery Plan

## Phase 1: Viewer MVP

- [ ] Open large PDF from backend
- [ ] Support byte-range loading
- [ ] Show first pages quickly
- [ ] Virtualized scrolling
- [ ] Jump-to-page
- [ ] Basic loading/error states

### Done When

- [ ] 2800-page file opens without freezing
- [ ] last page can be reached without loading all pages into DOM

## Phase 2: Thumbnails and Metadata

- [ ] Precompute page metadata
- [ ] Generate thumbnails
- [ ] Virtualized thumbnail sidebar
- [ ] Smooth page navigation

### Done When

- [ ] thumbnail sidebar does not freeze browser
- [ ] deep navigation feels responsive

## Phase 3: Annotations

- [ ] Add annotation overlay
- [ ] Save/load annotations by page range
- [ ] Optimistic UI
- [ ] Retry/failure handling

### Done When

- [ ] annotation actions feel instant
- [ ] reload restores saved annotations

## Phase 4: Structural Editing

- [ ] Insert blank page
- [ ] Delete page
- [ ] Reorder page
- [ ] Rotate page
- [ ] Store operations in backend
- [ ] Update local logical page order

### Done When

- [ ] UI reflects structural edits immediately
- [ ] no full-PDF rebuild occurs in browser

## Phase 5: Export Pipeline

- [ ] Async job queue
- [ ] Final PDF build using source PDF + operations + annotations
- [ ] Downloadable result
- [ ] Export status UI

### Done When

- [ ] user can export edited document reliably
- [ ] export works for very large PDFs

---

# 11. Performance Validation Checklist

## Client Metrics

- [ ] time to first visible page
- [ ] time to jump to deep page
- [ ] memory usage after 5 minutes scrolling
- [ ] memory usage after annotation session
- [ ] frame drops during fast scroll
- [ ] number of rendered pages simultaneously

## Backend Metrics

- [ ] range request latency
- [ ] metadata extraction time
- [ ] thumbnail generation time
- [ ] export queue wait time
- [ ] export processing time
- [ ] error rate on annotation/operation APIs

---

# 12. QA Scenarios

## Viewer Scenarios

- [ ] Open huge PDF on fast network
- [ ] Open huge PDF on slow network
- [ ] Scroll from page 1 to 500 quickly
- [ ] Jump directly to page 2751
- [ ] Jump back to page 10
- [ ] Zoom in/out repeatedly
- [ ] Resize browser window

## Annotation Scenarios

- [ ] Add annotations on early pages
- [ ] Add annotations on deep pages
- [ ] Edit existing annotation
- [ ] Delete annotation
- [ ] Reload and verify persistence

## Structural Edit Scenarios

- [ ] Insert blank page near start
- [ ] Insert blank page near end
- [ ] Delete page
- [ ] Move page
- [ ] Rotate page
- [ ] Split document into ranges
- [ ] Export and verify result

## Failure Scenarios

- [ ] annotation save fails mid-session
- [ ] network drops during page load
- [ ] export job fails
- [ ] metadata missing for one page
- [ ] malformed PDF handling

---

# 13. Technology Recommendations

## Frontend

- [ ] PDF.js for rendering/viewing baseline
- [ ] React/Vue/etc. with list virtualization support
- [ ] Web Workers for parsing/render tasks where supported

## Backend

- [ ] Strong PDF processing library for server-side finalization
- [ ] Worker queue for long jobs
- [ ] Object storage or CDN-backed storage for source files/thumbnails/exports
- [ ] Relational DB or document DB for annotations/operations/metadata

---

# 14. Definition of Done

## Frontend Done

- [ ] Large PDF opens without full-document render
- [ ] Visible-only rendering works
- [ ] Page virtualization works
- [ ] Jump-to-page works for deep pages
- [ ] Annotations work with optimistic save
- [ ] Structural edits update logical document state
- [ ] Thumbnail sidebar performs well
- [ ] Browser memory remains bounded during long sessions

## Backend Done

- [ ] Range requests work in production environment
- [ ] Metadata is precomputed and stored
- [ ] Thumbnails are available
- [ ] Annotations are persisted separately
- [ ] Structural operations are persisted separately
- [ ] Export jobs rebuild final PDF correctly
- [ ] Async jobs are observable and retryable
- [ ] Access control is enforced

---

# 15. Final Implementation Rules

- [ ] Treat the PDF as a progressively fetched binary resource
- [ ] Treat pages as virtual UI items
- [ ] Treat annotations as separate overlay data
- [ ] Treat structural edits as operation records
- [ ] Treat final PDF generation as a backend job

---

# 16. Priority Order

## P0

- [ ] byte-range file serving
- [ ] page metadata extraction
- [ ] virtualized viewer
- [ ] visible-only rendering
- [ ] jump-to-page
- [ ] basic annotation storage

## P1

- [ ] thumbnails
- [ ] optimistic annotation sync
- [ ] structural operation model
- [ ] revision tracking
- [ ] async export

## P2

- [ ] collaboration features
- [ ] undo/redo across revisions
- [ ] advanced annotation tools
- [ ] background pre-generation optimizations
- [ ] document compare/versioning

---

# 17. One-Sentence Rule for the Team

Build the browser as a fast virtualized viewer/editor of document state, and build the server as the owner of heavy PDF processing and final file generation.
pdf_readme.md
Displaying pdf_readme.md.