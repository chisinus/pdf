import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
} from '@angular/core';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';
import { FileService } from '../../../../services/file.service';

@Component({
  selector: 'app-pdfjs-thumbnail-sidebar',
  templateUrl: './pdfjs-thumbnail-sidebar.component.html',
  styleUrls: ['./pdfjs-thumbnail-sidebar.component.scss'],
  standalone: true,
  imports: [ScrollingModule],
})
export class PdfjsThumbnailSidebarComponent implements OnInit, OnChanges {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;
  @Input() activePage: number = 1;
  @Output() pageSelected = new EventEmitter<number>();

  pages: number[] = [];

  @ViewChild(CdkVirtualScrollViewport) viewport!: CdkVirtualScrollViewport;
  private suppressNextScroll = false;

  constructor(private fileService: FileService) {}

  ngOnInit() {
    this.loadBatch(1);
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['pageCount'] && this.pageCount > 0) {
      this.pages = Array.from({ length: this.pageCount }, (_, i) => i + 1);
    }

    if (changes['activePage'] && this.viewport) {
      // If the change originated from a local click, skip scrolling so the
      // thumbnail stays in its current position. Otherwise, jump immediately
      // to the active thumbnail (no smooth animation).
      if (this.suppressNextScroll) {
        this.suppressNextScroll = false;
      } else {
        this.viewport.scrollToIndex(this.activePage - 1);
      }
    }
  }

  handleImageError(event: Event) {
    // Placeholder fallback can be added here if thumbnail requests fail.
  }

  selectPage(page: number) {
    // Prevent the sidebar from auto-scrolling the clicked thumbnail to the
    // top — let it remain visually where the user clicked.
    this.suppressNextScroll = true;
    this.pageSelected.emit(page);
  }

  // Load thumbnails in batches to avoid overwhelming the server with requests.
  thumbnailUrls: { [page: number]: string } = {};
  batchSize = 50;

  loadBatch(startPage: number) {
    console.log('>>>>>>>>>>>>>> loadBatch', startPage);
    const endPage = startPage + this.batchSize - 1;

    this.fileService.getThumbnailRange(this.documentId, startPage, endPage).subscribe((result) => {
      for (const item of result.thumbnails) {
        this.thumbnailUrls[item.page] = item.url;
      }
    });

    console.log('>>>>>>>>>>>>>> loadBatch end', startPage, endPage, this.thumbnailUrls);
  }

  onScrolledIndexChange(index: number) {
    console.log('>>>>>>>>>>>>>> onScrolledIndexChange', index);
    const page = this.pages[index];

    // If near the end of loaded thumbnails, load next batch
    if (page % this.batchSize === 0) {
      this.loadBatch(page + 1);
    }
  }
}
