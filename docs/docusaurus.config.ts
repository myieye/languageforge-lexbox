import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type {Options as PresetOptions, ThemeConfig} from '@docusaurus/preset-classic';
import type {Options as DocsOptions} from '@docusaurus/plugin-content-docs';

// Docusaurus appends the path of each doc relative to this directory.
const editUrl = 'https://github.com/sillsdev/languageforge-lexbox/tree/develop/docs';
const repoUrl = 'https://github.com/sillsdev/languageforge-lexbox';

const config: Config = {
  title: 'FieldWorks Lite & Lexbox Docs',
  tagline: 'Guides for using FieldWorks Lite and Lexbox, and technical documentation for developers.',
  favicon: 'img/favicon.png',

  future: {
    v4: true,
  },

  // The final home (probably somewhere on lexbox.org) is still a team decision.
  // Until then, CI overrides these to the repo's GitHub Pages URL — the defaults
  // here would 404 all assets when served under /<repo>/ (see docs.yaml).
  url: process.env.DOCS_URL ?? 'https://docs.lexbox.org',
  baseUrl: process.env.DOCS_BASE_URL ?? '/',

  organizationName: 'sillsdev',
  projectName: 'languageforge-lexbox',

  onBrokenLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  markdown: {
    mermaid: true,
  },
  themes: ['@docusaurus/theme-mermaid'],

  presets: [
    [
      'classic',
      {
        docs: false,
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies PresetOptions,
    ],
  ],

  // One docs instance per product/audience. These route bases are deep-link targets
  // for the apps (FW Lite help menu → /fw-lite/, lexbox.org help → /lexbox/,
  // project-page sync help → /fw-lite/how-sync-works) — don't move them without redirects.
  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'fw-lite',
        path: 'fw-lite',
        routeBasePath: 'fw-lite',
        sidebarPath: './sidebars.ts',
        editUrl,
      } satisfies DocsOptions,
    ],
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'lexbox',
        path: 'lexbox',
        routeBasePath: 'lexbox',
        sidebarPath: './sidebars.ts',
        editUrl,
      } satisfies DocsOptions,
    ],
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'technical',
        path: 'technical',
        routeBasePath: 'technical',
        sidebarPath: './sidebars.ts',
        editUrl,
      } satisfies DocsOptions,
    ],
    [
      '@docusaurus/plugin-client-redirects',
      {
        createRedirects: (path: string) =>
          path.startsWith('/fw-lite/') || path === '/fw-lite'
            ? [path.replace('/fw-lite', '/user-guide')]
            : undefined,
      },
    ],
  ],

  themeConfig: {
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'FieldWorks Lite & Lexbox',
      logo: {
        alt: 'FieldWorks Lite & Lexbox',
        src: 'img/logo.svg',
        srcDark: 'img/logo-dark.svg',
      },
      items: [
        {to: '/fw-lite/', label: 'FieldWorks Lite', position: 'left'},
        {to: '/lexbox/', label: 'Lexbox', position: 'left'},
        {to: '/technical/', label: 'Technical', position: 'left'},
        {href: repoUrl, label: 'GitHub', position: 'right'},
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {label: 'Lexbox', href: 'https://lexbox.org'},
        {label: 'GitHub', href: repoUrl},
      ],
      copyright: `Copyright © ${new Date().getFullYear()} SIL Global`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
    },
  } satisfies ThemeConfig,
};

export default config;
