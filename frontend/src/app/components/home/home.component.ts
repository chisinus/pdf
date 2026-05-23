import { Component, OnInit } from '@angular/core';
import { UploadDialogComponent } from '../../upload-dialog/upload-dialog.component';
import { FileService } from '../../services/file.service';
import { FileInfo } from '../../models/file-info.interface';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.css'],
  imports: [UploadDialogComponent],
})
export class HomeComponent implements OnInit {
  files: FileInfo[] = [];
  showUpload = false;

  constructor(private fileService: FileService) {}

  ngOnInit(): void {
    // this.loadFiles();
  }

  loadFiles() {
    this.fileService.getFiles().subscribe((f) => (this.files = f));
  }

  deleteFile(id: string) {
    if (!confirm('Delete file?')) return;
    this.fileService.deleteFile(id).subscribe(() => this.loadFiles());
  }

  openFile(f: FileInfo) {
    if (f.url) {
      window.open(f.url, '_blank');
    }
  }

  onUploadClicked() {
    this.showUpload = true;
  }

  onUploadFinished(success: boolean) {
    this.showUpload = false;
    if (success) this.loadFiles();
  }
}
