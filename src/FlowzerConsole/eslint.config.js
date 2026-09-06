import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'src/lib/api/schema.gen.ts'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/consistent-type-imports': ['warn', { fixStyle: 'separate-type-imports' }],
    },
  },
  {
    // Die Stilblaetter von Form.io duerfen nur aus formioStyles.ts kommen. Importiert eine
    // Komponente eines davon direkt, entscheidet wieder die Ladereihenfolge der Module,
    // welches zuletzt steht — und der Eigenschaftendialog des Editors faellt im dunklen
    // Thema zurueck in sein Ausgangsgrau. Genau so ist der Fehler entstanden.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/components/forms/formioStyles.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@formio/js/dist/*.css', 'bootstrap-icons/**/*.css'],
              message:
                'Form.io-Stilblaetter nur ueber src/components/forms/formioStyles.ts einbinden — sonst kippt die Ladereihenfolge.',
            },
          ],
        },
      ],
    },
  },
);
