import { Component } from '@angular/core';
import { StatusLoadDictionary} from '../../shared/status-load-dictionary/status-load-dictionary';

@Component({
  selector: 'app-catalog',
  imports: [StatusLoadDictionary],
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog {}
