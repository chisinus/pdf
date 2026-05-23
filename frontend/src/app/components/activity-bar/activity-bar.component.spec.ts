import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ActivityBarComponent } from './activity-bar.component';

describe('ActivityBarComponent', () => {
  let component: ActivityBarComponent;
  let fixture: ComponentFixture<ActivityBarComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [ActivityBarComponent]
    });
    fixture = TestBed.createComponent(ActivityBarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
