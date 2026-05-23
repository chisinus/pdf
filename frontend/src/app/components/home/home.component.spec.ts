import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of } from 'rxjs';

import { HomeComponent } from './home.component';
import { FileService } from '../../services/file.service';
import { UploadDialogComponent } from '../../upload-dialog/upload-dialog.component';
import { FileInfo } from '../../models/file-info.interface';

describe('HomeComponent', () => {
  let component: HomeComponent;
  let fixture: ComponentFixture<HomeComponent>;
  let fileServiceMock: {
    getFiles: jest.Mock;
    deleteFile: jest.Mock;
  };

  beforeEach(async () => {
    fileServiceMock = {
      getFiles: jest.fn().mockReturnValue(of([])),
      deleteFile: jest.fn().mockReturnValue(of({})),
    };

    // Mock global window methods to prevent actual browser popups/alerts during tests
    jest.spyOn(window, 'confirm').mockImplementation(() => true);
    jest.spyOn(window, 'open').mockImplementation(() => null);

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [{ provide: FileService, useValue: fileServiceMock }],
      schemas: [NO_ERRORS_SCHEMA],
    })
      .overrideComponent(HomeComponent, {
        remove: { imports: [UploadDialogComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(HomeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load files from the file service', () => {
    const mockFiles = [
      {
        name: 'doc.pdf',
        uniqueName: 'folder-1',
        url: '/api/pdf/1/download',
        size: 1024,
        thumbnails: 3,
      } as FileInfo,
    ];
    fileServiceMock.getFiles.mockReturnValue(of(mockFiles));

    component.loadFiles();

    expect(fileServiceMock.getFiles).toHaveBeenCalled();
    expect(component.files).toEqual(mockFiles);
  });

  it('should delete file and reload if confirmed', () => {
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    const loadFilesSpy = jest.spyOn(component, 'loadFiles');

    component.deleteFile('test-id');

    expect(window.confirm).toHaveBeenCalledWith('Delete file?');
    expect(fileServiceMock.deleteFile).toHaveBeenCalledWith('test-id');
    expect(loadFilesSpy).toHaveBeenCalled();
  });

  it('should not delete file if not confirmed', () => {
    jest.spyOn(window, 'confirm').mockReturnValue(false);

    component.deleteFile('test-id');

    expect(window.confirm).toHaveBeenCalledWith('Delete file?');
    expect(fileServiceMock.deleteFile).not.toHaveBeenCalled();
  });

  it('should open file in a new window if url exists', () => {
    const file = {
      name: 'doc.pdf',
      uniqueName: 'folder-1',
      url: 'http://test.com/doc.pdf',
      size: 100,
      thumbnails: 1,
    } as FileInfo;
    component.openFile(file);
    expect(window.open).toHaveBeenCalledWith('http://test.com/doc.pdf', '_blank');
  });

  it('should not open file if url is falsy', () => {
    const file = {
      name: 'doc.pdf',
      uniqueName: 'folder-1',
      url: '',
      size: 100,
      thumbnails: 1,
    } as FileInfo;
    component.openFile(file);
    expect(window.open).not.toHaveBeenCalled();
  });

  it('should display upload dialog when upload button is clicked', () => {
    component.onUploadClicked();
    expect(component.showUpload).toBe(true);
  });

  it('should hide upload dialog and reload files if upload succeeds', () => {
    const loadFilesSpy = jest.spyOn(component, 'loadFiles');
    component.showUpload = true;

    component.onUploadFinished(true);

    expect(component.showUpload).toBe(false);
    expect(loadFilesSpy).toHaveBeenCalled();
  });

  it('should hide upload dialog and not reload files if upload fails or is cancelled', () => {
    const loadFilesSpy = jest.spyOn(component, 'loadFiles');
    component.showUpload = true;

    component.onUploadFinished(false);

    expect(component.showUpload).toBe(false);
    expect(loadFilesSpy).not.toHaveBeenCalled();
  });
});
