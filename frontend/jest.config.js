/** @type {import('jest').Config} */
module.exports = {
  preset: 'jest-preset-angular',
  testEnvironment: '<rootDir>/custom-jest-env.js',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  globalSetup: 'jest-preset-angular/global-setup',

  transform: {
    '^.+\\.(ts|mjs|js|html)$': [
      'jest-preset-angular',
      {
        tsconfig: '<rootDir>/tsconfig.spec.json',
        stringifyContentPathRegex: '\\.html$',
      },
    ],
  },

  moduleFileExtensions: ['ts', 'js', 'mjs', 'html', 'json'],
  extensionsToTreatAsEsm: ['.ts'],

  moduleNameMapper: {
    '^src/(.*)$': '<rootDir>/src/$1',
  },

  resolver: 'jest-preset-angular/build/resolvers/ng-jest-resolver.js',
};
