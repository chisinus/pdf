import { Injectable } from '@angular/core';
import * as pdfjsLib from 'pdfjs-dist/legacy/build/pdf.mjs';

const NativePromise = (async () => {})().constructor as PromiseConstructor;

function ensurePromisePolyfills() {
  if (!(Promise as any).try) {
    (Promise as any).try = function (fn: any) {
      return new NativePromise((resolve, reject) => {
        try {
          resolve(fn());
        } catch (e) {
          reject(e);
        }
      });
    };
  }

  if (!(Promise as any).withResolvers) {
    (Promise as any).withResolvers = function () {
      let resolve: (value: any) => void;
      let reject: (reason?: any) => void;
      const promise = new NativePromise((res, rej) => {
        resolve = res;
        reject = rej;
      });
      return { promise, resolve: resolve!, reject: reject! };
    };
  }
}

@Injectable({ providedIn: 'root' })
export class PdfjsService {
  constructor() {
    ensurePromisePolyfills();
  }

  private async ensureFakeWorker() {
    const globalPdfjsWorker = (globalThis as any).pdfjsWorker as
      | { WorkerMessageHandler?: any }
      | undefined;

    if (globalPdfjsWorker?.WorkerMessageHandler) {
      return;
    }

    const workerModule = await import('pdfjs-dist/legacy/build/pdf.worker.min.mjs');
    (globalThis as any).pdfjsWorker = {
      ...globalPdfjsWorker,
      WorkerMessageHandler: workerModule.WorkerMessageHandler,
    };
  }

  async loadDocument(url: string) {
    await this.ensureFakeWorker();
    return pdfjsLib.getDocument({
      url,
      disableStream: true,
      disableRange: true,
    }).promise;
  }
}
