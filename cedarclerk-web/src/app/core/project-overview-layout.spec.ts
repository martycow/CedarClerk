import {
  defaultOverviewLayout,
  moveOverviewPanel,
  normalizeOverviewLayout,
} from './project-overview-layout';

describe('project overview preferences', () => {
  it('normalizes corrupt, duplicate and unknown panels without losing sections', () => {
    const layout = normalizeOverviewLayout({
      version: 1,
      panels: [
        null,
        { id: 'unknown' },
        { id: 'journal', width: 'bad', hidden: true },
        { id: 'journal' },
      ],
    });
    expect(layout.panels.map((p) => p.id)).toEqual([
      'journal',
      'documents',
      'analytics',
      'links',
      'planning',
    ]);
    expect(layout.panels[0]).toEqual({ id: 'journal', width: 'full', hidden: true });
    expect(normalizeOverviewLayout({ version: 2 })).toEqual(defaultOverviewLayout());
  });

  it('drops a banner an older layout stored, since the project owns it now', () => {
    expect(normalizeOverviewLayout({ version: 1, bannerUrl: '/media/abc.png' })).toEqual(
      defaultOverviewLayout(),
    );
  });

  it('moves a section while retaining widths and visibility and rejecting invalid positions', () => {
    const initial = defaultOverviewLayout();
    initial.panels[0].hidden = true;
    const moved = moveOverviewPanel(initial, 'documents', 4);
    expect(moved.panels.map((p) => p.id)).toEqual([
      'analytics',
      'links',
      'planning',
      'journal',
      'documents',
    ]);
    expect(moved.panels[4]).toEqual(initial.panels[0]);
    expect(initial.panels[0].id).toBe('documents');
    expect(moveOverviewPanel(initial, 'documents', -1)).toBe(initial);
  });
});
