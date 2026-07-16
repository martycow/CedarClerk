# Secrets handling

- `CedarClerk.Server/appsettings.Development.json` is gitignored and holds the local `InviteCode`/`BotToken` — keep it that way, never commit it.
- Production secrets live in a systemd drop-in on the Pi: `/etc/systemd/system/cedarclerk.service.d/data.conf` (`Environment=Cedar__Key__SubKey=value` — double underscore stands in for `:`). **Never move secrets into the repo.** See `docs/integrations-setup.md` for the full list of provider keys (Stripe, PayPal, Telegram Stars, translation providers) and where each one comes from.
- `D:\Moo.exe\_Documents_\CedarClerk\Secrets\` (outside this repo) holds real payment/bot secret values for Marty's own reference — never read, quote, or copy its contents into chat, code, or any repo-tracked file.
- If a secret ever lands in a commit: rotate it first (e.g. BotFather `/revoke` for the bot token), then clean history — in that order, rotation before cleanup.
