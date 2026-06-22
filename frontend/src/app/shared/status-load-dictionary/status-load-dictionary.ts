import { Component, input } from '@angular/core';

@Component({
  selector: 'app-status-load-dictionary',
  imports: [],
  templateUrl: './status-load-dictionary.html',
  styleUrl: './status-load-dictionary.scss',
})
export class StatusLoadDictionary {
  instrumentsTotal = input.required<number>();
  instrumentsStock = input.required<number>();
  instrumentsFutures = input.required<number>();

  // Состояние плашки: загрузка | ошибка | пусто | данные.
  state = input<'loading' | 'error' | 'empty' | 'data'>('data');
}
