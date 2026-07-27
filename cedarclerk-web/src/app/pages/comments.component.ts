import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { CommentsService, AllCommentsComment } from '../core/comments.service';
import { LucideTrash2 as Trash2 } from '@lucide/angular';

// Rendered as the Posts Manager's "reactions and comments" tab (N7). It no longer owns any page
// chrome — no header, no theme toggle, no back link.
@Component({
    selector: 'app-comments',
    imports: [DatePipe, Trash2],
    templateUrl: 'comments.component.html',
    styleUrls: ['comments.component.css']
})
export class CommentsComponent implements OnInit {
    private commentsApi = inject(CommentsService);

    loading = signal(true);
    reactions = signal<{ likes: number; dislikes: number }>({ likes: 0, dislikes: 0 });
    comments = signal<AllCommentsComment[]>([]);

    async ngOnInit() {
        this.loading.set(true);
        try {
            const feedback = await this.commentsApi.listAll();
            this.reactions.set(feedback.reactions);
            this.comments.set(feedback.comments);
        } finally {
            this.loading.set(false);
        }
    }

    async deleteComment(id: string) {
        await this.commentsApi.remove(id);
        this.comments.update(list => list.filter(c => c.id !== id));
    }
}
