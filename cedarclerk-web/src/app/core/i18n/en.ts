// Canonical dictionary (B26, ADR-044). THIS FILE DEFINES THE SHAPE: ru.ts is typed as
// `typeof en`, so adding a key here without adding it there is a build error, and a typo in a
// template is a build error too. Grouped by screen, mirroring the component files.
//
// Not localized on purpose: /terms and /privacy (long-form legal prose still carrying unfilled
// [BRACKETED] placeholders), and every message that comes back from the server in an { error }
// body — those stay English until a separate server-side pass. See ADR-044.
// No `as const`: the literal types it produces would force ru.ts to repeat the English strings
// verbatim. Plain `string` members are exactly what's wanted — the *keys* are what must match.
export const en = {
    common: {
        cancel: 'Cancel',
        delete: 'Delete',
        save: 'Save',
        saving: 'Saving…',
        loading: 'Loading…',
        close: 'Close',
        toggleTheme: 'Toggle theme',
        nothingHere: 'Nothing here.',
    },
    login: {
        tagline: 'Write here. Publish there. Moo.',
        email: 'Email',
        password: 'Password',
        submit: 'Log in',
        noAccount: 'No account?',
        register: 'Register',
        inviteRequired: '· invite required',
        failed: 'Wrong email or password',
    },
    register: {
        title: 'Join the herd',
        tagline: 'Cedar Clerk is invite-only for now.',
        taglineAsk: 'Ask Marty for a code.',
        email: 'Email',
        password: 'Password',
        passwordPlaceholder: '8+ characters',
        inviteCode: 'Invite code',
        submit: 'Create account',
        haveAccount: 'Already have one?',
        login: 'Log in',
        legalPrefix: 'By creating an account you agree to the',
        legalTerms: 'Terms',
        legalAnd: 'and',
        legalPrivacy: 'Privacy Policy',
    },
    drafts: {
        crumb: 'Drafts',
        title: 'Drafts',
        // Plural forms differ per language, so these are functions rather than strings — ru.ts
        // has to match the signature, which is exactly the point.
        postsCount: (n: number) => `${n} ${n === 1 ? 'post' : 'posts'}`,
        search: 'Search title or tag',
        untitled: 'Untitled',
        privatePost: 'Private post',
        newDraft: 'New draft',
        importCedar: 'Import .cedar',
        importZip: 'Import .zip',
        importZipTitle: 'Import Markdown (.zip)',
        viewTable: 'Table',
        viewGrid: 'Grid',
        folders: {
            filter: 'Filter by folder',
            all: 'All folders',
            none: 'No folder',
            add: 'Add folder',
            newPlaceholder: 'New folder…',
            rename: 'Rename',
            deleteTitle: (name: string) => `Delete "${name}"?`,
            deleteOrphans: (n: number) => `${n} draft${n === 1 ? '' : 's'} will become uncategorized.`,
            deleteKeepsDrafts: 'The drafts themselves are not deleted.',
            deleteConfirm: 'Delete folder',
        },
        filters: {
            all: 'All',
            draft: 'Drafts',
            scheduled: 'Scheduled',
            published: 'Published',
            attention: 'Needs attention',
            archived: 'Archived',
        },
        columns: {
            title: 'Title',
            state: 'State',
            languages: 'Languages',
            folder: 'Folder',
            tags: 'Tags',
            activity: 'Activity',
            updated: 'Updated',
        },
        resetColumns: 'Reset column widths',
        status: {
            archived: 'Archived',
            publishFailed: 'Publish failed',
            scheduled: 'Scheduled',
            translationIncomplete: 'Translation incomplete',
            translationBehind: (langs: string) => `${langs} behind`,
            published: 'Published',
            blog: 'Blog',
            telegram: 'Telegram',
            draft: 'Draft',
        },
        activity: {
            views: 'Blog views',
            reactions: 'Reactions (likes + dislikes)',
        },
        actions: {
            archive: 'Archive',
            unarchive: 'Unarchive',
            deleteDraft: 'Delete',
        },
        deleteDraftTitle: 'Delete this draft?',
        deleteDraftBody: 'This cannot be undone.',
        errors: {
            load: 'Failed to load drafts',
            move: 'Failed to move draft',
            createFolder: 'Failed to create folder',
            renameFolder: 'Failed to rename folder',
            deleteFolder: 'Failed to delete folder',
            update: 'Failed to update draft',
            delete: 'Failed to delete draft',
            import: 'Import failed — check the file and try again',
            importUnmatched: (n: number, names: string) => `Imported, but ${n} image(s) could not be matched: ${names}`,
        },
    },
    settings: {
        language: {
            nav: 'Language',
            title: 'Interface language',
            hint: 'Applies to the app interface, not to the language your posts are written in. Saved to your account, so it follows you to every device.',
            failed: 'Could not save the language',
        },
    },
};

export type Dict = typeof en;
