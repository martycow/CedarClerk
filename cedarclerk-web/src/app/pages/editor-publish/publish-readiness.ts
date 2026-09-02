import { DOCUMENT_KINDS, DocumentKindCounts } from '../../core/document-kinds';
import { PublishCapabilities } from '../../core/publish.service';
import { MatrixWords, matrixCell } from '../../shared/publish-matrix.component';

/**
 * The number behind «Compatibility · N warnings» (ADR-242 clause 5): one per kind the document
 * holds that a ticked destination takes with limits or drops. Read off the same capability record
 * the matrix draws, so the count cannot disagree with the table one disclosure below it.
 */
export function matrixWarningCount(
    present: DocumentKindCounts,
    capabilities: readonly PublishCapabilities[],
    ticked: readonly string[],
    words: MatrixWords,
): number {
    let count = 0;
    for (const network of ticked) {
        const cap = capabilities.find(c => c.network === network);
        if (!cap) continue;
        for (const kind of DOCUMENT_KINDS) {
            if (!(present[kind] > 0)) continue;
            if (matrixCell(kind, cap, words).verdict !== 'yes') count++;
        }
    }
    return count;
}
