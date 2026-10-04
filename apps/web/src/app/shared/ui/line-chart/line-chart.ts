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
import type { Chart, ChartConfiguration, ChartDataset } from 'chart.js';

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

/** The grid and tick colours: slate, so the chart ink matches the rest of the screen and the lines lead. */
const GRID_COLOR = '#e2e8f0';
const ZERO_LINE_COLOR = '#94a3b8';
const TICK_COLOR = '#475569';

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

  private chart: Chart<'line', (number | null)[], string> | null = null;
  private destroyed = false;

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
      pointBorderColor: '#ffffff',
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

      if (this.chart !== null) {
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
    const reducedMotion =
      typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    const series = this.series();

    return {
      type: 'line',
      data: this.data(),
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: reducedMotion ? false : undefined,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: {
            grid: { display: false },
            ticks: { color: TICK_COLOR, maxTicksLimit: 8, autoSkip: true },
          },
          y: {
            title: { display: true, text: this.yTitle(), color: TICK_COLOR },
            grid: {
              color: (context) => (context.tick.value === 0 ? ZERO_LINE_COLOR : GRID_COLOR),
            },
            ticks: { color: TICK_COLOR },
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (context) => `${context.dataset.label}: ${context.formattedValue}`,
              afterLabel: (context) =>
                series[context.datasetIndex]?.notes?.[context.dataIndex] ?? '',
            },
          },
        },
      },
    };
  }
}
