import { Injectable } from '@angular/core';

export type Tool = 'pen' | 'rect' | 'arrow' | 'text' | 'highlight' | null;

@Injectable({ providedIn: 'root' })
export class AnnotationService {
  currentTool: Tool = null;
  strokeColor = '#ff0000';
  strokeWidth = 2;

  setTool(tool: Tool) {
    this.currentTool = tool;
  }
}
