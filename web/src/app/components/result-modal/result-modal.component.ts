import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ImportResponse } from '../../models/job';

@Component({
  selector: 'app-result-modal',
  standalone: true,
  templateUrl: './result-modal.component.html',
  styleUrl: './result-modal.component.scss',
})
export class ResultModalComponent {
  @Input({ required: true }) result!: ImportResponse;
  @Output() readonly closed = new EventEmitter<void>();

  close(): void {
    this.closed.emit();
  }
}
