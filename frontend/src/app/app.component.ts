import { Component } from "@angular/core";

@Component({
    selector: "app-root",
    template: `
    <h1>File Manager</h1>
    <router-outlet></router-outlet>
  `,
    standalone: false
})
export class AppComponent {}
