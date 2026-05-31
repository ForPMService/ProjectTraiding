import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { PageKey } from '../../shared/page-key';

interface ReadyChip {
  label: string;
  state: 'ok' | 'warn';
}
interface Metric {
  label: string;
  value: string;
  sub: string;
}
interface Alert {
  title: string;
  text: string;
  actionLabel: string;
  action: PageKey;
}
interface LoadTask {
  secid: string;
  data_kind: string;
  kind_label: string; // акция / фьюч.
  period: string;
  rows: string;
  status: 'done' | 'running' | 'error' | 'attention';
  time: string;
  is_tracked: boolean;
  highlight?: boolean;
}

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  readonly pageChange = output<PageKey>();

  protected readonly readyChips: readonly ReadyChip[] = [
    { label: 'Инструменты', state: 'ok' },
    { label: 'Календарь', state: 'ok' },
    { label: 'Ограничения', state: 'ok' },
    { label: 'Обогащение', state: 'warn' },
    { label: 'Тарифы', state: 'ok' },
  ];

  protected readonly metrics: readonly Metric[] = [
    { label: 'Инструменты', value: '247', sub: '183 акции · 64 фьючерса' },
    { label: 'Карточки', value: '186 / 247', sub: 'обогащены MarketStatistics' },
    { label: 'Задания сегодня', value: '32', sub: '27 успешно · 5 требуют внимания' },
    { label: 'Строки за 24 ч', value: '45 832', sub: 'file · 4 типа данных' },
  ];

  protected readonly criticalAlerts: readonly Alert[] = [
    {
      title: 'GMKN candles — задание упало, диапазон 20.12—31.12 не загружен',
      text: 'HTTP 429, 3 попытки исчерпаны. Ограничений по инструменту не найдено.',
      actionLabel: 'Повторить →',
      action: 'card-stock',
    },
  ];

  protected readonly warningAlerts: readonly Alert[] = [
    {
      title: '61 инструмент без обогащения MarketStatistics',
      text: 'Карточки доступны, но часть полей MarketStatistics ещё не заполнена.',
      actionLabel: 'Открыть инструменты →',
      action: 'catalog-stocks',
    },
    {
      title: '3 активные приостановки торгов',
      text: 'Перед созданием заданий проверьте ограничения по инструментам.',
      actionLabel: 'Открыть ограничения →',
      action: 'catalog-stocks',
    },
    {
      title: 'SiM6 — экспирация через 12 дней',
      text: 'Ближний контракт Si. Проверьте таблицу ролла и переключение.',
      actionLabel: 'Открыть фьючерсы →',
      action: 'catalog-futures',
    },
  ];

  protected readonly loadTasks: readonly LoadTask[] = [
    { secid: 'SBER', data_kind: 'candles', kind_label: 'акция', period: '01.12 – 31.12', rows: '22 341', status: 'done', time: '09:42:18', is_tracked: true },
    { secid: 'GAZP', data_kind: 'tradestats', kind_label: 'акция', period: '01.12 – 31.12', rows: '18 902', status: 'done', time: '09:41:55', is_tracked: false },
    { secid: 'LKOH', data_kind: 'obstats', kind_label: 'акция', period: '15.12 – 31.12', rows: '8 210', status: 'running', time: '09:40:11', is_tracked: true },
    { secid: 'SiM6', data_kind: 'futoi', kind_label: 'фьюч.', period: '01.12 – 31.12', rows: '1 440', status: 'done', time: '09:39:02', is_tracked: true },
    { secid: 'GMKN', data_kind: 'candles', kind_label: 'акция', period: '20.12 – 31.12', rows: '3 180', status: 'error', time: '09:38:44', is_tracked: false },
    { secid: 'ROSN', data_kind: 'orderstats', kind_label: 'акция', period: '01.12 – 31.12', rows: '25 118', status: 'done', time: '09:37:21', is_tracked: false },
    { secid: 'MONT', data_kind: 'candles', kind_label: 'акция', period: '01.12 – 15.12', rows: '11 402', status: 'attention', time: '09:36:09', is_tracked: true, highlight: true },
    { secid: 'SiH7', data_kind: 'candles', kind_label: 'фьюч.', period: '01.12 – 31.12', rows: '14 220', status: 'done', time: '09:35:47', is_tracked: true },
    { secid: 'VTBR', data_kind: 'candles', kind_label: 'акция', period: '01.12 – 31.12', rows: '19 004', status: 'done', time: '09:34:12', is_tracked: false },
    { secid: 'NVTK', data_kind: 'obstats', kind_label: 'акция', period: '10.12 – 31.12', rows: '7 821', status: 'done', time: '09:33:28', is_tracked: false },
    { secid: 'TATN', data_kind: 'candles', kind_label: 'акция', period: '01.12 – 31.12', rows: '20 156', status: 'done', time: '09:32:05', is_tracked: false },
    { secid: 'ALRS', data_kind: 'tradestats', kind_label: 'акция', period: '01.12 – 31.12', rows: '12 447', status: 'done', time: '09:30:43', is_tracked: false },
  ];

  private readonly taskStatusLabels: Record<string, string> = {
    done: 'успешно',
    running: 'выполняется',
    error: 'ошибка',
    attention: 'внимание',
  };

  protected taskStatusLabel(status: string): string {
    return this.taskStatusLabels[status] ?? status;
  }

  protected taskActionLabel(status: string): string {
    if (status === 'error') return 'Повторить';
    if (status === 'attention') return 'Проверить';
    if (status === 'running') return 'Детали';
    return 'Открыть';
  }

  /** Клик по строке → карточка инструмента (акция/фьючерс по типу). */
  protected openRow(task: LoadTask): void {
    this.pageChange.emit(task.kind_label === 'фьюч.' ? 'card-futures' : 'card-stock');
  }
}
