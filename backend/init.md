# Description

I am working on a C# Web API project. It uses PdfSharpCore to process PDF files. The app serves PDF files with byte-range support.

# Goals

1. It has a controller: pdf.controller.cs
2. It has an `uploads` folder that stores all uploaded files.
3. Create a `Models` folder. It has a model `FileInfo`:
   ```csharp
   public class FileInfo
   {
       public string Name { get; set; }
       public string PDFName { get; set; }
       public string Url { get; set; }
       public long Size { get; set; }
       public int Thumbnails { get; set; } // Count of available thumbnails
   }
   ```
4. The controller has the following endpoints:
   * **POST WebAPI** to upload a file to the upload folder:
     * Create a new subfolder with a GUID as the folder name.
     * Save the file into the new folder.
     * **Return `202 Accepted` immediately** and queue a background job (e.g., using Hangfire or `BackgroundService`) to prevent HTTP timeouts on large 300MB files.
     * **Background Job Execution:**
       * Check if the PDF document is linearized (Fast Web View enabled).
       * If not linearized, convert the PDF document to enable Fast Web View *(Note: Use QPDF or Ghostscript, as PdfSharpCore does not natively support linearization)*.
       * Create a thumbnail of each page in the PDF document, saved in the same folder as `thumbnail.[page number].png` *(Note: Use a library like PdfiumViewer or Magick.NET, as PdfSharpCore cannot render images)*.
       * Create document-level metadata as `metadata.json`.
       * Create page-level metadata **in a single `pages-metadata.json` array or a SQLite database** (avoiding thousands of individual `.json` files).
   * **GET WebAPI** to return PDF files info (returns a `List<FileInfo>`).
   * **DELETE WebAPI** to delete a file by its ID/GUID.
   * **GET WebAPI** to load a PDF file.
     * Must support `206 Partial Content` / byte-range requests.
     * *(Note: In ASP.NET Core, use `PhysicalFile` or `FileStreamResult` with `enableRangeProcessing: true`)*.
