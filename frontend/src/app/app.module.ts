import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { routes } from './app.routes';

@NgModule({
  declarations: [],
  bootstrap: [],
  imports: [BrowserModule, FormsModule, ReactiveFormsModule, RouterModule.forRoot(routes)],
  providers: [provideHttpClient(withInterceptorsFromDi())],
})
export class AppModule {}
