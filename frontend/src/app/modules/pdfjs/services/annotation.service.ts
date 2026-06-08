import { Injectable } from '@angular/core';
import { AnnotationType } from '../../../models/enum';

@Injectable({ providedIn: 'root' })
export class AnnotationService {
  currentTool: AnnotationType | null= null;
  strokeColor = '#ff0000';
  strokeWidth = 2;

  setTool(tool: AnnotationType) {
    this.currentTool = tool;
  }
}
