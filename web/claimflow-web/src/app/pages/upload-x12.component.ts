import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, describeError } from '../core/api.service';
import { X12BatchResult } from '../core/models';

@Component({
  selector: 'cf-upload-x12',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page">
      <header class="page-head">
        <div>
          <h1>Upload an 837 file</h1>
          <p>
            Send an ASC X12 837P (005010X222A1) professional claim file. Each claim in the file is accepted or
            rejected on its own, so one bad claim never blocks the rest of the batch.
          </p>
        </div>
      </header>

      <section class="panel">
        <div class="panel-body">
          <label class="drop" [class.over]="dragging()" (dragover)="$event.preventDefault(); dragging.set(true)" (dragleave)="dragging.set(false)" (drop)="onDrop($event)">
            <input type="file" accept=".txt,.edi,.x12,.837,text/plain" (change)="onPick($event)" />
            <strong>{{ file()?.name ?? 'Choose a file or drop it here' }}</strong>
            <span class="muted">{{ file() ? kb(file()!.size) + ' KB' : 'Plain-text X12, up to 5 MB' }}</span>
          </label>
          <div class="row">
            <span class="muted">No file handy? Try <a href="sample-837p.txt" download>the sample 837</a>: four claims, two of them deliberately broken.</span>
            <button class="btn btn-primary" type="button" (click)="upload()" [disabled]="!file() || busy()">{{ busy() ? 'Uploading…' : 'Upload file' }}</button>
          </div>
        </div>
      </section>

      @if (error()) {
        <div class="alert alert-error" role="alert">{{ error() }}</div>
      }

      @if (result(); as r) {
        <section class="panel results" aria-live="polite">
          <div class="panel-head">
            <h2>Interchange {{ r.interchangeControlNumber ?? 'unknown' }}</h2>
            <span class="muted">{{ r.claimsInFile }} claims in file · {{ r.accepted.length }} accepted · {{ r.rejected.length }} rejected</span>
          </div>
          @for (e of r.fileErrors; track e) {
            <div class="alert alert-error file-error">{{ e }}</div>
          }
          <ul class="outcomes">
            @for (a of r.accepted; track a.id) {
              <li class="ok">
                <span class="tag">Accepted</span>
                <a class="code" [routerLink]="['/claims', a.id]">{{ a.claimNumber }}</a>
                <span class="muted">queued for adjudication</span>
              </li>
            }
            @for (x of r.rejected; track x.index) {
              <li class="bad">
                <span class="tag">Rejected</span>
                <span>Claim {{ x.index }} in file{{ x.patientControlNumber ? ' (' + x.patientControlNumber + ')' : '' }}</span>
                <span class="why">{{ x.errors.join(' ') }}</span>
              </li>
            }
          </ul>
        </section>
      }
    </div>
  `,
  styles: `
    .panel { max-width: 860px; }
    .drop { display: grid; place-items: center; gap: 0.35rem; padding: 2.5rem 1rem; border: 2px dashed var(--line-strong); border-radius: var(--radius); cursor: pointer; text-align: center; background: var(--paper); }
    .drop.over, .drop:hover { border-color: var(--teal); background: var(--teal-wash); }
    .drop input { position: absolute; width: 1px; height: 1px; opacity: 0; }
    .drop:focus-within { outline: 3px solid var(--gold); outline-offset: 2px; }
    .row { display: flex; flex-wrap: wrap; gap: 1rem; justify-content: space-between; align-items: center; margin-top: 1rem; }
    .alert { max-width: 860px; margin-top: 1.25rem; }
    .results { margin-top: 1.5rem; }
    .file-error { margin: 1rem 1.25rem 0; }
    .outcomes { list-style: none; margin: 0; padding: 0; }
    .outcomes li { display: grid; grid-template-columns: 6rem auto 1fr; gap: 0.75rem; align-items: baseline; padding: 0.75rem 1.25rem; border-bottom: 1px solid var(--line); }
    .outcomes li:last-child { border-bottom: 0; }
    .tag { font-weight: 700; font-size: 0.8125rem; }
    .ok .tag { color: var(--green); }
    .bad .tag { color: var(--red); }
    .why { font-size: 0.875rem; }
    @media (max-width: 640px) { .outcomes li { grid-template-columns: 1fr; gap: 0.2rem; } }
  `
})
export class UploadX12Component {
  private readonly api = inject(ApiService);
  protected readonly file = signal<File | null>(null);
  protected readonly dragging = signal(false);
  protected readonly busy = signal(false);
  protected readonly result = signal<X12BatchResult | null>(null);
  protected readonly error = signal<string | null>(null);

  protected kb(bytes: number): string {
    return (bytes / 1024).toFixed(1);
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.select(input.files?.[0] ?? null);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.select(event.dataTransfer?.files?.[0] ?? null);
  }

  protected upload(): void {
    const file = this.file();
    if (!file) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.uploadX12(file).subscribe({
      next: (r) => {
        this.result.set(r);
        this.busy.set(false);
      },
      error: (e) => {
        this.error.set(describeError(e).message);
        this.busy.set(false);
      }
    });
  }

  private select(file: File | null): void {
    this.result.set(null);
    this.error.set(null);
    if (file && file.size > 5 * 1024 * 1024) {
      this.error.set('That file is larger than 5 MB. Split the batch and upload each part.');
      return;
    }
    this.file.set(file);
  }
}
