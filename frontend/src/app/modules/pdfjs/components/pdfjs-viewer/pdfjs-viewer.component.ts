import {
  Component,
  AfterViewInit,
  OnDestroy,
  ElementRef,
  ViewChild,
  Input,
  NgZone,
} from '@angular/core';

import { PdfjsService } from '../../services/pdfjs.service';
import { AnnotationService } from '../../services/annotation.service';


// Use the PDF.js legacy build and a fake-worker integration to avoid the
// worker stream handshake bug in this Angular app.

@Component({
  selector: 'app-pdfjs-viewer',
  templateUrl: './pdfjs-viewer.component.html',
  styleUrls: ['./pdfjs-viewer.component.scss'],
  standalone: true,
})
export class PdfjsViewerComponent implements AfterViewInit, OnDestroy {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;

  @ViewChild('container', { static: true }) container!: ElementRef;

  private isDrawing = false;
  private startX = 0;
  private startY = 0;
  private tempCanvas?: HTMLCanvasElement;
  private tempCtx?: CanvasRenderingContext2D | null;

  private pdfDoc: any = null;
  private observer: IntersectionObserver | null = null;
  private renderedPages = new Set<number>();

  constructor(
    private ngZone: NgZone,
    private pdfjsService: PdfjsService,
    private annotationService: AnnotationService,
  ) {}

  async ngAfterViewInit() {
    if (!this.documentId) return;

    this.ngZone.runOutsideAngular(async () => {
      const url = `http://localhost:4001/api/download/${this.documentId}`;

      const pdf = await this.pdfjsService.loadDocument(url);
      this.pdfDoc = pdf;

      // Get the first page to estimate placeholder dimensions
      const firstPage = await pdf.getPage(1);
      const viewport = firstPage.getViewport({ scale: 1.5 });
      const defaultWidth = viewport.width;
      const defaultHeight = viewport.height;

      // Setup IntersectionObserver to only render visible pages
      this.observer = new IntersectionObserver(
        (entries) => {
          entries.forEach((entry) => {
            const pageNum = Number(entry.target.getAttribute('data-page-number'));
            if (entry.isIntersecting) {
              this.loadAndRenderPage(pageNum, entry.target as HTMLElement);
            } else {
              this.cleanupPage(pageNum, entry.target as HTMLElement);
            }
          });
        },
        { rootMargin: '1000px 0px' },
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
        wrapper.style.backgroundColor = '#f3f4f6'; // Light gray placeholder
        wrapper.style.boxShadow = '0 4px 6px rgba(0,0,0,0.1)';

        this.container.nativeElement.appendChild(wrapper);
        this.observer!.observe(wrapper);
      }
    });
  }

  async loadAndRenderPage(pageNum: number, wrapper: HTMLElement) {
    if (this.renderedPages.has(pageNum)) return;
    this.renderedPages.add(pageNum);

    try {
      const page = await this.pdfDoc.getPage(pageNum);
      const viewport = page.getViewport({ scale: 1.5 });

      wrapper.style.width = `${viewport.width}px`;
      wrapper.style.height = `${viewport.height}px`;

      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d')!;
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      wrapper.appendChild(canvas);

      await page.render({ canvasContext: ctx, viewport }).promise;

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

      wrapper.appendChild(overlay);
    } catch (err) {
      console.error(`Error rendering page ${pageNum}:`, err);
      this.renderedPages.delete(pageNum);
    }
  }

  cleanupPage(pageNum: number, wrapper: HTMLElement) {
    if (!this.renderedPages.has(pageNum)) return;

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

    const ctx = this.tempCtx;
    ctx.clearRect(0, 0, overlay.width, overlay.height);

    ctx.strokeStyle = this.annotationService.strokeColor;
    ctx.lineWidth = this.annotationService.strokeWidth;

    const tool = this.annotationService.currentTool;

    if (tool === 'pen') {
      ctx.lineTo(x, y);
      ctx.stroke();
    }

    if (tool === 'rect') {
      ctx.strokeRect(this.startX, this.startY, x - this.startX, y - this.startY);
    }

    if (tool === 'arrow') {
      this.drawArrow(ctx, this.startX, this.startY, x, y);
    }

    if (tool === 'highlight') {
      ctx.globalAlpha = 0.3;
      ctx.fillStyle = 'yellow';
      ctx.fillRect(this.startX, this.startY, x - this.startX, y - this.startY);
      ctx.globalAlpha = 1;
    }
  }

  stopDrawing() {
    if (!this.isDrawing) return;
    this.isDrawing = false;

    if (this.tempCanvas) {
      const overlay = this.tempCanvas.previousSibling as HTMLCanvasElement;
      const overlayCtx = overlay.getContext('2d')!;

      overlayCtx.drawImage(this.tempCanvas, 0, 0);
      this.tempCanvas.remove();
    }

    this.tempCanvas = undefined;
    this.tempCtx = null;
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

  setTool(tool: any) {
    this.annotationService.setTool(tool);
  }
}
