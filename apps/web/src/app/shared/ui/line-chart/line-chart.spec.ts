import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LineChart, LineChartSeries } from './line-chart';

/**
 * The line chart wrapper.
 *
 * jsdom has no canvas, so `chart.js` is replaced by a recorder: the specs assert what the component asks
 * Chart.js to draw, not what Chart.js paints. The things that matter are that the library is loaded on
 * demand and only after the first render, that each series keeps its colour, dash and point style, that an
 * input change updates the one chart rather than building another, and that nothing is left behind when the
 * component goes.
 */

interface RecordedChart {
  readonly canvas: HTMLCanvasElement;
  readonly config: {
    type: string;
    data: { labels: string[]; datasets: Record<string, unknown>[] };
    options: {
      plugins: {
        legend: { display: boolean };
        tooltip: {
          callbacks: {
            label: (context: unknown) => string;
            afterLabel: (context: unknown) => string;
          };
        };
      };
    };
  };
  data: { labels: string[]; datasets: Record<string, unknown>[] };
  update: ReturnType<typeof vi.fn>;
  destroy: ReturnType<typeof vi.fn>;
}

const created = vi.hoisted(() => [] as unknown[]);
const registered = vi.hoisted(() => [] as unknown[][]);

vi.mock('chart.js', () => {
  class FakeChart {
    static register(...parts: unknown[]): void {
      registered.push(parts);
    }

    data: unknown;
    update = vi.fn();
    destroy = vi.fn();

    constructor(
      readonly canvas: HTMLCanvasElement,
      readonly config: { data: unknown },
    ) {
      this.data = config.data;
      created.push(this);
    }
  }

  const part = (name: string) => ({ name });

  return {
    Chart: FakeChart,
    CategoryScale: part('CategoryScale'),
    LinearScale: part('LinearScale'),
    LineController: part('LineController'),
    LineElement: part('LineElement'),
    PointElement: part('PointElement'),
    Tooltip: part('Tooltip'),
  };
});

const series: readonly LineChartSeries[] = [
  {
    label: 'Winger',
    data: [0.4, null, 0.5],
    color: '#0072B2',
    dash: [],
    pointStyle: 'circle',
    notes: ['+1 Pace', null, null],
  },
  {
    label: 'Recovery',
    data: [null, 0, null],
    color: '#D55E00',
    dash: [6, 3],
    pointStyle: 'triangle',
  },
];

describe('LineChart', () => {
  let fixture: ComponentFixture<LineChart>;
  let root: HTMLElement;

  const charts = () => created as RecordedChart[];

  async function loaded(): Promise<void> {
    await fixture.whenStable();
    // The dynamic import resolves on a later turn than the render that started it.
    await new Promise((resolve) => setTimeout(resolve));
  }

  beforeEach(async () => {
    created.length = 0;
    registered.length = 0;

    await TestBed.configureTestingModule({ imports: [LineChart] }).compileComponents();

    fixture = TestBed.createComponent(LineChart);
    fixture.componentRef.setInput('labels', ['1 Oct', '2 Oct', '3 Oct']);
    fixture.componentRef.setInput('series', series);
    fixture.componentRef.setInput('ariaLabel', 'Daily growth, 2 regimes.');

    root = fixture.nativeElement as HTMLElement;
  });

  it('describes the canvas to assistive technology', async () => {
    await loaded();

    const canvas = root.querySelector('canvas')!;

    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toBe('Daily growth, 2 regimes.');
  });

  it('loads the library after the first render and registers only what a line chart needs', async () => {
    await loaded();

    expect(charts()).toHaveLength(1);
    expect(charts()[0].canvas).toBe(root.querySelector('canvas'));
    expect(registered[0].map((part) => (part as { name: string }).name).sort()).toEqual([
      'CategoryScale',
      'LineController',
      'LineElement',
      'LinearScale',
      'PointElement',
      'Tooltip',
    ]);
  });

  it('draws each series with its own colour, dash and point style, and breaks the line on a null', async () => {
    await loaded();

    const { type, data, options } = charts()[0].config;

    expect(type).toBe('line');
    expect(data.labels).toEqual(['1 Oct', '2 Oct', '3 Oct']);
    expect(data.datasets[0]).toMatchObject({
      label: 'Winger',
      data: [0.4, null, 0.5],
      borderColor: '#0072B2',
      borderDash: [],
      pointStyle: 'circle',
      spanGaps: false,
    });
    expect(data.datasets[1]).toMatchObject({
      borderColor: '#D55E00',
      borderDash: [6, 3],
      pointStyle: 'triangle',
    });

    // The legend is HTML, so the canvas one is off.
    expect(options.plugins.legend.display).toBe(false);
  });

  it('puts the day’s attribute changes under its value in the tooltip', async () => {
    await loaded();

    const { callbacks } = charts()[0].config.options.plugins.tooltip;
    const context = (datasetIndex: number, dataIndex: number) => ({
      datasetIndex,
      dataIndex,
      dataset: { label: series[datasetIndex].label },
      formattedValue: '0.4',
    });

    expect(callbacks.label(context(0, 0))).toBe('Winger: 0.4');
    expect(callbacks.afterLabel(context(0, 0))).toBe('+1 Pace');
    expect(callbacks.afterLabel(context(0, 2))).toBe('');
    expect(callbacks.afterLabel(context(1, 1))).toBe('');
  });

  it('shows a legend that names every series and repeats its dash and shape', async () => {
    await loaded();

    const entries = Array.from(root.querySelectorAll('ul[aria-label="Legend"] li'));

    expect(entries.map((entry) => entry.textContent?.trim())).toEqual(['Winger', 'Recovery']);
    expect(entries[0].querySelector('line')?.getAttribute('stroke-dasharray')).toBeNull();
    expect(entries[1].querySelector('line')?.getAttribute('stroke-dasharray')).toBe('6 3');
    expect(entries[0].querySelector('circle')).not.toBeNull();
    expect(entries[1].querySelector('polygon')).not.toBeNull();
  });

  it('updates the one chart when an input changes, instead of building another', async () => {
    await loaded();

    const chart = charts()[0];

    fixture.componentRef.setInput('labels', ['4 Oct']);
    fixture.componentRef.setInput('series', [series[0]]);
    await fixture.whenStable();

    expect(charts()).toHaveLength(1);
    expect(chart.update).toHaveBeenCalled();
    expect(chart.data.labels).toEqual(['4 Oct']);
    expect(chart.data.datasets).toHaveLength(1);
  });

  it('has no legend when there is nothing to plot', async () => {
    fixture.componentRef.setInput('series', []);
    fixture.componentRef.setInput('labels', []);
    await loaded();

    expect(root.querySelector('ul[aria-label="Legend"]')).toBeNull();
  });

  it('destroys the chart with the component', async () => {
    await loaded();

    const chart = charts()[0];

    fixture.destroy();

    expect(chart.destroy).toHaveBeenCalledOnce();
  });

  it('never builds a chart if the component goes before the library arrives', async () => {
    fixture.detectChanges();
    fixture.destroy();
    await new Promise((resolve) => setTimeout(resolve));

    expect(charts()).toHaveLength(0);
  });
});
