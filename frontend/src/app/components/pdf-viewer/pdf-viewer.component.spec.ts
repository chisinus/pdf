import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';

import { PdfViewerComponent } from './pdf-viewer.component';
import { ActivityBarComponent } from '../activity-bar/activity-bar.component';
import { PdfContentComponent } from '../pdf-content/pdf-content.component';
import { ThumbnailSidebarComponent } from '../thumbnail-sidebar/thumbnail-sidebar.component';

describe('PdfViewerComponent', () => {
  let component: PdfViewerComponent;
  let fixture: ComponentFixture<PdfViewerComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PdfViewerComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(PdfViewerComponent, {
      remove: { imports: [ActivityBarComponent, PdfContentComponent, ThumbnailSidebarComponent] },
    });

    fixture = TestBed.createComponent(PdfViewerComponent);
    component = fixture.componentInstance;

    // Initialize required inputs before the first lifecycle hooks trigger
    component.documentId = 'test-document-id';
    component.pageCount = 10;

    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
