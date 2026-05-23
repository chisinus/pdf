import { Component, Input } from '@angular/core';
import { ThumbnailSidebarComponent } from '../thumbnail-sidebar/thumbnail-sidebar.component';
import { PdfContentComponent } from '../pdf-content/pdf-content.component';
import { ActivityBarComponent } from '../activity-bar/activity-bar.component';

@Component({
  selector: 'app-pdf-viewer',
  templateUrl: './pdf-viewer.component.html',
  styleUrls: ['./pdf-viewer.component.css'],
  standalone: true,
  imports: [ActivityBarComponent, PdfContentComponent, ThumbnailSidebarComponent],
})
export class PdfViewerComponent {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;

  currentVisiblePage: number = 1;

  onVisiblePageChanged(page: number) {
    this.currentVisiblePage = page;
  }

  onSidebarPageSelected(page: number) {
    this.currentVisiblePage = page;
  }
}
