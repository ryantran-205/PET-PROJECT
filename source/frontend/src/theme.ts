import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

/** Aura preset re-tinted with a warm amber primary and espresso surfaces (always dark). */
export const BrewlinePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#fff8ef',
      100: '#ffecd4',
      200: '#ffd6a6',
      300: '#ffbd72',
      400: '#ffa64d',
      500: '#f0903a',
      600: '#d8752a',
      700: '#b35a22',
      800: '#8c4520',
      900: '#6e381d',
      950: '#3d1c0d',
    },
    colorScheme: {
      dark: {
        surface: {
          0: '#ffffff',
          50: '#f6ece2',
          100: '#e6d6c8',
          200: '#cdb8a6',
          300: '#b09885',
          400: '#8f7866',
          500: '#6f5b4c',
          600: '#4f4036',
          700: '#3a2e27',
          800: '#2a201b',
          900: '#1d1612',
          950: '#140f0c',
        },
        primary: {
          color: '{primary.400}',
          contrastColor: '#1d1612',
          hoverColor: '{primary.300}',
          activeColor: '{primary.200}',
        },
        highlight: {
          background: 'rgba(255, 166, 77, .16)',
          focusBackground: 'rgba(255, 166, 77, .24)',
          color: 'rgba(255, 236, 212, .95)',
          focusColor: 'rgba(255, 236, 212, .95)',
        },
      },
    },
  },
})
