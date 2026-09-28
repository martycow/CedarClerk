# Localization

`CedarClerk.Localization` owns language resources and presentation rules (ADR-292).
Its .NET assembly has no dependency on Core, Server, Angular or a translation provider.

| Resource | Home |
|---|---|
| Content languages, endonyms and available interface dictionaries | `CedarClerk.Localization/languages.json` |
| API errors in nine languages | `ErrorMessages.cs` and `ErrorMessages.<language>.cs` |
| Blog, registration gate, reading menu and Showcase text | `BlogTexts.*.cs` |
| Landing and Discovery text | `LandingTexts.*.cs`, `LandingText.cs`, `DiscoveryTexts.cs` |
| Email subjects and bodies | `EmailTexts.cs` |
| Built-in document templates and section headings | `TemplateLibrary.cs`, `DocumentTexts.cs` |
| Date formatting and account timezones | `BlogDateFormatter.cs`, `DisplayTime.cs`, `TimeZones.cs` |
| Localized author values, declensions and transliteration | `LocalizedTextMap.cs`, `RussianDeclensions.cs`, `Transliteration.cs` |
| Default cross-link labels | `CrossLinkTexts.cs` |
| Browser dictionaries, emoji search terms, interpolation, language selection and display time | `Web/` |

The paths in this table are relative to `CedarClerk.Localization`, except the first full path.

## Consumers

Core and Server reference the .NET project. Angular imports `Web/` through the
`@localization/*` TypeScript path alias. No generation or file-copy step is required.
The .NET assembly embeds `languages.json`; TypeScript imports the same file.

`LocaleService` owns Angular signals, dictionary loading state, browser storage and the document language.
English loads with the application. Russian remains a separate lazy chunk.
`Web/dictionaries.ts` defines available loaders and the interface language type.
`Web/en.ts` defines `Dict`; `Web/ru.ts` must satisfy that shape.

Server retrieves the account language in `LanguagePreference.OfUserAsync`.
`LanguageNegotiation` parses the browser header. The request middleware applies `CurrentUICulture`.
Email and error resources read that culture. Public pages pass their existing language selection explicitly.

## Boundaries

Nine content languages do not imply nine translated application interfaces.
The application interface supports English and Russian. Errors and the registration gate support all nine content languages.
Existing fallback rules remain specific to each surface.

Core owns CedarJson and registration form serialization. Server owns translation jobs,
provider calls, quotas, account preferences and persisted translations. These operations consume Localization resources.
Language resources do not query accounts, charge credits, send mail or modify documents.
The Free plan language restriction remains a plan rule in Core and its frontend adapter.

## Adding text or a language

1. Put fixed interface text in its Localization catalog. Keep markup escaping at its current boundary.
2. For an application interface key, update both `Web/en.ts` and `Web/ru.ts`.
3. For a language, update `languages.json` and the catalogs that support that surface.
4. For a new application interface dictionary, add its loader in `Web/dictionaries.ts`.
5. Run the full gate (`AGENTS.md` §Key commands); add the smoke suite when browser behavior changes.

`LocalizationOwnershipTests` guards assembly ownership, reader catalog coverage and email culture isolation.
Frontend resource tests guard the shared catalog against missing dictionary loaders.
Existing date, form, renderer, error and template tests cover the consumers.
