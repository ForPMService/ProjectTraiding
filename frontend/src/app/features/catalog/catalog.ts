import { Component } from '@angular/core';
import { ReferenceLoadStatus} from '../../shared/reference-load-status/reference-load-status';

@Component({
  selector: 'app-catalog',
  imports: [ReferenceLoadStatus],
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog {}
