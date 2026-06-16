import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FileInfo } from '../models/file-info.interface';
import { AnnotationBase } from '../models/annotation.interface';
import JSZip from 'jszip';

@Injectable({ providedIn: 'root' })
export class FileService {
  private api = 'http://localhost:4001/api';
  constructor(private http: HttpClient) {}

  getFiles(): Observable<FileInfo[]> {
    return this.http.get<FileInfo[]>(`${this.api}/files`);
  }

  upload(file: File): Observable<any> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return this.http.post(`${this.api}/upload`, fd);
  }

  deleteFile(id: string): Observable<any> {
    return this.http.delete(`${this.api}/delete/${id}`);
  }

  getThumbnailRange(documentId: string, start: number, end: number) {
    return this.http.get<any>(`http://localhost:4001/api/thumbnails/range/${documentId}/${start}/${end}`);
  }

  saveAnnotations(documentId: string, annotations: AnnotationBase[]): Observable<any> {
    return this.http.post(`${this.api}/annotations/${documentId}`, annotations);
  }

  getPagesMetadata(documentId: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.api}/pagesmetadata/${documentId}`);
  }

  applyAnnotationsToPdf(documentId: string): Observable<any> {
    return this.http.post(`${this.api}/annotations/apply/${documentId}`, {});
  }

  getThumbnailsZip(documentId: string): Observable<Blob> {
    return this.http.get(`${this.api}/pdf/thumbnails/zip/${documentId}`, {
      responseType: 'blob'
    });
  }

  async getThumbnailRangeZip(documentId: string, start: number, end: number): Promise<any[]> {
    // Append a timestamp to bust the browser cache when annotations are saved and thumbnails are updated
    const url = `${this.api}/pdf/thumbnails/rangezip/${documentId}/${start}/${end}?t=${new Date().getTime()}`;

    // 1. Download ZIP as ArrayBuffer
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Failed to download ZIP: ${response.status}`);
    }

    const arrayBuffer = await response.arrayBuffer();

    // 2. Load ZIP
    const zip = await JSZip.loadAsync(arrayBuffer);

    // 3. Parse manifest.json
    const manifestFile = zip.file("manifest.json");
    if (!manifestFile) {
      throw new Error("manifest.json missing in ZIP");
    }

    const manifestText = await manifestFile.async("string");
    const manifest = JSON.parse(manifestText);

    // 4. Extract thumbnails
    const thumbnails = [];

    for (const item of manifest) {
      const fileName = item.file;
      const file = zip.file(fileName);
      if (!file) continue;

      const blob = await file.async("blob");
      const blobUrl = URL.createObjectURL(blob);

      thumbnails.push({
        page: Number(item.page),
        url: blobUrl,
        size: item.size
      });
    }

    // 5. Sort by page number
    thumbnails.sort((a, b) => a.page - b.page);

    return thumbnails;    
  }
}
