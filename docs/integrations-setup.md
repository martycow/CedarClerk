# Интеграции: платежи и автоперевод — что настроить и куда прокинуть ключи

_Восстановлено из `_Documents_/CedarClerk/OLD/integrations-setup.md` (был архивирован при переносе документации в репозиторий, но два других файла — `docs/Handoff_2026-07-15.md` и `docs/ROADMAP.md` — уже ссылаются на него по пути `docs/integrations-setup.md`, так что он возвращён сюда без изменений содержания)._

_Все секреты живут в systemd drop-in на Pi: `/etc/systemd/system/cedarclerk.service.d/data.conf`
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
  Заработает сразу после прокидывания ключей.
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
Environment=Cedar__Translate__AnthropicApiKey=sk-ant-...
```

Модель по умолчанию — `claude-opus-4-8` (лучшее качество; ~$5/$25 за млн токенов —
перевод одного поста стоит копейки). Сэкономить можно, переключив на Sonnet:

```
Environment=Cedar__Translate__AnthropicModel=claude-sonnet-5
```

### Вариант B: OpenAI (ChatGPT API)

1. https://platform.openai.com → API keys → Create new secret key (`sk-...`). Нужен баланс.
2. Прокинь:

```
Environment=Cedar__Translate__Provider=openai
Environment=Cedar__Translate__OpenAiApiKey=sk-...
Environment=Cedar__Translate__OpenAiModel=gpt-4o
```

(модель поменяй на актуальную, какая тебе нравится — ключ `OpenAiModel`).

### Вариант C: DeepL — самый дешёвый, но «тупой» построчный перевод

1. https://www.deepl.com/pro-api → DeepL API Free (500k символов/мес бесплатно) или Pro.
2. Account → API Keys → скопируй ключ (у Free-ключей суффикс `:fx` — по нему код сам
   выбирает нужный хост API).
3. Прокинь:

```
Environment=Cedar__Translate__Provider=deepl
Environment=Cedar__Translate__DeepLApiKey=xxxx-xxxx-xxxx:fx
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

## 3. Чеклист прокидывания на Pi

```bash
ssh martycow@raspberrypi.local
sudo nano /etc/systemd/system/cedarclerk.service.d/data.conf
# добавить строки Environment=... из разделов выше
sudo systemctl daemon-reload
sudo systemctl restart cedarclerk
journalctl -u cedarclerk -n 20 --no-pager   # проверить, что поднялся
```

Проверка без UI:
- `curl -s https://cedarclerk.mooexe.dev/api/health` — жив ли сервер
- В UI: Account-попап → появились ли кнопки Upgrade (Stripe/Stars)
- Вкладка EN → Auto-translate (если 501 — конфиг перевода не подхватился)
