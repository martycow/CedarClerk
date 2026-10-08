export const OVERVIEW_SECTIONS = [
  'documents',
  'analytics',
  'links',
  'planning',
  'journal',
] as const;
export type OverviewSection = (typeof OVERVIEW_SECTIONS)[number];
export type OverviewWidth = 'narrow' | 'wide' | 'full';
export interface OverviewPanel {
  id: OverviewSection;
  width: OverviewWidth;
  hidden: boolean;
}
export interface OverviewLayout {
  version: 1;
  panels: OverviewPanel[];
}

export function defaultOverviewLayout(): OverviewLayout {
  return {
    version: 1,
    panels: OVERVIEW_SECTIONS.map((id) => ({
      id,
      width:
        id === 'journal' ? 'full' : id === 'analytics' || id === 'planning' ? 'narrow' : 'wide',
      hidden: false,
    })),
  };
}

export function normalizeOverviewLayout(value: unknown): OverviewLayout {
  const fallback = defaultOverviewLayout();
  if (!value || typeof value !== 'object' || !('version' in value) || value.version !== 1)
    return fallback;
  const input = value as Partial<OverviewLayout>;
  const panels: OverviewPanel[] = [];
  if (Array.isArray(input.panels)) {
    for (const panel of input.panels) {
      if (!panel || !OVERVIEW_SECTIONS.includes(panel.id) || panels.some((p) => p.id === panel.id))
        continue;
      panels.push({
        id: panel.id,
        width: ['narrow', 'wide', 'full'].includes(panel.width)
          ? panel.width
          : fallback.panels.find((p) => p.id === panel.id)!.width,
        hidden: panel.hidden === true,
      });
    }
  }
  panels.push(...fallback.panels.filter((panel) => !panels.some((p) => p.id === panel.id)));
  return { version: 1, panels };
}

export function moveOverviewPanel(
  layout: OverviewLayout,
  id: OverviewSection,
  to: number,
): OverviewLayout {
  const panels = [...layout.panels];
  const from = panels.findIndex((panel) => panel.id === id);
  if (from < 0 || to < 0 || to >= panels.length) return layout;
  panels.splice(to, 0, panels.splice(from, 1)[0]);
  return { ...layout, panels };
}
