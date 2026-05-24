# PDF Processing Dependencies

## QPDF for Linearization

### The Hard Truth About Linearization in C# .NET Core

PDF linearization requires completely tearing apart the binary structure of the file and rebuilding it from the ground up to place the xref tables at the top of the file. Because it is incredibly complex:

* **Syncfusion** explicitly does not support the `EnableFastWebView` property in .NET Core or ASP.NET Core. They only include this feature in their older .NET Framework packages.
* **Zero completely free open-source native C# .NET Core libraries support this.** (Not PdfSharp, not iText7, not PdfPig).
* The only native C# libraries that support this in .NET Core are commercial and will inject watermarks without a paid license (like Aspose.PDF or IronPDF).

### The Best Workaround: Bundle QPDF Locally

Since native free C# libraries can't do it, everyone uses `qpdf`. However, to solve your original error (`The system cannot find the file specified`), you don't need to force a system-wide installation.

Instead, you can download `qpdf.exe`, drop it in your project folder, and call it directly. This guarantees it always works no matter what computer runs your API.

### Why you should use qpdf.exe directly in a Web API

While calling the DLL directly is faster because it avoids the overhead of spawning a new process, using the `.exe` via `Process.Start` is heavily preferred for Backend APIs. Here's why:

1. **Process Isolation (The biggest issue):** PDFs are notoriously complex. If a user uploads a malformed or corrupted PDF that causes the QPDF C-library to trigger a Segmentation Fault or Access Violation, your entire ASP.NET Core Web API will instantly crash. You cannot catch unmanaged C++ memory exceptions using a C# `try/catch`. By using `qpdf.exe`, the OS isolates the memory. If the `.exe` crashes, your C# `Process` object simply returns a bad exit code, you log the error, and your API stays alive.
2. **DLL Dependencies:** `qpdf.dll` usually isn't standalone. It often requires specific C++ redistributables or dependencies like `zlib1.dll` and `libjpeg-8.dll` to be present on the host server.
3. **API Brittleness:** The C API signatures (like the ones in `[DllImport]`) can change slightly between QPDF versions, which will throw hard-to-debug `EntryPointNotFoundException` errors. The CLI arguments (`--linearize`) almost never change.

If you are processing hundreds of PDFs a second, the DLL approach might be worth the risk for the performance gain. But for standard web uploads, keeping the `.exe` approach you currently have is much safer!

---

## Ghostscript for Thumbnail Processing

While Magick.NET is a fantastic image processing library, it does not have a native PDF renderer built-in due to licensing conflicts, so it delegates PDF reading to Ghostscript.

You have two options to fix this:

1. **Install Ghostscript system-wide** (easiest for development).
2. **Bundle Ghostscript locally** (best for deploying your API to other servers, similar to what you did with QPDF).