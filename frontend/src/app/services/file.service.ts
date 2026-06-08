import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FileInfo } from '../models/file-info.interface';
import { AnnotationBase } from '../models/annotation.interface';

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
}
