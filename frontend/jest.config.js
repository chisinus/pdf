module.exports = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testEnvironment: '<rootDir>/custom-jest-env.js',
  globalSetup: 'jest-preset-angular/global-setup',
  transform: {
    '^.+\\.(ts|js|mjs|html)$': [
      'jest-preset-angular',
      {
        stringifyContentPathRegex: '\\.html$',
      },
    ],
  },
};
