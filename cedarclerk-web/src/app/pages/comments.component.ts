import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { ThemeService } from '../core/theme.service';
import { CommentsService, AllCommentsComment } from '../core/comments.service';
import { CedarLogoComponent } from '../shared/cedar-logo.component';
import {
    LucideArrowLeft as ArrowLeft, LucideTrash2 as Trash2,
} from '@lucide/angular';

@Component({
    selector: 'app-comments',
    imports: [DatePipe, RouterLink, CedarLogoComponent, ArrowLeft, Trash2],
    templateUrl: 'comments.component.html',
    styleUrls: ['comments.component.css']
})
export class CommentsComponent implements OnInit {
    auth = inject(AuthService);
    theme = inject(ThemeService);
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

    avatarInitial(): string {
        const email = this.auth.userEmail();
        return email ? email[0].toUpperCase() : '?';
    }

    async deleteComment(id: string) {
        await this.commentsApi.remove(id);
        this.comments.update(list => list.filter(c => c.id !== id));
    }
}
