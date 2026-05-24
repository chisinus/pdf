# Angular File Manager

Minimal Angular project showing a Home page with file list retrieved from a Web API, and an Upload dialog.

Run:

```bash
cd frontend
npm install
npm run start
```

APIs expected:
- `GET /api/files` -> returns array of files
- `POST /api/upload` -> accepts multipart form upload
- `DELETE /api/files/:id` -> deletes file


# Client Side PDF Editing

## If you want a polished, ready‑to‑use annotation UI
✔ PSPDFKit
✔ PDFTron / Apryse WebViewer

## If you want free + open‑source
✔ PDF.js + pdf-annotate.js

## If you want Angular‑friendly + easy
✔ ngx-extended-pdf-viewer