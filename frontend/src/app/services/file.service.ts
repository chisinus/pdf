import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { FileInfo } from "../models/file-info.interface";

@Injectable({ providedIn: "root" })
export class FileService {
  private api = "/api";
  constructor(private http: HttpClient) {}

  getFiles(): Observable<FileInfo[]> {
    return this.http.get<FileInfo[]>(`${this.api}/files`);
  }

  upload(file: File): Observable<any> {
    const fd = new FormData();
    fd.append("file", file, file.name);
    return this.http.post(`${this.api}/upload`, fd);
  }

  deleteFile(id: string): Observable<any> {
    return this.http.delete(`${this.api}/files/${id}`);
  }
}
