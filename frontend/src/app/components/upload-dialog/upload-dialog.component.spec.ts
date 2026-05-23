import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { UploadDialogComponent } from './upload-dialog.component';
import { FileService } from '../../services/file.service';

describe('UploadDialogComponent', () => {
  let component: UploadDialogComponent;
  let fixture: ComponentFixture<UploadDialogComponent>;
  let fileServiceMock: { upload: jest.Mock };

  beforeEach(async () => {
    fileServiceMock = {
      upload: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [UploadDialogComponent],
      providers: [{ provide: FileService, useValue: fileServiceMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(UploadDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should correctly select a file via input change', () => {
    const file = new File([''], 'test.pdf', { type: 'application/pdf' });
    const event = {
      target: { files: [file] },
    } as unknown as Event;

    component.onFileSelected(event);

    expect(component.file).toBe(file);
    expect(component.selectedFileName).toBe('test.pdf');
  });

  it('should handle file drop correctly', () => {
    const file = new File([''], 'dropped.pdf', { type: 'application/pdf' });
    const event = {
      preventDefault: jest.fn(),
      dataTransfer: { files: [file] },
    } as unknown as DragEvent;

    component.dragging = true;
    component.onDrop(event);

    expect(event.preventDefault).toHaveBeenCalled();
    expect(component.dragging).toBe(false);
    expect(component.file).toBe(file);
    expect(component.selectedFileName).toBe('dropped.pdf');
  });

  it('should manage drag state on dragover and dragleave', () => {
    const event = { preventDefault: jest.fn() } as unknown as DragEvent;

    component.onDragOver(event);
    expect(event.preventDefault).toHaveBeenCalled();
    expect(component.dragging).toBe(true);

    component.onDragLeave();
    expect(component.dragging).toBe(false);
  });

  it('should upload file and emit true on success', () => {
    const file = new File([''], 'test.pdf');
    component.file = file;
    fileServiceMock.upload.mockReturnValue(of({}));
    jest.spyOn(component.close, 'emit');

    component.upload();

    expect(fileServiceMock.upload).toHaveBeenCalledWith(file);
    expect(component.uploading).toBe(false);
    expect(component.close.emit).toHaveBeenCalledWith(true);
  });

  it('should emit false and reset uploading state on upload error', () => {
    const file = new File([''], 'test.pdf');
    component.file = file;
    fileServiceMock.upload.mockReturnValue(throwError(() => new Error('Upload failed')));
    jest.spyOn(component.close, 'emit');

    component.upload();

    expect(fileServiceMock.upload).toHaveBeenCalledWith(file);
    expect(component.uploading).toBe(false);
    expect(component.close.emit).toHaveBeenCalledWith(false);
  });

  it('should not upload if no file is selected', () => {
    component.file = undefined;
    component.upload();
    expect(fileServiceMock.upload).not.toHaveBeenCalled();
  });

  it('should emit false on cancel', () => {
    jest.spyOn(component.close, 'emit');
    component.cancel();
    expect(component.close.emit).toHaveBeenCalledWith(false);
  });
});
