import { Component, AfterViewInit, ElementRef, ViewChild } from '@angular/core';

import * as pdfjsLib from 'pdfjs-dist';

pdfjsLib.GlobalWorkerOptions.workerSrc = '/assets/pdf.worker.min.mjs';

@Component({
  selector: 'app-pdf-viewer',
  templateUrl: './pdfjs-viewer.component.html',
  styleUrls: ['./pdfjs-viewer.component.scss'],
  standalone: true,
})
export class PdfjsViewerComponent implements AfterViewInit {
  @ViewChild('container', { static: true }) container!: ElementRef;

  async ngAfterViewInit() {
    const pdf = await pdfjsLib.getDocument('/assets/sample.pdf').promise;

    for (let pageNum = 1; pageNum <= pdf.numPages; pageNum++) {
      const page = await pdf.getPage(pageNum);
      await this.renderPage(page, pageNum);
    }
  }

  async renderPage(page: any, pageNum: number) {
    const viewport = page.getViewport({ scale: 1.5 });

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d')!;

    canvas.width = viewport.width;
    canvas.height = viewport.height;

    const wrapper = document.createElement('div');
    wrapper.classList.add('page-wrapper');

    wrapper.appendChild(canvas);
    this.container.nativeElement.appendChild(wrapper);

    await page.render({ canvasContext: ctx, viewport }).promise;

    // Add annotation overlay
    const overlay = document.createElement('canvas');
    overlay.classList.add('annotation-layer');
    overlay.width = viewport.width;
    overlay.height = viewport.height;

    wrapper.appendChild(overlay);
  }
}
