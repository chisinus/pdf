const JSDOMEnvironment = require('jest-environment-jsdom').default;

class CustomJestEnvironment extends JSDOMEnvironment {
  constructor(config, context) {
    super(config, context);

    // Bind Node's Uint8Array and ArrayBuffer to JSDOM's global scope
    this.global.Uint8Array = Uint8Array;
    this.global.ArrayBuffer = ArrayBuffer;

    // Optional: Add TextEncoder/TextDecoder which are frequently required
    // by Angular/RxJS in newer Node and Jest versions
    if (!this.global.TextEncoder) {
      const { TextEncoder, TextDecoder } = require('util');
      this.global.TextEncoder = TextEncoder;
      this.global.TextDecoder = TextDecoder;
    }
  }
}

module.exports = CustomJestEnvironment;
