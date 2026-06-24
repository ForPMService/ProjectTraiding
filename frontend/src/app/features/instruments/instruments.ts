import { Component } from '@angular/core';
import { StatusLoadDictionary} from '../../shared/status-load-dictionary/status-load-dictionary';

@Component({
  selector: 'app-instruments',
  imports: [StatusLoadDictionary],
  templateUrl: './instruments.html',
  styleUrls: ['./instruments.scss'],
})
export class Instruments {}
