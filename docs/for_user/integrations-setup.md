---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: ранбук ключей платёжных и AI-провайдеров
guard: none
---

# Интеграции: платежи, автоперевод, email — что настроить и куда прокинуть ключи

_Восстановлен 15.07.2026 из внерепозиторного архива Марти, с тех пор дополнялся (§3b админ, §5 R2, §6 мониторинг) и правился по факту. Сверен с `Consts.cs` 18.08.2026 — имена ключей в §2 были выдуманы и исправлены._

_Все секреты живут в systemd drop-in на продовом дроплете DigitalOcean: `/etc/systemd/system/cedarclerk.service.d/data.conf`
(строки вида `Environment=Cedar__Ключ__Подключ=значение` — двойное подчёркивание вместо `:`).
Локально — в `CedarClerk.Server/appsettings.Development.json` (он в .gitignore).
После правки data.conf: `sudo systemctl daemon-reload && sudo systemctl restart cedarclerk`._

---

## 1. Платежи (апгрейд Free → Pro / Pro Plus / Trial)

Тарифы (обновлено 11.07.2026 — раньше был только один платный тир Pro, теперь три плана):
- **Pro** — $3/мес
- **Pro Plus** — $6/мес (включает AI-фичи — `PlanLimitations.HasAiFeatures`)
- **Trial** — $1 разово, даёт Pro Plus на 7 дней, один раз на аккаунт (`TrialUsedAt`)

Что уже реализовано в коде — **все три провайдера полноценно**, включая PayPal (раньше был
заглушкой, теперь настоящий Orders API v2 checkout+capture):
- **Stripe** — hosted Checkout (subscription для Pro/Pro Plus, one-time payment для Trial) + webhook
  (`checkout.session.completed`, `invoice.paid` — продление, `customer.subscription.deleted`).
  **Работает на проде и проверен реальными деньгами** (первый платёж 26.07.2026); ниже — как это
  настраивалось. Через тот же Checkout идёт и покупка пакетов кредитов (ADR-092, metadata
  `credits_pack`).
- **Telegram Stars** — бот выставляет invoice юзеру с привязанным Telegram-аккаунтом; для Pro/Pro Plus
  это нативная 30-дневная recurring-подписка Stars (`subscriptionPeriod`), Trial — разовый платёж.
- **PayPal** — Orders API v2, полный цикл checkout → capture (`GET /api/billing/paypal/capture`
  как return_url). Пожизненный доступ на 30 дней (ручное продление, без recurring — PayPal Subscriptions
  API не подключён; это осознанное финальное решение, см. `docs/DECISIONS.md` ADR-013, не техдолг).

Кнопки апгрейда появляются в Account-попапе (под аватаром), только у Free-юзеров, и только для
настроенных провайдеров (см. `GET /api/billing/status` → `providers`).

### 1.1 Stripe — что делать на dashboard.stripe.com

1. Зарегистрируйся / зайди на https://dashboard.stripe.com. Сначала можно всё сделать в
   **Test mode** (переключатель вверху) — тестовые карты `4242 4242 4242 4242`.
2. **Products**: Product catalog → + Add product — **два** продукта:
   - «Cedar Clerk Pro» — recurring $3/month → скопируй **Price ID** (`price_...`)
   - «Cedar Clerk Pro Plus» — recurring $6/month → скопируй **Price ID** (`price_...`)
   Trial ($1/7 дней) отдельного продукта в Stripe не требует — сервер создаёт one-time
   `price_data` инлайн при чекауте.
3. **Secret key**: Developers → API keys → Secret key (`sk_test_...` / `sk_live_...`).
4. **Webhook**: Developers → Webhooks → + Add endpoint:
   - URL: `https://cedarclerk.mooexe.dev/api/billing/stripe/webhook`
   - События: `checkout.session.completed`, `invoice.paid`, `customer.subscription.deleted`,
     `invoice.payment_failed` (`invoice.paid` нужен для продления подписки — без него после первого
     цикла юзер не обновит `PlanExpiresAt`; `invoice.payment_failed` пока только логируется на
     сервере для видимости — Stripe сам ретраит неудачные списания по своему расписанию, а если все
     ретраи провалятся — придёт `customer.subscription.deleted`, который уже обрабатывается)
   - После создания скопируй **Signing secret** (`whsec_...`).
5. **Customer Portal** (самостоятельная отмена/смена карты — включена в коде 11.07.2026): Settings →
   Billing → Customer portal → Activate. Без активации кнопка «Manage billing (Stripe)» в
   Account-попапе будет отвечать ошибкой от Stripe API.
6. Прокинь четыре ключа:

```
Environment=Cedar__Stripe__SecretKey=sk_live_...
Environment=Cedar__Stripe__WebhookSecret=whsec_...
Environment=Cedar__Stripe__ProPriceId=price_...
Environment=Cedar__Stripe__ProPlusPriceId=price_...
```

Как это работает: кнопка в UI → сервер создаёт Checkout Session (Pro/Pro Plus — `mode=subscription`
с выбранным Price ID; Trial — `mode=payment` с инлайн $1) → редирект на страницу Stripe → после оплаты
Stripe стучится в webhook → юзер получает тир (`SubscriptionPlan.ApplyPurchase`). Продление подписки →
`invoice.paid` → `PlanExpiresAt` сдвигается на ещё 30 дней + 2 дня grace-периода (страхует от лага
вебхука). Отмена подписки в Stripe → `customer.subscription.deleted` → тир не сбрасывается сразу,
юзер остаётся на оплаченном тире до истечения `PlanExpiresAt`, дальше — Free.

### 1.2 Telegram Stars — что делать

Ничего внешнего не нужно (работает через существующий бот-токен). Цены заданы разумными значениями
по умолчанию — трогать не обязательно, но можно переопределить:

```
Environment=Cedar__Telegram__ProStarsPrice=150      # default 150 ⭐ (~$3)
Environment=Cedar__Telegram__ProPlusStarsPrice=250   # default 250 ⭐ (~$5)
Environment=Cedar__Telegram__TrialStarsPrice=50      # default 50 ⭐ (~$1)
```

Требование для юзера: привязанный Telegram-аккаунт (Account-попап → Link Telegram) — invoice
отправляется ему в личку от бота. Юзер должен хотя бы раз нажать /start у бота, иначе
Telegram не даст боту писать первым.

Вывод денег: Stars копятся на боте, выводятся через Fragment (минимум 1000 Stars, холд 21 день).

### 1.3 PayPal — теперь тоже полноценно

1. Business-аккаунт PayPal.
2. https://developer.paypal.com → Apps & Credentials → Create App → Client ID + Secret.
   Там же переключатель Sandbox/Live — начни с Sandbox для проверки.
3. Прокинь:

```
Environment=Cedar__PayPal__ClientId=...
Environment=Cedar__PayPal__SecretKey=...
Environment=Cedar__PayPal__Mode=sandbox   # или live; по умолчанию (без ключа) — live
```

Как это работает: кнопка в UI → сервер создаёт Order (Orders API v2) → редирект на approve-ссылку
PayPal → после подтверждения PayPal редиректит на `GET /api/billing/paypal/capture?token=...` →
сервер захватывает платёж и выдаёт тир. Recurring не поддержан — это разовый платёж на 30 дней
(как и Trial), продлевать нужно вручную повторной оплатой.

---

## 2. Автоперевод (кнопка «✦ Auto-translate» на вкладке EN)

Реализованы три провайдера, активный выбирается конфигом. Без конфига кнопка отвечает
понятной ошибкой 501. Выбери ОДИН вариант:

### Вариант A: Claude API (Anthropic) — рекомендую, лучшее качество для постов

1. https://console.anthropic.com → зарегистрируйся, пополни баланс (Billing).
2. API Keys → Create Key → скопируй `sk-ant-...`.
3. Прокинь:

```
Environment=Cedar__Translate__Provider=anthropic
Environment=Cedar__Anthropic__ApiKey=sk-ant-...
```

Модель по умолчанию — `claude-haiku-4-5` (`Consts.Anthropic.DefaultModel`; дёшево, для перевода
достаточно). Поднять качество можно, переключив на Sonnet — это дороже Haiku:

```
Environment=Cedar__Anthropic__Model=claude-sonnet-5
```

### Вариант B: OpenAI (ChatGPT API)

1. https://platform.openai.com → API keys → Create new secret key (`sk-...`). Нужен баланс.
2. Прокинь:

```
Environment=Cedar__Translate__Provider=openai
Environment=Cedar__OpenAi__ApiKey=sk-...
Environment=Cedar__OpenAi__Model=gpt-4o
```

(модель поменяй на актуальную, какая тебе нравится — ключ `Cedar:OpenAi:Model`).

### Вариант C: DeepL — самый дешёвый, но «тупой» построчный перевод

1. https://www.deepl.com/pro-api → DeepL API Free (500k символов/мес бесплатно) или Pro.
2. Account → API Keys → скопируй ключ (у Free-ключей суффикс `:fx` — по нему код сам
   выбирает нужный хост API).
3. Прокинь:

```
Environment=Cedar__Translate__Provider=deepl
Environment=Cedar__DeepL__ApiKey=xxxx-xxxx-xxxx:fx
```

Отличие от LLM: DeepL переводит каждый текстовый фрагмент отдельно (структура документа
сохраняется идеально, но контекст между абзацами хуже). LLM-провайдеры переводят весь
документ целиком одним запросом.

### Как пользоваться

- Вкладка EN без перевода → кнопка **«✦ Auto-translate»** — переводит RU-версию и открывает
  результат в редакторе для вычитки (обычный автосейв).
- Если RU правился после перевода (оранжевая точка на вкладке EN) → на EN-вкладке появляется
  **«↻ Re-translate»** (с confirm — перезапишет текущий EN).

---

## 3. Email (Resend) — нужен для приватных постов (invite-ссылки на email)

В проекте раньше вообще не было отправки почты — добавлено 26.07.2026 специально под приватные
посты (владелец приглашает читателей по email, ссылка с токеном приходит письмом). Провайдер —
[Resend](https://resend.com): простой HTTP API (без возни с SMTP), щедрый бесплатный тариф.

Без настройки фича не сломается — просто письма не будут уходить, но саму invite-ссылку всегда
можно скопировать вручную из карточки приглашения и отправить любым другим способом (Telegram,
мессенджер и т.д.).

1. Зарегистрируйся на https://resend.com (бесплатного тарифа хватит с большим запасом).
2. **Домен**: Domains → Add Domain → впиши `mooexe.dev` (или поддомен, например `mail.mooexe.dev`,
   если не хочешь трогать основной домен). Resend покажет несколько DNS-записей (обычно TXT для
   верификации + DKIM + опционально MX) — добавь их в Cloudflare (тот же аккаунт, что уже держит
   зону `mooexe.dev` для Cloudflare Tunnel) через DNS → Add record, по одной. Подождать
   верификацию (обычно быстро, иногда до пары часов).
3. **API-ключ**: API Keys → Create API Key → скопируй (`re_...`, показывается один раз).
4. Прокинь:

```
Environment=Cedar__Email__ResendApiKey=re_...
Environment=Cedar__Email__FromAddress=Cedar Clerk <noreply@mooexe.dev>
```

`FromAddress` должен быть на верифицированном домене — иначе Resend отклонит отправку. Если не
задать `FromAddress` вообще, код по умолчанию подставит `onboarding@resend.dev` (тестовый адрес
Resend, работает без верификации домена, но выглядит не как твой домен — годится только чтобы
быстро проверить, что сама отправка вообще работает, прежде чем настраивать DNS).

---

## 3b. Админ-панель (IF2) — одна строка конфига, не секрет

Права админа выдаются на старте сервера аккаунту, чья почта указана в `Cedar:AdminEmail`. Без этой строки панель просто никому не доступна — приложение стартует нормально, `/admin` отдаёт редирект, `/api/admin` отвечает 404.

```
Environment=Cedar__AdminEmail=cedarworks@mooexe.dev
```

Почему через конфиг: первого админа физически нельзя выдать из самой панели, а так это работает и на чистой базе, и на восстановленной из бэкапа, без ручного SQL на сервере.

Важно: бутстрап **только выдаёт права и никогда не отзывает**. Убрать строку — не значит разжаловать админа; это сделано специально, чтобы случайная правка конфига не заперла панель.

Проверка после рестарта:
- `journalctl -u cedarclerk -n 30 --no-pager | grep "Admin rights"` — строка появляется один раз, при первой выдаче
- в UI: меню аккаунта → пункт «Панель админа» виден только у этого аккаунта

## 3c. Продуктовая аналитика — PostHog (EU). `T-153`, ADR-236

Зачем: воронка «лендинг → регистрация → первая публикация» и то, где люди застревают. Восемь из
десяти событий сервер пишет сам (`docs/product/METRICS.md` §4); провайдер нужен для двух оставшихся
и для живого дашборда.

Где взять ключ:

1. Регистрация на **eu.posthog.com** — именно EU-регион, не US. Регион выбирается один раз при
   создании аккаунта и потом не меняется, а `/privacy` называет его явно.
2. Project Settings → **Project API Key** (начинается с `phc_`).

Три строки в дроп-ин (`Cedar__` = `Cedar:`, двойное подчёркивание вместо двоеточия):

```
Environment=Cedar__Analytics__Enabled=true
Environment=Cedar__Analytics__ProjectKey=phc_ваш_ключ
Environment=Cedar__Analytics__Host=https://eu.i.posthog.com
```

`Host` можно не указывать — по умолчанию берётся EU. Без `Enabled=true` или без ключа аналитика
выключена целиком: провайдер не регистрируется, баннер согласия не показывается, `/api/health` не
отдаёт секцию `analytics`. Это же и есть режим для локального запуска и для self-hosted установки.

**Project API Key публичный** — он и так уезжает в браузер внутри скрипта страницы, поэтому едет
через `/api/health`, а не зашит в бандл. Секретом он не является; в отличие от Personal API Key,
который здесь не нужен и который в конфиг класть нельзя.

Проверка после рестарта:
- `curl -s https://cedarclerk.mooexe.dev/api/health | grep -o '"analytics":[^,]*'` — секция есть
- инкогнито-визит на `/welcome`: внизу слева баннер согласия; до нажатия «Принять» в Network нет
  ни одного запроса на `eu.i.posthog.com`
- после «Принять» — запрос на `/static/array.js`, затем события в PostHog → Activity

## 3d. Вход через Google и Telegram. `T-003`, ADR-237

Кнопки появляются на `/login` и `/register` только когда провайдер настроен. Не настроено ничего —
двери выглядят ровно как раньше.

### Google

1. **console.cloud.google.com** → новый проект (или существующий).
2. APIs & Services → **OAuth consent screen**: тип External, название, почта поддержки, домен
   `cedarclerk.app`, ссылки на `/terms` и `/privacy`. Пока приложение в Testing, входить
   могут только добавленные тестовые адреса — для закрытой беты этого хватает; для открытой нужен
   Publish, а он требует верификации домена.
3. Credentials → Create credentials → **OAuth client ID** → Web application.
   - Authorized redirect URI: **`https://cedarclerk.app/signin-google`**
   - Это путь по умолчанию у `AddGoogle`, он не совпадает с нашим `/api/auth/external/callback` —
     колбэк провайдера и наш экран после него разные вещи.
4. Client ID и Client secret — в дроп-ин:

```
Environment=Cedar__Auth__Google__ClientId=....apps.googleusercontent.com
Environment=Cedar__Auth__Google__ClientSecret=GOCSPX-....
```

Обе строки обязательны: без любой из них схема не регистрируется и `/api/auth/external/google`
отвечает 501.

После изменения systemd drop-in выполните на сервере:

```bash
sudo systemctl daemon-reload
sudo systemctl restart cedarclerk
```

Если Google всё ещё скрыт, проверьте `systemctl show cedarclerk -p NeedDaemonReload`
и поле `externalAuth.google` в `/api/health`. Создание credentials в Google Cloud
не передаёт их приложению автоматически. Значения ключей не публикуйте в логах.

### Telegram

Отдельных ключей не нужно — используется тот же `Cedar:Telegram:BotToken`. Но **виджету нужен домен,
привязанный к боту**, иначе он не отрисуется:

```
@BotFather → /setdomain → выбрать бота → cedarclerk.app
```

При неправильном домене виджет показывает `Bot domain invalid`. Для `cedar_clerk_bot`
должен быть разрешён фактический домен страницы: `cedarclerk.app`. Localhost не наследует
разрешение production-домена.

**Вход через Telegram не создаёт аккаунт** (ADR-237 п.4): у Telegram нет почты, а она нужна для
приглашений, чеков и восстановления. Незнакомому Telegram отвечает 404 с объяснением. Привязка —
по-прежнему в Настройках → Интеграции.

Проверка после рестарта:
- `curl -s https://cedarclerk.app/api/health | grep -o '"externalAuth":{[^}]*}'` — видно
  `"google":true` и имя бота
- на `/login` появились кнопка Google и виджет Telegram

## 3e. Восстановление пароля

1. Настройте отправку через Resend по разделу email выше.
2. Проверьте `Cedar:MainHost`: адрес ссылки должен начинаться с `https://cedarclerk.app`.
3. Откройте `/login` и нажмите «Забыли пароль?».
4. Введите адрес тестового аккаунта с подтверждённой почтой и паролем.
5. Откройте ссылку из письма и задайте новый пароль.
6. Проверьте вход с новым паролем. Старый пароль должен отклоняться.
7. Повторно откройте использованную ссылку. Повторный сброс должен отклоняться.

Ссылка действует один час. Аккаунты без подтверждённой почты и аккаунты только с внешним входом
не получают письмо. Форма не раскрывает наличие аккаунта по адресу.
Если почта не настроена, форма сообщает о временной недоступности.
Лимиты: 20 запросов письма и 60 попыток сброса в минуту на установку;
дополнительно не более 50 запросов письма в час.

## 4. Чеклист прокидывания на прод

```bash
ssh -t martycow@periwinkle.mooexe.dev            # -t нужен: дальше sudo спросит пароль
sudo nano /etc/systemd/system/cedarclerk.service.d/data.conf
# добавить строки Environment=... из разделов выше
sudo systemctl daemon-reload
sudo systemctl restart cedarclerk
sudo journalctl -u cedarclerk -n 20 --no-pager   # проверить, что поднялся
```

Про `journalctl`: **sudo не нужен** (проверено 12.08.2026) — юнит работает под `User=martycow`, так
что его записи принадлежат этому пользователю. Нужен флаг `-q`: без него journalctl печатает
подсказку про чужие юниты, которая читается как отказ. И запрос всегда ограничивать по числу строк —
сервис пишет ~1.5 млн строк в сутки.

Проверка без UI:
- `curl -s https://cedarclerk.mooexe.dev/api/health` — жив ли сервер
- В UI: Account-попап → появились ли кнопки Upgrade (Stripe/Stars)
- Вкладка EN → Auto-translate (если 501 — конфиг перевода не подхватился)

---

## 5. Бэкап за пределы дроплета — Cloudflare R2 (`T-147`)

**Зачем.** Ночная копия базы лежит на том же диске, что и сама база, а недельный образ дроплета —
в том же аккаунте DigitalOcean. Потеря дроплета, неоплата или угон логина забирают оригинал и копию
вместе. R2 — чужой аккаунт по отношению к DO, 10 ГБ бесплатно навсегда, исходящий трафик бесплатный;
наших данных ~1 ГБ (`media/` 938 МБ + копии базы по 2.8 МБ).

**Что уже сделано (12.08.2026, без твоего участия):**
- `rclone` v1.75 стоит в `~/bin/rclone` — статичный бинарь, sudo не потребовался. **Полный путь
  обязателен**: `~/bin` нет в PATH у cron, и `command -v rclone` отвечает «да» в интерактивной
  сессии и «нет» в 03:30.
- Скрипт бэкапа переписан и лежит в репозитории: `Scripts/server/backup.sh` (раньше он существовал
  только на сервере и нигде не версионировался). На дроплете — копия в `~/bin/backup.sh`, предыдущая
  версия сохранена как `~/bin/backup.sh.prev`.
- Секреты вынесены из скрипта в `~/.config/cedar-backup.env` (права 600): ping-URL healthchecks —
  это тоже секрет, кто им владеет, тот может погасить тревогу.
- Заготовка `~/.config/rclone/rclone.conf` (права 600) с пустыми ключами.
- Пока `R2_REMOTE` пуст, выгрузка **молча пропускается** — локальный бэкап от неё не зависит.

**Что нужно от тебя:**

1. `dash.cloudflare.com` → **R2** → Create bucket, имя `cedar-backup`, регион Automatic.
2. Там же **Manage R2 API Tokens** → Create API token → права **Object Read & Write**, только на этот
   бакет. Скопировать три значения: `Access Key ID`, `Secret Access Key`, `Endpoint`
   (`https://<account_id>.r2.cloudflarestorage.com`). Секрет показывают один раз.
3. `healthchecks.io` → New Check, имя `cedar-offsite`, период 1 день, grace 2 часа. Скопировать
   ping-URL. Это **второй** чек, отдельный от того, что уже есть на базу: «копия на дроплете упала» и
   «копия за пределами дроплета упала» — разные аварии, и они не должны глушить друг друга.
4. Вписать всё на сервере:

```bash
ssh martycow@periwinkle.mooexe.dev
nano ~/.config/rclone/rclone.conf     # access_key_id, secret_access_key, endpoint
nano ~/.config/cedar-backup.env       # HC_OFFSITE_URL=..., R2_REMOTE=r2:cedar-backup
~/bin/backup.sh && echo OK            # первый прогон: зальёт ~940 МБ, несколько минут
~/bin/rclone ls r2:cedar-backup/db | head; ~/bin/rclone size r2:cedar-backup
```

**Как это устроено.** База: `rclone copy` дневной копии в `db/`, на удалённой стороне хранится
30 дней (дольше локальных 14 — место бесплатное, а off-box копия и нужна для случая, когда локальных
уже нет). `media/`: `rclone sync` с `--backup-dir` в `media-removed/<дата>` — то, что sync собрался бы
удалить, переезжает в датированную папку. Без этого выгрузка была бы зеркалом, а не бэкапом:
случайное `rm` и шифровальщик выглядят для sync одинаково, и удаление уехало бы следом.

---

## 6. Мониторинг и статус-страница — UptimeRobot. **Настроено 13.08.2026 (`T-148`)**

Живая конфигурация (Марти, Free-тариф, интервал 5 минут) — запись, а не чек-лист:

| Что | Тип | URL | Ключевое слово |
|---|---|---|---|
| API | Keyword | `https://cedarclerk.mooexe.dev/api/health` | `I'm fine, thanks.` |
| Блог | HTTP(s) | `https://blog.mooexe.dev` | — |
| Десктоп-обновления | HTTP(s) | `https://cedarclerk.mooexe.dev/downloads/latest.yml` | — |

Keyword-монитор на `/api/health` — не роскошь: Cloudflare отдаёт 200 со страницей ошибки, когда
туннель лёг, так что проверка «пришёл ли 200» скажет «всё хорошо» ровно в тот момент, когда всё
плохо. Ответ содержит и версию — по нему видно, что задеплоено. Побочный урок первого дня: монитор
блога сначала «падал» при живом сайте — UptimeRobot ходит `HEAD`, а блог отвечал только на GET;
починено в 0.11.1 (`BlogHeadRequestTests`).

**Статус-страница**: `stats.uptimerobot.com/jKcnizZ9vU`, ссылка Status в футере блога. Свой домен
(`status.mooexe.dev`) у UptimeRobot платный — CNAME отложен до решения о платном тарифе; если
когда-нибудь включится, запись в Cloudflare обязана быть **DNS only** (проксирование ломает им
выпуск сертификата — тот же случай, что и `periwinkle`).

**Чего от них не ждать.** UptimeRobot проверяет снаружи и видит только HTTP. Он не заметит, что
кончается диск, что ночной бэкап не отработал (это healthchecks.io) и что база растёт быстрее
обычного — для этого есть `cedar status`.

---

## 7. Соцсети-коннекторы — X и Bluesky. **Работают на проде**

Оба подключаются пользователем в **Settings → Integrations** (ADR-095), ключи приложений — в drop-in.

**X/Twitter** (ADR-092/093): приложение в X Developer Portal (аккаунт Марти), OAuth 2.0 PKCE,
callback `https://cedarclerk.mooexe.dev/api/targets/x/callback`. В drop-in:
`Cedar__X__ClientId` + `Cedar__X__ClientSecret`. Права приложения — Read and write; подключение
запрашивает scope `tweet.read tweet.write users.read offline.access media.write` (ADR-241 —
`media.write` нужен для картинок; аккаунт, подключённый до 02.09.2026, надо переподключить, иначе
посты уходят без картинок и строка аккаунта об этом говорит). Публикация платная **для автора** — 1 кредит за
пост (пакеты кредитов см. §1/Stripe и Stars ниже); у самого приложения в X — свой pay-per-use
баланс, пополняется в портале X.

**Bluesky** (ADR-079): без приложения и review — пользователь вводит handle + app password,
хранится зашифрованным (DataProtection), сессия на каждую публикацию. Ключей в drop-in не требует.

**Кредиты** (ADR-092): покупка идёт существующими флоу — Stripe Checkout (metadata `credits_pack`)
и Stars-invoice (payload `credits-{pack}:{user}`); отдельных ключей нет, работает на тех же, что §1.

---

## 8. Свой домен для витрины проекта (`T-300`, ADR-216, ADR-245)

Витрина проекта живёт на `https://<имя>.cedarclerk.app/showcase/<слаг>`; старые ссылки
`/games/<слаг>` остаются совместимыми. Чтобы витрина отвечала ещё и на собственном домене
(`mygame.com`), нужно **две** вещи: строка в проекте и маршрут снаружи. Код умеет только первое.

1. **В приложении**: проект → «Витрина» → «Публикация и адрес» → поле «Свой домен» → `mygame.com`.
   Схему, `www.` и слэш можно не убирать — сервер приведёт к голому хосту сам. Домен обязан быть
   свободен: один хост принадлежит одному проекту на всю установку.
2. **В Cloudflare** (аккаунт с этим доменом): добавить домен в тот же tunnel, что обслуживает
   `cedarclerk.mooexe.dev` — public hostname на `http://127.0.0.1:8080`. Правится в
   `/etc/cloudflared/config.yml` на дроплете (файл читается root'ом, нужен `ssh -t`), затем
   `sudo systemctl reload cloudflared`. Сертификат Cloudflare выпускает сам.
3. **Проверить**: `curl -I https://mygame.com/` — должен прийти 200 и HTML витрины проекта, а не
   лендинг. Лендинг означает, что сервер про домен не знает: перечитайте шаг 1 (и вспомните, что
   ответ про хост кэшируется на секунды — подождите полминуты).

Пока домен не направлен на нас, строка в проекте ничего не ломает: она просто ни на что не отвечает.
Порядок «сначала DNS, потом поле» тоже рабочий, важно лишь, чтобы в итоге были обе половины.
