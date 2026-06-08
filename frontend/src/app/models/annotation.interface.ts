import { AnnotationType } from "./enum";

export interface AnnotationBase {
  type: AnnotationType;
  page: number;
  position: {
    x: number;
    y: number;
  };
}

export interface AnnotationRectangle extends AnnotationBase {
  type: AnnotationType.Rectangle;
  width: number;
  height: number
  color: string;
}

export interface AnnotationFreehand extends AnnotationBase {
  type: AnnotationType.Freehand;
  points: { x: number; y: number }[];
  color: string;
  strokeWidth: number;
}

export interface AnnotationText extends AnnotationBase {
  type: AnnotationType.Text;
  text: string;
  color: string;
  fontSize: number;
}

export interface AnnotationHighlight extends AnnotationBase {
  type: AnnotationType.Highlight;
  color: string;
  opacity: number;
}