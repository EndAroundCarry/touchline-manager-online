import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlayerTrainingStore } from '../../../core/training/player-training-store';
import {
  ALL_REGIMES,
  ChartMode,
  ChartRange,
  DEFAULT_CHART_FILTER,
  buildChart,
  chartAriaLabel,
} from '../../../core/training/training-chart';
import {
  attributeLabel,
  intensityLabel,
  weightStyle,
} from '../../../core/training/training-presentation';
import { preferredLocale } from '../../../core/world/presentation';
import { FORM_ERROR, LINK_ACTION, SELECT_INPUT } from '../../../shared/forms/control-styles';
import { LineChart } from '../../../shared/ui/line-chart/line-chart';

/** The range select's choices. */
const RANGE_OPTIONS: readonly { readonly value: ChartRange; readonly label: string }[] = [
  { value: '30', label: 'Last 30 days' },
  { value: '90', label: 'Last 90 days' },
  { value: 'all', label: 'All recorded days' },
];

/**
 * The player page's Training tab (`TRN-17`).
 *
 * Three things, top to bottom: the regime the player is on now (programme, intensity, and the skills it
 * trains by weight), a chart of what training has done for them day by day, and the numbers behind it. The
 * chart shows one line per regime so a change of programme is visible as a new line, and the summary table
 * and the plotted-points table carry every value the chart draws, so nothing depends on seeing it (ADR-0039).
 *
 * The history builds up as the daily progression runs; before it has, the tab says so.
 */
@Component({
  selector: 'app-player-training',
  imports: [RouterLink, LineChart],
  templateUrl: './player-training.html',
})
export class PlayerTraining implements OnInit {
  /** The player whose training is shown. */
  readonly playerId = input.required<string>();

  private readonly store = inject(PlayerTrainingStore);

  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.error;

  /** The history, once it is the one for this player: the store keeps the last read, whoever it was for. */
  protected readonly history = computed(() => {
    const history = this.store.history();

    return history !== null && history.playerId === this.playerId() ? history : null;
  });

  protected readonly regime = signal(ALL_REGIMES);
  protected readonly mode = signal<ChartMode>(DEFAULT_CHART_FILTER.mode);
  protected readonly range = signal<ChartRange>(DEFAULT_CHART_FILTER.range);

  protected readonly rangeOptions = RANGE_OPTIONS;

  /** "All regimes", then each programme the player has history for, in the order they first trained it. */
  protected readonly regimeOptions = computed(() => [
    { value: ALL_REGIMES, label: 'All regimes' },
    ...(this.history()?.summary ?? []).map((line) => ({
      value: line.programme,
      label: line.label,
    })),
  ]);

  /** The skills the current programme trains, heaviest first, each with the mark and word for its weight. */
  protected readonly trainedSkills = computed(() =>
    (this.history()?.regime.attributes ?? []).map((attribute) => {
      const style = weightStyle(attribute.weight);

      return {
        key: attribute.name,
        label: attributeLabel(attribute.name),
        tintClass: style?.tintClass ?? '',
        marker: style?.marker ?? '',
        weight: style?.label ?? '',
      };
    }),
  );

  /** What the chart and the plotted-points table draw, for the controls as they stand. */
  protected readonly chart = computed(() => {
    const history = this.history();

    return history === null
      ? null
      : buildChart(
          history.days,
          history.summary,
          { regime: this.regime(), mode: this.mode(), range: this.range() },
          preferredLocale(),
        );
  });

  protected readonly chartLabel = computed(() => {
    const chart = this.chart();

    return chart === null ? '' : chartAriaLabel(chart, this.mode());
  });

  protected readonly yTitle = computed(() =>
    this.mode() === 'daily' ? 'Net points per day' : 'Net points so far',
  );

  protected readonly selectClass = SELECT_INPUT;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK_ACTION;

  /** Reads the player's history when the tab opens. */
  ngOnInit(): void {
    this.store.load(this.playerId());
  }

  protected onRegimeChange(event: Event): void {
    this.regime.set((event.target as HTMLSelectElement).value);
  }

  protected onRangeChange(event: Event): void {
    this.range.set((event.target as HTMLSelectElement).value as ChartRange);
  }

  protected setMode(mode: ChartMode): void {
    this.mode.set(mode);
  }

  /** Names an intensity. */
  protected intensityName(code: string): string {
    return intensityLabel(code);
  }

  /** A signed number for the table: a gain reads `+1`, a loss `−1`. */
  protected signed(value: number): string {
    return value > 0 ? `+${value}` : value < 0 ? `−${Math.abs(value)}` : '0';
  }
}
