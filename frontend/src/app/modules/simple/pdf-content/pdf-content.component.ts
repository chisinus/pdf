import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  QueryList,
  SimpleChanges,
  ViewChildren,
} from '@angular/core';

@Component({
  selector: 'app-pdf-content',
  templateUrl: './pdf-content.component.html',
  styleUrls: ['./pdf-content.component.scss'],
  standalone: true,
  imports: [],
})
export class PdfContentComponent implements AfterViewInit, OnDestroy, OnChanges {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;
  @Input() activePage: number = 1;
  @Output() visiblePageChanged = new EventEmitter<number>();

  pages: number[] = [];
  bufferSize = 3; // Number of pages to render above and below the active page
  private lastEmittedPage = 1;

  @ViewChildren('pageContainer') pageElements!: QueryList<ElementRef>;
  private observer!: IntersectionObserver;
  private visibilityMap = new Map<number, number>();

  ngOnChanges(changes: SimpleChanges) {
    if (changes['pageCount'] && this.pageCount > 0) {
      this.pages = Array.from({ length: this.pageCount }, (_, i) => i + 1);
    }
    if (changes['activePage'] && !changes['activePage'].isFirstChange()) {
      const newPage = changes['activePage'].currentValue;
      // Scroll to the page if the change came from the parent (e.g. sidebar click)
      if (newPage !== this.lastEmittedPage) {
        this.scrollToPage(newPage);
      }
    }
  }

  ngAfterViewInit() {
    this.setupObserver();
    this.observePages();

    this.pageElements.changes.subscribe(() => {
      this.observer.disconnect();
      this.observePages();
    });
  }

  ngOnDestroy() {
    if (this.observer) this.observer.disconnect();
  }

  isPageRendered(page: number): boolean {
    // Only true if the page is near the currently active page on-screen
    return Math.abs(page - this.activePage) <= this.bufferSize;
  }

  scrollToPage(page: number) {
    const targetElement = this.pageElements.find(
      (el) => Number(el.nativeElement.getAttribute('data-page-number')) === page,
    );
    if (targetElement) {
      targetElement.nativeElement.scrollIntoView({
        behavior: 'smooth',
        block: 'start',
      });
    }
  }

  private setupObserver() {
    const options = {
      root: null,
      rootMargin: '0px',
      threshold: [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0],
    };

    this.observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        const pageNum = Number(entry.target.getAttribute('data-page-number'));
        this.visibilityMap.set(pageNum, entry.intersectionRatio);
      });

      this.calculateMostVisiblePage();
    }, options);
  }

  private observePages() {
    this.pageElements.forEach((el) => this.observer.observe(el.nativeElement));
  }

  private calculateMostVisiblePage() {
    let maxRatio = 0;
    let mostVisiblePage = this.activePage;

    this.visibilityMap.forEach((ratio, pageNum) => {
      if (ratio > maxRatio) {
        maxRatio = ratio;
        mostVisiblePage = pageNum;
      }
    });

    if (maxRatio > 0 && mostVisiblePage !== this.activePage) {
      this.activePage = mostVisiblePage;
      this.lastEmittedPage = mostVisiblePage;
      this.visiblePageChanged.emit(mostVisiblePage);
    }
  }
}
