import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
} from '@angular/core';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';

@Component({
  selector: 'app-pdfjs-thumbnail-sidebar',
  templateUrl: './pdfjs-thumbnail-sidebar.component.html',
  styleUrls: ['./pdfjs-thumbnail-sidebar.component.scss'],
  standalone: true,
  imports: [ScrollingModule],
})
export class PdfjsThumbnailSidebarComponent implements OnChanges {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;
  @Input() activePage: number = 1;
  @Output() pageSelected = new EventEmitter<number>();

  pages: number[] = [];

  @ViewChild(CdkVirtualScrollViewport) viewport!: CdkVirtualScrollViewport;

  ngOnChanges(changes: SimpleChanges) {
    if (changes['pageCount'] && this.pageCount > 0) {
      this.pages = Array.from({ length: this.pageCount }, (_, i) => i + 1);
    }

    if (changes['activePage'] && this.viewport) {
      // Jump immediately to the active thumbnail (no smooth animation)
      this.viewport.scrollToIndex(this.activePage - 1);
    }
  }

  handleImageError(event: Event) {
    // Placeholder fallback can be added here if thumbnail requests fail.
  }

  selectPage(page: number) {
    this.pageSelected.emit(page);
  }
}
