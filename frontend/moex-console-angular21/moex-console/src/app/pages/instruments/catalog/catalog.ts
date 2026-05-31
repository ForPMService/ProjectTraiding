import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { PageKey } from '../../../shared/page-key';

type DataStatus = 'loaded' | 'partial' | 'not_loaded' | 'needs_check';

interface StockRow {
  secid: string;
  shortname: string;
  list_level: number | null;
  lotsize: number;
  minstep: number;
  data_status: DataStatus;
  loaded_data_kinds: string[];
  period: string | null;
  coverage_percent: number | null;
  coverage_label?: string;
  flags: string[];
  is_tracked: boolean;
}

interface FuturesRow {
  secid: string;
  subtitle: string;
  asset_code: string;
  expiration: string;
  days_to_expiration: number;
  initial_margin: string;
  fee: string | null;
  data_status: DataStatus;
  loaded_data_kinds: string[];
  period: string | null;
  coverage_percent: number | null;
  flags: string[];
  is_tracked: boolean;
}

@Component({
  selector: 'app-catalog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog {
  readonly activeTab = input<'stocks' | 'futures'>('stocks');
  readonly showDrawer = input(false);
  readonly pageChange = output<PageKey>();

  private readonly dataStatusLabels: Record<string, string> = {
    loaded: 'загружено',
    partial: 'частично',
    not_loaded: 'не загружено',
    needs_check: 'требует проверки',
  };

  private readonly flagLabels: Record<string, string> = {
    not_enriched: 'не обогащён',
    enrichment_partial: 'обогащение частично',
    no_relation: 'нет связи',
    asset_code_only: 'только asset_code',
    expiration: 'экспирация',
    suspension: 'ограничение',
    changed: 'изменён',
    stale: 'устарело',
  };

  protected readonly stocks: readonly StockRow[] = [
    {
      secid: 'SBER', shortname: 'Сбербанк', list_level: 1, lotsize: 10, minstep: 0.01,
      data_status: 'partial', loaded_data_kinds: ['candles', 'tradestats', 'orderstats'],
      period: '01.12 – 31.12', coverage_percent: 92, flags: ['enrichment_partial'], is_tracked: true,
    },
    {
      secid: 'GAZP', shortname: 'Газпром', list_level: 1, lotsize: 10, minstep: 0.01,
      data_status: 'partial', loaded_data_kinds: ['candles', 'tradestats'],
      period: '01.12 – 20.12', coverage_percent: 65, flags: ['stale'], is_tracked: false,
    },
    {
      secid: 'LKOH', shortname: 'Лукойл', list_level: 1, lotsize: 1, minstep: 0.5,
      data_status: 'not_loaded', loaded_data_kinds: [],
      period: null, coverage_percent: 0, flags: [], is_tracked: false,
    },
    {
      secid: 'GMKN', shortname: 'Норникель', list_level: 1, lotsize: 1, minstep: 1.0,
      data_status: 'needs_check', loaded_data_kinds: ['candles'],
      period: '20.12 – 31.12', coverage_percent: null, coverage_label: 'пропуски',
      flags: ['suspension', 'changed'], is_tracked: true,
    },
    {
      secid: 'VTBR', shortname: 'ВТБ', list_level: 1, lotsize: 10000, minstep: 0.0001,
      data_status: 'loaded', loaded_data_kinds: ['candles', 'orderstats', 'obstats'],
      period: '01.12 – 31.12', coverage_percent: 100, flags: [], is_tracked: false,
    },
  ];

  protected readonly futures: readonly FuturesRow[] = [
    {
      secid: 'SiM6', subtitle: 'Фьюч. USD/RUB', asset_code: 'Si', expiration: '20.06.26',
      days_to_expiration: 12, initial_margin: '15 420 ₽', fee: '3.21 ₽',
      data_status: 'partial', loaded_data_kinds: ['candles', 'futoi'],
      period: '01.12 – 31.12', coverage_percent: 88, flags: ['expiration'], is_tracked: true,
    },
    {
      secid: 'SiU6', subtitle: 'Фьюч. USD/RUB', asset_code: 'Si', expiration: '19.09.26',
      days_to_expiration: 102, initial_margin: '15 420 ₽', fee: '3.21 ₽',
      data_status: 'not_loaded', loaded_data_kinds: [],
      period: null, coverage_percent: 0, flags: ['asset_code_only'], is_tracked: false,
    },
    {
      secid: 'SiZ6', subtitle: 'Фьюч. USD/RUB', asset_code: 'Si', expiration: '18.12.26',
      days_to_expiration: 192, initial_margin: '15 420 ₽', fee: null,
      data_status: 'not_loaded', loaded_data_kinds: [],
      period: null, coverage_percent: 0, flags: ['not_enriched'], is_tracked: false,
    },
    {
      secid: 'BRN6', subtitle: 'Фьюч. Brent', asset_code: 'BR', expiration: '01.07.26',
      days_to_expiration: 23, initial_margin: '8 200 ₽', fee: '2.00 ₽',
      data_status: 'loaded', loaded_data_kinds: ['candles', 'futoi'],
      period: '01.12 – 31.12', coverage_percent: 100, flags: [], is_tracked: true,
    },
    {
      secid: 'GOLD-6.26', subtitle: 'Фьюч. золото', asset_code: 'GOLD', expiration: '15.06.26',
      days_to_expiration: 7, initial_margin: '22 100 ₽', fee: '5.50 ₽',
      data_status: 'partial', loaded_data_kinds: ['candles'],
      period: '15.12 – 31.12', coverage_percent: 52, flags: ['expiration'], is_tracked: true,
    },
  ];

  protected statusLabel(status: string): string {
    return this.dataStatusLabels[status] ?? status;
  }

  protected flagLabel(flag: string): string {
    return this.flagLabels[flag] ?? flag;
  }

  protected actionLabel(status: DataStatus): string {
    switch (status) {
      case 'loaded': return 'Открыть';
      case 'partial': return 'Дозагрузить';
      case 'needs_check': return 'Проверить';
      default: return 'Загрузить';
    }
  }

  protected actionClass(status: DataStatus): string {
    if (status === 'partial') return 'link-btn--warning';
    if (status === 'needs_check') return 'act-check';
    return 'link-btn';
  }

  protected coverageText(row: { coverage_percent: number | null; coverage_label?: string }): string {
    if (row.coverage_label) return row.coverage_label;
    if (row.coverage_percent === null) return '—';
    return row.coverage_percent + '%';
  }

  protected coverageClass(row: { coverage_percent: number | null; coverage_label?: string }): string {
    if (row.coverage_label) return 'cov-warn';
    if (row.coverage_percent === null) return 'cov-muted';
    if (row.coverage_percent === 100) return 'cov-ok';
    if (row.coverage_percent === 0) return 'cov-muted';
    return 'cov-warn';
  }

  protected daysClass(days: number): string {
    if (days <= 14) return 'days-error';
    if (days <= 30) return 'days-warn';
    return 'days-muted';
  }

  protected isDim(status: DataStatus): boolean {
    return status === 'not_loaded';
  }

  /** «Все» в минимальной версии показывает акции. */
  protected selectTab(tab: 'all' | 'stocks' | 'futures'): void {
    this.pageChange.emit(tab === 'futures' ? 'catalog-futures' : 'catalog-stocks');
  }

  /** Клик по строке → карточка инструмента. */
  protected openCard(): void {
    this.pageChange.emit(this.activeTab() === 'stocks' ? 'card-stock' : 'card-futures');
  }

  /** Клик по action-кнопке → drawer с учётом вкладки. */
  protected openDrawer(): void {
    this.pageChange.emit(
      this.activeTab() === 'stocks' ? 'catalog-stocks-drawer' : 'catalog-futures-drawer',
    );
  }

  protected closeDrawer(): void {
    this.pageChange.emit(
      this.activeTab() === 'stocks' ? 'catalog-stocks' : 'catalog-futures',
    );
  }
}
