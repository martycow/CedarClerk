# Forest authentication design QA

Final result: passed

## Evidence

- Source visual truth: `%USERPROFILE%/.codex/generated_images/01a07fae-d079-7bd3-88e4-2664877e2df3/exec-7b29795b-e1e9-4a2e-a0b3-12c3ef530553.png`.
- Evidence directory: `%USERPROFILE%/.codex/visualizations/2026/09/08/01a07fae-d079-7bd3-88e4-2664877e2df3/forest-auth/`.
- Implementation: `login-en-1448.png`; full-view comparison: `comparison-final.png`; focused comparison: `comparison-card.png`.
- Both full images are 1448 x 1086 pixels. Browser viewport: 1448 x 1086 CSS pixels. The accessibility screenshot is normalized by the browser to that viewport; no additional scaling was applied to either comparison input.
- State: signed-out login, English, dark theme. Provider visibility uses an isolated local health fixture; the actual Telegram SDK loads. No account sign-in is claimed by that fixture.
- Local application: `http://localhost:8098/login`, isolated development data, bot disabled. Visual fixture: port 8099.

## Comparison history

1. Initial combined comparison (`%LOCALAPPDATA%/Temp/cedar-forest-comparison.png`): P2 body text too small, heading too heavy, sheet positioned too low. The decorative bookmark was outlined and the footer lacked the short rule. Set 16 px form text, 32 px regular Literata heading, adjusted sheet/footer spacing, and used the existing Phosphor fill asset for the marker.
2. Second screenshot (`%LOCALAPPDATA%/Temp/cedar-forest-second.png`): logo cropping was visible after enlarging its fit. Restored contain sizing to preserve the approved asset. The Russian registration sheet exceeded a 320 px viewport because of the grid track's intrinsic minimum. Set a zero-minimum grid track, a shrinkable sheet, and the mobile heading token. Legal navigation now uses independent nominative Russian labels.
3. Final combined and focused comparisons: no actionable P0/P1/P2 differences. Card width is 436 px, height 747.6 px; top 141.2 px against approximately 134 px in the source. Both provider buttons are 366 x 44 px. Their positions closely match the reference. Borders were strengthened to pass the 3:1 control-boundary requirement.

## Fidelity surfaces

| Surface | Result |
|---|---|
| Fonts and typography | Literata headings and slogan; Source Sans 3 form text. Regular 32 px desktop heading, 27 px mobile heading, 16 px controls and 14 px secondary text. Russian headings wrap without truncation. Antialiasing differs between the generated source and browser capture. |
| Spacing and layout | One centered 436 px paper sheet; shared outer header/footer; equal provider controls. Long registration forms scroll naturally. No horizontal control overflow at 320 px after the grid correction. |
| Colors and tokens | Warm ivory, dark olive forest, pine action, coral bookmark. Dedicated auth roles preserve light paper in both themes. Eleven contrast pairs cover paper text, boundaries, button labels and the plain dark canvas in both themes. The background artwork remains decorative. |
| Images and assets | Approved logo SVG kept intact. Generated forest and transparent cedar ornament follow the selected reference. Official Google identity asset and existing Telegram brand mark identify providers; the decorative marker comes from Phosphor. No handcrafted illustration substitutes. |
| Copy | Existing product registration and recovery copy retained. Login subtitle matches the selected direction. Terms/Privacy use correct standalone labels in both languages. |

## Interaction and responsive checks

- Login/register cross-links, header language/theme controls, and password visibility.
- Keyboard Tab reaches the forgot-password link from the email field.
- Russian registration at 320 x 740: top and bottom captured; all fields, submit, consent and footer links remain inside the viewport. Evidence: `register-ru-320.png`, `register-ru-320-bottom.png`.
- Russian recovery at 320 px in light theme: empty-submit validation visible and no overflow (`recovery-ru-320.png`).
- Invalid reset link shows the recovery path; valid-token submission and mismatch behavior are covered by existing unit tests.
- External completion at 360 px: Russian copy and inputs fit (`complete-ru-360.png`).
- Final inspected login/reset browser logs contained no warnings or errors.
- 1,993 backend tests and 783 frontend tests passed. SDK tests cover blocked popup, no messaging permission, script retry, callback forwarding, cancellation and teardown. Icon, contrast, density and production builds passed.

## Residual gaps and P3 polish

- The supplied approved logo has different proportions from its image-generated rendering; it is intentionally kept intact. The generated sprig and existing Telegram glyph differ slightly in detail from the source. These are minor asset-fidelity differences.
- Real Google/Telegram account confirmation, email delivery and mobile device keyboards were not exercised. Provider account testing requires configured services and the registered public origin.
- This report certifies local implementation and visual checks; deployment is verified separately through public health and the LIVE tag.

## Implementation checklist

- [x] Match shared authentication composition and approved identity.
- [x] Preserve existing endpoints and password-manager behavior.
- [x] Verify provider callback and failure behavior in unit tests.
- [x] Correct narrow Russian layout and legal copy.
- [x] Compare the built result with the selected visual in combined inputs.
- [x] Leave the actual local application available for review.
