import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ThumbnailSidebarComponent } from './thumbnail-sidebar.component';

describe('ThumbnailSidebarComponent', () => {
  let component: ThumbnailSidebarComponent;
  let fixture: ComponentFixture<ThumbnailSidebarComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [ThumbnailSidebarComponent]
    });
    fixture = TestBed.createComponent(ThumbnailSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
