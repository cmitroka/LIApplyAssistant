import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-job-grid',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './job-grid.component.html',
  styleUrl: './job-grid.component.scss',
})
export class JobGridComponent {}
