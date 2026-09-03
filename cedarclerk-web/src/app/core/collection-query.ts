export type SortDirection = 'asc' | 'desc';

export function ariaSort(active: boolean, direction: SortDirection): 'ascending' | 'descending' | null {
    if (!active) return null;
    return direction === 'asc' ? 'ascending' : 'descending';
}
