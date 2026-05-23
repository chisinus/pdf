import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PdfContentComponent } from './pdf-content.component';

describe('PdfContentComponent', () => {
  let component: PdfContentComponent;
  let fixture: ComponentFixture<PdfContentComponent>;

  beforeEach(() => {
    (window as any).IntersectionObserver = jest.fn().mockImplementation(() => ({
      observe: jest.fn(),
      unobserve: jest.fn(),
      disconnect: jest.fn(),
    }));

    TestBed.configureTestingModule({
      imports: [PdfContentComponent],
    });
    fixture = TestBed.createComponent(PdfContentComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
