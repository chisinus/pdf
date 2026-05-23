import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
} from "@angular/core";
import { CdkVirtualScrollViewport } from "@angular/cdk/scrolling";

@Component({
    selector: "app-thumbnail-sidebar",
    templateUrl: "./thumbnail-sidebar.component.html",
    styleUrls: ["./thumbnail-sidebar.component.css"],
    imports: [
        CdkVirtualScrollViewport
    ]
})
export class ThumbnailSidebarComponent implements OnChanges {
  @Input() documentId!: string;
  @Input() pageCount: number = 0;
  @Input() activePage: number = 1;
  @Output() pageSelected = new EventEmitter<number>();

  pages: number[] = [];

  @ViewChild(CdkVirtualScrollViewport) viewport!: CdkVirtualScrollViewport;

  ngOnChanges(changes: SimpleChanges) {
    if (changes["pageCount"] && this.pageCount > 0) {
      this.pages = Array.from({ length: this.pageCount }, (_, i) => i + 1);
    }
    if (changes["activePage"] && this.viewport) {
      this.viewport.scrollToIndex(this.activePage - 1, "smooth");
    }
  }

  handleImageError(event: Event) {
    const img = event.target as HTMLImageElement;
    img.src = "assets/thumbnail-placeholder.png";
  }

  selectPage(page: number) {
    this.pageSelected.emit(page);
  }
}
