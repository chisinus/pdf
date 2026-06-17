## Core Principles & Performance Notes

1. **Canvas Lifecycle & Memory**: Use an LRU (Least Recently Used) cache for recently visited pages. Aggressively unmount/recycle `<canvas>` elements for pages far out of view to prevent browser crashes on large PDFs.
2. **Layering**: Only mount and apply Text and Annotation layers to currently visible pages.
3. **Progressive Rendering**: Always render a low-resolution base bitmap or placeholder first, then upgrade to high-resolution content when the page becomes stable in the viewport.
4. **Render Cancellation**: If a page is scrolled out of the viewport, immediately cancel its active fetch and render jobs. Never let invisible pages block the main render queue.
5. **Virtualization Window**: Request content for visible pages first, plus a forward buffer (e.g., 3-5 pages) and a smaller backward buffer (1-2 pages) since users scroll down more frequently.
6. **Thread Management**: Move heavy parsing and rendering off the main thread using PDF.js Web Workers.
7. **Thumbnails**: Fetch server-generated thumbnails asynchronously. Do not rely on client-side generation for massive PDFs as it will block the UI thread and consume too much memory. *(Note: If client-side generation is required for the MVP, it must be strictly lazy-loaded and virtualized).*
8. **Network**: Ensure HTTP Range requests (`206 Partial Content`) are working correctly so the browser incrementally fetches only the byte chunks needed for the visible pages.
9. **Responsiveness**: Ensure the layout works seamlessly across different screen resolutions, zoom levels, and device pixel ratios.

## Questions

1. **Save metadata into database?** It becomes bigger if we include all info (extracted text, bookmarks, and tags, etc.) in the metadata file, making it hard to search text.
   * **Suggestion/Note**: Keep structural metadata (page count, width, height, rotation) lightweight and available immediately for UI placeholders. Heavy data (like extracted text for searching) should be offloaded to a dedicated database table or a separate searchable index, not bundled with the initial UI payload.
2. **We have to create all thumbnails because users can toggle to Fullscreen Thumbnails mode.**
   * **Suggestion/Note**: Even in Fullscreen mode, the UI must use a virtualized grid (e.g., displaying only the 50 thumbnails visible on screen). Generate or fetch them only as the user scrolls. Rendering 3000 thumbnails simultaneously will crash the browser.

## Page Load Lifecycle

1. **Fetch Metadata**: Load lightweight page metadata first (width, height) to instantly create virtual DOM placeholders and determine exact scroll heights, preventing layout shifts.
2. **Initial Render**: Based on the metadata:
    * Establish the document context (e.g., file path, version). 
    * Load the first `n` visible pages and save them to the rendered page cache.
3. **Thumbnail Initialization**: Fetch the zipped thumbnail cache from the backend to optimize bulk thumbnail loading.
4. **Apply Overlays**: Mount Text layers (opacity = 0) and Annotation layers *only* for the initially visible pages. Maintain two distinct annotation lists in the local state: **Existing** (from server) and **New** (local edits).
5. **Pre-fetch**: Start quietly pre-fetching and rendering low-resolution versions of the next few buffer pages in the background.

## Scroll PDF Page (Virtualization)

1. **Cancel Obsolete Work**: Immediately cancel pending render tasks for pages that just scrolled out of the active buffer window.
2. **Progressive Rendering**: Render placeholders or low-resolution bitmaps for pages currently scrolling into view.
3. **High-Resolution Upgrade**: Once scrolling stops or slows, render the high-resolution content for the stable visible pages.
4. **Overlay Management**: Apply text layers and annotations to newly visible pages; immediately detach/destroy them from pages that scroll out.
5. **Sync Thumbnails**: Update the active state of thumbnails in the sidebar corresponding to the newly visible pages.
6. **Cache Maintenance**: Update the LRU (Least Recently Used) cache:
    * Evict and destroy `<canvas>` elements for pages far away from the current viewport to free up RAM.
    * Save current visible and pre-fetched buffer pages to the cache.

## Scroll Thumbnail List

1. **Lazy Load**: Load thumbnails from the downloaded zip cache while scrolling through the virtualized thumbnail list. 
    * If a page thumbnail does not exist in the zip file (e.g., the user opens the PDF before backend generation is complete), request it individually from the server or generate it on-the-fly as a fallback.
2. **Cancel Requests**: Abort thumbnail fetch requests (or client-side fallback generation) if the thumbnail is scrolled out of view before it completes.
3. **Navigation**: Clicking on a thumbnail brings the corresponding page into view using pre-calculated prefix-sum offsets for an instant jump, bypassing intermediate page parsing, and follows the "Scroll PDF Page" steps.

## Annotation Command to PDF Viewer

1. User clicks on an Annotation tool button.
2. System updates the `store/annotationAction` state.
3. PDF Viewer reacts to the `store/annotationAction` state changes.
4. **PDF Viewer Execution**: 
    * Executes the annotation drawing action.
    * Applies the optimistic UI state locally. 
    * Saves the annotation to the **New Annotations** list.
    * Remains active to draw another one if in continuous mode.
5. **Save Operation**:
    * Submit a background request to the backend with the new annotations.
    * Upon success, clear the "New" list and reload the PDF metadata/UI with the new revision/version number.

## Toolbar Command to PDF Viewer

1. User clicks on a toolbar button.
2. The click triggers specific state processing. For example: 
    * **Next Page** button updates the `currentPageNumber` in the store.
    * **Rotate** button sets `store/toolbarAction`. 
3. PDF Viewer reacts to these specific store property changes. For example:
    * **Page Number Changed**: Jump to the new current page using immediate offset calculation.
    * **Toolbar Action Triggered**: 
        * Execute the designated action (e.g., rotate viewport).
        * Clear the `store/toolbarAction` state.
