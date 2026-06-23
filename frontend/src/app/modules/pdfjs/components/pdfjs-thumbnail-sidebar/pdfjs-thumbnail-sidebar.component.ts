import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  DestroyRef,
} from '@angular/core';
import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';
import { FileService } from '../../../../services/file.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

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

  constructor(
    private fileService: FileService,
    private destroyRef: DestroyRef
  ) {}

  ngOnInit() {
    this.loadBatch(1);
    
    // This slows down the initial load of both the thumbnails and the PDF itself, so it's commented out for now.
    this.downloadThumbnailsZip();
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

      const startPage = this.getBatchStart(this.activePage);
      this.loadBatch(startPage);
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
  requestedBatches = new Set<number>();
  batchSize = 20;

  loadBatch(startPage: number, force = false) {
    if (this.pageCount <= 0) return;

    if (!force && this.requestedBatches.has(startPage)) return;

    this.requestedBatches.add(startPage);

    const endPage = Math.min(startPage + this.batchSize - 1, this.pageCount);

    this.fileService.getThumbnailRange(this.documentId, startPage, endPage).pipe(takeUntilDestroyed(this.destroyRef)).subscribe((result) => {
      for (const item of result.thumbnails) {
        this.thumbnailUrls[item.page] = item.url;
      }
    });
  }

  onScrolledIndexChange(index: number) {
    const page = this.pages[index];
    const url = this.thumbnailUrls[page];

    if (!url) {
      this.loadBatch(this.getBatchStart(page));
    }
  }

  async downloadThumbnailsZip() {
    const thumbnails = await this.fileService.getThumbnailRangeZip(this.documentId, 1, this.pageCount);
    for (const item of thumbnails) {
      this.thumbnailUrls[item.page] = item.url;
    }
  }

  refreshLoadedThumbnails() {
    for (const startPage of this.requestedBatches) {
      this.loadBatch(startPage, true);
    }
  }

  getBatchStart(page: number): number {
    return Math.floor((page - 1) / this.batchSize) * this.batchSize + 1;
  }
}
