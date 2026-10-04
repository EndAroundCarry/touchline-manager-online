import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import type { Chart, ChartConfiguration, ChartDataset, ChartOptions } from 'chart.js';
import { ThemeStore } from '../../../core/theme/theme-store';

/** One line on the chart. */
export interface LineChartSeries {
  /** The name in the legend and the tooltip. */
  readonly label: string;

  /** One value per label, or null where the line breaks. */
  readonly data: readonly (number | null)[];

  /** The line and point colour. */
  readonly color: string;

  /** The dash pattern, empty for solid. Together with `pointStyle` it tells series apart without colour. */
  readonly dash: readonly number[];

  /** The Chart.js point style: `circle`, `triangle`, `rect`, `rectRot`, `cross` or `star`. */
  readonly pointStyle: string;

  /** An optional note per label, shown under that day's value in the tooltip. */
  readonly notes?: readonly (string | null)[];
}

/** The chart ink: the screen's own tokens, read from the page so the chart follows the theme (ADR-0059). */
interface ChartInk {
  readonly grid: string;
  readonly zeroLine: string;
  readonly tick: string;
  readonly pointBorder: string;
}

/** Reads the ink from the CSS tokens now in force; the fallbacks are the dark theme's values. */
function readInk(): ChartInk {
  const style = typeof document === 'undefined' ? null : getComputedStyle(document.documentElement);
  const token = (name: string, fallback: string): string =>
    style?.getPropertyValue(name).trim() || fallback;

  return {
    grid: token('--color-line', '#2a3441'),
    zeroLine: token('--color-line-strong', '#3d495a'),
    tick: token('--color-muted', '#98a4b5'),
    pointBorder: token('--color-panel', '#171d25'),
  };
}

/**
 * A line chart drawn with Chart.js.
 *
 * Chart.js is imported on demand, after the first render, so the library lands in its own lazy chunk and
 * never in the initial bundle. Only the pieces a line chart needs are registered, which keeps that chunk
 * small. The chart is created once, updated from an `effect` when the inputs change, and destroyed with the
 * component.
 *
 * A canvas is invisible to assistive technology, so it carries `role="img"` and a caller-supplied summary,
 * and the caller is responsible for putting the same numbers in a table beside it. The legend is HTML, not
 * canvas: it can show each series' dash and point shape as well as its colour (ADR-0039), and it is text a
 * screen reader reads.
 */
@Component({
  selector: 'app-line-chart',
  templateUrl: './line-chart.html',
  host: { class: 'block min-w-0 max-w-full' },
})
export class LineChart {
  /** The x-axis labels, one per data point. */
  readonly labels = input.required<readonly string[]>();

  /** The lines. */
  readonly series = input.required<readonly LineChartSeries[]>();

  /** What a screen reader hears for the canvas. */
  readonly ariaLabel = input.required<string>();

  /** The y-axis title. */
  readonly yTitle = input('Points');

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly destroyRef = inject(DestroyRef);
  private readonly theme = inject(ThemeStore);

  private chart: Chart<'line', (number | null)[], string> | null = null;
  private destroyed = false;

  /** The chart ink, read again whenever the theme changes. */
  private readonly ink = computed(() => {
    this.theme.mode();

    return readInk();
  });

  /** The data in the shape Chart.js takes, recomputed when an input changes. */
  private readonly data = computed(() => ({
    labels: [...this.labels()],
    datasets: this.series().map((line): ChartDataset<'line', (number | null)[]> => ({
      label: line.label,
      data: [...line.data],
      borderColor: line.color,
      backgroundColor: line.color,
      borderWidth: 2,
      borderDash: [...line.dash],
      pointStyle: line.pointStyle as ChartDataset<'line'>['pointStyle'],
      pointRadius: 3,
      pointHoverRadius: 5,
      pointHitRadius: 12,
      pointBackgroundColor: line.color,
      pointBorderColor: this.ink().pointBorder,
      pointBorderWidth: 1,
      spanGaps: false,
      tension: 0,
    })),
  }));

  /** The legend's entries: the same colour, dash and point shape the chart draws. */
  protected readonly legend = computed(() =>
    this.series().map((line) => ({
      label: line.label,
      color: line.color,
      dash: line.dash.join(' '),
      pointStyle: line.pointStyle,
    })),
  );

  constructor() {
    afterNextRender(() => {
      void this.create();
    });

    // Reads the data signal, so it reruns when an input changes. Before the chart exists there is nothing
    // to update, and `create` reads the signal itself.
    effect(() => {
      const data = this.data();

      const ink = this.ink();

      if (this.chart !== null) {
        this.chart.options = this.options(ink);
        this.chart.data.labels = data.labels;
        this.chart.data.datasets = data.datasets;
        this.chart.update();
      }
    });

    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.chart?.destroy();
      this.chart = null;
    });
  }

  private async create(): Promise<void> {
    const {
      Chart,
      CategoryScale,
      LinearScale,
      LineController,
      LineElement,
      PointElement,
      Tooltip,
    } = await import('chart.js');

    // The component can be gone by the time the library arrives.
    if (this.destroyed) {
      return;
    }

    Chart.register(CategoryScale, LinearScale, LineController, LineElement, PointElement, Tooltip);

    this.chart = new Chart(this.canvas().nativeElement, this.configuration());
  }

  private configuration(): ChartConfiguration<'line', (number | null)[], string> {
    return { type: 'line', data: this.data(), options: this.options(this.ink()) };
  }

  /** The chart options in the given ink. Rebuilt when the theme changes so the axes and the grid follow it. */
  private options(ink: ChartInk): ChartOptions<'line'> {
    const reducedMotion =
      typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    const series = this.series();

    return {
      responsive: true,
      maintainAspectRatio: false,
      animation: reducedMotion ? false : undefined,
      interaction: { mode: 'index', intersect: false },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: ink.tick, maxTicksLimit: 8, autoSkip: true },
        },
        y: {
          title: { display: true, text: this.yTitle(), color: ink.tick },
          grid: {
            color: (context) => (context.tick.value === 0 ? ink.zeroLine : ink.grid),
          },
          ticks: { color: ink.tick },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => `${context.dataset.label}: ${context.formattedValue}`,
            afterLabel: (context) => series[context.datasetIndex]?.notes?.[context.dataIndex] ?? '',
          },
        },
      },
    };
  }
}
