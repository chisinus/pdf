import { Component, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { UntilDestroy, untilDestroyed } from '@ngneat/until-destroy';
import { UploadDialogComponent } from '../../upload-dialog/upload-dialog.component';
import { PdfViewerComponent as SimplePdfViewerComponent } from '../../modules/simple/pdf-viewer/pdf-viewer.component';
import { PdfViewerComponent as PdfJsViewerComponent } from '../../modules/pdfjs/components/pdfjs-viewer/pdfjs-viewer.component';
import { FileService } from '../../services/file.service';
import { FileInfo } from '../../models/file-info.interface';

@UntilDestroy()
@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  standalone: true,
  imports: [DecimalPipe, UploadDialogComponent, SimplePdfViewerComponent, PdfJsViewerComponent],
})
export class HomeComponent implements OnInit {
  files: FileInfo[] = [];
  showUpload = false;
  selectedFile: FileInfo | null = null;
  activeViewer: 'simple' | 'pdfjs' | null = null;

  constructor(private fileService: FileService) {}

  ngOnInit(): void {
    this.loadFiles();
  }

  loadFiles() {
    this.fileService
      .getFiles()
      .pipe(untilDestroyed(this))
      .subscribe((f) => {
        this.files = f;
      });
  }

  deleteFile(id: string) {
    if (!confirm('Delete file?')) return;
    this.fileService
      .deleteFile(id)
      .pipe(untilDestroyed(this))
      .subscribe(() => this.loadFiles());
  }

  openFile(f: FileInfo) {
    this.selectedFile = f;
    this.activeViewer = 'simple';
  }

  openWithPdfJs(f: FileInfo) {
    this.selectedFile = f;
    this.activeViewer = 'pdfjs';
  }

  closeViewer() {
    this.selectedFile = null;
    this.activeViewer = null;
  }

  onUploadClicked() {
    this.showUpload = true;
  }

  onUploadFinished(success: boolean) {
    this.showUpload = false;
    if (success) this.loadFiles();
  }
}
