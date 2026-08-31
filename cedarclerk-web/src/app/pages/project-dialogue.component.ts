import { Component, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { LocaleService } from '../core/i18n/locale.service';
import { httpErrorMessage } from '../core/http-error.util';
import { DialogueNode, DialoguesService } from '../core/dialogues.service';
import { RailActionsService } from '../core/rail-actions.service';
import { RulerService } from '../core/ruler.service';
import { IconComponent } from '../shared/icon.component';
import { ModalComponent } from '../shared/modal.component';
import { ButtonComponent } from '../bench/forms/button.component';
import { InputComponent } from '../bench/forms/input.component';
import { ShelfPanelComponent } from '../bench/chrome/shelf-panel.component';
import { StampBadgeComponent } from '../bench/display/stamp-badge.component';
import { WorktopComponent } from '../bench/worktop/worktop.component';

/** Node card footprint on the graph — fixed, so edge anchors need no measuring. */
const NODE_W = 220;
const NODE_H = 110;
const ZOOM_MIN = 0.25;
const ZOOM_MAX = 2.5;
const SAVE_DELAY_MS = 1500;

interface View { x: number; y: number; z: number }
interface Edge { from: DialogueNode; to: DialogueNode }
type Gesture =
    | { kind: 'pan'; startX: number; startY: number; viewX: number; viewY: number }
    | { kind: 'node'; id: string; startX: number; startY: number; nodeX: number; nodeY: number };

/** Jump targets a node body names: `<<jump T>>`, `<<detour T>>`, `[[text|T]]`, `[[T]]`. */
function jumpTargets(body: string): string[] {
    const targets: string[] = [];
    for (const m of body.matchAll(/<<\s*(?:jump|detour)\s+([^>\s][^>]*?)\s*>>/g)) targets.push(m[1]);
    for (const m of body.matchAll(/\[\[(?:[^\][|]*\|)?([^\][|]+)\]\]/g)) targets.push(m[1].trim());
    return targets;
}

/** The client's copy of YarnDialogue.SafeTitle, so `<<jump Second_Act>>` finds "Second Act". */
function safeTitle(title: string): string {
    let safe = title.trim().replace(/[^A-Za-z0-9_]/g, '_');
    if (!safe) safe = 'Node';
    if (/^\d/.test(safe)) safe = '_' + safe;
    return safe;
}

// The Yarn node-graph editor. Same surface decision as the canvas (ADR-218): a div world layer
// under one CSS transform, never a <canvas> — node text stays selectable and the node list stays
// one accessible tree. Edges are an SVG drawn in world coordinates from the bodies' own jumps, so
// there is no edge data to store or to drift: the text is the graph.
@Component({
    selector: 'app-project-dialogue',
    imports: [
        FormsModule, IconComponent, ModalComponent, WorktopComponent, ShelfPanelComponent,
        StampBadgeComponent, ButtonComponent, InputComponent,
    ],
    templateUrl: 'project-dialogue.component.html',
    styleUrls: ['project-dialogue.component.css'],
})
export class ProjectDialogueComponent implements OnDestroy {
    private api = inject(DialoguesService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private ruler = inject(RulerService);
    private rail = inject(RailActionsService);
    t = inject(LocaleService).t;

    readonly nodeW = NODE_W;
    readonly nodeH = NODE_H;

    private stageRef = viewChild<ElementRef<HTMLElement>>('stage');

    projectId = signal('');
    scriptId = signal('');
    name = signal('');
    languages = signal<readonly string[]>([]);
    nodes = signal<readonly DialogueNode[]>([]);
    selectedId = signal<string | null>(null);
    view = signal<View>({ x: 0, y: 0, z: 1 });

    loading = signal(true);
    loadError = signal<string | null>(null);
    dirty = signal(false);
    saving = signal(false);
    saveError = signal<string | null>(null);
    downloading = signal<'yarn' | 'xlsx' | null>(null);
    exportError = signal<string | null>(null);
    importing = signal(false);
    importReport = signal<string | null>(null);
    importError = signal<string | null>(null);
    sheetLanguages = signal('');
    confirmDeleteNode = signal<DialogueNode | null>(null);

    private gesture: Gesture | null = null;
    private saveTimer: ReturnType<typeof setTimeout> | null = null;
    /** Bumped on every edit; a save answer only lands if nothing was typed while it flew. */
    private editStamp = 0;

    selected = computed(() => this.nodes().find(n => n.id === this.selectedId()) ?? null);
    zoomPercent = computed(() => Math.round(this.view().z * 100));

    /** Derived, never stored: every jump a body names that resolves to another node's title. */
    edges = computed<readonly Edge[]>(() => {
        const nodes = this.nodes();
        const byTitle = new Map<string, DialogueNode>();
        for (const n of nodes) byTitle.set(safeTitle(n.title), n);
        const edges: Edge[] = [];
        for (const from of nodes) {
            for (const target of jumpTargets(from.body)) {
                const to = byTitle.get(safeTitle(target));
                if (to && to.id !== from.id) edges.push({ from, to });
            }
        }
        return edges;
    });

    saveWord = computed(() => {
        const t = this.t().projects.dialogues;
        return this.saving() ? t.saving : this.dirty() ? t.unsaved : t.saved;
    });

    constructor() {
        this.route.paramMap.subscribe(params => {
            const projectId = params.get('id') ?? '';
            const scriptId = params.get('scriptId') ?? '';
            if (!scriptId) return;
            this.projectId.set(projectId);
            this.scriptId.set(scriptId);
            void this.load();
        });

        // Attached by hand so a drag works past the stage's edge; registered only for the length
        // of a gesture, so an idle graph costs nothing.
        effect(() => {
            const t = this.t().projects.dialogues;
            this.ruler.publish({
                label: this.name(),
                left: [
                    { text: t.nodeCount(this.nodes().length) },
                    { text: this.saveWord() },
                ],
            });
            this.rail.publish({
                save: { state: this.saving() || this.dirty() ? 'forming' : 'set' },
                primary: { label: t.addNode, icon: 'plus', run: () => this.addNode() },
            });
        });
    }

    ngOnDestroy(): void {
        if (this.saveTimer) clearTimeout(this.saveTimer);
        // An uncommitted edit is on screen already — flush it rather than lose it.
        if (this.dirty()) void this.saveNow();
        this.ruler.clear();
        this.rail.clear();
        this.detachWindow();
    }

    async load() {
        this.loading.set(true);
        this.loadError.set(null);
        try {
            const script = await this.api.get(this.scriptId());
            this.name.set(script.name);
            this.languages.set(script.languages);
            this.nodes.set(JSON.parse(script.graphJson) as DialogueNode[]);
            this.fit();
        } catch (e) {
            this.loadError.set(httpErrorMessage(e, this.t().projects.dialogues.scriptNotFound));
            void this.router.navigate(['/projects', this.projectId(), 'dialogues']);
            return;
        } finally {
            this.loading.set(false);
        }
    }

    // ---- editing ----

    select(id: string | null) {
        this.selectedId.set(id);
    }

    setTitle(title: string) {
        const node = this.selected();
        if (!node) return;
        this.patchNode(node.id, { title });
    }

    setBody(body: string) {
        const node = this.selected();
        if (!node) return;
        this.patchNode(node.id, { body });
    }

    addNode() {
        const stage = this.stageRef()?.nativeElement;
        const view = this.view();
        const cx = stage ? (stage.clientWidth / 2 - view.x) / view.z : 80;
        const cy = stage ? (stage.clientHeight / 2 - view.y) / view.z : 80;
        const titles = new Set(this.nodes().map(n => safeTitle(n.title)));
        let i = this.nodes().length + 1;
        while (titles.has(`Node_${i}`)) i++;
        const node: DialogueNode = {
            id: crypto.randomUUID().replace(/-/g, '').slice(0, 8),
            title: `Node_${i}`,
            x: Math.round(cx - NODE_W / 2),
            y: Math.round(cy - NODE_H / 2),
            body: '',
        };
        this.nodes.set([...this.nodes(), node]);
        this.selectedId.set(node.id);
        this.markDirty();
    }

    removeNode() {
        const node = this.confirmDeleteNode();
        if (!node) return;
        this.nodes.set(this.nodes().filter(n => n.id !== node.id));
        if (this.selectedId() === node.id) this.selectedId.set(null);
        this.confirmDeleteNode.set(null);
        this.markDirty();
    }

    private patchNode(id: string, patch: Partial<DialogueNode>) {
        this.nodes.set(this.nodes().map(n => (n.id === id ? { ...n, ...patch } : n)));
        this.markDirty();
    }

    private markDirty() {
        this.editStamp++;
        this.dirty.set(true);
        if (this.saveTimer) clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => void this.saveNow(), SAVE_DELAY_MS);
    }

    async saveNow() {
        if (this.saving()) return;
        const stamp = this.editStamp;
        this.saving.set(true);
        this.saveError.set(null);
        try {
            const answer = await this.api.save(this.scriptId(), { graphJson: JSON.stringify(this.nodes()) });
            // The answer carries the freshly stamped #line: ids. It only lands if nothing was
            // typed while it flew — otherwise the next save re-stamps and lands instead.
            if (stamp === this.editStamp) {
                this.nodes.set(JSON.parse(answer.graphJson) as DialogueNode[]);
                this.dirty.set(false);
            }
        } catch (e) {
            this.saveError.set(httpErrorMessage(e, this.t().projects.actionFailed));
        } finally {
            this.saving.set(false);
        }
    }

    // ---- viewport ----

    zoomBy(factor: number, atX?: number, atY?: number) {
        const stage = this.stageRef()?.nativeElement;
        const view = this.view();
        const z = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, view.z * factor));
        if (z === view.z) return;
        const px = atX ?? (stage ? stage.clientWidth / 2 : 0);
        const py = atY ?? (stage ? stage.clientHeight / 2 : 0);
        // The point under the cursor stays under the cursor.
        this.view.set({ z, x: px - ((px - view.x) / view.z) * z, y: py - ((py - view.y) / view.z) * z });
    }

    fit() {
        const stage = this.stageRef()?.nativeElement;
        const nodes = this.nodes();
        if (!stage || !nodes.length) return;
        const minX = Math.min(...nodes.map(n => n.x));
        const minY = Math.min(...nodes.map(n => n.y));
        const maxX = Math.max(...nodes.map(n => n.x + NODE_W));
        const maxY = Math.max(...nodes.map(n => n.y + NODE_H));
        const pad = 48;
        const z = Math.min(1,
            stage.clientWidth / (maxX - minX + pad * 2),
            stage.clientHeight / (maxY - minY + pad * 2));
        this.view.set({
            z,
            x: (stage.clientWidth - (maxX - minX) * z) / 2 - minX * z,
            y: (stage.clientHeight - (maxY - minY) * z) / 2 - minY * z,
        });
    }

    worldTransform(): string {
        const v = this.view();
        return `translate(${v.x}px, ${v.y}px) scale(${v.z})`;
    }

    nodeTransform(node: DialogueNode): string {
        return `translate(${node.x}px, ${node.y}px)`;
    }

    edgePath(edge: Edge): string {
        const x1 = edge.from.x + NODE_W;
        const y1 = edge.from.y + NODE_H / 2;
        const x2 = edge.to.x;
        const y2 = edge.to.y + NODE_H / 2;
        const bend = Math.max(40, Math.abs(x2 - x1) / 2);
        return `M ${x1} ${y1} C ${x1 + bend} ${y1}, ${x2 - bend} ${y2}, ${x2} ${y2}`;
    }

    // ---- gestures ----

    private windowMove = (e: PointerEvent) => this.onMove(e);
    private windowUp = () => this.onUp();

    onStageDown(event: PointerEvent) {
        if (event.button !== 0) return;
        this.select(null);
        const view = this.view();
        this.gesture = { kind: 'pan', startX: event.clientX, startY: event.clientY, viewX: view.x, viewY: view.y };
        this.attachWindow();
    }

    onNodeDown(event: PointerEvent, node: DialogueNode) {
        if (event.button !== 0) return;
        event.stopPropagation();
        this.select(node.id);
        this.gesture = { kind: 'node', id: node.id, startX: event.clientX, startY: event.clientY, nodeX: node.x, nodeY: node.y };
        this.attachWindow();
    }

    private onMove(event: PointerEvent) {
        const g = this.gesture;
        if (!g) return;
        if (g.kind === 'pan') {
            this.view.set({ ...this.view(), x: g.viewX + event.clientX - g.startX, y: g.viewY + event.clientY - g.startY });
        } else {
            const z = this.view().z;
            const x = Math.round(g.nodeX + (event.clientX - g.startX) / z);
            const y = Math.round(g.nodeY + (event.clientY - g.startY) / z);
            const node = this.nodes().find(n => n.id === g.id);
            if (node && (node.x !== x || node.y !== y)) this.patchNode(g.id, { x, y });
        }
    }

    private onUp() {
        this.gesture = null;
        this.detachWindow();
    }

    private attachWindow() {
        window.addEventListener('pointermove', this.windowMove);
        window.addEventListener('pointerup', this.windowUp);
        window.addEventListener('pointercancel', this.windowUp);
    }

    private detachWindow() {
        window.removeEventListener('pointermove', this.windowMove);
        window.removeEventListener('pointerup', this.windowUp);
        window.removeEventListener('pointercancel', this.windowUp);
    }

    onWheel(event: WheelEvent) {
        event.preventDefault();
        if (event.ctrlKey) {
            const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
            this.zoomBy(Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top);
        } else {
            const view = this.view();
            this.view.set({ ...view, x: view.x - event.deltaX, y: view.y - event.deltaY });
        }
    }

    onStageKey(event: KeyboardEvent) {
        if (event.key === 'Escape') this.select(null);
    }

    onNodeKey(event: KeyboardEvent, node: DialogueNode) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            this.select(node.id);
        } else if (event.key === 'Delete' || event.key === 'Backspace') {
            event.preventDefault();
            this.confirmDeleteNode.set(node);
        }
    }

    /** The card's preview: body text with the tag noise trimmed off each line. */
    preview(node: DialogueNode): string {
        return node.body
            .split('\n')
            .map(line => line.replace(/\s*#line:\w+\s*$/, ''))
            .join('\n');
    }

    // ---- export / import ----

    async download(kind: 'yarn' | 'xlsx') {
        if (this.downloading()) return;
        // The graph on the wire is what the file is built from — flush the editor first.
        if (this.dirty()) await this.saveNow();
        this.downloading.set(kind);
        this.exportError.set(null);
        try {
            const url = kind === 'yarn'
                ? this.api.yarnUrl(this.scriptId())
                : this.api.xlsxUrl(this.scriptId(), this.sheetLanguages());
            const response = await fetch(url);
            if (!response.ok) throw new Error(String(response.status));
            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = objectUrl;
            link.download = this.fileNameFrom(response.headers.get('content-disposition'))
                ?? `${this.name() || 'dialogue'}.${kind}`;
            link.click();
            // Revoked on the next tick: revoking synchronously can cancel the download in Safari.
            setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
        } catch {
            this.exportError.set(this.t().projects.dialogues.exportFailed);
        } finally {
            this.downloading.set(null);
        }
    }

    private fileNameFrom(header: string | null): string | null {
        const match = header?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
        return match ? decodeURIComponent(match[1]) : null;
    }

    async onSheetChosen(event: Event) {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        input.value = '';
        if (!file || this.importing()) return;
        this.importing.set(true);
        this.importError.set(null);
        this.importReport.set(null);
        try {
            const report = await this.api.importXlsx(this.scriptId(), file);
            const t = this.t().projects.dialogues;
            const lines = [t.importReport(report.added, report.updated)];
            if (report.unknownLines > 0) lines.push(t.importUnknown(report.unknownLines));
            this.importReport.set(lines.join(' '));
            this.languages.set([...new Set([...this.languages(), ...report.languages])].sort());
        } catch (e) {
            this.importError.set(httpErrorMessage(e, this.t().projects.dialogues.importFailed));
        } finally {
            this.importing.set(false);
        }
    }
}
