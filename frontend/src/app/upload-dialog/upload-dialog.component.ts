import { Component, EventEmitter, Output } from "@angular/core";
import { FileService } from "../services/file.service";

@Component({
  selector: "app-upload-dialog",
  templateUrl: "./upload-dialog.component.html",
  styleUrls: ["./upload-dialog.component.css"],
  standalone: true,
})
export class UploadDialogComponent {
  @Output() close = new EventEmitter<boolean>();
  file?: File;
  dragging = false;
  uploading = false;

  constructor(private fileService: FileService) {}

  onFileSelected(ev: Event) {
    const input = ev.target as HTMLInputElement;
    if (input.files && input.files.length) this.file = input.files[0];
  }

  onDrop(ev: DragEvent) {
    ev.preventDefault();
    this.dragging = false;
    if (
      ev.dataTransfer &&
      ev.dataTransfer.files &&
      ev.dataTransfer.files.length
    ) {
      this.file = ev.dataTransfer.files[0];
    }
  }

  onDragOver(ev: DragEvent) {
    ev.preventDefault();
    this.dragging = true;
  }
  onDragLeave() {
    this.dragging = false;
  }

  upload() {
    if (!this.file) return;
    this.uploading = true;
    this.fileService.upload(this.file).subscribe({
      next: () => {
        this.uploading = false;
        this.close.emit(true);
      },
      error: () => {
        this.uploading = false;
        this.close.emit(false);
      },
    });
  }

  cancel() {
    this.close.emit(false);
  }
}
