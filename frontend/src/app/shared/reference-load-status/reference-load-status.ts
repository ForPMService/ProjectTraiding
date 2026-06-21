import { Component, input } from '@angular/core';

@Component({
  selector: 'app-reference-load-status',
  imports: [],
  templateUrl: './reference-load-status.html',
  styleUrl: './reference-load-status.scss',
})
export class ReferenceLoadStatus {
  label = input.required<string>();
  value = input.required<string>();
  subtitle = input.required<string>();

}
