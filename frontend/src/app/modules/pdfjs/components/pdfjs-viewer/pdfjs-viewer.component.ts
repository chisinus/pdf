import {
  Component,
  AfterViewInit,
  OnDestroy,
  ElementRef,
  ViewChild,
  Input,
  NgZone,
  ChangeDetectorRef,
} from '@angular/core';

import { PdfjsService } from '../../services/pdfjs.service';
import { AnnotationService } from '../../services/annotation.service';
import { PdfjsThumbnailSidebarComponent } from '../pdfjs-thumbnail-sidebar/pdfjs-thumbnail-sidebar.component';
import { AnnotationType } from '../../../../models/enum';
import { FileService } from '../../../../services/file.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-pdfjs-viewer',
  templateUrl: './pdfjs-viewer.component.html',
  styleUrls: ['./pdfjs-viewer.component.scss'],
  standalone: true,
  imports: [PdfjsThumbnailSidebarComponent],
})
export class PdfjsViewerComponent implements AfterViewInit, OnDestroy {
  public AnnotationType = AnnotationType;

  @Input() documentId!: string;
  @Input() pageCount: number = 0;

  @ViewChild('container', { static: true }) container!: ElementRef;
  @ViewChild('scrollContainer', { static: true }) scrollContainer!: ElementRef;
  @ViewChild(PdfjsThumbnailSidebarComponent) thumbnailSidebar?: PdfjsThumbnailSidebarComponent;

  currentVisiblePage: number = 1;
  numPages: number = 0;

  private isDrawing = false;
  private startX = 0;
  private startY = 0;
  private currentEndX = 0;
  private currentEndY = 0;
  private currentDrawingPage = 0;
  private currentPoints: { x: number; y: number }[] = [];
  public annotations: any[] = []; // List to store saved annotations
  private tempCanvas?: HTMLCanvasElement;
  private tempCtx?: CanvasRenderingContext2D | null;

  private pdfDoc: any = null;
  private observer: IntersectionObserver | null = null;
  private renderedPages = new Set<number>();
  private renderingPages = new Set<number>();
  private renderTasks = new Map<number, any>();
  private renderQueue: Array<{ pageNum: number; wrapper: HTMLElement }> = [];
  private renderQueueRunning = false;

  constructor(
    private ngZone: NgZone,
    private pdfjsService: PdfjsService,
    private annotationService: AnnotationService,
    private cdr: ChangeDetectorRef,
    private fileService: FileService
  ) {}

  async ngAfterViewInit() {
    if (!this.documentId) return;

    // Load the existing annotations before rendering the PDF pages
    try {
      const pagesMeta = await firstValueFrom(this.fileService.getPagesMetadata(this.documentId));
      this.annotations = [];
      pagesMeta.forEach((meta: any) => {
        const pageAnns = meta.Annotations || meta.annotations;
        if (pageAnns && Array.isArray(pageAnns)) {
          this.annotations.push(...pageAnns);
        }
      });
    } catch (e) {
      console.error('Failed to load metadata', e);
    }

    this.ngZone.runOutsideAngular(async () => {
      // Append a timestamp to the URL to bypass browser cache
      const url = `http://localhost:4001/api/download/${this.documentId}?t=${new Date().getTime()}`;

      const pdf = await this.pdfjsService.loadDocument(url);
      this.pdfDoc = pdf;
      this.numPages = pdf.numPages;

      // Get the first page to estimate placeholder dimensions
      const firstPage = await pdf.getPage(1);
      const viewport = firstPage.getViewport({ scale: 1.5 });
      const defaultWidth = viewport.width;
      const defaultHeight = viewport.height;

      // Setup IntersectionObserver to only render visible pages inside the scroll container
      this.observer = new IntersectionObserver(
        (entries) => {
          // Compute visible area for each page wrapper and choose the page
          // with the largest intersection area inside the scroll container.
          const scrollEl = this.scrollContainer.nativeElement as HTMLElement;
          const containerRect = scrollEl.getBoundingClientRect();

          const wrappers = Array.from(
            this.container.nativeElement.querySelectorAll('.page-wrapper'),
          ) as HTMLElement[];

          let bestWrapper: HTMLElement | null = null;
          let bestArea = -1;
          let bestPageNum = 0;

          wrappers.forEach((w) => {
            const r = w.getBoundingClientRect();
            const intersectTop = Math.max(r.top, containerRect.top);
            const intersectBottom = Math.min(r.bottom, containerRect.bottom);
            const intersectHeight = Math.max(0, intersectBottom - intersectTop);
            const intersectWidth = Math.max(
              0,
              Math.min(r.right, containerRect.right) - Math.max(r.left, containerRect.left),
            );
            const area = intersectHeight * intersectWidth;
            if (area > bestArea) {
              bestArea = area;
              bestWrapper = w;
            }
          });

          if (bestWrapper && bestArea > 0) {
            bestPageNum = Number((bestWrapper as HTMLElement).getAttribute('data-page-number'));
            this.ngZone.run(() => {
              this.currentVisiblePage = bestPageNum;
              this.cdr.markForCheck();
            });

            this.loadAndRenderPage(bestPageNum, bestWrapper, true);
          }

          // Load intersecting pages and cleanup non-intersecting pages
          entries.forEach((entry) => {
            const pn = Number(entry.target.getAttribute('data-page-number'));
            if (entry.isIntersecting) {
              if (pn !== bestPageNum) {
                this.loadAndRenderPage(pn, entry.target as HTMLElement);
              }
            } else {
              this.cleanupPage(pn, entry.target as HTMLElement);
            }
          });
        },
        {
          root: this.scrollContainer.nativeElement,
          rootMargin: '1000px 0px',
        },
      ); // Preload pages 1000px before they appear

      // Create placeholders for all pages
      for (let pageNum = 1; pageNum <= pdf.numPages; pageNum++) {
        const wrapper = document.createElement('div');
        wrapper.classList.add('page-wrapper');
        wrapper.setAttribute('data-page-number', pageNum.toString());
        wrapper.style.width = `${defaultWidth}px`;
        wrapper.style.height = `${defaultHeight}px`;
        wrapper.style.position = 'relative';
        wrapper.style.margin = '0 auto 20px auto';
        wrapper.style.backgroundColor = '#f3f4f6';
        wrapper.style.boxShadow = '0 4px 6px rgba(0,0,0,0.1)';

        this.container.nativeElement.appendChild(wrapper);
        this.observer!.observe(wrapper);
      }
    });
  }

  loadAndRenderPage(pageNum: number, wrapper: HTMLElement, priority = false) {
    if (this.renderedPages.has(pageNum) || this.renderingPages.has(pageNum)) return;

    const existingIndex = this.renderQueue.findIndex((item) => item.pageNum === pageNum);
    if (existingIndex >= 0 ) {
      const [existing] = this.renderQueue.splice(existingIndex, 1);
      existing.wrapper = wrapper; // Update wrapper in case it changed
      if (priority) {
        this.renderQueue.unshift(existing);
      } else {
        this.renderQueue.push(existing);
      }
      return;
    }

    const queueItem = { pageNum, wrapper };
    if (priority) {
      this.renderQueue.unshift(queueItem);
    } else {
      this.renderQueue.push(queueItem);
    }

    this.processRenderQueue();
  }

  private async processRenderQueue() {
    if (this.renderQueueRunning) return;
    
    this.renderQueueRunning = true;
    
    try {
      while (this.renderQueue.length > 0) {
        const next = this.renderQueue.shift()!;

        if (this.renderedPages.has(next.pageNum) || this.renderingPages.has(next.pageNum)) {
          continue;
        }

        if (!this.isWrapperNearViewport(next.wrapper)) {
          continue;
        }

        await this.renderPage(next.pageNum, next.wrapper);
      }
    } finally {
      this.renderQueueRunning = false;
    }
  }

  private async renderPage(pageNum: number, wrapper: HTMLElement) {
    this.renderingPages.add(pageNum);

    try {
      const page = await this.pdfDoc.getPage(pageNum);
      if (!this.renderingPages.has(pageNum) || !this.isWrapperNearViewport(wrapper)) {
        return;
      }

      const viewport = page.getViewport({ scale: 1.5 });

      wrapper.style.width = `${viewport.width}px`;
      wrapper.style.height = `${viewport.height}px`;

      while (wrapper.firstChild) {
        wrapper.removeChild(wrapper.firstChild);
      }

      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d')!;
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      wrapper.appendChild(canvas);

      const renderTask = page.render({ canvasContext: ctx, viewport });
      this.renderTasks.set(pageNum, renderTask);
      await renderTask.promise;
      this.renderTasks.delete(pageNum);

      if (!this.renderingPages.has(pageNum)) {
        return;
      }

      // Add annotation overlay
      const overlay = document.createElement('canvas');
      overlay.classList.add('annotation-layer');
      overlay.width = viewport.width;
      overlay.height = viewport.height;
      overlay.style.position = 'absolute';
      overlay.style.top = '0';
      overlay.style.left = '0';

      overlay.addEventListener('mousedown', (e) => this.startDrawing(e, overlay, pageNum));
      overlay.addEventListener('mousemove', (e) => this.draw(e, overlay, pageNum));
      overlay.addEventListener('mouseup', () => this.stopDrawing());
      overlay.addEventListener('mouseleave', () => this.stopDrawing());

      // Draw existing annotations
      this.drawExistingAnnotations(pageNum, overlay);

      wrapper.appendChild(overlay);
      this.renderedPages.add(pageNum);
    } catch (err) {
      if ((err as any)?.name === 'RenderingCancelledException') {
        console.error(`Error rendering page ${pageNum}:`, err);
      }
    } finally {
      this.renderTasks.delete(pageNum);
      this.renderingPages.delete(pageNum);
    }
  }

  cleanupPage(pageNum: number, wrapper: HTMLElement) {
    this.cancelQueuedRender(pageNum);

    const renderTask = this.renderTasks.get(pageNum);
    if (renderTask) {
      renderTask.cancel();
      this.renderTasks.delete(pageNum);
    }

    this.renderingPages.delete(pageNum);

    if (!this.renderedPages.has(pageNum) && !wrapper.firstChild) return;

    // Destroy canvases to free browser memory
    while (wrapper.firstChild) {
      wrapper.removeChild(wrapper.firstChild);
    }
    this.renderedPages.delete(pageNum);
  }

  ngOnDestroy() {
    if (this.observer) {
      this.observer.disconnect();
    }

    this.renderQueue = [];
    this.renderTasks.forEach((task) => task.cancel());
    this.renderTasks.clear();
    this.renderingPages.clear();

    if (this.pdfDoc) {
      this.pdfDoc.destroy();
    }
  }

  startDrawing(event: MouseEvent, overlay: HTMLCanvasElement, page: number) {
    if (!this.annotationService.currentTool) return;

    this.isDrawing = true;

    const rect = overlay.getBoundingClientRect();
    this.startX = event.clientX - rect.left;
    this.startY = event.clientY - rect.top;

    this.currentEndX = this.startX;
    this.currentEndY = this.startY;
    this.currentDrawingPage = page;
    this.currentPoints = [{ x: this.startX, y: this.startY }];

    // Create a temporary canvas for shapes
    this.tempCanvas = document.createElement('canvas');
    this.tempCanvas.width = overlay.width;
    this.tempCanvas.height = overlay.height;
    this.tempCanvas.classList.add('temp-layer');

    overlay.parentElement?.appendChild(this.tempCanvas);
    this.tempCtx = this.tempCanvas.getContext('2d');
  }

  draw(event: MouseEvent, overlay: HTMLCanvasElement, page: number) {
    if (!this.isDrawing || !this.tempCtx) return;

    const rect = overlay.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;

    this.currentEndX = x;
    this.currentEndY = y;

    const ctx = this.tempCtx;
    ctx.clearRect(0, 0, overlay.width, overlay.height);

    ctx.strokeStyle = this.annotationService.strokeColor;
    ctx.lineWidth = this.annotationService.strokeWidth;

    const tool = this.annotationService.currentTool;

    if (tool === AnnotationType.Freehand) {
      this.currentPoints.push({ x, y });
      ctx.lineTo(x, y);
      ctx.stroke();
    }

    if (tool === AnnotationType.Rectangle) {
      ctx.strokeRect(this.startX, this.startY, x - this.startX, y - this.startY);
    }

    if (tool === AnnotationType.Arrow) {
      this.drawArrow(ctx, this.startX, this.startY, x, y);
    }

    if (tool === AnnotationType.Highlight) {
      ctx.globalAlpha = 0.3;
      ctx.fillStyle = 'yellow';
      ctx.fillRect(this.startX, this.startY, x - this.startX, y - this.startY);
      ctx.globalAlpha = 1;
    }
  }

  stopDrawing() {
    if (!this.isDrawing) return;
    this.isDrawing = false;

    const tool = this.annotationService.currentTool;
    if (tool && this.currentDrawingPage > 0) {
      const baseAnnotation = {
        type: tool,
        page: this.currentDrawingPage,
        position: { x: this.startX, y: this.startY },
      };

      // Save the annotation properties based on the tool used
      switch (tool) {
        case AnnotationType.Rectangle:
          this.annotations.push({
            ...baseAnnotation,
            width: this.currentEndX - this.startX,
            height: this.currentEndY - this.startY,
            color: this.annotationService.strokeColor,
          });
          break;
        case AnnotationType.Highlight:
          this.annotations.push({
            ...baseAnnotation,
            width: this.currentEndX - this.startX,
            height: this.currentEndY - this.startY,
            color: 'yellow',
            opacity: 0.3,
          });
          break;
        case AnnotationType.Freehand:
          this.annotations.push({
            ...baseAnnotation,
            points: [...this.currentPoints],
            color: this.annotationService.strokeColor,
            strokeWidth: this.annotationService.strokeWidth,
          });
          break;
        case AnnotationType.Arrow:
          this.annotations.push({
            ...baseAnnotation,
            endPosition: { x: this.currentEndX, y: this.currentEndY },
            color: this.annotationService.strokeColor,
            strokeWidth: this.annotationService.strokeWidth,
          });
          break;
      }
    }

    if (this.tempCanvas) {
      const overlay = this.tempCanvas.previousSibling as HTMLCanvasElement;
      const overlayCtx = overlay.getContext('2d')!;

      overlayCtx.drawImage(this.tempCanvas, 0, 0);
      this.tempCanvas.remove();
    }

    this.tempCanvas = undefined;
    this.tempCtx = null;
  }

  drawExistingAnnotations(pageNum: number, overlay: HTMLCanvasElement) {
    const pageAnnotations = this.annotations.filter((a) => a.page === pageNum);
    if (!pageAnnotations.length) return;

    const ctx = overlay.getContext('2d');
    if (!ctx) return;

    pageAnnotations.forEach((ann: any) => {
      ctx.strokeStyle = ann.color || '#ff0000';
      ctx.lineWidth = ann.strokeWidth || 2;
      ctx.fillStyle = ann.color || 'yellow';

      if (ann.type === AnnotationType.Rectangle) {
        ctx.strokeRect(ann.position.x, ann.position.y, ann.width, ann.height);
      } else if (ann.type === AnnotationType.Highlight) {
        ctx.globalAlpha = ann.opacity || 0.3;
        ctx.fillRect(ann.position.x, ann.position.y, ann.width, ann.height);
        ctx.globalAlpha = 1;
      } else if (ann.type === AnnotationType.Freehand) {
        if (ann.points && ann.points.length > 0) {
          ctx.beginPath();
          ctx.moveTo(ann.points[0].x, ann.points[0].y);
          for (let i = 1; i < ann.points.length; i++) {
            ctx.lineTo(ann.points[i].x, ann.points[i].y);
          }
          ctx.stroke();
        }
      } else if (ann.type === AnnotationType.Arrow) {
        if (ann.endPosition) {
          this.drawArrow(ctx, ann.position.x, ann.position.y, ann.endPosition.x, ann.endPosition.y);
        }
      }
    });

    // Todo: Need a better way to manage page annotations. If we add new annotations, and click Save, 
    // the existing annotations will be duplicated at the backend since we are sending all annotations of the page to the server. 
    // We should ideally only send new/updated annotations to the server, but that requires tracking which annotations are new/updated/deleted on the frontend.
  }

  drawArrow(ctx: CanvasRenderingContext2D, x1: number, y1: number, x2: number, y2: number) {
    const headLength = 10;
    const dx = x2 - x1;
    const dy = y2 - y1;
    const angle = Math.atan2(dy, dx);

    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.lineTo(
      x2 - headLength * Math.cos(angle - Math.PI / 6),
      y2 - headLength * Math.sin(angle - Math.PI / 6),
    );
    ctx.moveTo(x2, y2);
    ctx.lineTo(
      x2 - headLength * Math.cos(angle + Math.PI / 6),
      y2 - headLength * Math.sin(angle + Math.PI / 6),
    );
    ctx.stroke();
  }

  scrollToPage(pageNum: number) {
    this.ngZone.run(() => {
      this.currentVisiblePage = pageNum;
      this.cdr.markForCheck();
    });

    const wrapper = this.container.nativeElement.querySelector(
      `[data-page-number="${pageNum}"]`,
    ) as HTMLElement | null;
    if (!wrapper) {
      return;
    }

    const scrollElement = this.scrollContainer.nativeElement as HTMLElement;
    const wrapperRect = wrapper.getBoundingClientRect();
    const containerRect = scrollElement.getBoundingClientRect();

    const fullyVisible =
      wrapperRect.top >= containerRect.top && wrapperRect.bottom <= containerRect.bottom;
    if (fullyVisible) {
      return;
    }

    const targetTop = wrapper.offsetTop;
    scrollElement.scrollTop = targetTop;
  }

  setTool(tool: AnnotationType) {
    this.annotationService.setTool(tool);
  }

  private cancelQueuedRender(pageNum: number) {
    this.renderQueue = this.renderQueue.filter((item) => item.pageNum !== pageNum);
  }

  private isWrapperNearViewport(wrapper: HTMLElement): boolean {
    const scrollEl = this.scrollContainer.nativeElement as HTMLElement;
    const containerRect = scrollEl.getBoundingClientRect();
    const wrapperRect = wrapper.getBoundingClientRect();
    const margin = 1000;

    return (
      wrapperRect.bottom >= containerRect.top - margin &&
      wrapperRect.top <= containerRect.bottom + margin
    );
  }

  save() {
    if (!this.documentId) return;

    this.fileService.saveAnnotations(this.documentId, this.annotations).subscribe({
      next: (res) => {
        console.log('Annotations saved successfully', res);
        if (this.thumbnailSidebar) {
          // this.thumbnailSidebar.downloadThumbnailsZip();
          this.thumbnailSidebar.refreshLoadedThumbnails();
        }
      },
      error: (err) => {
        console.error('Error saving annotations', err);
      }
    });
  }
}
