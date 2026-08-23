namespace CedarClerk.Core;

public static partial class DesignTokens
{
    /// <summary>
    /// The faces, for the surfaces the Angular bundle never reaches (ADR-178).
    ///
    /// The files are copied unhashed into <c>assets/fonts</c> by <c>angular.json</c>, which is the
    /// whole reason these URLs may be written by hand — the bundler's own copies carry a content
    /// hash that changes under them at every build.
    ///
    /// Every rule carries a unicode-range. @fontsource's per-subset stylesheets carry none, and two
    /// subsets of one family and weight declared without one do not compose: the later rule wins for
    /// the whole range and every glyph outside its subset falls to the next family in the stack.
    ///
    /// Departure Mono (ADR-180) is the exception that proves the rule: one 22 KB file covers latin,
    /// cyrillic and greek together, so there is no second rule for a range to be shadowed by. It is
    /// also the one face here that is not a @fontsource copy — it has no package, so it sits in
    /// <c>public/assets/fonts</c> and reaches the output the same way the textures do.
    ///
    /// Martian Mono (ADR-191) is --font-mono itself, so it carries every weight a server-rendered
    /// page reaches for: 400 (body, code, dates), 600 (the language switch/registration chips) and
    /// 700 (series part numbers, reaction counts).
    /// </summary>
    public const string FontFaces = """
        @font-face { font-family: 'Vollkorn'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/vollkorn-latin-600-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Vollkorn'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/vollkorn-cyrillic-600-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Vollkorn'; font-style: normal; font-weight: 700; font-display: swap;
          src: url(/assets/fonts/vollkorn-latin-700-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Vollkorn'; font-style: normal; font-weight: 700; font-display: swap;
          src: url(/assets/fonts/vollkorn-cyrillic-700-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Source Sans 3'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/source-sans-3-latin-400-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Source Sans 3'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/source-sans-3-cyrillic-400-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Source Sans 3'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/source-sans-3-latin-600-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Source Sans 3'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/source-sans-3-cyrillic-600-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Literata'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/literata-latin-400-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Literata'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/literata-cyrillic-400-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Literata'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/literata-latin-600-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Literata'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/literata-cyrillic-600-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Departure Mono'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/departure-mono-1.500.woff2) format('woff2'); }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/martian-mono-latin-400-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 400; font-display: swap;
          src: url(/assets/fonts/martian-mono-cyrillic-400-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/martian-mono-latin-600-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 600; font-display: swap;
          src: url(/assets/fonts/martian-mono-cyrillic-600-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 700; font-display: swap;
          src: url(/assets/fonts/martian-mono-latin-700-normal.woff2) format('woff2');
          unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD; }
        @font-face { font-family: 'Martian Mono'; font-style: normal; font-weight: 700; font-display: swap;
          src: url(/assets/fonts/martian-mono-cyrillic-700-normal.woff2) format('woff2');
          unicode-range: U+0301,U+0400-045F,U+0490-0491,U+04B0-04B1,U+2116; }
        """;
}
