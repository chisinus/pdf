import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';

import { ThumbnailSidebarComponent } from './thumbnail-sidebar.component';

describe('ThumbnailSidebarComponent', () => {
  let component: ThumbnailSidebarComponent;
  let fixture: ComponentFixture<ThumbnailSidebarComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ThumbnailSidebarComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(ThumbnailSidebarComponent, {
      remove: { imports: [CdkVirtualScrollViewport] },
    });

    fixture = TestBed.createComponent(ThumbnailSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
