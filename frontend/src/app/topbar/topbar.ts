import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-topbar',
  imports: [],
  templateUrl: './topbar.html',
  styleUrl: './topbar.scss',
})
export class Topbar {
  // Живая ячейка со временем. Стартовое значение — текущее время браузера.
  protected readonly time = signal(this.formatNow());

  constructor() {
    // Раз в минуту кладём в ячейку свежее время — разметка обновится сама.
    setInterval(() => this.time.set(this.formatNow()), 1000);
  }

  // Берём текущее время браузера и форматируем как ЧЧ:ММ:СС.
  private formatNow(): string {
    return new Date().toLocaleTimeString('ru-RU');
  }
}
